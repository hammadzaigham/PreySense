using System.Reflection;
using System.Runtime.CompilerServices;
using PreySense.Core.Services;
using PreySense.Frontend.ViewModels;
using PreySense.Mode;

namespace PreySense.Tests;

[TestClass]
public class FanModeRuntimeTests
{
    // Skip the UI constructor, which starts real hotkey watchers and startup work.
    // Supply only the state used by the fan controller, backed entirely by a fake.
    private static MainViewModel Create(RecordingFans hardware)
    {
        var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        Set(vm, "_hw", hardware);
        Set(vm, "_fanControlGate", new object());
        Set(vm, "_cpuFanFollower", new FanCurveFollower());
        Set(vm, "_gpuFanFollower", new FanCurveFollower());
        Set(vm, "_fanCoolingSafety", new FanCoolingSafety());
        return vm;
    }
    private static void Set(object target, string field, object value) =>
        target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static void Run(MainViewModel vm, string method, params object[] args) =>
        typeof(MainViewModel).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(vm, args);
    private static void Select(MainViewModel vm, FanControlMode mode)
    {
        Run(vm, "ResetFanFollower");
        Set(vm, "_runningFanProfileMode", (byte)1);
        Set(vm, "_runningFanProfile", new FanCurveProfile
        {
            ControlMode = mode, Cpu = Enumerable.Repeat(40, FanCurveProfile.PointCount).ToArray(), Gpu = Enumerable.Repeat(50, FanCurveProfile.PointCount).ToArray()
        });
    }
    [TestMethod]
    public void MaxOperatesIndependentlyAndReturnsToAutoWithoutRepeatedWrites()
    {
        var hardware = new RecordingFans();
        var vm = Create(hardware);
        Select(vm, FanControlMode.Max);
        Run(vm, "ProcessFanCurves", hardware.GetCurrentTelemetry());
        Run(vm, "ProcessFanCurves", hardware.GetCurrentTelemetry());
        CollectionAssert.AreEqual(new[] { "mode:1" }, hardware.Calls);
        Assert.AreEqual(100, vm.AppliedCpuFanPercent);
        Select(vm, FanControlMode.Auto);
        Run(vm, "ProcessFanCurves", hardware.GetCurrentTelemetry());
        Run(vm, "ProcessFanCurves", hardware.GetCurrentTelemetry());
        CollectionAssert.AreEqual(new[] { "mode:1", "mode:0" }, hardware.Calls);
        Assert.IsNull(vm.AppliedCpuFanPercent);
    }
    [TestMethod]
    public void CustomTargetsAreIndependentOfMaxAndReleaseOnExit()
    {
        var hardware = new RecordingFans();
        var vm = Create(hardware);
        Select(vm, FanControlMode.Max);
        Run(vm, "ProcessFanCurves", hardware.GetCurrentTelemetry());
        Select(vm, FanControlMode.Custom);
        Run(vm, "ProcessFanCurves", hardware.GetCurrentTelemetry());
        Run(vm, "ProcessFanCurves", hardware.GetCurrentTelemetry());
        Assert.AreEqual(40, vm.AppliedCpuFanPercent);
        Assert.AreEqual(50, vm.AppliedGpuFanPercent);
        Run(vm, "ReleaseCustomFans");
        CollectionAssert.AreEqual(new[] { "mode:1", "curve:40/50", "mode:0" }, hardware.Calls);
        Assert.IsNull(vm.AppliedGpuFanPercent);
    }
    private sealed class RecordingFans : MockHardwareService, IHardwareService
    {
        public List<string> Calls { get; } = new();
        public new bool SetFanMode(byte cpuMode, byte gpuMode) { Calls.Add($"mode:{cpuMode}"); return true; }
        public new bool SetFanSpeeds(byte cpuPercentage, byte gpuPercentage) { Calls.Add($"curve:{cpuPercentage}/{gpuPercentage}"); return true; }
    }
}
