using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using PreySense.Core.Services;
using PreySense.Frontend.ViewModels;

namespace PreySense.Tests;

[TestClass]
[DoNotParallelize]
public class MainWindowTelemetryTests
{
    [TestMethod]
    public void ShowingAndHidingAddsAndRemovesOnlyMainWindowDemand()
    {
        TelemetryCoordinator.Reset();
        var vm = Create();
        try
        {
            vm.SetMainWindowTelemetryVisible(true);
            Assert.AreEqual(TelemetryCoordinator.MainWindowMetrics, TelemetryCoordinator.RequestedMetrics);
            vm.SetMainWindowTelemetryVisible(true);
            vm.SetMainWindowTelemetryVisible(false);
            Assert.AreEqual(TelemetryMetrics.None, TelemetryCoordinator.RequestedMetrics);
            Assert.IsFalse(TelemetryCoordinator.HasActiveDemand);
            Assert.AreEqual("N/A", vm.CpuTemperatureText);
        }
        finally { vm.SetMainWindowTelemetryVisible(false); TelemetryCoordinator.Reset(); }
    }

    [TestMethod]
    public void ReadingsPreserveUnavailableSensorsAndStoppedFansAndClearWhenHidden()
    {
        TelemetryCoordinator.Reset();
        var vm = Create();
        try
        {
            vm.SetMainWindowTelemetryVisible(true);
            vm.ApplyMainWindowTelemetry(TelemetrySnapshot.Empty with
            {
                IsValid = true, CpuTemp = 67, GpuTemp = null, CpuRpm = 0, GpuRpm = 3500, DisplayGpuName = "Intel Graphics"
            });
            Assert.AreEqual("67°C", vm.CpuTemperatureText);
            Assert.AreEqual("Intel Graphics", vm.ActiveGpuDisplayMode);
            Assert.AreEqual("N/A", vm.GpuTemperatureText);
            Assert.AreEqual("0 RPM", vm.CpuFanText);
            Assert.AreEqual("3500 RPM", vm.GpuFanText);
            vm.SetMainWindowTelemetryVisible(false);
            vm.ApplyMainWindowTelemetry(TelemetrySnapshot.Empty with { IsValid = true, CpuTemp = 99 });
            Assert.AreEqual("N/A", vm.CpuTemperatureText);
            vm.SetMainWindowTelemetryVisible(true);
            Assert.AreEqual("N/A", vm.GpuFanText);
            Assert.AreEqual("N/A", vm.ActiveGpuDisplayMode);
        }
        finally { vm.SetMainWindowTelemetryVisible(false); TelemetryCoordinator.Reset(); }
    }

    [TestMethod]
    public void HidingMainWindowDoesNotDisableOverlayOrRequiredFanTemperatures()
    {
        TelemetryCoordinator.Reset();
        var vm = Create();
        try
        {
            TelemetryCoordinator.SetOverlayMetrics(TelemetryMetrics.CpuPower);
            TelemetryCoordinator.SetDemand(TelemetryDemand.Overlay, true);
            TelemetryCoordinator.SetDemand(TelemetryDemand.FanController, true);
            vm.SetMainWindowTelemetryVisible(true);
            Assert.AreEqual(TelemetryCoordinator.MainWindowMetrics | TelemetryMetrics.CpuPower, TelemetryCoordinator.RequestedMetrics);
            vm.SetMainWindowTelemetryVisible(false);
            Assert.AreEqual(TelemetryMetrics.CpuPower | TelemetryMetrics.Temperatures, TelemetryCoordinator.RequestedMetrics);
        }
        finally { vm.SetMainWindowTelemetryVisible(false); TelemetryCoordinator.Reset(); }
    }

    private static MainViewModel Create()
    {
        var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        typeof(MainViewModel).GetField("_dispatcher", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(vm, Dispatcher.CurrentDispatcher);
        return vm;
    }
}
