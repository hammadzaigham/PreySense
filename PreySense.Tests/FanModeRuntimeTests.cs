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
        Set(vm, "_dispatcher", System.Windows.Threading.Dispatcher.CurrentDispatcher);
        Set(vm, "_fanControlGate", new object());
        Set(vm, "_cpuFanFollower", new FanCurveFollower());
        Set(vm, "_gpuFanFollower", new FanCurveFollower());
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
    [TestMethod]
    public void HotSamplesUseSavedCurveAndMissingSamplesDoNotSwitchToAuto()
    {
        var hardware = new RecordingFans();
        var vm = Create(hardware);
        Select(vm, FanControlMode.Custom);
        var normal = hardware.GetCurrentTelemetry();
        Run(vm, "ProcessFanCurves", normal with { CpuTemp = 99, GpuTemp = 90 });
        Run(vm, "ProcessFanCurves", normal with { CpuTemp = -1, GpuTemp = -1 });
        Run(vm, "ProcessFanCurves", normal);
        CollectionAssert.AreEqual(new[] { "curve:40/50" }, hardware.Calls);
        Assert.AreEqual(0, hardware.PowerModeReads);
        Assert.AreEqual(40, vm.AppliedCpuFanPercent);
    }

    [TestMethod]
    public void FailedFanWriteDoesNotFallbackOrRetryUntilExplicitReset()
    {
        var hardware = new RecordingFans { FailWrites = true };
        var vm = Create(hardware);
        Select(vm, FanControlMode.Custom);
        for (int i = 0; i < 10; i++)
            Run(vm, "ProcessFanCurves", hardware.GetCurrentTelemetry());
        CollectionAssert.AreEqual(new[] { "curve:40/50" }, hardware.Calls);
        Assert.IsNull(vm.AppliedCpuFanPercent);
        hardware.FailWrites = false;
        Select(vm, FanControlMode.Custom);
        Run(vm, "ProcessFanCurves", hardware.GetCurrentTelemetry());
        Assert.HasCount(2, hardware.Calls);
        Assert.AreEqual(40, vm.AppliedCpuFanPercent);
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(0)]
    public void CustomControlWritesFromCpuTemperatureWhenGpuIsAsleep(int gpuTemperature)
    {
        var hardware = new RecordingFans();
        var vm = Create(hardware);
        Select(vm, FanControlMode.Custom);
        var temperatures = hardware.GetCurrentTelemetry() with { CpuTemp = 78, GpuTemp = gpuTemperature, CpuRpm = -1, GpuRpm = -1 };
        Run(vm, "ProcessFanCurves", temperatures);
        Run(vm, "ProcessFanCurves", temperatures);
        CollectionAssert.AreEqual(new[] { "curve:40/50" }, hardware.Calls);
        Assert.AreEqual(0, hardware.PowerModeReads);
    }

    [TestMethod]
    [DoNotParallelize]
    public void OnlyCustomControlSubscribesToSharedTemperatureSamples()
    {
        TelemetryCoordinator.Reset();
        var hardware = new RecordingFans();
        var vm = Create(hardware);
        try
        {
            int samples = 0;
            TelemetryCoordinator.ConfigureSampler(() =>
            {
                samples++;
                return TelemetrySnapshot.Empty with { CpuTemp = 78, GpuTemp = -1, IsValid = true, IsFanOnly = true };
            });
            Select(vm, FanControlMode.Auto);
            Run(vm, "UpdateFanDemand");
            TelemetryCoordinator.Poll();
            Assert.AreEqual(0, samples);
            Select(vm, FanControlMode.Custom);
            Run(vm, "UpdateFanDemand");
            Assert.AreEqual(TelemetryMetrics.Temperatures, TelemetryCoordinator.RequestedMetrics);
            TelemetryCoordinator.Poll();
            Assert.AreEqual(1, samples);
            CollectionAssert.AreEqual(new[] { "curve:40/50" }, hardware.Calls);
            Select(vm, FanControlMode.Max);
            Run(vm, "UpdateFanDemand");
            Assert.AreEqual(TelemetryMetrics.None, TelemetryCoordinator.RequestedMetrics);
        }
        finally { Run(vm, "DisposeFanTelemetry"); TelemetryCoordinator.Reset(); }
    }

    private sealed class RecordingFans : MockHardwareService, IHardwareService
    {
        public List<string> Calls { get; } = new();
        public bool FailWrites { get; set; }
        public int PowerModeReads { get; private set; }
        public bool TryGetPowerMode(out byte mode) { PowerModeReads++; mode = 1; return true; }
        public new bool SetFanMode(byte cpuMode, byte gpuMode) { Calls.Add($"mode:{cpuMode}"); return !FailWrites; }
        public new bool SetFanSpeeds(byte cpuPercentage, byte gpuPercentage) { Calls.Add($"curve:{cpuPercentage}/{gpuPercentage}"); return !FailWrites; }
    }
}
