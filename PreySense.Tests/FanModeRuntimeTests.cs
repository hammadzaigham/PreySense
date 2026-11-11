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
        Set(vm, "_fanFollower", new FanCurveFollower());
        return vm;
    }
    private static FanStage[] FixedDutyStages(int cpuPercent, int gpuPercent)
    {
        int cpuRpm = cpuPercent * FanStage.CpuMaxRpm / 100;
        int gpuRpm = gpuPercent * FanStage.GpuMaxRpm / 100;
        var stages = new FanStage[FanCurveProfile.StageCount];
        for (int i = 0; i < stages.Length; i++)
        {
            stages[i] = new FanStage
            {
                CpuRiseC = FanStage.DisabledTripCelsius,
                CpuFallC = FanStage.DisabledTripCelsius,
                GpuRiseC = FanStage.DisabledTripCelsius,
                GpuFallC = FanStage.DisabledTripCelsius,
                CpuTargetRpm = cpuRpm,
                GpuTargetRpm = gpuRpm
            };
        }
        return stages;
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
            ControlMode = mode,
            Stages = FixedDutyStages(40, 50)
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
        Assert.AreEqual(50, vm.AppliedCpuFanPercent);
        Assert.AreEqual(50, vm.AppliedGpuFanPercent);
        Run(vm, "ReleaseCustomFans");
        CollectionAssert.AreEqual(new[] { "mode:1", "curve:50/50", "mode:0" }, hardware.Calls);
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
        CollectionAssert.AreEqual(new[] { "curve:50/50" }, hardware.Calls);
        Assert.AreEqual(0, hardware.PowerModeReads);
        Assert.AreEqual(50, vm.AppliedCpuFanPercent);
    }

    [TestMethod]
    public void FailedFanWriteDoesNotFallbackOrRetryUntilExplicitReset()
    {
        var hardware = new RecordingFans { FailWrites = true };
        var vm = Create(hardware);
        Select(vm, FanControlMode.Custom);
        for (int i = 0; i < 10; i++)
            Run(vm, "ProcessFanCurves", hardware.GetCurrentTelemetry());
        CollectionAssert.AreEqual(new[] { "curve:50/50" }, hardware.Calls);
        Assert.IsNull(vm.AppliedCpuFanPercent);
        hardware.FailWrites = false;
        Select(vm, FanControlMode.Custom);
        Run(vm, "ProcessFanCurves", hardware.GetCurrentTelemetry());
        Assert.HasCount(2, hardware.Calls);
        Assert.AreEqual(50, vm.AppliedCpuFanPercent);
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task FailedMaxWriteIsReportedByApplyInsteadOfClaimingSuccess()
    {
        var store = PreySense.Core.Configuration.ConfigurationService.Instance;
        var original = store.GetCurrent();
        var hardware = new RecordingFans { FailWrites = true };
        var vm = Create(hardware);
        try
        {
            store.Update(config => config.Fans.Balanced.ControlMode = (int)FanControlMode.Max);
            await Assert.ThrowsAsync<InvalidOperationException>(() => vm.ApplyFanProfileAsync());
            CollectionAssert.AreEqual(new[] { "mode:1" }, hardware.Calls);
            for (int i = 0; i < 5; i++) Run(vm, "ProcessFanCurves", hardware.GetCurrentTelemetry());
            CollectionAssert.AreEqual(new[] { "mode:1" }, hardware.Calls);
        }
        finally { Run(vm, "DisposeFanTelemetry"); store.Save(original); }
    }

    [TestMethod]
    public void AutoFanWriteFailsImmediatelyWithoutTransientRetries()
    {
        var hardware = new RecordingFans { FailWrites = true };
        var vm = Create(hardware);
        Select(vm, FanControlMode.Auto);
        for (int i = 0; i < 5; i++)
            Run(vm, "ProcessFanCurves", hardware.GetCurrentTelemetry());
        CollectionAssert.AreEqual(new[] { "mode:0" }, hardware.Calls);
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
        CollectionAssert.AreEqual(new[] { "curve:50/50" }, hardware.Calls);
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
            CollectionAssert.AreEqual(new[] { "curve:50/50" }, hardware.Calls);
            Select(vm, FanControlMode.Max);
            Run(vm, "UpdateFanDemand");
            Assert.AreEqual(TelemetryMetrics.None, TelemetryCoordinator.RequestedMetrics);
        }
        finally { Run(vm, "DisposeFanTelemetry"); TelemetryCoordinator.Reset(); }
    }

    [TestMethod]
    [DoNotParallelize]
    public void SavingADraftKeepsTheCurrentCurveFollowingTemperature()
    {
        var store = PreySense.Core.Configuration.ConfigurationService.Instance;
        var original = store.GetCurrent();
        var hardware = new RecordingFans();
        var vm = Create(hardware);
        TelemetryCoordinator.Reset();
        try
        {
            Select(vm, FanControlMode.Custom);
            Run(vm, "UpdateFanDemand");
            var draft = FanCurveProfile.CreateDefault(1);
            draft.ControlMode = FanControlMode.Auto;
            vm.SaveFanProfile(1, draft);
            Assert.AreEqual(FanControlMode.Auto, vm.LoadFanProfile(1).ControlMode);
            Assert.AreEqual(TelemetryMetrics.Temperatures, TelemetryCoordinator.RequestedMetrics);
            TelemetryCoordinator.ConfigureSampler(() => TelemetrySnapshot.Empty with
            {
                CpuTemp = 78, IsValid = true, IsFanOnly = true
            });
            TelemetryCoordinator.Poll();
            CollectionAssert.AreEqual(new[] { "curve:50/50" }, hardware.Calls);
        }
        finally
        {
            Run(vm, "DisposeFanTelemetry");
            TelemetryCoordinator.Reset();
            store.Save(original);
        }
    }

    [TestMethod]
    [DoNotParallelize]
    public void FailedFanSavePreservesTheCommittedProfileAndCanBeRetried()
    {
        var store = PreySense.Core.Configuration.ConfigurationService.Instance;
        var original = store.GetCurrent();
        var vm = Create(new RecordingFans());
        try
        {
            store.Save(original);
            var draft = vm.LoadFanProfile(1);
            var priorMode = draft.ControlMode;
            draft.ControlMode = priorMode == FanControlMode.Max ? FanControlMode.Auto : FanControlMode.Max;
            using (var locked = System.IO.File.Open(store.ConfigPath, System.IO.FileMode.Open,
                       System.IO.FileAccess.Read, System.IO.FileShare.Read))
            {
                Assert.ThrowsExactly<System.IO.IOException>(() => vm.SaveFanProfile(1, draft));
                Assert.AreEqual(priorMode, vm.LoadFanProfile(1).ControlMode);
            }
            vm.SaveFanProfile(1, draft);
            Assert.AreEqual(draft.ControlMode, vm.LoadFanProfile(1).ControlMode);
        }
        finally { store.Save(original); }
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task FailedModeReadReleasesCustomFanDemand()
    {
        TelemetryCoordinator.Reset();
        var vm = Create(new RecordingFans { ModeReadable = false });
        try
        {
            Select(vm, FanControlMode.Custom);
            Run(vm, "UpdateFanDemand");
            Assert.AreEqual(TelemetryMetrics.Temperatures, TelemetryCoordinator.RequestedMetrics);
            await Assert.ThrowsAsync<InvalidOperationException>(() => vm.ApplyFanProfileAsync());
            Assert.AreEqual(TelemetryMetrics.None, TelemetryCoordinator.RequestedMetrics);
        }
        finally { Run(vm, "DisposeFanTelemetry"); TelemetryCoordinator.Reset(); }
    }

    private sealed class RecordingFans : MockHardwareService, IHardwareService
    {
        public List<string> Calls { get; } = new();
        public bool FailWrites { get; set; }
        public bool ModeReadable { get; init; } = true;
        public int PowerModeReads { get; private set; }
        public bool TryGetPowerMode(out byte mode) { PowerModeReads++; mode = 1; return ModeReadable; }
        public new bool SetFanMode(byte cpuMode, byte gpuMode) { Calls.Add($"mode:{cpuMode}"); return !FailWrites; }
        public new bool SetFanSpeeds(byte cpuPercentage, byte gpuPercentage) { Calls.Add($"curve:{cpuPercentage}/{gpuPercentage}"); return !FailWrites; }
    }
}
