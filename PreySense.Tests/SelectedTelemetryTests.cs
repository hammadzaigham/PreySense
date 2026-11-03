using PreySense.Core.Services;
using PreySense.Gpu;

namespace PreySense.Tests;

[TestClass]
[DoNotParallelize]
public class SelectedTelemetryTests
{
    [TestMethod]
    [DataRow(TelemetryMetrics.CpuTemperature, "cpuTemp")]
    [DataRow(TelemetryMetrics.CpuUsage, "cpuUsage")]
    [DataRow(TelemetryMetrics.CpuVoltage, "cpuVoltage")]
    [DataRow(TelemetryMetrics.CpuPower, "cpuPower")]
    [DataRow(TelemetryMetrics.CpuClock, "cpuClock")]
    [DataRow(TelemetryMetrics.RamUsage, "ramUsage")]
    [DataRow(TelemetryMetrics.RamSpeed, "ramSpeed")]
    [DataRow(TelemetryMetrics.CpuFanRpm, "fans")]
    [DataRow(TelemetryMetrics.GpuFanRpm, "fans")]
    [DataRow(TelemetryMetrics.GpuTemperature, "gpu")]
    [DataRow(TelemetryMetrics.GpuUsage, "gpu")]
    [DataRow(TelemetryMetrics.GpuClock, "gpu")]
    [DataRow(TelemetryMetrics.GpuVoltage, "gpu")]
    [DataRow(TelemetryMetrics.GpuPower, "gpu")]
    [DataRow(TelemetryMetrics.GpuMemory, "gpu")]
    [DataRow(TelemetryMetrics.GpuMemoryClock, "gpu")]
    public void SingleSelectionCallsOnlyItsPhysicalSource(TelemetryMetrics metric, string source)
    {
        var sources = new RecordingSources();
        TelemetryCollector.Read(metric, sources);
        CollectionAssert.AreEqual(new[] { source }, sources.Calls);
        Assert.AreEqual(metric & TelemetryMetrics.Gpu, sources.GpuRequest);
        Assert.AreEqual(metric == TelemetryMetrics.CpuFanRpm, sources.CpuFanRequested);
        Assert.AreEqual(metric == TelemetryMetrics.GpuFanRpm, sources.GpuFanRequested);
    }

    [TestMethod]
    public void SelectedTemperaturesAndRpmsCollectOnlyThoseSources()
    {
        var sources = new RecordingSources();
        var snapshot = TelemetryCollector.Read((TelemetryMetrics.Temperatures | TelemetryMetrics.CpuFanRpm | TelemetryMetrics.GpuFanRpm), sources);
        CollectionAssert.AreEquivalent(new[] { "cpuTemp", "gpu", "fans" }, sources.Calls);
        Assert.AreEqual(TelemetryMetrics.GpuTemperature, sources.GpuRequest);
        Assert.IsTrue(sources.CpuFanRequested && sources.GpuFanRequested);
        Assert.IsNull(snapshot.CpuUsage);
        Assert.IsNull(snapshot.CpuPkgPowerWatts);
        Assert.IsNull(snapshot.CpuClockMhz);
        Assert.IsNull(snapshot.RamUsedMb);
        Assert.IsNull(snapshot.RamSpeedMhz);
    }

    [TestMethod]
    public void DuplicateSelectionsReadTheUnderlyingSourceOnlyOnce()
    {
        var metrics = TelemetrySelection.Cpu("Usage") | TelemetrySelection.Cpu("Max Core") |
            TelemetrySelection.Cpu("P-Core Usage") | TelemetrySelection.Cpu("E-Core Usage") |
            TelemetrySelection.Gpu("Power") | TelemetrySelection.Gpu("Power");
        var sources = new RecordingSources();
        TelemetryCollector.Read(metrics, sources);
        CollectionAssert.AreEquivalent(new[] { "cpuUsage", "gpu" }, sources.Calls);
        Assert.AreEqual(TelemetryMetrics.GpuPower, sources.GpuRequest);
    }

    [TestMethod]
    public void HiddenOverlaySelectionsDoNotAddWorkAndFpsOnlyDoesNotReadMetrics()
    {
        TelemetryCoordinator.Reset();
        try
        {
            int reads = 0;
            TelemetryCoordinator.ConfigureSampler(() => { reads++; return TelemetrySnapshot.Empty; });
            TelemetryCoordinator.SetOverlayMetrics(TelemetryMetrics.All);
            
            Assert.AreEqual(TelemetryMetrics.None, TelemetryCoordinator.RequestedMetrics);
            
            TelemetryCoordinator.SetOverlayMetrics(TelemetryMetrics.None);
            TelemetryCoordinator.SetDemand(TelemetryDemand.Overlay, true);
            TelemetryCoordinator.Poll();
            Assert.AreEqual(0, reads);
            TelemetryCoordinator.SetOverlayMetrics(TelemetryMetrics.CpuPower);
            Assert.AreEqual(TelemetryMetrics.CpuPower, TelemetryCoordinator.RequestedMetrics);
            TelemetryCoordinator.SetDemand(TelemetryDemand.FanController, true);
            Assert.AreEqual(TelemetryMetrics.CpuPower | TelemetryMetrics.Temperatures, TelemetryCoordinator.RequestedMetrics);
            TelemetryCoordinator.SetDemand(TelemetryDemand.Overlay, false);
            Assert.AreEqual(TelemetryMetrics.Temperatures, TelemetryCoordinator.RequestedMetrics);
        }
        finally { TelemetryCoordinator.Reset(); }
    }

