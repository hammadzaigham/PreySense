using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Xml.Linq;
namespace PreySense.Frontend.Services
{
    public static class StartupTaskService
    {
        public const string TaskName = "PreySense";
        private const string TaskNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";

        private static readonly StartupTaskRegistration Registration = new(RunSchtasks,
            () => Environment.ProcessPath, () => WindowsIdentity.GetCurrent().User?.Value);

        public static bool IsStartupEnabled() => Registration.IsEnabled();
        public static bool SetStartupEnabled(bool enable) => Registration.SetEnabled(enable);
        public static bool ReconcileExistingTask() => Registration.ReconcileExisting();

        public static string BuildLogonTaskXml(string exePath, string userId)
        {
            string command = SecurityElement.Escape(exePath) ?? exePath;
            string workingDirectory = SecurityElement.Escape(Path.GetDirectoryName(exePath)) ?? "";
            string user = SecurityElement.Escape(userId) ?? userId;
            return $"""
                <?xml version="1.0" encoding="UTF-16"?>
                <Task version="1.2" xmlns="{TaskNamespace}">
                  <RegistrationInfo>
                    <URI>\{TaskName}</URI>
                  </RegistrationInfo>
                  <Principals>
                    <Principal id="Author">
                      <UserId>{user}</UserId>
                      <LogonType>InteractiveToken</LogonType>
                      <RunLevel>HighestAvailable</RunLevel>
                    </Principal>
                  </Principals>
                  <Settings>
                    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                    <AllowHardTerminate>true</AllowHardTerminate>
                    <StartWhenAvailable>true</StartWhenAvailable>
                    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                    <RunOnlyIfIdle>false</RunOnlyIfIdle>
                    <IdleSettings>
                      <StopOnIdleEnd>false</StopOnIdleEnd>
                      <RestartOnIdle>false</RestartOnIdle>
                    </IdleSettings>
                    <AllowStartOnDemand>true</AllowStartOnDemand>
                    <Enabled>true</Enabled>
                    <Hidden>false</Hidden>
                    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                    <Priority>7</Priority>
                  </Settings>
                  <Triggers>
                    <LogonTrigger>
                      <Enabled>true</Enabled>
                    </LogonTrigger>
                  </Triggers>
                  <Actions Context="Author">
                    <Exec>
                      <Command>{command}</Command>
                      <Arguments>--minimized</Arguments>
                      <WorkingDirectory>{workingDirectory}</WorkingDirectory>
                    </Exec>
                  </Actions>
                </Task>
                """;
        }

        internal readonly record struct SchtasksResult(int ExitCode, string Output);

