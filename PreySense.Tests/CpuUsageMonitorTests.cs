using System.Diagnostics;
using PreySense.Core.Services;

namespace PreySense.Tests;

[TestClass]
[DoNotParallelize]
public class CpuUsageMonitorTests
{
    [TestCleanup]
    public void Cleanup()
    {
        CpuUsageMonitor.Reset();
    }

    private static long SecondsToTicks(double seconds) =>
        (long)(seconds * Stopwatch.Frequency);

    [TestMethod]
    public void FirstSample_WithoutWarmup_ReturnsNullBaseline()
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

        // First call establishes baseline
        int? sample1 = CpuUsageMonitor.GetCpuUsage();
        Assert.IsNull(sample1);

        // Advance 1 second, 50% idle, 50% active
        mockTime += SecondsToTicks(1.0);
        idle += 500;
        kernel += 600;
        user += 400; // total active delta = (600 + 400) = 1000; idle = 500 => 50% usage

        int? sample2 = CpuUsageMonitor.GetCpuUsage();
        Assert.IsNotNull(sample2);
        Assert.AreEqual(50, sample2.Value);
    }

    [TestMethod]
    public void Warmup_EnablesImmediateFirstCalculation()
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

        // Advance 1 second, 20% idle => 80% usage
        mockTime += SecondsToTicks(1.0);
        idle += 200;
        kernel += 500;
        user += 500; // total = 1000; idle = 200 => 80% usage

        int? usage = CpuUsageMonitor.GetCpuUsage();
        Assert.IsNotNull(usage);
        Assert.AreEqual(80, usage.Value);
    }

    [TestMethod]
    public void RapidSuccessiveCalls_ReturnCachedWithoutSamplingNoise()
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

        // Advance 1s and sample: 70%
        mockTime += SecondsToTicks(1.0);
        idle += 300; kernel += 500; user += 500;
        int? sample1 = CpuUsageMonitor.GetCpuUsage();
        Assert.AreEqual(70, sample1);

        // Advance only 50ms (less than 200ms threshold)
        mockTime += SecondsToTicks(0.05);
        idle += 10; kernel += 20; user += 20;

        // Must return cached 70 without dividing by micro-delta
        int? sample2 = CpuUsageMonitor.GetCpuUsage();
        Assert.AreEqual(70, sample2);
    }

    [TestMethod]
    public void StaleReadings_ExpireAfterThreshold()
    {
        long mockTime = 1000;
        long idle = 1000, kernel = 2000, user = 1000;
        bool failSystemTimes = false;

        CpuUsageMonitor.ConfigureForTesting(
            timestampProvider: () => mockTime,
            systemTimesProvider: (out long i, out long k, out long u) =>
            {
                i = idle; k = kernel; u = user;
                return !failSystemTimes;
            },
            staleSec: 4.0);

        CpuUsageMonitor.Warmup();

        // Valid sample: 40%
        mockTime += SecondsToTicks(1.0);
        idle += 600; kernel += 500; user += 500;
        Assert.AreEqual(40, CpuUsageMonitor.GetCpuUsage());

        // SystemTimes fails for 2 seconds (< 4s stale threshold) => cached value retained
        failSystemTimes = true;
        mockTime += SecondsToTicks(2.0);
        Assert.AreEqual(40, CpuUsageMonitor.GetCpuUsage());

        // SystemTimes fails for 5 seconds total (> 4s stale threshold) => expires to null
        mockTime += SecondsToTicks(3.0);
        Assert.IsNull(CpuUsageMonitor.GetCpuUsage());
    }

    [TestMethod]
    public void SleepResume_DiscardsStaleDataAndRebaselines()
    {
        long mockTime = 1000;
        long idle = 1000, kernel = 2000, user = 1000;

        CpuUsageMonitor.ConfigureForTesting(
            timestampProvider: () => mockTime,
            systemTimesProvider: (out long i, out long k, out long u) =>
            {
                i = idle; k = kernel; u = user;
                return true;
            },
            suspendSec: 15.0);

        CpuUsageMonitor.Warmup();

        // High load before sleep: 95%
        mockTime += SecondsToTicks(1.0);
        idle += 50; kernel += 500; user += 500;
        Assert.AreEqual(95, CpuUsageMonitor.GetCpuUsage());

        // System goes to sleep and resumes 30 seconds later
        mockTime += SecondsToTicks(30.0);
        idle += 1000; kernel += 2000; user += 2000;

        // Wake tick must NOT show stale 95%; it must re-baseline and return null
        int? wakeTick = CpuUsageMonitor.GetCpuUsage();
        Assert.IsNull(wakeTick);

        // Next tick after wake (1 second later) returns fresh, accurate post-wake usage (e.g. 10%)
        mockTime += SecondsToTicks(1.0);
        idle += 900; kernel += 500; user += 500; // 900 idle / 1000 total = 10% usage
        int? postWake = CpuUsageMonitor.GetCpuUsage();
        Assert.IsNotNull(postWake);
        Assert.AreEqual(10, postWake.Value);
    }

    [TestMethod]
    public void ConcurrentSampling_IsThreadSafeAndConsistent()
    {
        CpuUsageMonitor.Reset();
        CpuUsageMonitor.Warmup();

        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        var threads = new List<Thread>();

        for (int i = 0; i < 16; i++)
        {
            var t = new Thread(() =>
            {
                try
                {
                    for (int j = 0; j < 50; j++)
                    {
                        var usage = CpuUsageMonitor.GetCpuUsage();
                        if (usage.HasValue)
                        {
                            Assert.IsTrue(usage.Value is >= 0 and <= 100);
                        }
                        Thread.Sleep(5);
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

        Assert.AreEqual(0, exceptions.Count, "Concurrent CPU sampling caused exceptions: " + string.Join(", ", exceptions.Select(e => e.Message)));
    }

    [TestMethod]
    public void ExplicitInvalidateBaseline_ClearsCachedUsageAndRequiresNewBaseline()
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

        // Valid sample: 60%
        mockTime += SecondsToTicks(1.0);
        idle += 400; kernel += 500; user += 500;
        Assert.AreEqual(60, CpuUsageMonitor.GetCpuUsage());

        // Invalidate baseline (simulating OS power broadcast or manual reset)
        CpuUsageMonitor.InvalidateBaseline();

        // First call after invalidation should re-baseline and return null
        int? afterInvalidate = CpuUsageMonitor.GetCpuUsage();
        Assert.IsNull(afterInvalidate);

        // Subsequent call produces fresh usage
        mockTime += SecondsToTicks(1.0);
        idle += 200; kernel += 500; user += 500; // 200 idle / 1000 total = 80% usage
        Assert.AreEqual(80, CpuUsageMonitor.GetCpuUsage());
    }

    [TestMethod]
    public void MaxLogicalCoreUtilization_CalculatesPeakLoadedCoreAcrossTopology()
    {
        long mockTime = 1000;
        int coreCount = 4;
        var coreTimes = new CpuUsageMonitor.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION[coreCount];
        for (int c = 0; c < coreCount; c++)
        {
            coreTimes[c].IdleTime = 1000;
            coreTimes[c].KernelTime = 2000;
            coreTimes[c].UserTime = 1000;
        }

        CpuUsageMonitor.ConfigureForTesting(
            timestampProvider: () => mockTime,
            systemTimesProvider: null,
            processorPerformanceProvider: (destination) =>
            {
                int copyCount = Math.Min(destination.Length, coreTimes.Length);
                Array.Copy(coreTimes, destination, copyCount);
                return true;
            },
            processorCount: coreCount);

        CpuUsageMonitor.Warmup();

        mockTime += SecondsToTicks(1.0);
        coreTimes[0].IdleTime += 0; coreTimes[0].KernelTime += 600; coreTimes[0].UserTime += 400;
        coreTimes[1].IdleTime += 500; coreTimes[1].KernelTime += 600; coreTimes[1].UserTime += 400;
        coreTimes[2].IdleTime += 800; coreTimes[2].KernelTime += 600; coreTimes[2].UserTime += 400;
        coreTimes[3].IdleTime += 1000; coreTimes[3].KernelTime += 600; coreTimes[3].UserTime += 400;

        var snapshot = CpuUsageMonitor.GetSnapshot();
        Assert.IsNotNull(snapshot);
        Assert.AreEqual(100, snapshot.Value.MaxCoreUsage);
        Assert.AreEqual(100, CpuUsageMonitor.GetMaxCoreUsage());
        Assert.AreEqual(43, snapshot.Value.TotalUsage);
        Assert.AreEqual(43, CpuUsageMonitor.GetCpuUsage());
    }

    [TestMethod]
    public void ClusterUtilization_SeparatesPCoreAndECoreClusters()
    {
        long mockTime = 1000;
        int coreCount = 4;
        var eMap = new bool[] { false, false, true, true };

        var coreTimes = new CpuUsageMonitor.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION[coreCount];
        for (int c = 0; c < coreCount; c++)
        {
            coreTimes[c].IdleTime = 1000;
            coreTimes[c].KernelTime = 2000;
            coreTimes[c].UserTime = 1000;
        }

        CpuUsageMonitor.ConfigureForTesting(
            timestampProvider: () => mockTime,
            systemTimesProvider: null,
            processorPerformanceProvider: (destination) =>
            {
                int copyCount = Math.Min(destination.Length, coreTimes.Length);
                Array.Copy(coreTimes, destination, copyCount);
                return true;
            },
            customECoreMap: eMap);

        CpuUsageMonitor.Warmup();

        mockTime += SecondsToTicks(1.0);
        coreTimes[0].IdleTime += 100; coreTimes[0].KernelTime += 600; coreTimes[0].UserTime += 400;
        coreTimes[1].IdleTime += 200; coreTimes[1].KernelTime += 600; coreTimes[1].UserTime += 400;
        coreTimes[2].IdleTime += 900; coreTimes[2].KernelTime += 600; coreTimes[2].UserTime += 400;
        coreTimes[3].IdleTime += 1000; coreTimes[3].KernelTime += 600; coreTimes[3].UserTime += 400;

        var snapshot = CpuUsageMonitor.GetSnapshot();
        Assert.IsNotNull(snapshot);
        Assert.AreEqual(90, snapshot.Value.MaxCoreUsage);
        Assert.AreEqual(85, snapshot.Value.PCoreUsage);
        Assert.AreEqual(85, CpuUsageMonitor.GetPCoreUsage());
        Assert.AreEqual(5, snapshot.Value.ECoreUsage);
        Assert.AreEqual(5, CpuUsageMonitor.GetECoreUsage());
    }

    [TestMethod]
    public void NonHybridSystem_ReportsPCoreUsageAsTotal_AndNullECoreUsage()
    {
        long mockTime = 1000;
        int coreCount = 2;
        var eMap = new bool[] { false, false };

        var coreTimes = new CpuUsageMonitor.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION[coreCount];
        for (int c = 0; c < coreCount; c++)
        {
            coreTimes[c].IdleTime = 1000;
            coreTimes[c].KernelTime = 2000;
            coreTimes[c].UserTime = 1000;
        }

        CpuUsageMonitor.ConfigureForTesting(
            timestampProvider: () => mockTime,
            systemTimesProvider: null,
            processorPerformanceProvider: (destination) =>
            {
                int copyCount = Math.Min(destination.Length, coreTimes.Length);
                Array.Copy(coreTimes, destination, copyCount);
                return true;
            },
            customECoreMap: eMap);

        CpuUsageMonitor.Warmup();

        mockTime += SecondsToTicks(1.0);
        coreTimes[0].IdleTime += 500; coreTimes[0].KernelTime += 600; coreTimes[0].UserTime += 400;
        coreTimes[1].IdleTime += 500; coreTimes[1].KernelTime += 600; coreTimes[1].UserTime += 400;

        var snapshot = CpuUsageMonitor.GetSnapshot();
        Assert.IsNotNull(snapshot);
        Assert.AreEqual(50, snapshot.Value.TotalUsage);
        Assert.AreEqual(50, snapshot.Value.PCoreUsage);
        Assert.IsNull(snapshot.Value.ECoreUsage);
        Assert.IsNull(CpuUsageMonitor.GetECoreUsage());
    }
}
