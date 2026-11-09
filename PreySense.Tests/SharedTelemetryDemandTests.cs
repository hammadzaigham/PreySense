using System.Diagnostics;
using System.Reflection;
using PreySense.Core.Services;
using PreySense.Gpu;

namespace PreySense.Tests;

[TestClass]
[DoNotParallelize]
public class SharedTelemetryDemandTests
{
    [TestMethod]
    public void VisibleMainOverlayAndCustomFansShareOnePhysicalPassAndFrameSnapshotPerSecond()
    {
        TelemetryCoordinator.Reset();
        try
        {
            long ticks = 1;
            int frames = 0;
            var sources = new Sources();
            var first = new List<TelemetrySnapshot>();
            var second = new List<TelemetrySnapshot>();
            var third = new List<TelemetrySnapshot>();
            TelemetryCoordinator.ConfigureMetricSampler((metrics, fanOnly) => TelemetryCollector.Read(metrics, sources, fanOnly), () => ticks);
            TelemetryCoordinator.ConfigureFrameSampler(() => { frames++; return new(60, 1000d / 60, 4242); });
            TelemetryCoordinator.SnapshotAvailable += first.Add;
            TelemetryCoordinator.SnapshotAvailable += second.Add;
            TelemetryCoordinator.SnapshotAvailable += third.Add;
            TelemetryCoordinator.SetDemand(TelemetryDemand.MainWindow, true);
            TelemetryCoordinator.SetOverlayMetrics(TelemetryMetrics.GpuTemperature | TelemetryMetrics.CpuFanRpm);
            TelemetryCoordinator.SetDemand(TelemetryDemand.Overlay, true);
            TelemetryCoordinator.SetDemand(TelemetryDemand.FanController, true);
            TelemetryCoordinator.SetOverlayActive(true);
            TelemetryCoordinator.Poll();
            TelemetryCoordinator.Poll();
            Assert.AreEqual(1, sources.CpuTemperatures);
            Assert.AreEqual(1, sources.GpuTemperatures);
            Assert.AreEqual(1, sources.Fans);
            Assert.AreEqual(1, frames);
            Assert.HasCount(1, first);
            Assert.AreEqual(first[0], second[0]);
            Assert.AreEqual(first[0], third[0]);
            Assert.AreEqual(60d, first[0].FramesPerSecond);
            Assert.AreEqual(4242, first[0].FrameProcessId);
            ticks += Stopwatch.Frequency;
            TelemetryCoordinator.Poll();
            Assert.AreEqual(2, sources.CpuTemperatures);
            Assert.AreEqual(2, sources.Fans);
            Assert.AreEqual(2, frames);

            TelemetryCoordinator.SetDemand(TelemetryDemand.MainWindow, false);
            TelemetryCoordinator.SetDemand(TelemetryDemand.Overlay, false);
            TelemetryCoordinator.SetOverlayActive(false);
            ticks += Stopwatch.Frequency;
            TelemetryCoordinator.Poll();
            Assert.AreEqual(3, sources.CpuTemperatures);
            Assert.AreEqual(3, sources.GpuTemperatures);
            Assert.AreEqual(2, sources.Fans, "Custom fan control requests temperatures, not RPM.");
            Assert.AreEqual(2, frames, "A hidden/unselected FPS consumer requests no frame read.");
            TelemetryCoordinator.SetDemand(TelemetryDemand.FanController, false);
            ticks += Stopwatch.Frequency;
            TelemetryCoordinator.Poll();
            Assert.AreEqual(3, sources.CpuTemperatures);
            Assert.IsFalse(TimerRunning());
        }
        finally { TelemetryCoordinator.Reset(); }
    }

    [TestMethod]
    public void FpsOnlyUsesTheSharedSnapshotWithoutCallingAnyHardwareSampler()
    {
        TelemetryCoordinator.Reset();
        try
        {
            int hardwareReads = 0, frameReads = 0;
            TelemetryCoordinator.ConfigureMetricSampler((_, _) => { hardwareReads++; return TelemetrySnapshot.Empty; });
            TelemetryCoordinator.ConfigureFrameSampler(() => { frameReads++; return new(120, 1000d / 120, 42); });
            TelemetryCoordinator.SetOverlayActive(true);
            TelemetryCoordinator.Poll();
            Assert.AreEqual(0, hardwareReads);
            Assert.AreEqual(1, frameReads);
            Assert.AreEqual(120d, TelemetryCoordinator.CurrentSnapshot.FramesPerSecond);
        }
        finally { TelemetryCoordinator.Reset(); }
    }