    [TestMethod]
    public void NoneDoesNotReadAnySource()
    {
        var sources = new RecordingSources();
        TelemetryCollector.Read(TelemetrySelection.Cpu("None") | TelemetrySelection.Gpu("None"), sources);
        Assert.IsEmpty(sources.Calls);
    }

    [TestMethod]
    [DataRow(PowerSourceKind.Ac, true)]
    [DataRow(PowerSourceKind.UsbC, true)]
    [DataRow(PowerSourceKind.Battery, false)]
    public void PowerSourceStateUpdatesWithoutTelemetryOrExtraHardwareReads(PowerSourceKind source, bool onAc)
    {
        var vm = (PreySense.Frontend.ViewModels.MainViewModel)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
            typeof(PreySense.Frontend.ViewModels.MainViewModel));
        vm.IsAcConnected = !onAc;
        typeof(PreySense.Frontend.ViewModels.MainViewModel)
            .GetMethod("UpdatePowerSourceState", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(vm, new object[] { source });
        Assert.AreEqual(onAc, vm.IsAcConnected);
    }

    [TestMethod]
    public void FanRpmGraphsDoNotRequireUnselectedGpuActivityQueries()
    {
        var snapshot = TelemetrySnapshot.Empty with { CpuRpm = 2100, GpuRpm = 2300, GpuInUse = false };
        Assert.AreEqual(2100f, PreySense.Overlay.TelemetryGraphValue.Read(snapshot, "Fan Speed", false));
        Assert.AreEqual(2300f, PreySense.Overlay.TelemetryGraphValue.Read(snapshot, "Fan Speed", true));
        Assert.AreEqual(0f, PreySense.Overlay.TelemetryGraphValue.Read(snapshot, "Power", true));
    }

    [TestMethod]
    public void MainWindowContainsOnlyControlsAndCannotRequestTelemetry()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string xaml = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "MainWindow.xaml"));
        string code = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "MainWindow.xaml.cs"));
        foreach (string removed in new[] { "CpuTemperatureText", "GpuTemperatureText", "CpuFanText", "GpuFanText", "DisplayPanelInfo", "ActiveGpuDisplayMode" })
            Assert.DoesNotContain(removed, xaml);
        Assert.DoesNotContain("SetTelemetryPollingActive", code);
        Assert.DoesNotContain("TelemetryCoordinator", code);
        Assert.DoesNotContain("MainWindow", Enum.GetNames<TelemetryDemand>());
        Assert.Contains("SetModeCommand", xaml);
        Assert.Contains("SetGpuMuxModeCommand", xaml);
        Assert.Contains("SelectedRefreshRateOption", xaml);
    }

    private sealed class RecordingSources : ITelemetrySources
    {
        public List<string> Calls { get; } = new();
        public TelemetryMetrics GpuRequest;
        public bool CpuFanRequested, GpuFanRequested;
        public CpuTelemetrySnapshot? ReadCpuUsage() { Calls.Add("cpuUsage"); return new(10, 20, 10, 5, 8); }
        public int? ReadCpuTemperature() { Calls.Add("cpuTemp"); return 60; }
        public float? ReadCpuVoltage() { Calls.Add("cpuVoltage"); return 1; }
        public float? ReadCpuPower() { Calls.Add("cpuPower"); return 15; }
        public (int? Performance, int? Efficiency) ReadCpuClocks() { Calls.Add("cpuClock"); return (4000, 2500); }
        public (int? Percent, int? UsedMb) ReadRamUsage() { Calls.Add("ramUsage"); return (40, 4000); }
        public int? ReadRamSpeed() { Calls.Add("ramSpeed"); return 5600; }
        public (int? Cpu, int? Gpu) ReadFanRpms(bool cpu, bool gpu)
        {
            Calls.Add("fans"); CpuFanRequested = cpu; GpuFanRequested = gpu;
            return (cpu ? 2000 : null, gpu ? 2100 : null);
        }
        public NvidiaGpuControl.Metrics ReadGpu(TelemetryMetrics metrics) { Calls.Add("gpu"); GpuRequest = metrics; return default; }
    }
}
