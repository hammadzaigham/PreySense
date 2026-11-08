using System.Diagnostics;
using PreySense.Core.Services;
using PreySense.Overlay;

namespace PreySense.Tests;

[TestClass]
[DoNotParallelize]
public class SharedSystemTelemetryTests
{
    [TestCleanup]
    public void Cleanup()
    {
        TelemetryCoordinator.Reset();
        TelemetryCoordinator.SetOverlayMetrics(TelemetryMetrics.All);
        CpuUsageMonitor.Reset();
        SystemMemoryMonitor.Reset();
    }

    private static void ConfigureSharedSystemSampler(Func<long> clock)
    {
        TelemetryCoordinator.Reset();
        TelemetryCoordinator.SetOverlayMetrics(TelemetryMetrics.All);
        TelemetryCoordinator.ConfigureSampler(() =>
        {
            var cpu = CpuUsageMonitor.GetSnapshot();
            var ram = SystemMemoryMonitor.GetMemoryInfo();
            return TelemetrySnapshot.Empty with
            {
                IsValid = true, TimestampTicks = clock(),
                CpuUsage = cpu?.TotalUsage, RamUsage = ram?.UsedPercent, RamUsedMb = ram?.UsedMb
            };
        }, timestampProvider: clock);
        TelemetryCoordinator.SetDemand(TelemetryDemand.Overlay, true);
    }

    private static long SecondsToTicks(double seconds) =>
        (long)(seconds * Stopwatch.Frequency);

    [TestMethod]
    public void BothConsumers_ReceiveIdenticalCachedCpuAndRamSample_WithinThrottleInterval()
    {
        long mockTime = 1000;
        long idle = 1000, kernel = 2000, user = 1000;
        int memoryApiCallCount = 0;

        CpuUsageMonitor.ConfigureForTesting(
            timestampProvider: () => mockTime,
            systemTimesProvider: (out long i, out long k, out long u) =>
            {
                i = idle; k = kernel; u = user;
                return true;
            });

        SystemMemoryMonitor.ConfigureForTesting(
            timestampProvider: () => mockTime,
            memoryStatusProvider: (ref SystemMemoryMonitor.MEMORYSTATUSEX status) =>
            {
                memoryApiCallCount++;
                status.dwMemoryLoad = 45;
                status.ullTotalPhys = 32UL * 1024 * 1024 * 1024;
                status.ullAvailPhys = 16UL * 1024 * 1024 * 1024;
                return true;
            });

        CpuUsageMonitor.Warmup();

        // Advance 1 second: 25% idle => 75% CPU usage
        mockTime += SecondsToTicks(1.0);
        idle += 250; kernel += 500; user += 500;

        ConfigureSharedSystemSampler(() => mockTime);
        TelemetryCoordinator.Poll();
        // Consumer 1 (e.g. MainWindow / MainViewModel)
        int? cpuConsumer1 = CpuUsageMonitor.GetCpuUsage();
        var ramConsumer1 = SystemMemoryMonitor.GetMemoryInfo();

        Assert.AreEqual(75, cpuConsumer1);
        Assert.IsNotNull(ramConsumer1);
        Assert.AreEqual(45, ramConsumer1.Value.UsedPercent);
        Assert.AreEqual(1, memoryApiCallCount);

        // Advance only 50ms (simulating HardwareOverlay polling slightly after MainWindow)
        mockTime += SecondsToTicks(0.05);

        // Consumer 2 (e.g. HardwareOverlay)
        HardwareControl.ApplySnapshot(TelemetryCoordinator.CurrentSnapshot);
        int? cpuConsumer2 = HardwareControl.cpuUsage;
        int? ramUsageConsumer2 = HardwareControl.ramUsage;

        // Must receive the exact same values without issuing duplicate system queries
        Assert.AreEqual(cpuConsumer1, cpuConsumer2);
        Assert.AreEqual(ramConsumer1.Value.UsedPercent, ramUsageConsumer2);
        Assert.AreEqual(1, memoryApiCallCount, "Memory API should have been throttled for the second consumer within 200ms");
    }

