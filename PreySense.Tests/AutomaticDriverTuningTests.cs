using System.Reflection;
using System.Runtime.CompilerServices;
using PreySense.Core.Configuration;
using PreySense.Core.Services;
using PreySense.Frontend.ViewModels;

namespace PreySense.Tests;

[TestClass]
public class AutomaticDriverTuningTests
{
    private static MainViewModel Create(PreySenseConfig config)
    {
        foreach (var profile in new[] { config.Tuning.Quiet, config.Tuning.Eco, config.Tuning.Balanced, config.Tuning.Performance, config.Tuning.Turbo })
        {
            profile.CpuPl1Watts ??= 45; profile.CpuPl2Watts ??= 80; profile.TauSeconds ??= 56;
            profile.GpuDeltaWatts ??= 20; profile.GpuDynamicBoostWatts ??= 15;
            profile.PlatformPowerOffsetWatts ??= 10;
            profile.CpuBoost ??= true; profile.CpuBoostMode ??= profile.CpuBoost == true ? 1 : 0;
            profile.ApplyCpuLimits = true;
            profile.ApplyGpuLimits = true;
        }
        var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        void Set(string name, object value) => typeof(MainViewModel)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm, value);
        Set("_hw", new MockHardwareService());
        Set("_modeSwitchGate", new SemaphoreSlim(1, 1));
        Set("_modeSwitchDebounceLock", new object());
        Set("_isHardwareTuningEnabled", true);
        vm.TuningConfigurationReader = () => config;
        return vm;
    }

    [TestMethod]
    [DataRow(0)] [DataRow(1)] [DataRow(4)] [DataRow(5)] [DataRow(6)]
    public void AutomaticApplication_UsesSavedModeValuesIncludingIndependentGpuOffsets(int mode)
    {
        var config = new PreySenseConfig();
        var profile = mode switch
        {
            0 => config.Tuning.Quiet, 1 => config.Tuning.Balanced, 4 => config.Tuning.Performance,
            5 => config.Tuning.Turbo, _ => config.Tuning.Eco
        };
        profile.CpuPl1Watts = 61; profile.CpuPl2Watts = 103; profile.TauSeconds = 32;
        profile.GpuDeltaWatts = 35; profile.GpuDynamicBoostWatts = 20; profile.PlatformPowerOffsetWatts = 15;
        var vm = Create(config);
        MainViewModel.DriverTuningRequest? captured = null;
        vm.DriverTuningExecutor = request => { captured = request; return new(true, "verified"); };
        var result = vm.ApplySavedDriverTuning((byte)mode);
        Assert.IsTrue(result.Success);
        Assert.IsNotNull(captured);
        Assert.AreEqual((byte)mode, captured.Value.Mode);
        Assert.AreEqual(61, captured.Value.Pl1);
        Assert.AreEqual(103, captured.Value.Pl2);
        Assert.AreEqual(32, captured.Value.Tau);
        Assert.AreEqual(35, captured.Value.Offsets.DeltaWatts);
        Assert.AreEqual(20, captured.Value.Offsets.DynamicBoostWatts);
        Assert.AreEqual(15, captured.Value.Offsets.PlatformOffsetWatts);
        // Re-reading must see newly saved values rather than an old editor draft.
        profile.CpuPl1Watts = 72;
        vm.ApplySavedDriverTuning((byte)mode);
        Assert.AreEqual(72, captured.Value.Pl1);
    }

    [TestMethod]
    public async Task EnableTuning_PersistsForTheNextStartup()
    {
        string directory = Path.Combine(Path.GetTempPath(), "PreySenseTuning-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "preysense.json");
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            typeof(MainViewModel).GetField("_modeSwitchGate", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(vm, new SemaphoreSlim(1, 1));
            typeof(MainViewModel).GetField("_modeSwitchDebounceLock", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(vm, new object());
            vm.TuningConfigurationStore = new ConfigurationService(path);
            vm.TuningConfigurationStore.Update(cfg => cfg.Tuning.FactoryDefaults = TuningCalibrationTests.Defaults());
            vm.IsHardwareTuningEnabled = true;
            await vm.TuningEnableTask;
            Assert.IsTrue(new ConfigurationService(path).GetCurrent().General.HardwareTuningEnabled);
            vm.IsHardwareTuningEnabled = false;
            Assert.IsFalse(new ConfigurationService(path).GetCurrent().General.HardwareTuningEnabled);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    [DataRow(true)] [DataRow(false)]
    public void StartupRestore_UsesDriverOnlyWhenTuningIsEnabled(bool enabled)
    {
        var vm = Create(new());
        typeof(MainViewModel).GetField("_isHardwareTuningEnabled", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(vm, enabled);
        int calls = 0;
        vm.DriverTuningExecutor = request =>
        {
            calls++;
            Assert.AreEqual((byte)5, request.Mode);
            return new(true, "verified");
        };
        var result = vm.RestoreSavedPowerMode(5);
        Assert.IsTrue(result.Success, result.Status);
        Assert.AreEqual(enabled ? 1 : 0, calls);
    }

    [TestMethod]
    public void StartupRestore_SurfacesDriverInitializationFailure()
    {
        var vm = Create(new());
        vm.DriverTuningExecutor = _ => throw new InvalidOperationException("driver unavailable");
        var result = vm.RestoreSavedPowerMode(5);
        Assert.IsFalse(result.Success);
        Assert.Contains("driver unavailable", result.Status);
    }

    [TestMethod]
    public void InvalidSavedProfile_DoesNotReachDriver()
    {
        var config = new PreySenseConfig();
        config.Tuning.Turbo.CpuPl1Watts = 300;
        var vm = Create(config);
        vm.DriverTuningExecutor = _ => throw new AssertFailedException("Invalid tuning must not reach the driver.");
        var result = vm.ApplySavedDriverTuning(5);
        Assert.IsFalse(result.Success);
        Assert.Contains("nothing was written", result.Status);
    }

    [TestMethod]
    public async Task ModeSwitchFailure_ReportsDriverErrorAndReleasesGate()
    {
        var vm = Create(new());
        int calls = 0;
        vm.DriverTuningExecutor = _ => { calls++; return new(false, "readback failed"); };
        await vm.ApplyModeSwitchAsync(5);
        Assert.AreEqual(1, calls);
        Assert.Contains("readback failed", vm.DiagnosticMessage);
        Assert.IsFalse(vm.IsBusy);
        await vm.ApplyModeSwitchAsync(4);
        Assert.AreEqual(2, calls, "A failed tuning operation must not retain the mode gate.");
    }

    [TestMethod]
    public async Task SimultaneousModeChanges_SerializeDriverTransactions()
    {
        var vm = Create(new());
        int active = 0, maximum = 0;
        vm.DriverTuningExecutor = _ =>
        {
            int count = Interlocked.Increment(ref active);
            maximum = Math.Max(maximum, count);
            Thread.Sleep(30);
            Interlocked.Decrement(ref active);
            return new(false, "test rejection");
        };
        await Task.WhenAll(vm.ApplyModeSwitchAsync(4), vm.ApplyModeSwitchAsync(5));
        Assert.AreEqual(1, maximum);
    }

    [TestMethod]
    public async Task ManualApply_UsesTheSameDriverExecutorAndReportsPartialFailure()
    {
        var vm = Create(new());
        MainViewModel.DriverTuningRequest? captured = null;
        vm.DriverTuningExecutor = request =>
        {
            captured = request;
            return new(false, "GPU profile applied; CPU limits failed: locked");
        };
        bool success = await vm.ApplyTuningProfileAsync(5, 70, 110, 56, new(30, 15), persist: false);
        Assert.IsFalse(success);
        Assert.IsNotNull(captured);
        Assert.AreEqual(70, captured.Value.Pl1);
        Assert.Contains("CPU limits failed: locked", vm.DiagnosticMessage);
        Assert.IsFalse(vm.IsBusy);
    }
}