        // All reads/repairs/toggles share a lock, so an in-flight repair cannot recreate a deleted task.
        internal sealed class StartupTaskRegistration(
            Func<string, SchtasksResult> run, Func<string?> executablePath, Func<string?> currentUser)
        {
            private readonly object _gate = new();

            public bool IsEnabled()
            {
                lock (_gate) return TryRead(out string xml) && CanLaunch(xml);
            }

            public bool ReconcileExisting()
            {
                lock (_gate)
                {
                    // Never create a missing entry or re-enable a task/trigger disabled by its owner.
                    if (!TryRead(out string xml) || !IsEnabledTaskXml(xml, currentUser())) return false;
                    string? exe = executablePath();
                    string? user = currentUser();
                    if (!string.IsNullOrWhiteSpace(exe) && !string.IsNullOrWhiteSpace(user) &&
                        !MatchesTaskXml(xml, exe, user))
                    {
                        if (Register(exe, user)) return true;
                    }
                    return CanLaunch(xml);
                }
            }

            public bool SetEnabled(bool enable)
            {
                lock (_gate)
                {
                    if (!enable)
                    {
                        var deleted = run($"/Delete /F /TN \"{TaskName}\"");
                        if (deleted.ExitCode != 0) AppLogger.Error($"Startup task removal failed: {deleted.Output}");
                        return deleted.ExitCode == 0;
                    }
                    string? exe = executablePath();
                    string? user = currentUser();
                    return !string.IsNullOrWhiteSpace(exe) && !string.IsNullOrWhiteSpace(user) && Register(exe, user);
                }
            }

            private bool CanLaunch(string xml) => IsEnabledTaskXml(xml, currentUser()) &&
                TryReadCommand(xml, out string command) && File.Exists(command);

            private bool TryRead(out string xml)
            {
                var result = run($"/Query /TN \"{TaskName}\" /XML");
                xml = result.Output;
                return result.ExitCode == 0;
            }

            private bool Register(string exe, string user)
            {
                if (!Path.IsPathFullyQualified(exe) || !File.Exists(exe)) return false;
                string path = Path.Combine(Path.GetTempPath(), $"PreySense-startup-{Guid.NewGuid():N}.xml");
                try
                {
                    File.WriteAllText(path, BuildLogonTaskXml(exe, user), Encoding.Unicode);
                    var result = run($"/Create /F /TN \"{TaskName}\" /XML \"{path}\"");
                    if (result.ExitCode != 0)
                    {
                        AppLogger.Error($"Startup task registration failed: {result.Output}");
                        return false;
                    }
                    if (!TryRead(out string saved))
                    {
                        AppLogger.Error("Startup task was created but could not be queried back.");
                        return false;
                    }
                    if (!MatchesTaskXml(saved, exe, user))
                    {
                        AppLogger.Error("Startup task was created but readback did not match the required logon policy.");
                        return false;
                    }
                    return true;
                }
                catch (Exception ex) { AppLogger.Error($"Startup task registration failed: {ex}"); return false; }
                finally { try { File.Delete(path); } catch { } }
            }
        }

        internal static bool IsEnabledTaskXml(string xml, string? user)
        {
            try
            {
                XNamespace ns = TaskNamespace;
                var root = XDocument.Parse(xml).Root;
                if (root?.Name != ns + "Task" || string.IsNullOrWhiteSpace(user)) return false;
                var principal = root.Element(ns + "Principals")?.Element(ns + "Principal");
                string? principalUser = principal?.Element(ns + "UserId")?.Value;
                if (string.IsNullOrWhiteSpace(principalUser) || !UserIdsMatch(principalUser, user)) return false;
                if (!Enabled(root.Element(ns + "Settings")?.Element(ns + "Enabled"))) return false;
                // Logon trigger is intentionally unscoped (any interactive logon). Older builds may still
                // have a per-user UserId that Task Scheduler rewrote to DOMAIN\user — accept both.
                return root.Element(ns + "Triggers")?.Elements(ns + "LogonTrigger").Any(trigger =>
                    Enabled(trigger.Element(ns + "Enabled")) &&
                    UserIdsMatch(trigger.Element(ns + "UserId")?.Value, user, allowEmpty: true)) == true;
            }
            catch { return false; }
        }

        private static bool Enabled(XElement? element) => element == null || element.Value.Trim() is "true" or "1";