    [TestMethod]
    public void OpeningAndClosingOverlay_DoesNotResetCpuBaseline()
    {
        long mockTime = 1000;
        long idle = 1000, kernel = 2000, user = 1000;

        CpuUsageMonitor.ConfigureForTesting(
            timestampProvider: () => mockTime,
            systemTimesProvider: (out long i, out long k, out long u) =>
            {
                i = idle; k = kernel; u = user;
                return true;
            });

        CpuUsageMonitor.Warmup();

        ConfigureSharedSystemSampler(() => mockTime);
        // Sample 1: 80% usage
        mockTime += SecondsToTicks(1.0);
        idle += 200; kernel += 500; user += 500;
        TelemetryCoordinator.Poll();
        HardwareControl.ApplySnapshot(TelemetryCoordinator.CurrentSnapshot);
        Assert.AreEqual(80, HardwareControl.cpuUsage);

        // Simulate overlay becoming invisible, closing, or re-initializing Warmup()
        HardwareControl.ApplySnapshot(TelemetryCoordinator.CurrentSnapshot);

        // Next tick 1s later: 40% usage
        mockTime += SecondsToTicks(1.0);
        idle += 600; kernel += 500; user += 500;
        TelemetryCoordinator.Poll();
        HardwareControl.ApplySnapshot(TelemetryCoordinator.CurrentSnapshot);

        // Must calculate accurately without returning null or resetting baseline
        Assert.AreEqual(40, HardwareControl.cpuUsage);
    }

    [TestMethod]
    public async Task OverlayReadersDoNotSampleWhileHardwareGateIsOccupied()
    {
        int calls = 0;
        TelemetryCoordinator.Reset();
        TelemetryCoordinator.SetOverlayMetrics(TelemetryMetrics.All);
        TelemetryCoordinator.ConfigureSampler(() => { calls++; return TelemetrySnapshot.Empty; });
        TelemetryCoordinator.SetDemand(TelemetryDemand.Overlay, true);
        using var gateLease = await HardwareOperationGate.EnterDisplaySwitchAsync();
        HardwareControl.ApplySnapshot(TelemetryCoordinator.CurrentSnapshot);
        HardwareControl.ApplySnapshot(TelemetryCoordinator.CurrentSnapshot);
        HardwareControl.ApplySnapshot(TelemetryCoordinator.CurrentSnapshot);
        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    public void ConcurrentCpuAndRamSampling_DoesNotCorruptState()
    {
        TelemetryCoordinator.Reset();
        TelemetryCoordinator.SetOverlayMetrics(TelemetryMetrics.All);
        CpuUsageMonitor.Reset();
        SystemMemoryMonitor.Reset();
        CpuUsageMonitor.Warmup();

        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        var threads = new List<Thread>();

        for (int i = 0; i < 16; i++)
        {
            var t = new Thread(() =>
            {
                try
                {
                    for (int j = 0; j < 30; j++)
                    {
                        var cpu = CpuUsageMonitor.GetCpuUsage();
                        if (cpu.HasValue)
                        {
                            Assert.IsTrue(cpu.Value is >= 0 and <= 100);
                        }

                        var mem = SystemMemoryMonitor.GetMemoryInfo();
                        if (mem.HasValue)
                        {
                            Assert.IsTrue(mem.Value.UsedPercent is >= 0 and <= 100);
                            Assert.IsGreaterThanOrEqualTo(0, mem.Value.UsedMb);
                        }

                        Thread.Sleep(2);
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });
            threads.Add(t);
        }

        foreach (var t in threads) t.Start();
        foreach (var t in threads) t.Join();

        Assert.IsEmpty(exceptions, "Concurrent sampling failed with: " + string.Join(", ", exceptions.Select(e => e.Message)));
    }
}
