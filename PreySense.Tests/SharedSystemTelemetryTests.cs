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
        CpuUsageMonitor.Reset();
        SystemMemoryMonitor.Reset();
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
        HardwareControl.ReadSystemMetrics();
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

        // Sample 1: 80% usage
        mockTime += SecondsToTicks(1.0);
        idle += 200; kernel += 500; user += 500;
        HardwareControl.ReadSystemMetrics();
        Assert.AreEqual(80, HardwareControl.cpuUsage);

        // Simulate overlay becoming invisible, closing, or re-initializing Warmup()
        HardwareControl.Warmup();

        // Next tick 1s later: 40% usage
        mockTime += SecondsToTicks(1.0);
        idle += 600; kernel += 500; user += 500;
        HardwareControl.ReadSystemMetrics();

        // Must calculate accurately without returning null or resetting baseline
        Assert.AreEqual(40, HardwareControl.cpuUsage);
    }

    [TestMethod]
    public async Task CpuAndRamMetrics_ContinueSampling_WhenHardwareGateIsOccupied()
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

        SystemMemoryMonitor.ConfigureForTesting(
            timestampProvider: () => mockTime,
            memoryStatusProvider: (ref SystemMemoryMonitor.MEMORYSTATUSEX status) =>
            {
                status.dwMemoryLoad = 55;
                status.ullTotalPhys = 16UL * 1024 * 1024 * 1024;
                status.ullAvailPhys = 8UL * 1024 * 1024 * 1024;
                return true;
            });

        CpuUsageMonitor.Warmup();

        // Lock the hardware gate (simulating an ongoing GPU display switch or lengthy EC transaction)
        using var gateLease = await HardwareOperationGate.EnterDisplaySwitchAsync();
        Assert.IsNotNull(gateLease);

        // HardwareOperationGate is now held; TryEnterTelemetry(0) will fail:
        using var hardwareLease = HardwareOperationGate.TryEnterTelemetry(0);
        Assert.IsNull(hardwareLease);

        // Advance time 1 second
        mockTime += SecondsToTicks(1.0);
        idle += 300; kernel += 500; user += 500;

        // Even though hardware gate is completely locked, system metrics (CPU + RAM) must sample successfully
        HardwareControl.ReadSystemMetrics();
        Assert.AreEqual(70, HardwareControl.cpuUsage);
        Assert.AreEqual(55, HardwareControl.ramUsage);
    }

    [TestMethod]
    public void ConcurrentCpuAndRamSampling_DoesNotCorruptState()
    {
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
                            Assert.IsTrue(mem.Value.UsedMb >= 0);
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

        Assert.AreEqual(0, exceptions.Count, "Concurrent sampling failed with: " + string.Join(", ", exceptions.Select(e => e.Message)));
    }
}
