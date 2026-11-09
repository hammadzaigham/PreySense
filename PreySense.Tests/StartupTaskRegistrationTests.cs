using System.Text.RegularExpressions;
using System.Xml.Linq;
using PreySense.Frontend.Services;
using static PreySense.Frontend.Services.StartupTaskService;

namespace PreySense.Tests;

[TestClass]
public class StartupTaskRegistrationTests
{
    private const string User = "S-1-5-21-1-2-3-1001";
    private string _directory = null!;
    private string _exe = null!;
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    [TestInitialize]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "PreySenseStartupTask-" + Guid.NewGuid());
        Directory.CreateDirectory(_directory);
        _exe = Path.Combine(_directory, "PreySense.exe");
        File.WriteAllText(_exe, "test fixture; never executed");
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_directory, true);

    [TestMethod]
    public void MissingTask_RemainsOffAndIsNeverCreatedByDiscovery()
    {
        var fake = new Scheduler();
        var registration = Create(fake);
        Assert.IsFalse(registration.IsEnabled());
        Assert.IsFalse(registration.ReconcileExisting());
        Assert.AreEqual(0, fake.Creates);
    }

    [TestMethod]
    [DataRow("Settings")]
    [DataRow("LogonTrigger")]
    public void DisabledTaskOrTrigger_RemainsOffWithoutAutomaticEnable(string disabledPart)
    {
        var doc = XDocument.Parse(BuildLogonTaskXml(_exe, User));
        doc.Descendants(Ns + disabledPart).Single().Element(Ns + "Enabled")!.Value = "false";
        string original = doc.ToString();
        var fake = new Scheduler { Xml = original };
        var registration = Create(fake);
        Assert.IsFalse(registration.IsEnabled());
        Assert.IsFalse(registration.ReconcileExisting());
        Assert.AreEqual(original, fake.Xml);
        Assert.AreEqual(0, fake.Creates);
    }

    [TestMethod]
    public void AnotherUsersTask_IsNotReportedEnabledOrTakenOver()
    {
        var fake = new Scheduler { Xml = BuildLogonTaskXml(_exe, "S-1-5-21-9-9-9-1002") };
        Assert.IsFalse(Create(fake).ReconcileExisting());
        Assert.AreEqual(0, fake.Creates);
    }

    [TestMethod]
    public void EnabledOldTask_UsesCurrentExecutableAndWorkingDirectory()
    {
        var old = XDocument.Parse(BuildLogonTaskXml(Path.Combine(_directory, "Old", "PreySense.exe"), User));
        old.Descendants(Ns + "DisallowStartIfOnBatteries").Single().Value = "true";
        old.Descendants(Ns + "RunOnlyIfIdle").Single().Value = "true";
        var fake = new Scheduler { Xml = old.ToString() };
        Assert.IsTrue(Create(fake).ReconcileExisting());
        Assert.AreEqual(1, fake.Creates);
        Assert.IsTrue(MatchesTaskXml(fake.Xml!, _exe, User));
        var saved = XDocument.Parse(fake.Xml!);
        Assert.AreEqual(_directory, saved.Descendants(Ns + "WorkingDirectory").Single().Value);
        Assert.IsFalse(saved.Descendants(Ns + "Delay").Any(), "No logon delay is requested.");
    }

    [TestMethod]
    public void HealthyTask_WithSchedulerMetadata_IsLeftUntouched()
    {
        var doc = XDocument.Parse(BuildLogonTaskXml(_exe, User));
        doc.Root!.Element(Ns + "RegistrationInfo")!.Add(new XElement(Ns + "Date", "2026-11-09T00:00:00"));
        var fake = new Scheduler { Xml = doc.ToString() };
        Assert.IsTrue(Create(fake).ReconcileExisting());
        Assert.AreEqual(0, fake.Creates);
    }

    [TestMethod]
    public void RegistrationRequiresReadback_NotJustSuccessfulCreateExitCode()
    {
        var fake = new Scheduler { CorruptReadback = true };
        Assert.IsFalse(Create(fake).SetEnabled(true));
        Assert.AreEqual(1, fake.Creates);
    }

    [TestMethod]
    public void WindowsNormalizedTask_WithUnscopedLogonTrigger_IsAccepted()
    {
        // Mirrors Export-ScheduledTask after /Create: defaults stripped, no trigger UserId.
        string xml = $$"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><URI>\PreySense</URI></RegistrationInfo>
              <Principals>
                <Principal id="Author">
                  <UserId>{{User}}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <StartWhenAvailable>true</StartWhenAvailable>
                <IdleSettings>
                  <StopOnIdleEnd>false</StopOnIdleEnd>
                  <RestartOnIdle>false</RestartOnIdle>
                </IdleSettings>
              </Settings>
              <Triggers><LogonTrigger /></Triggers>
              <Actions Context="Author">
                <Exec>
                  <Command>{{_exe}}</Command>
                  <Arguments>--minimized</Arguments>
                  <WorkingDirectory>{{_directory}}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;
        Assert.IsTrue(IsEnabledTaskXml(xml, User));
        Assert.IsTrue(MatchesTaskXml(xml, _exe, User));
        var fake = new Scheduler { NormalizedReadback = xml };
        Assert.IsTrue(Create(fake).SetEnabled(true));
        Assert.AreEqual(1, fake.Creates);
    }

    [TestMethod]
    public void NewRegistration_OmitsPerUserLogonTriggerUserId()
    {
        string xml = BuildLogonTaskXml(_exe, User);
        Assert.Contains($"<UserId>{User}</UserId>", xml);
        StringAssert.DoesNotMatch(xml,
            new Regex(@"<LogonTrigger>\s*<Enabled>true</Enabled>\s*<UserId>", RegexOptions.CultureInvariant));
    }

    [TestMethod]
    public void LegacyPerUserTrigger_WithoutUserIdOrWithEmptyUserId_IsAccepted()
    {
        var doc = XDocument.Parse(BuildLogonTaskXml(_exe, User));
        var trigger = doc.Descendants(Ns + "LogonTrigger").Single();
        trigger.Element(Ns + "Enabled")?.Remove();
        Assert.IsTrue(IsEnabledTaskXml(doc.ToString(), User));
        Assert.IsTrue(MatchesTaskXml(doc.ToString(), _exe, User));
    }

    [TestMethod]
    public void MalformedTask_IsOffAndNotAutomaticallyReplaced()
    {
        var fake = new Scheduler { Xml = "<broken>" };
        Assert.IsFalse(Create(fake).ReconcileExisting());
        Assert.AreEqual(0, fake.Creates);
    }

    [TestMethod]
    public async Task DisablingWhileRepairIsInFlight_CannotRecreateTheTaskAfterDeletion()
    {
        using var readStarted = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var fake = new Scheduler
        {
            Xml = BuildLogonTaskXml(Path.Combine(_directory, "Old", "PreySense.exe"), User),
            ReadStarted = readStarted, ReleaseRead = release
        };
        var registration = Create(fake);
        Task<bool> repair = Task.Run(registration.ReconcileExisting);
        try
        {
            Assert.IsTrue(readStarted.Wait(TimeSpan.FromSeconds(5)));
            Task<bool> disable = Task.Run(() => registration.SetEnabled(false));
            release.Set();
            await repair.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsTrue(await disable.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.IsNull(fake.Xml);
            Assert.IsFalse(registration.ReconcileExisting());
            Assert.AreEqual(1, fake.Creates);
            Assert.AreEqual(1, fake.Deletes);
        }
        finally { release.Set(); await repair; }
    }

    [TestMethod]
    public void RegistrationXml_IsAcceptedByWindowsTaskSchedulerWithoutRegisteringATask()
    {
        dynamic? scheduler = null;
        dynamic? definition = null;
        try
        {
            scheduler = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!);
            scheduler!.Connect();
            definition = scheduler.NewTask(0);
            string user = System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
            definition.XmlText = BuildLogonTaskXml(_exe, user);
            Assert.IsTrue(MatchesTaskXml((string)definition.XmlText, _exe, user));
        }
        finally
        {
            if (definition is not null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(definition);
            if (scheduler is not null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(scheduler);
        }
    }

    private StartupTaskRegistration Create(Scheduler fake) => new(fake.Run, () => _exe, () => User);

    private sealed class Scheduler
    {
        public string? Xml;
        public string? NormalizedReadback;
        public int Creates, Deletes;
        public bool CorruptReadback;
        public ManualResetEventSlim? ReadStarted, ReleaseRead;
        private int _blocked;
        public SchtasksResult Run(string args)
        {
            if (args.StartsWith("/Query"))
            {
                if (ReadStarted is not null && Interlocked.Exchange(ref _blocked, 1) == 0)
                {
                    ReadStarted.Set();
                    if (!ReleaseRead!.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
                }
                return Xml is null ? new(1, "Task not found") : new(0, Xml);
            }
            if (args.StartsWith("/Delete"))
            {
                Deletes++;
                Xml = null;
                return new(0, "Deleted");
            }
            if (args.StartsWith("/Create"))
            {
                Creates++;
                string path = Regex.Match(args, "/XML \"([^\"]+)\"").Groups[1].Value;
                Xml = CorruptReadback ? "<broken>" : NormalizedReadback ?? File.ReadAllText(path);
                return new(0, "Created");
            }
            throw new InvalidOperationException(args);
        }
    }
}
