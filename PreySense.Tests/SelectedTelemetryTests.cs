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
    [DataRow(TelemetryMetrics.CpuEffectiveClock, "cpuClock")]
    [DataRow(TelemetryMetrics.RamUsage, "ramUsage")]
    [DataRow(TelemetryMetrics.RamSpeed, "ramSpeed")]
    [DataRow(TelemetryMetrics.CpuFanRpm, "fans")]
    [DataRow(TelemetryMetrics.GpuFanRpm, "fans")]
    [DataRow(TelemetryMetrics.GpuTemperature, "gpu")]
    [DataRow(TelemetryMetrics.DisplayAdapter, "displayGpu")]
    [DataRow(TelemetryMetrics.GpuUsage, "gpu")]
    [DataRow(TelemetryMetrics.GpuActiveClock, "gpu")]
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
        Assert.IsTrue(float.IsNaN(PreySense.Overlay.TelemetryGraphValue.Read(snapshot, "Power", true)));
    }

    [TestMethod]
    public void NewTelemetryMetrics_GraphAndSelectionDemand_WireAccurately()
    {
        var snapshot = TelemetrySnapshot.Empty with
        {
            CpuActiveFrequencyMhz = 4800,
            CpuEffectiveFrequencyMhz = 3200,
            CpuVoltage = 1.125f,
            CpuPl1Watts = 75,
            CpuPl2Watts = 140,
            CpuPkgPowerWatts = 65.5f,
            GpuHotspotTemp = 74,
            GpuVoltage = 0.925f,
            GpuPowerWatts = 115.0f,
            GpuPowerLimitWatts = 140.0f,
            GpuThrottleReasons = 0x8000000000000000UL
        };

        Assert.AreEqual(4800f, PreySense.Overlay.TelemetryGraphValue.Read(snapshot, "Active Clock", false));
        Assert.AreEqual(3200f, PreySense.Overlay.TelemetryGraphValue.Read(snapshot, "Effective Clock", false));
        Assert.AreEqual(1.125f, PreySense.Overlay.TelemetryGraphValue.Read(snapshot, "CPU VID", false));
        Assert.AreEqual(75f, PreySense.Overlay.TelemetryGraphValue.Read(snapshot, "PL1", false));
        Assert.AreEqual(140f, PreySense.Overlay.TelemetryGraphValue.Read(snapshot, "PL2", false));
        Assert.AreEqual(74f, PreySense.Overlay.TelemetryGraphValue.Read(snapshot, "Hotspot", true));
        Assert.AreEqual(140f, PreySense.Overlay.TelemetryGraphValue.Read(snapshot, "Power Limit", true));

        // Demand selection
        Assert.AreEqual(TelemetryMetrics.CpuEffectiveClock, TelemetrySelection.Cpu("Effective Clock"));
        Assert.AreEqual(TelemetryMetrics.CpuPowerLimit, TelemetrySelection.Cpu("PL1"));
        Assert.AreEqual(TelemetryMetrics.CpuPowerLimit, TelemetrySelection.Cpu("PL2"));
        Assert.AreEqual(TelemetryMetrics.CpuPowerLimit, TelemetrySelection.Cpu("Power Limit"));
        Assert.AreEqual(TelemetryMetrics.CpuVoltage, TelemetrySelection.Cpu("CPU VID"));
        Assert.AreEqual(TelemetryMetrics.GpuHotspot, TelemetrySelection.Gpu("Hotspot"));
        Assert.AreEqual(TelemetryMetrics.GpuPowerLimit, TelemetrySelection.Gpu("Power Limit"));
        Assert.AreEqual(TelemetryMetrics.GpuThrottleReasons, TelemetrySelection.Gpu("PerfCap Reason"));
    }

    [TestMethod]
    public void MainWindowRequestsOnlyTemperaturesAndRpmsWhileVisible()
    {
        TelemetryCoordinator.Reset();
        try
        {
            Assert.AreEqual(TelemetryMetrics.None, TelemetryCoordinator.RequestedMetrics);
            TelemetryCoordinator.SetDemand(TelemetryDemand.MainWindow, true);
            Assert.IsTrue(TelemetryCoordinator.HasUiDemand);
            Assert.AreEqual(TelemetryMetrics.Temperatures | TelemetryMetrics.CpuFanRpm | TelemetryMetrics.GpuFanRpm | TelemetryMetrics.DisplayAdapter,
                TelemetryCoordinator.RequestedMetrics);
            TelemetryCoordinator.SetDemand(TelemetryDemand.FanController, true);
            TelemetryCoordinator.SetDemand(TelemetryDemand.MainWindow, false);
            Assert.IsFalse(TelemetryCoordinator.HasUiDemand);
            Assert.AreEqual(TelemetryMetrics.Temperatures, TelemetryCoordinator.RequestedMetrics);
            TelemetryCoordinator.SetDemand(TelemetryDemand.FanController, false);
            Assert.AreEqual(TelemetryMetrics.None, TelemetryCoordinator.RequestedMetrics);
        }
        finally { TelemetryCoordinator.Reset(); }
    }

    [TestMethod]
    public void CpuClocks_ShareOnePassAndPreserveDistinctAndUnavailableValues()
    {
        var sources = new RecordingSources { ClockResult = new(3000, 4200, 4200, 1700) };
        var selected = TelemetryMetrics.CpuClock | TelemetryMetrics.CpuEffectiveClock;
        var snapshot = TelemetryCollector.Read(selected, sources);
        CollectionAssert.AreEqual(new[] { "cpuClock" }, sources.Calls);
        Assert.AreEqual(3000, snapshot.CpuClockMhz);
        Assert.AreEqual(4200, snapshot.ECoreClockMhz);
        Assert.AreEqual(4200, snapshot.CpuActiveFrequencyMhz);
        Assert.AreEqual(1700, snapshot.CpuEffectiveFrequencyMhz);
        sources.ClockResult = default;
        snapshot = TelemetryCollector.Read(selected, sources);
        Assert.IsNull(snapshot.CpuClockMhz);
        Assert.IsNull(snapshot.CpuActiveFrequencyMhz);
        Assert.IsNull(snapshot.CpuEffectiveFrequencyMhz);
    }

    private sealed class RecordingSources : ITelemetrySources
    {
        public List<string> Calls { get; } = new();
        public TelemetryMetrics GpuRequest;
        public bool CpuFanRequested, GpuFanRequested;
        public NvidiaGpuControl.Metrics GpuResult;
        public CpuClockSnapshot ClockResult = new(4000, 2500, 4000, 2000);
        public CpuClockSnapshot ReadCpuClockSnapshot(TelemetryMetrics requested) { Calls.Add("cpuClock"); return ClockResult; }
        public CpuTelemetrySnapshot? ReadCpuUsage() { Calls.Add("cpuUsage"); return new(10, 20, 10, 5, 8); }
        public string? ReadDisplayGpuName() { Calls.Add("displayGpu"); return "Intel Graphics"; }
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
        public NvidiaGpuControl.Metrics ReadGpu(TelemetryMetrics metrics) { Calls.Add("gpu"); GpuRequest = metrics; return GpuResult; }
    }

    [TestMethod]
    public void GpuClock_UsesNvapiWithoutAddingPowerQueries()
    {
        var sources = new RecordingSources
        {
            GpuResult = new(true, null, null, 1980, null, null, null, null, null, ActiveClockMhz: 2070)
        };
        var selected = TelemetrySelection.Gpu("Clock");
        var snapshot = TelemetryCollector.Read(selected, sources);
        Assert.AreEqual(TelemetryMetrics.GpuActiveClock, sources.GpuRequest);
        Assert.AreEqual(TelemetryMetrics.None, sources.GpuRequest & TelemetryMetrics.GpuPower);
        Assert.AreEqual(2070, snapshot.GpuActiveClockMhz);
        Assert.AreEqual(1980, snapshot.GpuClockMhz);
        Assert.AreEqual(TelemetryMetrics.GpuActiveClock, TelemetrySelection.Gpu("Clock"));
        Assert.AreEqual(TelemetryMetrics.None, TelemetrySelection.Gpu("Effective Clock"));
        CollectionAssert.AreEqual(new[] { "gpu" }, sources.Calls);
    }
}
