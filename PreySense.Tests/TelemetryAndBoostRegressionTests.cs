using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using PreySense.Core.Configuration;
using PreySense.Core.Services;
using PreySense.Frontend.ViewModels;
using PreySense.Mode;

namespace PreySense.Tests;

[TestClass]
[DoNotParallelize]
public class TelemetryAndBoostRegressionTests
{
    private static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);

    private static MainViewModel Create(RecordingHardware hardware)
    {
        var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        Set(vm, "<AvailableRefreshRates>k__BackingField", new System.Collections.ObjectModel.ObservableCollection<string>());
        Set(vm, "_hw", hardware);
        Set(vm, "_dispatcher", Dispatcher.CurrentDispatcher);
        Set(vm, "_fanControlGate", new object());
        Set(vm, "_displaySettingsDebounceLock", new object());
        Set(vm, "_cpuFanFollower", new FanCurveFollower());
        Set(vm, "_gpuFanFollower", new FanCurveFollower());
        Set(vm, "_runningFanProfileMode", (byte)1);
        Set(vm, "_runningFanProfile", new FanCurveProfile { ControlMode = FanControlMode.Auto });
        Set(vm, "_autoFanModeApplied", true);
        return vm;
    }

    private static Task RunOnSensorThread(Action action)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        new Thread(() =>
        {
            try { action(); completed.SetResult(); }
            catch (Exception ex) { completed.SetException(ex); }
        }) { IsBackground = true }.Start();
        return completed.Task;
    }

    [TestMethod]
    public async Task HiddenCustomFansReadOnlyTemperaturesAndKeepFollowingTheCurve()
    {
        var hardware = new RecordingHardware();
        var vm = Create(hardware);
        Set(vm, "_runningFanProfile", new FanCurveProfile
        {
            ControlMode = FanControlMode.Custom,
            Cpu = Enumerable.Repeat(40, FanCurveProfile.PointCount).ToArray(),
            Gpu = Enumerable.Repeat(50, FanCurveProfile.PointCount).ToArray()
        });
        Set(vm, "_fanOverrideActive", true);
        Set(vm, "_fanSnapshotsSubscribed", true);
        await RunOnSensorThread(() => { TelemetryCoordinator.Poll(); TelemetryCoordinator.Poll(); });
        typeof(MainViewModel).GetMethod("OnFanSnapshot", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(vm, new object[] { TelemetrySnapshot.Empty with { IsValid = true, IsFanOnly = true, CpuTemp = 55, GpuTemp = 45 } });
        Assert.AreEqual(0, hardware.Reads);
        Assert.AreEqual(0, hardware.TemperatureReads, "Fan control must not perform independent reads.");
        Assert.AreEqual(0, hardware.AutoWrites);
        Assert.AreEqual(40, vm.AppliedCpuFanPercent);
        Assert.AreEqual(50, vm.AppliedGpuFanPercent);
    }

    [TestMethod]
    public void OnOffBoostPersistsThroughProfileManager()
    {
        string directory = Path.Combine(Path.GetTempPath(), "PreySenseBoost-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var previousStore = ProfileManager.ConfigurationStore;
        try
        {
            var store = new ConfigurationService(Path.Combine(directory, ConfigurationService.ConfigFileName));
            ProfileManager.ConfigurationStore = store;

            var vm = Create(new RecordingHardware());
            Set(vm, "_activePowerMode", (byte)1);
            vm.SetCpuBoostForModeIndex(4, 2);
            Assert.AreEqual(2, vm.CpuBoostModeForModeIndex(4));

            var profile = ProfileManager.LoadProfile(5);
            profile.CpuBoost = 2;
            profile.ExtendedCpuBoost = 2;
            Assert.IsTrue(ProfileManager.SaveProfile(profile));
            Assert.AreEqual(2, store.GetCurrent().Tuning.Turbo.CpuBoostMode);
            Assert.IsTrue(store.GetCurrent().Tuning.Turbo.CpuBoost);

            vm.SetCpuBoostForModeIndex(4, 0);
            Assert.AreEqual(0, vm.CpuBoostModeForModeIndex(4));
        }
        finally
        {
            ProfileManager.ConfigurationStore = previousStore;
            try { Directory.Delete(directory, true); } catch { }
        }
    }

    [TestMethod]
    public async Task TelemetrySnapshotDoesNotPollDisplayOrDds()
    {
        var hardware = new RecordingHardware();
        var vm = Create(hardware);

        hardware.DdsReads = 0;
        hardware.RefreshRateReads = 0;

        var onSnapshot = typeof(MainViewModel).GetMethod("OnFanSnapshot", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var snapshot = TelemetrySnapshot.Empty with
        {
            CpuTemp = 55,
            GpuTemp = 45,
            CpuRpm = 2000,
            GpuRpm = 2200,
            CpuPkgPowerWatts = 25f,
            CpuUsage = 10,
            CpuMaxCoreUsage = 15,
            CpuPCoreUsage = 12,
            CpuECoreUsage = 8,
            GpuClockMhz = 1500,
            GpuPowerWatts = 30f,
            RamUsage = 40,
            BatteryPercent = 90,
            IsAcConnected = true,
            PowerSource = PowerSourceKind.Ac,
            IsValid = true
        };

        await RunOnSensorThread(() => onSnapshot.Invoke(vm, new object[] { snapshot }));

        Assert.AreEqual(0, hardware.DdsReads, "Telemetry snapshot must not poll DDS status.");
        Assert.AreEqual(0, hardware.RefreshRateReads, "Telemetry snapshot must not poll display refresh rate.");
    }

    [TestMethod]
    public async Task EventDrivenRefreshUpdatesDisplayAndDdsState()
    {
        var hardware = new RecordingHardware();
        var vm = Create(hardware);

        hardware.DdsReads = 0;
        hardware.RefreshRateReads = 0;

        await vm.RefreshDisplayAndDdsStateAsync();

        Assert.IsGreaterThanOrEqualTo(1, hardware.DdsReads, "RefreshDisplayAndDdsStateAsync must query DDS status.");
        Assert.IsGreaterThanOrEqualTo(1, hardware.RefreshRateReads, "RefreshDisplayAndDdsStateAsync must query display refresh rate.");
    }

    private sealed class RecordingHardware : MockHardwareService, IHardwareService
    {
        public int Reads, TemperatureReads, AutoWrites, DdsReads, RefreshRateReads;
        public new HardwareTelemetry GetCurrentTelemetry() { Reads++; return base.GetCurrentTelemetry(); }
        public HardwareTelemetry GetFanTemperatures() { TemperatureReads++; return base.GetCurrentTelemetry(); }
        public new bool SetFanMode(byte cpuMode, byte gpuMode) { if (cpuMode == 0 && gpuMode == 0) AutoWrites++; return true; }
        public new PreySense.Gpu.DdsStatus? GetDdsStatus() { DdsReads++; return base.GetDdsStatus(); }
        public new int GetDisplayRefreshRate() { RefreshRateReads++; return base.GetDisplayRefreshRate(); }
    }
}
