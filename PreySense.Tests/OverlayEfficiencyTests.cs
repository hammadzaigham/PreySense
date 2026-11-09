using System.Reflection;
using PreySense.Core.Configuration;
using PreySense.Core.Services;
using PreySense.Overlay;

namespace PreySense.Tests;

[TestClass]
[DoNotParallelize]
public class OverlayEfficiencyTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [TestMethod]
    public void FpsOnlyOverlayConsumesTheSharedFrameSampleWithoutCopyingOtherSensors()
    {
        TelemetryCoordinator.Reset();
        HardwareControl.ResetMetrics();
        using var overlay = new HardwareOverlay();
        try
        {
            Set(overlay, "_active", true);
            TelemetryCoordinator.SetDemand(TelemetryDemand.MainWindow, true);
            Assert.AreNotEqual(TelemetryMetrics.None, TelemetryCoordinator.RequestedMetrics);
            Deliver(overlay, TelemetrySnapshot.Empty with { IsValid = true, CpuTemp = 67, FramesPerSecond = 60 });
            Assert.IsNull(HardwareControl.cpuTemp, "An FPS-only overlay must ignore another consumer's sensor fields.");
            Assert.AreEqual(60, typeof(HardwareOverlay).GetField("_currentFps", PrivateInstance)!.GetValue(overlay));
        }
        finally { TelemetryCoordinator.Reset(); HardwareControl.ResetMetrics(); }
    }

    [TestMethod]
    public void SensorOverlayPaintsFromSnapshotInsteadOfDuplicateHeartbeatWork()
    {
        TelemetryCoordinator.Reset();
        HardwareControl.ResetMetrics();
        using var overlay = new HardwareOverlay();
        try
        {
            Set(overlay, "_active", true);
            Set(overlay, "_selectedMetrics", TelemetryMetrics.CpuTemperature);
            Deliver(overlay, TelemetrySnapshot.Empty with { IsValid = true, CpuTemp = 67 });
            Assert.AreEqual(67f, HardwareControl.cpuTemp);
        }
        finally { TelemetryCoordinator.Reset(); HardwareControl.ResetMetrics(); }
    }

    [TestMethod]
    public void RestoringOverlayPlacementDoesNotWriteConfiguration()
    {
        var store = ConfigurationService.Instance;
        string path = store.ConfigPath;
        byte[]? before = File.Exists(path) ? File.ReadAllBytes(path) : null;
        DateTime beforeWrite = File.GetLastWriteTimeUtc(path);
        using var overlay = new HardwareOverlay();
        typeof(HardwareOverlay).GetMethod("RestorePosition", PrivateInstance)!.Invoke(overlay,
            new object[] { new OverlayConfig { PositionHorizontal = "Right", PositionVertical = "Bottom" } });
        Assert.AreEqual(before != null, File.Exists(path));
        if (before != null) CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
        Assert.AreEqual(beforeWrite, File.GetLastWriteTimeUtc(path));
    }

    [TestMethod]
    public void PositionPreferenceWhileOverlayStoppedDoesNotConstructAnOverlay()
    {
        var store = ConfigurationService.Instance;
        var original = store.GetCurrent();
        var field = typeof(HardwareOverlayManager).GetField("_overlay", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.IsNull(field.GetValue(null));
        try
        {
            HardwareOverlayManager.Reposition("Right", "Bottom");
            var overlay = store.GetCurrent().Overlay;
            Assert.AreEqual("Right", overlay.PositionHorizontal);
            Assert.AreEqual("Bottom", overlay.PositionVertical);
            Assert.IsNull(field.GetValue(null), "Changing a stopped overlay's preferences must not allocate a native overlay.");
        }
        finally { store.Save(original); }
    }

    [TestMethod]
    public void FrameCounterAcceptsLastAsyncBatchButExpiresAfterTwoSharedIntervals()
    {
        using var monitor = new EtwFpsMonitor();
        var fields = BindingFlags.Instance | BindingFlags.NonPublic;
        var frames = (long[])typeof(EtwFpsMonitor).GetField("_frameTimes", fields)!.GetValue(monitor)!;
        long frequency = System.Diagnostics.Stopwatch.Frequency;
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        long newest = now - (long)(frequency * 1.1);
        frames[0] = newest - frequency / 50;
        frames[1] = newest - frequency / 100;
        frames[2] = newest;
        typeof(EtwFpsMonitor).GetField("_framesFilled", fields)!.SetValue(monitor, 3);
        typeof(EtwFpsMonitor).GetField("_frameHead", fields)!.SetValue(monitor, 3);
        Assert.IsTrue(double.IsFinite(monitor.SampleFps()), "Last interval's asynchronous ETW batch is still usable.");
        for (int i = 0; i < 3; i++) frames[i] -= frequency * 2;
        Assert.IsTrue(double.IsNaN(monitor.SampleFps()), "An older batch must become unavailable, not remain frozen.");
    }

    private static void Set(HardwareOverlay overlay, string field, object value) =>
        typeof(HardwareOverlay).GetField(field, PrivateInstance)!.SetValue(overlay, value);
    private static void Deliver(HardwareOverlay overlay, TelemetrySnapshot snapshot) =>
        typeof(HardwareOverlay).GetMethod("OnTelemetrySnapshot", PrivateInstance)!.Invoke(overlay, new object[] { snapshot });
}
