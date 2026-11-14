using System.Runtime.CompilerServices;
using PreySense.Frontend.ViewModels;
using PreySense.Mode;
using PreySense.Overlay;

namespace PreySense.Tests;

[TestClass]
public class TemperatureAndMissingReadingTests
{
    [TestMethod]
    public void PackageTemperatureUsesTheReportedTargetAndValidDigitalReadout()
    {
        Assert.AreEqual(53, IntelCpuTemperature.DecodePackageTemperature(105UL << 16, (1UL << 31) | (52UL << 16)));
        Assert.AreEqual(100, IntelCpuTemperature.DecodePackageTemperature(100UL << 16, 1UL << 31));
        Assert.IsNull(IntelCpuTemperature.DecodePackageTemperature(100UL << 16, 20UL << 16));
        Assert.IsNull(IntelCpuTemperature.DecodePackageTemperature(0, 1UL << 31));
        Assert.IsNull(IntelCpuTemperature.DecodePackageTemperature(100UL << 16, (1UL << 31) | (110UL << 16)));
    }

    [TestMethod]
    public void MissingOverlayValuesAreNotFormattedAsZero()
    {
        Assert.AreEqual("—", TelemetryText.Format(null, "W"));
        Assert.AreEqual("—", TelemetryText.Format(-1, "MHz"));
        Assert.AreEqual("—", TelemetryText.Format(double.NaN, "%"));
        Assert.AreEqual("—", TelemetryText.Format(double.PositiveInfinity, "W"));
        Assert.AreEqual("—", TelemetryText.Format(0, "°C", allowZero: false));
        Assert.AreEqual("0RPM", TelemetryText.Format(0, "RPM"));
        Assert.AreEqual("0%", TelemetryText.Format(0, "%"));
        Assert.AreEqual("52°C", TelemetryText.Format(52, "°C", allowZero: false));
        Assert.AreEqual("58.7°C", TelemetryText.Format(58.71875f, "°C", 1, allowZero: false));
        Assert.AreEqual("64.0°C", TelemetryText.Format(64.0f, "°C", 1, allowZero: false));
        Assert.AreEqual("—", TelemetryText.Format(null, "°C", 1, allowZero: false));
    }

    [TestMethod]
    public void NvapiThermal_DecodeHotspotSample_DecodesValidFixedPoint()
    {
        // 0x40003AB8: bit 30 set, payload = 0x3AB8 = 15032. 15032 / 256.0 = 58.71875
        float? decoded = PreySense.Gpu.NvapiThermal.DecodeHotspotSample(0x40003AB8);
        Assert.IsNotNull(decoded);
        Assert.AreEqual(58.71875f, decoded.Value, 0.0001f);

        // Missing/bad fixed-point samples return null
        Assert.IsNull(PreySense.Gpu.NvapiThermal.DecodeHotspotSample(0));
        Assert.IsNull(PreySense.Gpu.NvapiThermal.DecodeHotspotSample(0x3AB8)); // bit 30 not set
        Assert.IsNull(PreySense.Gpu.NvapiThermal.DecodeHotspotSample(0xbadf0000)); // bad food sentinel
        Assert.IsNull(PreySense.Gpu.NvapiThermal.DecodeHotspotSample(0x40000000)); // 0 raw value
    }

    [TestMethod]
    public void TelemetryGraphValue_ReadsHotspotAndMemoryTemperature()
    {
        var snapshot = PreySense.Core.Services.TelemetrySnapshot.Empty with
        {
            GpuHotspotTemp = 58.71875f,
            GpuMemoryTemp = 64.0f
        };

        Assert.AreEqual(58.71875f, TelemetryGraphValue.Read(snapshot, "Hotspot", true), 0.0001f);
        Assert.AreEqual(64.0f, TelemetryGraphValue.Read(snapshot, "Memory Temperature", true), 0.0001f);

        var empty = PreySense.Core.Services.TelemetrySnapshot.Empty;
        Assert.IsTrue(float.IsNaN(TelemetryGraphValue.Read(empty, "Hotspot", true)));
        Assert.IsTrue(float.IsNaN(TelemetryGraphValue.Read(empty, "Memory Temperature", true)));
    }

}
