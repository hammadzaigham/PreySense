using System.Reflection;
using System.Runtime.CompilerServices;
using PreySense.Core.Configuration;
using PreySense.Core.Driver;
using PreySense.Frontend.ViewModels;

namespace PreySense.Tests;

[TestClass]
public class GpuProfileTargetTests
{
    [TestMethod]
    [DataRow((byte)6)] [DataRow((byte)0)] [DataRow((byte)1)] [DataRow((byte)4)] [DataRow((byte)5)]
    public void EveryModeResolvesCustomTargetAndRestoresCapturedFactoryBytes(byte mode)
    {
        var cfg = new PreySenseConfig();
        cfg.Tuning.GpuBasePowerWatts = 85;
        string name = new[] { "Eco", "Quiet", "Balanced", "Performance", "Turbo" }[MainViewModel.GpuModeIndex(mode)];
        cfg.Tuning.FactoryDefaults[name] = new() { GpuDeltaWatts = 55, GpuDynamicBoostWatts = 25, PlatformPowerOffsetWatts = 35 };
        var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        vm.TuningConfigurationReader = () => cfg;
        Assert.IsTrue(vm.TryResolveGpuTarget(mode, 140, out var custom, out string status), status);
        Assert.AreEqual(new GpuPowerOffsets(40, 15, 35), custom);
        Assert.IsTrue(vm.TryResolveGpuTarget(mode, 165, out var factory, out status), status);
        Assert.AreEqual(new GpuPowerOffsets(55, 25, 35), factory);
        Assert.IsTrue(vm.TryResolveGpuTarget(mode, 210, out var highest, out status), status);
        Assert.AreEqual(new GpuPowerOffsets(110, 15, 35), highest);
        Assert.IsFalse(vm.TryResolveGpuTarget(mode, 84, out _, out _));
        Assert.IsFalse(vm.TryResolveGpuTarget(mode, 999, out _, out _));
    }

    [TestMethod]
    public void MissingGpuTableDoesNotInventOffsets()
    {
        var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        vm.TuningConfigurationReader = () => new();
        Assert.IsFalse(vm.TryResolveGpuTarget(5, 140, out var target, out string status));
        Assert.IsNull(target);
        Assert.Contains("not found", status);
    }
    [TestMethod]
    [DataRow((byte)6)] [DataRow((byte)0)] [DataRow((byte)1)] [DataRow((byte)4)] [DataRow((byte)5)]
    public void SavedIndependentOffsetsReachApplyWithoutWattageConversion(byte mode)
    {
        var cfg = new PreySenseConfig();
        var profile = mode switch { 6 => cfg.Tuning.Eco, 0 => cfg.Tuning.Quiet,
            1 => cfg.Tuning.Balanced, 4 => cfg.Tuning.Performance, _ => cfg.Tuning.Turbo };
        profile.ApplyGpuLimits = true;
        profile.GpuDeltaWatts = 255; profile.GpuDynamicBoostWatts = 25; profile.PlatformPowerOffsetWatts = 255;
        var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        typeof(MainViewModel).GetField("_hw", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(vm, new PreySense.Core.Services.MockHardwareService());
        vm.TuningConfigurationReader = () => cfg;
        MainViewModel.DriverTuningRequest? captured = null;
        vm.DriverTuningExecutor = request => { captured = request; return new(true, "captured"); };
        Assert.IsTrue(vm.ApplySavedDriverTuning(mode).Success);
        Assert.IsNotNull(captured);
        Assert.AreEqual(new GpuPowerOffsets(255, 25, 255), captured.Value.GpuOffsets);
        Assert.IsNull(captured.Value.GpuRuntimeLimitWatts);
        Assert.IsFalse(captured.Value.ApplyCpuLimits);
    }

    [TestMethod]
    public void SavedGpuOffsetsRemainSeparateFromUnsavedDrafts()
    {
        var cfg = new PreySenseConfig();
        cfg.Tuning.Balanced.GpuDeltaWatts = 100;
        cfg.Tuning.Balanced.GpuDynamicBoostWatts = 15;
        cfg.Tuning.Balanced.PlatformPowerOffsetWatts = 255;
        var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        vm.TuningConfigurationReader = () => cfg;
        Assert.AreEqual(new GpuPowerOffsets(100, 15, 255), vm.GpuOffsetsForMode(2));
        vm.StoreGpuOffsets(2, new(255, 25, 100));
        cfg.Tuning.Balanced.GpuDeltaWatts = 50;
        Assert.AreEqual(new GpuPowerOffsets(255, 25, 100), vm.GpuOffsetsForMode(2));
    }

}
