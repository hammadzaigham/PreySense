using System.Diagnostics;
using PreySense.Core.Services;
using PreySense.Frontend.Services;

namespace PreySense.Tests;

[TestClass]
[DoNotParallelize]
public class FanWindowTelemetryTests
{
    [TestMethod]
    public void VisibleReadoutsShareTheProducerAndReleaseOnlyTheirOwnDemand()
    {
        TelemetryCoordinator.Reset();
        try
        {
            long now = Stopwatch.Frequency;
            int samples = 0, delivered = 0;
            TelemetryMetrics requested = TelemetryMetrics.None;
            TelemetryCoordinator.ConfigureMetricSampler((metrics, fanOnly) =>
            {
                samples++;
                requested = metrics;
                return TelemetrySnapshot.Empty with { TimestampTicks = now, IsValid = true, CpuTemp = 65, GpuTemp = 50, CpuRpm = 3000, GpuRpm = 2100 };
            }, () => now);
            using var readouts = new FanWindowTelemetry();
            readouts.SnapshotAvailable += _ => delivered++;
            TelemetryCoordinator.SetDemand(TelemetryDemand.FanController, true);
            readouts.SetVisible(true);
            readouts.SetVisible(true);
            Assert.AreEqual(TelemetryMetrics.Temperatures | TelemetryMetrics.CpuFanRpm | TelemetryMetrics.GpuFanRpm,
                TelemetryCoordinator.RequestedMetrics);
            Assert.IsTrue(TelemetryCoordinator.HasUiDemand);
            TelemetryCoordinator.Poll();
            Assert.AreEqual(1, samples);
            Assert.AreEqual(1, delivered, "Repeated visibility notifications must not duplicate subscriptions.");
            Assert.AreEqual(TelemetryCoordinator.RequestedMetrics, requested);
            Assert.AreEqual(TelemetryMetrics.None, requested & TelemetryMetrics.DisplayAdapter);
            readouts.SetVisible(false);
            Assert.AreEqual(TelemetryDemand.FanController, TelemetryCoordinator.ActiveDemand);
            Assert.AreEqual(TelemetryMetrics.Temperatures, TelemetryCoordinator.RequestedMetrics);
            Assert.IsFalse(TelemetryCoordinator.HasUiDemand);
            now += Stopwatch.Frequency * 2;
            TelemetryCoordinator.Poll();
            Assert.AreEqual(1, delivered, "Hidden windows must unsubscribe without stopping custom fan control.");
            readouts.SetVisible(true);
            readouts.Dispose();
            Assert.AreEqual(TelemetryDemand.FanController, TelemetryCoordinator.ActiveDemand);
            readouts.SetVisible(true);
            Assert.AreEqual(TelemetryDemand.FanController, TelemetryCoordinator.ActiveDemand);
        }
        finally { TelemetryCoordinator.Reset(); }
    }

    [TestMethod]
    public void ReadoutsUseMeasuredRpmAndShowDashesForUnavailableValues()
    {
        var measured = FanWindowTelemetry.Format(TelemetrySnapshot.Empty with
        {
            IsValid = true, CpuTemp = 75, GpuTemp = 52, CpuRpm = 0, GpuRpm = 4780,
            CpuFanMaxRpm = 6400, GpuFanMaxRpm = 6400
        });
        Assert.AreEqual(("75°C", "0 RPM", "52°C", "4780 RPM"), measured);
        Assert.AreEqual(("—", "—", "—", "—"), FanWindowTelemetry.Format(TelemetrySnapshot.Empty));
        Assert.AreEqual(("—", "—", "—", "—"), FanWindowTelemetry.Format(TelemetrySnapshot.Empty with
            { IsValid = true, CpuTemp = 0, GpuTemp = 127, CpuRpm = -1, GpuRpm = null }));
    }
}