    [TestMethod]
    public void EmptySensorSelectionStopsTimerAndChangingOnlyMetricsRestartsIt()
    {
        TelemetryCoordinator.Reset();
        try
        {
            TelemetryCoordinator.ConfigureMetricSampler((_, _) => TelemetrySnapshot.Empty);
            TelemetryCoordinator.SetDemand(TelemetryDemand.Overlay, true);
            TelemetryCoordinator.SetOverlayMetrics(TelemetryMetrics.None);
            Assert.IsFalse(TimerRunning());
            TelemetryCoordinator.SetOverlayMetrics(TelemetryMetrics.CpuTemperature);
            Assert.IsTrue(TimerRunning());
            TelemetryCoordinator.SetOverlayMetrics(TelemetryMetrics.None);
            Assert.IsFalse(TimerRunning());
            TelemetryCoordinator.SetOverlayActive(true);
            Assert.IsTrue(TimerRunning());
            TelemetryCoordinator.SetOverlayActive(false);
            Assert.IsFalse(TimerRunning());
        }
        finally { TelemetryCoordinator.Reset(); }
    }

    [TestMethod]
    public void FrameFailureDoesNotDiscardSuccessfulTemperatureReadings()
    {
        TelemetryCoordinator.Reset();
        try
        {
            TelemetryCoordinator.ConfigureMetricSampler((_, _) => TelemetrySnapshot.Empty with { IsValid = true, CpuTemp = 60 });
            TelemetryCoordinator.ConfigureFrameSampler(() => throw new InvalidOperationException("Simulated unavailable frame source"));
            TelemetryCoordinator.SetDemand(TelemetryDemand.MainWindow, true);
            TelemetryCoordinator.SetOverlayActive(true);
            TelemetrySnapshot delivered = default;
            TelemetryCoordinator.SnapshotAvailable += snapshot => delivered = snapshot;
            TelemetryCoordinator.Poll();
            Assert.AreEqual(60, delivered.CpuTemp);
            Assert.IsNull(delivered.FramesPerSecond);
        }
        finally { TelemetryCoordinator.Reset(); }
    }

    [TestMethod]
    public void FrameOnlyUsesCoordinatorClockAndDeselectionClearsCachedFrameFields()
    {
        TelemetryCoordinator.Reset();
        try
        {
            long ticks = 1;
            TelemetryCoordinator.ConfigureMetricSampler((_, fanOnly) => TelemetrySnapshot.Empty with
                { IsValid = true, IsFanOnly = fanOnly, CpuTemp = 60, ThermalTimestampTicks = ticks }, () => ticks);
            TelemetryCoordinator.ConfigureFrameSampler(() => new(60, 1000d / 60, 42));
            TelemetryCoordinator.SetOverlayActive(true);
            TelemetryCoordinator.Poll();
            Assert.AreEqual(ticks, TelemetryCoordinator.CurrentSnapshot.TimestampTicks);
            TelemetryCoordinator.SetOverlayActive(false);
            TelemetryCoordinator.SetDemand(TelemetryDemand.FanController, true);
            ticks += Stopwatch.Frequency;
            TelemetryCoordinator.Poll();
            var snapshot = TelemetryCoordinator.CurrentSnapshot;
            Assert.IsNull(snapshot.FramesPerSecond);
            Assert.IsNull(snapshot.FrameTimeMilliseconds);
            Assert.IsNull(snapshot.FrameProcessId);
        }
        finally { TelemetryCoordinator.Reset(); }
    }

    private static bool TimerRunning() => ((System.Timers.Timer)typeof(TelemetryCoordinator)
        .GetField("MasterTimer", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!).Enabled;

    private sealed class Sources : ITelemetrySources
    {
        public int CpuTemperatures, GpuTemperatures, Fans;
        public int? ReadCpuTemperature() { CpuTemperatures++; return 60; }
        public (int? Cpu, int? Gpu) ReadFanRpms(bool cpu, bool gpu) { Fans++; return (cpu ? 2500 : null, gpu ? 2300 : null); }
        public NvidiaGpuControl.Metrics ReadGpu(TelemetryMetrics metrics)
        {
            Assert.AreEqual(TelemetryMetrics.GpuTemperature, metrics);
            GpuTemperatures++;
            return new() { Temperature = 50 };
        }
        public CpuTelemetrySnapshot? ReadCpuUsage() => throw new AssertFailedException("Unrequested CPU usage read.");
        public float? ReadCpuVoltage() => throw new AssertFailedException("Unrequested CPU voltage read.");
        public float? ReadCpuPower() => throw new AssertFailedException("Unrequested CPU power read.");
        public (int? Performance, int? Efficiency) ReadCpuClocks() => throw new AssertFailedException("Unrequested CPU clock read.");
        public (int? Percent, int? UsedMb) ReadRamUsage() => throw new AssertFailedException("Unrequested RAM read.");
        public int? ReadRamSpeed() => throw new AssertFailedException("Unrequested RAM speed read.");
    }
}
