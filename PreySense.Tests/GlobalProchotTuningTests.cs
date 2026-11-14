using System.Reflection;
using System.Runtime.CompilerServices;
using PreySense.Core.Configuration;
using PreySense.Core.Services;
using PreySense.Frontend.ViewModels;

namespace PreySense.Tests;

[TestClass]
public class GlobalProchotTuningTests
{
    private static MainViewModel Create(PreySenseConfig config)
    {
        var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        void Set(string name, object value) => typeof(MainViewModel).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, value);
        Set("_hw", new MockHardwareService()); Set("_modeSwitchGate", new SemaphoreSlim(1, 1));
        Set("_modeSwitchLock", new object()); Set("_isHardwareTuningEnabled", true);
        vm.TuningConfigurationReader = () => config;
        return vm;
    }

    [TestMethod]
    public void SaveProchotPersistsOnlyOffsetAndSwitchWithoutHardwareWrites()
    {
        string directory = Path.Combine(Path.GetTempPath(), "PreySenseProchotSave-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new ConfigurationService(Path.Combine(directory, "preysense.json"));
            store.Update(cfg => { cfg.Tuning.Balanced.CpuPl1Watts = 65; cfg.Tuning.Balanced.GpuDeltaWatts = 255; });
            var vm = Create(store.GetCurrent()); vm.TuningConfigurationStore = store;
            vm.DriverTuningExecutor = _ => throw new AssertFailedException("Save must not write hardware.");
            vm.ProchotOffsetCelsius = 10; vm.IsProchotOffsetEnabled = true;
            Assert.IsTrue(vm.SaveProchotSettings());
            var saved = store.GetCurrent().Tuning;
            Assert.AreEqual(10, saved.ProchotOffsetCelsius); Assert.IsTrue(saved.ApplyProchotOffset);
            Assert.AreEqual(65, saved.Balanced.CpuPl1Watts); Assert.AreEqual(255, saved.Balanced.GpuDeltaWatts);
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void ActivationLabel_RequiresAnOffsetAndPreservesExplicitZero()
    {
        string directory = Path.Combine(Path.GetTempPath(), "PreySenseProchotLabel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new ConfigurationService(Path.Combine(directory, "preysense.json"));
            var vm = Create(new());
            vm.TuningConfigurationStore = store;
            vm.TjMaxCelsius = 100;
            Assert.AreEqual("PROCHOT —", vm.ProchotActivationLabel);
            vm.ProchotOffsetCelsius = 0;
            Assert.AreEqual("PROCHOT 100°C", vm.ProchotActivationLabel);
            vm.ProchotOffsetCelsius = null;
            store.Update(config => config.Tuning.FactoryProchotOffsetCelsius = 7);
            Assert.AreEqual("PROCHOT 93°C", vm.ProchotActivationLabel);
            vm.IsProchotOffsetLocked = true;
            Assert.AreEqual("PROCHOT 93°C - Locked", vm.ProchotActivationLabel);
            vm.IsProchotOffsetLocked = false;
            Assert.AreEqual("PROCHOT 93°C", vm.ProchotActivationLabel);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void UnreadableProchotState_DisablesBothControls()
    {
        var vm = Create(new());
        vm.IsProchotOffsetEnabled = true;
        vm.UpdateProchotLockState(null);
        Assert.IsFalse(vm.CanToggleProchotOffset);
        Assert.IsFalse(vm.CanEditProchotOffset);
        Assert.IsFalse(vm.IsProchotOffsetEnabled);
    }

    [TestMethod]
    public async Task LockedOffset_DisablesControlsAndSkipsOffsetWhileApplyingOtherLimits()
    {
        var vm = Create(new());
        vm.IsProchotOffsetEnabled = true;
        vm.UpdateProchotLockState(new(7, 100, true));
        Assert.IsFalse(vm.IsProchotOffsetEnabled);
        Assert.IsFalse(vm.CanToggleProchotOffset);
        Assert.IsFalse(vm.CanEditProchotOffset);
        MainViewModel.DriverTuningRequest? captured = null;
        vm.DriverTuningExecutor = request => { captured = request; return new(false, "test interception"); };
        await vm.ApplyTuningProfileAsync(5, 60, 100, 56, 140, false, 10, true);
        Assert.IsNotNull(captured);
        Assert.IsNull(captured.Value.ProchotOffsetCelsius);
        Assert.AreEqual(60, captured.Value.Pl1);
        vm.UpdateProchotLockState(new(7, 100, false));
        Assert.IsTrue(vm.CanToggleProchotOffset);
        Assert.IsFalse(vm.CanEditProchotOffset);
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public void SavedProfilesIncludeTheSameGlobalOffsetOnlyWhenSwitchedOn(bool enabled)
    {
        var config = new PreySenseConfig();
        config.Tuning.ApplyProchotOffset = enabled; config.Tuning.ProchotOffsetCelsius = 12;
        foreach (var profile in new[] { config.Tuning.Quiet, config.Tuning.Turbo })
        {
            profile.CpuPl1Watts = 55; profile.CpuPl2Watts = 90; profile.TauSeconds = 56;
            profile.GpuRuntimeLimitWatts = 140; profile.CpuBoost = true;
            profile.ApplyCpuLimits = true;
        }
        var vm = Create(config);
        var requests = new List<MainViewModel.DriverTuningRequest>();
        vm.DriverTuningExecutor = request => { requests.Add(request); return new(false, "test interception"); };
        vm.ApplySavedDriverTuning(0); vm.ApplySavedDriverTuning(5);
        Assert.HasCount(2, requests);
        foreach (var request in requests)
        {
            Assert.AreEqual(enabled ? 12 : (int?)null, request.ProchotOffsetCelsius);
            Assert.AreEqual(55, request.Pl1);
        }
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task ManualApplyCombinesOtherTuningWithTheOptionalGlobalOffset(bool enabled)
    {
        var vm = Create(new());
        MainViewModel.DriverTuningRequest? captured = null;
        vm.DriverTuningExecutor = request => { captured = request; return new(false, "test interception"); };
        await vm.ApplyTuningProfileAsync(5, 60, 100, 56, 140, false, 10, enabled);
        Assert.IsNotNull(captured);
        Assert.AreEqual(enabled ? 10 : (int?)null, captured.Value.ProchotOffsetCelsius);
        Assert.AreEqual(60, captured.Value.Pl1);
        Assert.AreEqual(100, captured.Value.Pl2);
        Assert.AreEqual(140, captured.Value.GpuRuntimeLimitWatts);
    }

    [TestMethod]
    public async Task ManualApply_AllowsProchotOnlyWhenCpuAndGpuLimitsAreOff()
    {
        var vm = Create(new());
        MainViewModel.DriverTuningRequest? captured = null;
        vm.DriverTuningExecutor = request => { captured = request; return new(true, "verified"); };
        Assert.IsTrue(await vm.ApplyTuningProfileAsync(5, 60, 100, 56, null, false, 10, true,
            applyCpuLimits: false));
        Assert.IsNotNull(captured);
        Assert.AreEqual(10, captured.Value.ProchotOffsetCelsius);
        Assert.IsFalse(captured.Value.ApplyCpuLimits);
        Assert.IsFalse(captured.Value.ApplyGpuLimits);

        Assert.IsFalse(await vm.ApplyTuningProfileAsync(5, 60, 100, 56, null, false, 10, false,
            applyCpuLimits: false));
        Assert.Contains("PROCHOT", vm.DiagnosticMessage);
    }

    [TestMethod]
    public void SavedRestore_AppliesProchotWhenModeLimitsAreOff()
    {
        var config = new PreySenseConfig();
        config.Tuning.ApplyProchotOffset = true;
        config.Tuning.ProchotOffsetCelsius = 9;
        config.Tuning.Turbo.ApplyCpuLimits = false;
        config.Tuning.Turbo.GpuRuntimeLimitWatts = null;
        var vm = Create(config);
        MainViewModel.DriverTuningRequest? captured = null;
        vm.DriverTuningExecutor = request => { captured = request; return new(true, "verified"); };
        var result = vm.ApplySavedDriverTuning(5);
        Assert.IsTrue(result.Success, result.Status);
        Assert.IsNotNull(captured);
        Assert.AreEqual(9, captured.Value.ProchotOffsetCelsius);
        Assert.IsFalse(captured.Value.ApplyCpuLimits);
        Assert.IsFalse(captured.Value.ApplyGpuLimits);
    }

    [TestMethod]
    public void GlobalOffsetRoundTripsInJsonOutsidePerModeProfiles()
    {
        string directory = Path.Combine(Path.GetTempPath(), "PreySenseProchot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "preysense.json");
            var store = new ConfigurationService(path);
            store.Update(cfg => { cfg.Tuning.ApplyProchotOffset = true; cfg.Tuning.ProchotOffsetCelsius = 10; cfg.Tuning.FactoryProchotOffsetLocked = true; });
            var saved = new ConfigurationService(path).GetCurrent();
            Assert.AreEqual(10, saved.Tuning.ProchotOffsetCelsius);
            Assert.IsTrue(saved.Tuning.ApplyProchotOffset);
            Assert.IsTrue(saved.Tuning.FactoryProchotOffsetLocked);
            Assert.IsNull(saved.Tuning.Quiet.CpuPl1Watts);
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            Assert.IsFalse(json.RootElement.GetProperty("Tuning").GetProperty("Quiet").TryGetProperty("ProchotOffsetCelsius", out _));
            store.Update(cfg => cfg.Tuning.ApplyProchotOffset = false);
            Assert.AreEqual(10, store.Reload().Tuning.ProchotOffsetCelsius, "Turning the switch off preserves the chosen value without applying it.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
