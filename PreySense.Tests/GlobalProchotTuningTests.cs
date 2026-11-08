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
        Set("_modeSwitchDebounceLock", new object()); Set("_isHardwareTuningEnabled", true);
        vm.TuningConfigurationReader = () => config;
        return vm;
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
        await vm.ApplyTuningProfileAsync(5, 60, 100, 56, new(25, 15), false, 10, true);
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
            profile.GpuDeltaWatts = 20; profile.GpuDynamicBoostWatts = 15; profile.PlatformPowerOffsetWatts = 10; profile.CpuBoost = true;
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
            Assert.AreEqual(20, request.Offsets.DeltaWatts);
        }
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task ManualApplyCombinesOtherTuningWithTheOptionalGlobalOffset(bool enabled)
    {
        var vm = Create(new());
        MainViewModel.DriverTuningRequest? captured = null;
        vm.DriverTuningExecutor = request => { captured = request; return new(false, "test interception"); };
        await vm.ApplyTuningProfileAsync(5, 60, 100, 56, new(25, 15), false, 10, enabled);
        Assert.IsNotNull(captured);
        Assert.AreEqual(enabled ? 10 : (int?)null, captured.Value.ProchotOffsetCelsius);
        Assert.AreEqual(60, captured.Value.Pl1);
        Assert.AreEqual(100, captured.Value.Pl2);
        Assert.AreEqual(25, captured.Value.Offsets.DeltaWatts);
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
