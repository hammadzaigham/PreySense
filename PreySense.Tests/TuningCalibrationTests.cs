using System.Reflection;
using System.Runtime.CompilerServices;
using PreySense.Core.Configuration;
using PreySense.Frontend.ViewModels;

namespace PreySense.Tests;

[TestClass]
public class TuningCalibrationTests
{
    internal static Dictionary<string, ModeTuningConfig> Defaults() =>
        new[] { "Eco", "Quiet", "Balanced", "Performance", "Turbo" }.ToDictionary(name => name, _ =>
            new ModeTuningConfig { CpuPl1Watts = 45, CpuPl2Watts = 80, TauSeconds = 56,
                GpuRuntimeLimitWatts = 140, GpuWatts = 120, CpuBoost = true });

    private static MainViewModel.FactoryDefaultsCapture Capture() => new(Defaults(), 55, 80);

    private string _directory = null!;
    private MainViewModel _vm = null!;
    private ConfigurationService _store = null!;
    [TestInitialize]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "PreySenseCalibration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _store = new ConfigurationService(Path.Combine(_directory, "preysense.json"));
        _vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        typeof(MainViewModel).GetField("_modeSwitchGate", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_vm, new SemaphoreSlim(1, 1));
        typeof(MainViewModel).GetField("_modeSwitchLock", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_vm, new object());
        _vm.TuningConfigurationStore = _store;
    }
    [TestCleanup] public void Cleanup() => Directory.Delete(_directory, recursive: true);

    [TestMethod]
    public async Task EnableWithMissingStaticLimits_RemainsOffWithoutStartingCapture()
    {
        _store.Update(cfg => cfg.Tuning.FactoryDefaults = Defaults());
        _vm.CaptureTuningDefaults = () => throw new AssertFailedException("Enable must not initiate capture.");
        _vm.IsHardwareTuningEnabled = true;
        await _vm.TuningEnableTask;
        Assert.IsFalse(_vm.IsHardwareTuningEnabled);
        Assert.IsFalse(_store.Reload().General.HardwareTuningEnabled);
    }

    [TestMethod]
    public async Task ConcurrentCaptureRequests_ShareOneOperation()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int captures = 0;
        _vm.CaptureTuningDefaults = () =>
        {
            Interlocked.Increment(ref captures);
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            return Capture();
        };
        Task first = _vm.CaptureMissingFactoryDefaultsAsync();
        try
        {
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(10)));
            Assert.IsTrue(_vm.IsBusy);
            Task second = _vm.CaptureMissingFactoryDefaultsAsync();
            Assert.AreSame(first, second);
        }
        finally { release.Set(); await first; }
        Assert.AreEqual(1, captures);
        Assert.IsFalse(_vm.IsBusy);
        Assert.IsTrue(_store.Reload().Tuning.HasFactoryStaticCpuLimits);
    }

    [TestMethod]
    public async Task CaptureFailure_ReleasesModeGateAndBusyStateWithoutSaving()
    {
        _vm.CaptureTuningDefaults = () => throw new InvalidOperationException("read failed");
        await _vm.CaptureMissingFactoryDefaultsAsync();
        Assert.IsFalse(_vm.IsBusy);
        Assert.IsFalse(_vm.IsHardwareTuningEnabled);
        var gate = (SemaphoreSlim)typeof(MainViewModel).GetField("_modeSwitchGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_vm)!;
        Assert.AreEqual(1, gate.CurrentCount);
        Assert.IsEmpty(_store.GetCurrent().Tuning.FactoryDefaults);
        Assert.Contains("read failed", _vm.DiagnosticMessage);
    }

    [TestMethod]
    public async Task IncompleteSavedCapture_IsReplacedRatherThanMerged()
    {
        _store.Update(cfg =>
        {
            cfg.Tuning.FactoryDefaults = Defaults();
            cfg.Tuning.FactoryDefaults["Turbo"].CpuPl1Watts = 70;
            cfg.Tuning.FactoryProchotOffsetCelsius = 9;
            cfg.Tuning.FactoryTjMaxCelsius = 100;
        });
        _vm.CaptureTuningDefaults = Capture;
        await _vm.CaptureMissingFactoryDefaultsAsync();
        var saved = _store.Reload().Tuning;
        Assert.AreEqual(45, saved.FactoryDefaults["Turbo"].CpuPl1Watts);
        Assert.IsNull(saved.FactoryProchotOffsetCelsius);
        Assert.IsNull(saved.FactoryTjMaxCelsius);
    }

    [TestMethod]
    public async Task InvalidStaticLimits_DoNotCommitAProfileCapture()
    {
        _vm.CaptureTuningDefaults = () => new(Defaults(), 200, 80);
        await _vm.CaptureMissingFactoryDefaultsAsync();
        Assert.IsEmpty(_store.Reload().Tuning.FactoryDefaults);
        Assert.IsFalse(_vm.IsBusy);
    }

    [TestMethod]
    public async Task StartupCapture_SavesDefaultsAndMakesTuningAvailable()
    {
        _vm.CaptureTuningDefaults = Capture;
        await _vm.CaptureMissingFactoryDefaultsAsync();
        Assert.IsTrue(_store.Reload().Tuning.HasFactoryDefaults);
        Assert.IsTrue(_vm.IsHardwareTuningEnabled);
        Assert.IsFalse(_store.GetCurrent().General.HardwareTuningEnabled);
        _vm.CaptureTuningDefaults = () => throw new AssertFailedException("Existing defaults must be preserved.");
        await _vm.CaptureMissingFactoryDefaultsAsync();
    }

    [TestMethod]
    public async Task StartupCaptureFailure_DoesNotSavePartialDefaults()
    {
        _vm.CaptureTuningDefaults = () => new(new() { ["Eco"] = Defaults()["Eco"] }, 55, 80);
        await _vm.CaptureMissingFactoryDefaultsAsync();
        Assert.IsEmpty(_store.Reload().Tuning.FactoryDefaults);
        Assert.Contains("Could not capture factory defaults", _vm.DiagnosticMessage);
    }

    [TestMethod]
    public async Task EnableTuning_RequiresExistingFactoryDefaults()
    {
        _store.Update(cfg =>
        {
            cfg.Tuning.FactoryDefaults = Defaults();
            cfg.Tuning.FactoryStaticCpuPl1Watts = 55;
            cfg.Tuning.FactoryStaticCpuPl2Watts = 80;
        });
        _vm.CaptureTuningDefaults = () => throw new AssertFailedException("Enabling must not calibrate.");
        _vm.IsHardwareTuningEnabled = true;
        await _vm.TuningEnableTask;
        var saved = new ConfigurationService(_store.ConfigPath).GetCurrent();
        Assert.IsTrue(_vm.IsHardwareTuningEnabled);
        Assert.IsTrue(saved.General.HardwareTuningEnabled);
        Assert.AreEqual(45, saved.Tuning.FactoryDefaults["Turbo"].CpuPl1Watts);
        Assert.IsNull(saved.Tuning.Turbo.CpuPl1Watts);
        _vm.IsHardwareTuningEnabled = false;
        _vm.IsHardwareTuningEnabled = true;
        await _vm.TuningEnableTask;
        Assert.IsTrue(_vm.IsHardwareTuningEnabled);
    }

    [TestMethod]
    public async Task EnableWithoutFactoryDefaults_LeavesTuningOff()
    {
        _vm.CaptureTuningDefaults = () => throw new AssertFailedException("Enabling must not calibrate.");
        _vm.IsHardwareTuningEnabled = true;
        await _vm.TuningEnableTask;
        Assert.IsFalse(_vm.IsHardwareTuningEnabled);
        Assert.IsFalse(_store.Reload().General.HardwareTuningEnabled);
        Assert.IsEmpty(_store.GetCurrent().Tuning.FactoryDefaults);
        Assert.Contains("factory defaults are ready", _vm.DiagnosticMessage);
    }

    [TestMethod]
    public async Task DisableTuning_ClearsUserProfilesButKeepsFactoryDefaults()
    {
        _store.Update(cfg =>
        {
            cfg.Tuning.FactoryDefaults = Defaults();
            cfg.Tuning.Turbo.CpuPl1Watts = 99;
            cfg.Tuning.Turbo.CpuPl2Watts = 140;
            cfg.Tuning.Turbo.TauSeconds = 32;
            cfg.Tuning.Turbo.GpuRuntimeLimitWatts = 150;
            cfg.Tuning.Turbo.CpuBoostMode = 1;
            cfg.Tuning.ProchotOffsetCelsius = 7;
            cfg.Tuning.ApplyProchotOffset = true;
            cfg.General.HardwareTuningEnabled = true;
        });
        typeof(MainViewModel).GetField("_isHardwareTuningEnabled", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(_vm, true);
        _vm.IsHardwareTuningEnabled = false;
        var saved = _store.Reload();
        Assert.IsFalse(saved.General.HardwareTuningEnabled);
        Assert.IsTrue(saved.Tuning.HasFactoryDefaults);
        Assert.AreEqual(45, saved.Tuning.FactoryDefaults["Turbo"].CpuPl1Watts);
        Assert.IsNull(saved.Tuning.Turbo.CpuPl1Watts);
        Assert.IsNull(saved.Tuning.Turbo.CpuPl2Watts);
        Assert.IsNull(saved.Tuning.Turbo.TauSeconds);
        Assert.IsNull(saved.Tuning.Turbo.GpuRuntimeLimitWatts);
        Assert.IsNull(saved.Tuning.ProchotOffsetCelsius);
        Assert.IsFalse(saved.Tuning.ApplyProchotOffset);
        await Task.CompletedTask;
    }

    [TestMethod]
    public void Editor_UsesFactoryFallbackWithoutChangingSavedOrFactoryProfiles()
    {
        _store.Update(cfg =>
        {
            cfg.Tuning.FactoryDefaults = Defaults();
            cfg.Tuning.Turbo.CpuPl1Watts = 60;
            cfg.Tuning.FactoryProchotOffsetCelsius = 5;
        });
        var draft = _vm.TuningEditorMode(4);
        Assert.AreEqual(60, draft.CpuPl1Watts);
        Assert.AreEqual(80, draft.CpuPl2Watts);
        Assert.IsNull(draft.GpuRuntimeLimitWatts, "Factory defaults must not become a saved custom GPU target.");
        Assert.AreEqual(5, _vm.TuningEditorProchotOffset);
        draft.CpuPl2Watts = 100;
        Assert.IsNull(_store.GetCurrent().Tuning.Turbo.CpuPl2Watts);
        Assert.AreEqual(80, _store.GetCurrent().Tuning.FactoryDefaults["Turbo"].CpuPl2Watts);
        Assert.IsNull(_store.GetCurrent().Tuning.ProchotOffsetCelsius);
    }

    [TestMethod]
    public void MissingTuningValues_RoundTripAsNull()
    {
        _store.Update(cfg => cfg.General.LightTheme = true);
        var tuning = new ConfigurationService(_store.ConfigPath).GetCurrent().Tuning;
        foreach (var profile in new[] { tuning.Eco, tuning.Quiet, tuning.Balanced, tuning.Performance, tuning.Turbo })
        {
            Assert.IsFalse(profile.HasValues);
            Assert.IsNull(profile.CpuPl1Watts); Assert.IsNull(profile.CpuPl2Watts);
            Assert.IsNull(profile.TauSeconds); Assert.IsNull(profile.GpuWatts);
            Assert.IsNull(profile.GpuRuntimeLimitWatts);
            Assert.IsNull(profile.CpuBoost);
        }
    }

    [TestMethod]
    public async Task EnableWhileBusy_StillSucceedsWhenDefaultsExist()
    {
        _store.Update(cfg =>
        {
            cfg.Tuning.FactoryDefaults = Defaults();
            cfg.Tuning.FactoryStaticCpuPl1Watts = 55;
            cfg.Tuning.FactoryStaticCpuPl2Watts = 80;
        });
        _vm.IsBusy = true;
        _vm.CaptureTuningDefaults = () => throw new AssertFailedException("Existing defaults must not trigger recalibration.");
        _vm.IsHardwareTuningEnabled = true;
        await _vm.TuningEnableTask;
        Assert.IsTrue(_vm.IsHardwareTuningEnabled);
        Assert.IsTrue(_store.GetCurrent().General.HardwareTuningEnabled);
        Assert.IsFalse(string.IsNullOrEmpty(_vm.DiagnosticMessage));
        Assert.DoesNotContain("Calibration failed", _vm.DiagnosticMessage);
        Assert.DoesNotContain("stays off", _vm.DiagnosticMessage);
    }

    [TestMethod]
    public async Task StartupCapture_AutomaticallyEnablesTuningEvenWhenLegacySwitchWasOff()
    {
        _store.Update(cfg => cfg.General.HardwareTuningEnabled = false);
        Assert.IsFalse(_store.GetCurrent().Tuning.HasFactoryDefaults);
        Assert.IsFalse(_vm.IsHardwareTuningEnabled);
        _vm.CaptureTuningDefaults = Capture;
        await _vm.CaptureMissingFactoryDefaultsAsync();
        Assert.IsTrue(_store.Reload().Tuning.HasFactoryDefaults);
        Assert.IsTrue(_vm.IsHardwareTuningEnabled);
        Assert.IsFalse(_store.GetCurrent().General.HardwareTuningEnabled, "The obsolete preference is not rewritten.");
    }

    [TestMethod]
    public async Task StartupCapture_DoesNotReuseSavedProchotMetadata()
    {
        _store.Update(cfg =>
        {
            cfg.Tuning.FactoryProchotOffsetCelsius = 12;
            cfg.Tuning.FactoryProchotOffsetLocked = true;
        });
        _vm.CaptureTuningDefaults = Capture;
        await _vm.CaptureMissingFactoryDefaultsAsync();
        var saved = _store.Reload().Tuning;
        Assert.IsTrue(saved.HasFactoryDefaults);
        Assert.IsNull(saved.FactoryProchotOffsetCelsius);
        Assert.IsNull(saved.FactoryProchotOffsetLocked);
    }

    [TestMethod]
    public async Task StartupCapture_MissingProchotReadStaysNull()
    {
        _store.Update(cfg => cfg.Tuning.FactoryProchotOffsetCelsius = 12);
        _vm.CaptureTuningDefaults = Capture;
        await _vm.CaptureMissingFactoryDefaultsAsync();
        Assert.IsNull(_store.Reload().Tuning.FactoryProchotOffsetCelsius);
    }

    [TestMethod]
    public void ResetMode_RestoresFactoryProchotOffset()
    {
        _store.Update(cfg =>
        {
            cfg.Tuning.FactoryDefaults = Defaults();
            cfg.Tuning.FactoryProchotOffsetCelsius = 8;
            cfg.Tuning.FactoryProchotOffsetLocked = false;
            cfg.Tuning.ProchotOffsetCelsius = 20;
            cfg.Tuning.ApplyProchotOffset = true;
        });
        _vm.ProchotOffsetCelsius = 20;
        _vm.IsProchotOffsetEnabled = true;
        _vm.ResetModeToCapturedFactoryDefaults(4);
        Assert.AreEqual(8, _vm.ProchotOffsetCelsius);
        Assert.IsFalse(_vm.IsProchotOffsetEnabled, "Reset restores the factory offset and clears the apply switch.");
        Assert.IsFalse(_vm.IsProchotOffsetLocked);
    }
}
