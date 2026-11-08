using PreySense.Core.Services;
using PreySense.Gpu;
using PreySense.Overlay;

namespace PreySense.Tests;

[TestClass]
public class GpuClockTelemetryTests
{
    [TestMethod]
    [DataRow("P-Core Usage")]
    [DataRow("E-Core Usage")]
    [DataRow("Max Core")]
    [DataRow("Max Core Usage")]
    [DataRow("P-Core")]
    [DataRow("E-Core")]
    public void RemovedCpuMetrics_DoNotRequestSensors(string metric)
    {
        Assert.AreEqual(TelemetryMetrics.None, TelemetrySelection.Cpu(metric));
        Assert.AreEqual("None", TelemetrySelection.NormalizeCpu(metric));
    }

    [TestMethod]
    [DataRow(2070499u, 2070)]
    [DataRow(2070500u, 2071)]
    [DataRow(2070000u, 2070)]
    [DataRow(uint.MaxValue, 4294967)]
    public void NvapiKilohertz_RoundsToNearestOverlayMegahertz(uint khz, int mhz) =>
        Assert.AreEqual(mhz, GpuClockReading.FromKilohertz(true, khz));

    [TestMethod]
    public void MissingNvapiSample_RemainsUnavailable()
    {
        Assert.IsNull(GpuClockReading.FromKilohertz(false, 2070000));
        Assert.IsNull(GpuClockReading.FromKilohertz(true, 0));
    }

    [TestMethod]
    public void CpuAndGpuClockGraphs_UseTheirOwnSources()
    {
        var snapshot = TelemetrySnapshot.Empty with
        {
            GpuActiveClockMhz = 2070, GpuClockMhz = 1980,
            CpuActiveFrequencyMhz = 4800, CpuEffectiveFrequencyMhz = 3200,
            GpuPowerWatts = 88.5f
        };
        Assert.AreEqual(2070f, TelemetryGraphValue.Read(snapshot, "Active Clock", true));
        Assert.IsTrue(float.IsNaN(TelemetryGraphValue.Read(snapshot, "Effective Clock", true)));
        Assert.AreEqual(2070f, TelemetryGraphValue.Read(snapshot, "Clock", true));
        Assert.AreEqual(4800f, TelemetryGraphValue.Read(snapshot, "Active Clock", false));
        Assert.AreEqual(3200f, TelemetryGraphValue.Read(snapshot, "Effective Clock", false));
        Assert.AreEqual(88.5f, TelemetryGraphValue.Read(snapshot, "Power", true));
    }

    [TestMethod]
    public void StaleTelemetry_ExpiresBothGpuClockSources()
    {
        var snapshot = TelemetrySnapshot.Empty with
        {
            TimestampTicks = 1, GpuActiveClockMhz = 2070, GpuClockMhz = 1980
        };
        var expired = snapshot.ExpireFullTelemetryIfStale(System.Diagnostics.Stopwatch.Frequency * 10);
        Assert.IsNull(expired.GpuActiveClockMhz);
        Assert.IsNull(expired.GpuClockMhz);
    }
}
