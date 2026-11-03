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
        Assert.AreEqual("N/A", TelemetryText.Format(null, "W"));
        Assert.AreEqual("N/A", TelemetryText.Format(-1, "MHz"));
        Assert.AreEqual("N/A", TelemetryText.Format(double.NaN, "%"));
        Assert.AreEqual("N/A", TelemetryText.Format(double.PositiveInfinity, "W"));
        Assert.AreEqual("N/A", TelemetryText.Format(0, "°C", allowZero: false));
        Assert.AreEqual("0RPM", TelemetryText.Format(0, "RPM"));
        Assert.AreEqual("0%", TelemetryText.Format(0, "%"));
        Assert.AreEqual("52°C", TelemetryText.Format(52, "°C", allowZero: false));
    }

    [TestMethod]
    public void MainWindowDistinguishesMissingSensorsFromStoppedFans()
    {
        var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        vm.CpuTemp = -1;
        vm.GpuTemp = -1;
        vm.CpuRpm = -1;
        vm.GpuRpm = 0;
        Assert.AreEqual("N/A", vm.CpuTemperatureText);
        Assert.AreEqual("N/A", vm.GpuTemperatureText);
        Assert.AreEqual("N/A", vm.CpuFanText);
        Assert.AreEqual("0 RPM", vm.GpuFanText);
        vm.CpuTemp = 53;
        Assert.AreEqual("53°C", vm.CpuTemperatureText);
    }
}
