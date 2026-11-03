using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using Microsoft.Win32;
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
        Set(vm, "_hw", hardware);
        Set(vm, "_dispatcher", Dispatcher.CurrentDispatcher);
        Set(vm, "_fanControlGate", new object());
        Set(vm, "_cpuFanFollower", new FanCurveFollower());
        Set(vm, "_gpuFanFollower", new FanCurveFollower());
        Set(vm, "_fanCoolingSafety", new FanCoolingSafety());
        Set(vm, "_runningFanProfileMode", (byte)1);
        Set(vm, "_runningFanProfile", new FanCurveProfile { ControlMode = FanControlMode.Auto });
        Set(vm, "_fanFallbackActive", true);
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
    [DataRow(false, 0)]
    [DataRow(true, 1)]
    public async Task WindowVisibilityControlsSensorReads(bool windowVisible, int expectedReads)
    {
        var hardware = new RecordingHardware();
        var vm = Create(hardware);
        vm.SetTelemetryPollingActive(windowVisible);
        await RunOnSensorThread(() => typeof(MainViewModel).GetMethod("PollTelemetry", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(vm, null));
        Assert.AreEqual(expectedReads, hardware.Reads);
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
        var poll = typeof(MainViewModel).GetMethod("PollTelemetry", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await RunOnSensorThread(() => { poll.Invoke(vm, null); poll.Invoke(vm, null); });
        Assert.AreEqual(0, hardware.Reads);
        Assert.AreEqual(2, hardware.TemperatureReads);
        Assert.AreEqual(0, hardware.AutoWrites);
        Assert.AreEqual(40, vm.AppliedCpuFanPercent);
        Assert.AreEqual(50, vm.AppliedGpuFanPercent);
    }

    [TestMethod]
    public void ExtendedBoostSurvivesReloadAndSimpleBoostProfileSaves()
    {
        const string path = @"SOFTWARE\PreySense\Profiles\Turbo";
        using var key = Registry.CurrentUser.CreateSubKey(path);
        var original = key.GetValueNames().ToDictionary(n => n, n => (Value: key.GetValue(n)!, Kind: key.GetValueKind(n)));
        try
        {
            var vm = Create(new RecordingHardware());
            Set(vm, "_isExtendedCpuBoostEnabled", true);
            Set(vm, "_activePowerMode", (byte)1); // Editing Turbo must not touch real powercfg.
            for (int boost = 0; boost <= 6; boost++)
            {
                vm.SetExtendedCpuBoostForModeIndex(4, boost);
                var profile = ProfileManager.LoadProfile(5);
                Assert.AreEqual(boost, profile.CpuBoost);
                Assert.AreEqual(boost, profile.ExtendedCpuBoost);
                // The simple editor can save an on/off choice independently.
                profile.CpuBoost = 1;
                Assert.IsTrue(ProfileManager.SaveProfile(profile));
                var reloaded = Create(new RecordingHardware());
                Set(reloaded, "_isExtendedCpuBoostEnabled", true);
                Assert.AreEqual(boost, reloaded.CpuBoostModeForModeIndex(4));
            }
        }
        finally
        {
            foreach (string name in key.GetValueNames())
                if (!original.ContainsKey(name)) key.DeleteValue(name);
            foreach (var (name, stored) in original) key.SetValue(name, stored.Value, stored.Kind);
        }
    }

    private sealed class RecordingHardware : MockHardwareService, IHardwareService
    {
        public int Reads, TemperatureReads, AutoWrites;
        public new HardwareTelemetry GetCurrentTelemetry() { Reads++; return base.GetCurrentTelemetry(); }
        public HardwareTelemetry GetFanTemperatures() { TemperatureReads++; return base.GetCurrentTelemetry(); }
        public new bool SetFanMode(byte cpuMode, byte gpuMode) { if (cpuMode == 0 && gpuMode == 0) AutoWrites++; return true; }
    }
}