        // Principal stays as the enabling account (SID). Scheduler may still rewrite older trigger UserIds.
        internal static bool UserIdsMatch(string? actual, string expected, bool allowEmpty = false)
        {
            if (string.IsNullOrWhiteSpace(actual)) return allowEmpty;
            if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) return true;
            try
            {
                static string ToSid(string value) => value.StartsWith("S-", StringComparison.OrdinalIgnoreCase)
                    ? value
                    : ((SecurityIdentifier)new NTAccount(value).Translate(typeof(SecurityIdentifier))).Value;
                return string.Equals(ToSid(actual), ToSid(expected), StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        internal static bool MatchesTaskXml(string xml, string exe, string user)
        {
            if (!IsEnabledTaskXml(xml, user) || !TryReadCommand(xml, out string command) ||
                !string.Equals(command, exe, StringComparison.OrdinalIgnoreCase)) return false;
            try
            {
                XNamespace ns = TaskNamespace;
                var actual = XDocument.Parse(xml).Root!;
                var settings = actual.Element(ns + "Settings");
                var principal = actual.Element(ns + "Principals")?.Element(ns + "Principal");
                var trigger = actual.Element(ns + "Triggers")?.Element(ns + "LogonTrigger");
                var actions = actual.Element(ns + "Actions");
                var exec = actions?.Element(ns + "Exec");
                string workingDirectory = Path.GetDirectoryName(exe) ?? "";
                // Check durable policy only. Scheduler drops many default leaves and may rewrite UserId form.
                return principal?.Element(ns + "LogonType")?.Value == "InteractiveToken" &&
                    principal?.Element(ns + "RunLevel")?.Value == "HighestAvailable" &&
                    ValueOrDefault(settings, ns + "DisallowStartIfOnBatteries") == "false" &&
                    ValueOrDefault(settings, ns + "StopIfGoingOnBatteries") == "false" &&
                    ValueOrDefault(settings, ns + "StartWhenAvailable", "true") == "true" &&
                    ValueOrDefault(settings, ns + "ExecutionTimeLimit", "PT0S") == "PT0S" &&
                    ValueOrDefault(settings, ns + "MultipleInstancesPolicy", "IgnoreNew") == "IgnoreNew" &&
                    ValueOrDefault(settings, ns + "RunOnlyIfIdle", "false") == "false" &&
                    ValueOrDefault(settings, ns + "RunOnlyIfNetworkAvailable", "false") == "false" &&
                    ValueOrDefault(settings?.Element(ns + "IdleSettings"), ns + "StopOnIdleEnd", "false") == "false" &&
                    ValueOrDefault(settings?.Element(ns + "IdleSettings"), ns + "RestartOnIdle", "false") == "false" &&
                    actual.Element(ns + "Triggers")?.Elements().Count() == 1 &&
                    actions?.Elements().Count() == 1 &&
                    trigger?.Element(ns + "EndBoundary") == null &&
                    trigger?.Element(ns + "StartBoundary") == null &&
                    trigger?.Element(ns + "Delay") == null &&
                    exec?.Element(ns + "Arguments")?.Value == "--minimized" &&
                    string.Equals(exec?.Element(ns + "WorkingDirectory")?.Value, workingDirectory,
                        StringComparison.OrdinalIgnoreCase) &&
                    (string?)actions?.Attribute("Context") == (string?)principal?.Attribute("id");
            }
            catch { return false; }
        }

        private static string ValueOrDefault(XElement? parent, XName name, string whenMissing = "false") =>
            parent?.Element(name)?.Value.Trim() ?? whenMissing;

        private static bool TryReadCommand(string xml, out string command)
        {
            command = string.Empty;
            try
            {
                XNamespace ns = TaskNamespace;
                command = XDocument.Parse(xml).Root?.Element(ns + "Actions")?.Element(ns + "Exec")?
                    .Element(ns + "Command")?.Value.Trim().Trim('"') ?? string.Empty;
                return command.Length > 0;
            }
            catch { return false; }
        }

        private static SchtasksResult RunSchtasks(string arguments)
        {
            try
            {
                using var proc = Process.Start(new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });
                if (proc == null)
                    return new SchtasksResult(-1, string.Empty);

                var stdout = proc.StandardOutput.ReadToEndAsync();
                var stderr = proc.StandardError.ReadToEndAsync();
                if (!proc.WaitForExit(15000))
                {
                    try { proc.Kill(entireProcessTree: true); } catch { }
                    AppLogger.Error("Startup task operation timed out.");
                    return new SchtasksResult(-1, string.Empty);
                }

                string output = stdout.GetAwaiter().GetResult();
                string error = stderr.GetAwaiter().GetResult();
                return new SchtasksResult(proc.ExitCode, string.IsNullOrWhiteSpace(output) ? error : output);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Startup task operation failed: {ex}");
                return new SchtasksResult(-1, string.Empty);
            }
        }
    }
}
