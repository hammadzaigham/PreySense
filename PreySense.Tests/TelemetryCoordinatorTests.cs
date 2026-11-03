using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PreySense.Core.Services;

namespace PreySense.Tests
{
    [TestClass]
    [DoNotParallelize]
    public class TelemetryCoordinatorTests
    {
        [TestInitialize]
        public void Setup()
        {
            TelemetryCoordinator.Reset();
        }

        [TestCleanup]
        public void Cleanup()
        {
            TelemetryCoordinator.Reset();
        }

        [TestMethod]
        public void SharedSnapshot_BroadcastsIdenticalSnapshot_ToMultipleSubscribers()
        {
            int hardwareReadCount = 0;
            long now = Stopwatch.GetTimestamp();
            var expectedSnapshot = new TelemetrySnapshot(
                TimestampTicks: now,
                CpuUsage: 15,
                CpuMaxCoreUsage: 25,
                CpuPCoreUsage: 20,
                CpuECoreUsage: 10,
                RamUsage: 45,
                RamUsedMb: 7200,
                CpuTemp: 65,
                GpuTemp: 58,
                CpuRpm: 2400,
                GpuRpm: 2100,
                CpuFanMaxRpm: 5800,
                GpuFanMaxRpm: 5600,
                CpuPkgPowerWatts: 45.5f,
                GpuPowerWatts: 80.0f,
                CpuVoltage: 1.15f,
                GpuVoltage: 0.95f,
                CpuClockMhz: 4800,
                ECoreClockMhz: 3600,
                GpuClockMhz: 2100,
                GpuUsage: 75,
                VramUsedMb: 4000,
                VramTotalMb: 8192,
                VramSpeedMhz: 7000,
                GpuInUse: true,
                BatteryPercent: 100f,
                IsAcConnected: true,
                PowerSource: PowerSourceKind.Ac,
                IsValid: true,
                ThermalTimestampTicks: now);

            TelemetryCoordinator.ConfigureSampler(
                fullSampler: () =>
                {
                    Interlocked.Increment(ref hardwareReadCount);
                    return expectedSnapshot;
                },
                fanOnlySampler: null);

            var subscriber1Snapshots = new List<TelemetrySnapshot>();
            var subscriber2Snapshots = new List<TelemetrySnapshot>();

            Action<TelemetrySnapshot> sub1 = s => subscriber1Snapshots.Add(s);
            Action<TelemetrySnapshot> sub2 = s => subscriber2Snapshots.Add(s);

            try
            {
                TelemetryCoordinator.SnapshotAvailable += sub1;
                TelemetryCoordinator.SnapshotAvailable += sub2;

                TelemetryCoordinator.SetDemand(TelemetryDemand.MainWindow | TelemetryDemand.Overlay, true);
                TelemetryCoordinator.Poll();

                // Exactly ONE hardware read should have been performed
                Assert.AreEqual(1, hardwareReadCount, "Single polling producer should eliminate duplicate reads for multiple subscribers");

                // Both subscribers received identical snapshot
                Assert.AreEqual(1, subscriber1Snapshots.Count, "Subscriber 1 received snapshot");
                Assert.AreEqual(1, subscriber2Snapshots.Count, "Subscriber 2 received snapshot");

                Assert.AreEqual(expectedSnapshot.CpuUsage, subscriber1Snapshots[0].CpuUsage);
                Assert.AreEqual(expectedSnapshot.CpuMaxCoreUsage, subscriber1Snapshots[0].CpuMaxCoreUsage);
                Assert.AreEqual(expectedSnapshot.CpuTemp, subscriber1Snapshots[0].CpuTemp);
                Assert.AreEqual(expectedSnapshot.GpuTemp, subscriber1Snapshots[0].GpuTemp);
                Assert.AreEqual(expectedSnapshot.CpuClockMhz, subscriber1Snapshots[0].CpuClockMhz);

                Assert.AreEqual(subscriber1Snapshots[0], subscriber2Snapshots[0], "Both subscribers received identical telemetry snapshot");
                Assert.AreEqual(expectedSnapshot, TelemetryCoordinator.CurrentSnapshot, "Current cached snapshot matches published snapshot");

                // Instrumentation verification
                Assert.AreEqual(1, TelemetryInstrumentation.TotalSnapshotsProduced);
                Assert.AreEqual(2, TelemetryInstrumentation.TotalSnapshotsDelivered);
            }
            finally
            {
                TelemetryCoordinator.SnapshotAvailable -= sub1;
                TelemetryCoordinator.SnapshotAvailable -= sub2;
            }
        }

        [TestMethod]
        public void DemandTransitions_ZeroReadsWhenBothWindowsHidden_ResumesWhenVisible()
        {
            int hardwarePolls = 0;
            TelemetryCoordinator.ConfigureSampler(
                fullSampler: () =>
                {
                    Interlocked.Increment(ref hardwarePolls);
                    return new TelemetrySnapshot { IsValid = true, CpuUsage = 10, CpuTemp = 50, GpuTemp = 50 };
                },
                fanOnlySampler: null);

            // Initially no demand -> Poll() should do nothing
            TelemetryCoordinator.SetDemand(TelemetryDemand.MainWindow, false);
            TelemetryCoordinator.SetDemand(TelemetryDemand.Overlay, false);
            TelemetryCoordinator.SetDemand(TelemetryDemand.FanController, false);

            Assert.IsFalse(TelemetryCoordinator.HasActiveDemand);
            TelemetryCoordinator.Poll();
            Assert.AreEqual(0, hardwarePolls, "No hardware reads when all consumers are idle/hidden");

            // MainWindow becomes visible
            TelemetryCoordinator.SetDemand(TelemetryDemand.MainWindow, true);
            Assert.IsTrue(TelemetryCoordinator.HasActiveDemand);
            Assert.IsTrue(TelemetryCoordinator.HasUiDemand);

            TelemetryCoordinator.Poll();
            Assert.AreEqual(1, hardwarePolls, "Polled when MainWindow became visible");

            // HardwareOverlay also becomes visible -> still only 1 poll per tick (not 2x)
            TelemetryCoordinator.SetDemand(TelemetryDemand.Overlay, true);
            Assert.IsTrue(TelemetryCoordinator.HasUiDemand);
            Assert.AreEqual(TelemetryDemand.MainWindow | TelemetryDemand.Overlay, TelemetryCoordinator.ActiveDemand);

            TelemetryCoordinator.Poll();
            Assert.AreEqual(2, hardwarePolls, "Shared pipeline polls once even with both UIs visible");

            // Both windows hide
            TelemetryCoordinator.SetDemand(TelemetryDemand.MainWindow, false);
            TelemetryCoordinator.SetDemand(TelemetryDemand.Overlay, false);
            Assert.IsFalse(TelemetryCoordinator.HasActiveDemand);

            TelemetryCoordinator.Poll();
            Assert.AreEqual(2, hardwarePolls, "Telemetry paused when both windows hidden");
        }

        [TestMethod]
        public void FanControlIndependence_CustomCurveRunsFanOnly_AutoMaxIdlesWhenHidden()
        {
            int fullReads = 0;
            int fanReads = 0;

            TelemetryCoordinator.ConfigureSampler(
                fullSampler: () =>
                {
                    Interlocked.Increment(ref fullReads);
                    return new TelemetrySnapshot { IsValid = true, CpuTemp = 60, GpuTemp = 55 };
                },
                fanOnlySampler: () =>
                {
                    Interlocked.Increment(ref fanReads);
                    return new TelemetrySnapshot { IsValid = true, CpuTemp = 62, GpuTemp = 57 };
                });

            // Scenario 1: Windows are hidden, Fan Controller is in Custom mode (needs temperatures)
            TelemetryCoordinator.SetDemand(TelemetryDemand.MainWindow, false);
            TelemetryCoordinator.SetDemand(TelemetryDemand.Overlay, false);
            TelemetryCoordinator.SetDemand(TelemetryDemand.FanController, true);

            Assert.IsTrue(TelemetryCoordinator.HasActiveDemand);
            Assert.IsFalse(TelemetryCoordinator.HasUiDemand);
            Assert.IsTrue(TelemetryCoordinator.HasFanOnlyDemand);

            TelemetryCoordinator.Poll();

            Assert.AreEqual(0, fullReads, "Full UI telemetry did NOT run when windows are hidden");
            Assert.AreEqual(1, fanReads, "Fan temperature read ran in background for custom curve safety");

            // Scenario 2: Fan mode switches to Auto or Max (firmware control, no query needed when hidden)
            TelemetryCoordinator.SetDemand(TelemetryDemand.FanController, false);
            Assert.IsFalse(TelemetryCoordinator.HasActiveDemand);

            TelemetryCoordinator.Poll();
            Assert.AreEqual(0, fullReads);
            Assert.AreEqual(1, fanReads, "No polling occurs when fan is Auto/Max and windows are hidden");

            // Scenario 3: Independent fresh temperature request (Emergency cooling / mode change)
            var fresh = TelemetryCoordinator.RequestFreshFanTemperatures();
            Assert.AreEqual(62, fresh.CpuTemp, "RequestFreshFanTemperatures returned fresh sensor reading independently");
            Assert.AreEqual(2, fanReads, "Direct temperature query invoked fan sampler independently");
        }

        [TestMethod]
        public void GateContention_DoesNotBlockOrCrash_GracefullySkipsWhenGateBusy()
        {
            TelemetryCoordinator.ConfigureSampler(
                fullSampler: () => new TelemetrySnapshot { IsValid = true, CpuTemp = 70 },
                fanOnlySampler: null);

            TelemetryCoordinator.SetDemand(TelemetryDemand.MainWindow, true);

            // Simulate another hardware operation holding the gate
            using (var blockingLease = HardwareOperationGate.TryEnterTelemetry(100))
            {
                Assert.IsNotNull(blockingLease, "Acquired gate lock");

                // Attempting to poll while gate is held should safely skip without throwing
                TelemetryCoordinator.Poll();
            }

            // After gate releases, coordinator can sample normally
            int samplesProduced = 0;
            Action<TelemetrySnapshot> handler = _ => samplesProduced++;
            try
            {
                TelemetryCoordinator.SnapshotAvailable += handler;
                TelemetryCoordinator.Poll();
                Assert.AreEqual(1, samplesProduced, "Successfully sampled after gate released");
            }
            finally
            {
                TelemetryCoordinator.SnapshotAvailable -= handler;
            }
        }

        [TestMethod]
        public void InvalidationAndCleanup_ResetsCacheAndRemovesHandlersCleanly()
        {
            TelemetryCoordinator.ConfigureSampler(
                fullSampler: () => new TelemetrySnapshot { IsValid = true, CpuUsage = 50 },
                fanOnlySampler: null);

            TelemetryCoordinator.SetDemand(TelemetryDemand.MainWindow, true);
            TelemetryCoordinator.Poll();

            Assert.AreEqual(50, TelemetryCoordinator.CurrentSnapshot.CpuUsage);

            TelemetryCoordinator.InvalidateCurrentSnapshot();
            Assert.AreEqual(0, TelemetryCoordinator.CurrentSnapshot.TimestampTicks);
            Assert.IsNull(TelemetryCoordinator.CurrentSnapshot.CpuUsage);

            TelemetryCoordinator.Reset();
            Assert.IsFalse(TelemetryCoordinator.HasActiveDemand);
            Assert.AreEqual(0, TelemetryInstrumentation.TotalSnapshotsProduced);
        }

        [TestMethod]
        public void DeadlockPrevention_GateLockReleasedBeforeEventDispatch()
        {
            TelemetryCoordinator.ConfigureSampler(
                fullSampler: () => new TelemetrySnapshot { IsValid = true, CpuUsage = 40, CpuTemp = 60 },
                fanOnlySampler: null);

            bool subscriberAcquiredLock = false;
            Action<TelemetrySnapshot> handler = _ =>
            {
                // Attempt to acquire gate inside event subscriber.
                // If coordinator held the gate across event invocation, this would fail (return null) or deadlock!
                using var lease = HardwareOperationGate.TryEnterTelemetry(50);
                subscriberAcquiredLock = (lease != null);
            };

            try
            {
                TelemetryCoordinator.SnapshotAvailable += handler;
                TelemetryCoordinator.SetDemand(TelemetryDemand.MainWindow, true);
                TelemetryCoordinator.Poll();

                Assert.IsTrue(subscriberAcquiredLock, "Subscriber must be able to acquire HardwareOperationGate because coordinator released it prior to event dispatch");
            }
            finally
            {
                TelemetryCoordinator.SnapshotAvailable -= handler;
            }
        }

        [TestMethod]
        public void FanOnlySnapshot_PreservesPriorMetricsAndFreshness()
        {
            long now = Stopwatch.GetTimestamp();
            var initial = new TelemetrySnapshot(
                TimestampTicks: now,
                CpuUsage: 35,
                CpuMaxCoreUsage: 50,
                CpuPCoreUsage: 40,
                CpuECoreUsage: 25,
                RamUsage: 60,
                RamUsedMb: 9600,
                CpuTemp: 70,
                GpuTemp: 65,
                CpuRpm: 3000,
                GpuRpm: 2800,
                CpuFanMaxRpm: 5800,
                GpuFanMaxRpm: 5600,
                CpuPkgPowerWatts: 45.0f,
                GpuPowerWatts: 75.0f,
                CpuVoltage: 1.2f,
                GpuVoltage: 0.95f,
                CpuClockMhz: 4600,
                ECoreClockMhz: 3400,
                GpuClockMhz: 2000,
                GpuUsage: 80,
                VramUsedMb: 4500,
                VramTotalMb: 8192,
                VramSpeedMhz: 7000,
                GpuInUse: true,
                BatteryPercent: 100f,
                IsAcConnected: true,
                PowerSource: PowerSourceKind.Ac,
                IsValid: true,
                IsFanOnly: false);

            TelemetryCoordinator.ConfigureSampler(
                fullSampler: () => initial,
                fanOnlySampler: () => new TelemetrySnapshot { IsValid = true, CpuTemp = 82, GpuTemp = 78, IsFanOnly = true, TimestampTicks = 0, ThermalTimestampTicks = now + 100 });

            // 1. Full sample runs
            TelemetryCoordinator.SetDemand(TelemetryDemand.MainWindow, true);
            TelemetryCoordinator.Poll();

            var snap1 = TelemetryCoordinator.CurrentSnapshot;
            Assert.AreEqual(35, snap1.CpuUsage);
            Assert.AreEqual(70, snap1.CpuTemp);
            Assert.AreEqual(75.0f, snap1.GpuPowerWatts);

            // 2. UI demand is turned off, Custom fan demand runs
            TelemetryCoordinator.SetDemand(TelemetryDemand.MainWindow, false);
            TelemetryCoordinator.SetDemand(TelemetryDemand.FanController, true);
            TelemetryCoordinator.Poll();

            var snap2 = TelemetryCoordinator.CurrentSnapshot;
            Assert.IsTrue(snap2.IsValid);
            Assert.AreEqual(82, snap2.CpuTemp, "CPU temperature updated by fan-only sample");
            Assert.AreEqual(78, snap2.GpuTemp, "GPU temperature updated by fan-only sample");
            Assert.AreEqual(now, snap2.TimestampTicks, "Full telemetry timestamp must NOT be refreshed by fan-only sample");
            Assert.AreEqual(now + 100, snap2.EffectiveThermalTicks, "Thermal timestamp updated by fan-only sample");

            // Non-thermal fields MUST BE PRESERVED (not wiped or set to null/0)
            Assert.AreEqual(35, snap2.CpuUsage, "CPU usage preserved during fan-only update");
            Assert.AreEqual(50, snap2.CpuMaxCoreUsage, "CpuMaxCoreUsage preserved");
            Assert.AreEqual(60, snap2.RamUsage, "RAM usage preserved");
            Assert.AreEqual(75.0f, snap2.GpuPowerWatts, "GPU power preserved");
            Assert.AreEqual(4600, snap2.CpuClockMhz, "CPU clock preserved");
            Assert.AreEqual(100f, snap2.BatteryPercent, "Battery percent preserved");
            Assert.IsTrue(snap2.IsAcConnected, "AC connection preserved");
        }

        [TestMethod]
        public void PromptSampleOnDemandResume_TriggersImmediateSample()
        {
            int polls = 0;
            var resetEvent = new ManualResetEventSlim(false);

            TelemetryCoordinator.ConfigureSampler(
                fullSampler: () =>
                {
                    Interlocked.Increment(ref polls);
                    resetEvent.Set();
                    return new TelemetrySnapshot { IsValid = true, CpuUsage = 20 };
                },
                fanOnlySampler: null);

            TelemetryCoordinator.AutoPromptOnDemandResume = true;
            TelemetryCoordinator.SetDemand(TelemetryDemand.MainWindow, true);

            bool signaled = resetEvent.Wait(2000);
            Assert.IsTrue(signaled, "Prompt sample on demand resume executed immediately on thread pool without waiting for timer interval");
            Assert.IsTrue(polls >= 1, "Hardware sample completed promptly");
        }

        [TestMethod]
        public void ExpiredSnapshot_IsMarkedInvalidAfterStaleThreshold()
        {
            long mockTime = 10_000_000;
            TelemetryCoordinator.ConfigureSampler(
                fullSampler: () => new TelemetrySnapshot { IsValid = true, CpuUsage = 25, TimestampTicks = mockTime },
                fanOnlySampler: null,
                timestampProvider: () => mockTime);

            TelemetryCoordinator.SetDemand(TelemetryDemand.MainWindow, true);
            TelemetryCoordinator.Poll();

            Assert.IsFalse(TelemetryCoordinator.IsSnapshotExpired(), "Fresh snapshot is not expired");
            Assert.AreEqual(25, TelemetryCoordinator.CurrentSnapshot.CpuUsage);

            // Advance time past 4.0s (Stopwatch.Frequency ticks * 5)
            mockTime += System.Diagnostics.Stopwatch.Frequency * 5;

            Assert.IsTrue(TelemetryCoordinator.IsSnapshotExpired(), "Snapshot older than 4 seconds is marked expired");

            // Demand resume from 0 purges expired reading
            TelemetryCoordinator.SetDemand(TelemetryDemand.MainWindow, false);
            TelemetryCoordinator.SetDemand(TelemetryDemand.MainWindow, true);
            Assert.AreEqual(0, TelemetryCoordinator.CurrentSnapshot.TimestampTicks, "Expired reading invalidated on demand resume");
        }

        [TestMethod]
        public void InstrumentedReadCounts_MeasuresExactHardwareReads_AcrossAllVisibilityConditions()
        {
            int ecThermalCount = 0;
            int gpuNvmlCount = 0;
            int cpuCount = 0;
            int ramCount = 0;

            TelemetryCoordinator.ConfigureSampler(
                fullSampler: () =>
                {
                    Interlocked.Increment(ref ecThermalCount);
                    Interlocked.Increment(ref gpuNvmlCount);
                    Interlocked.Increment(ref cpuCount);
                    Interlocked.Increment(ref ramCount);
                    Interlocked.Increment(ref TelemetryInstrumentation.EcThermalReadCount);
                    Interlocked.Increment(ref TelemetryInstrumentation.GpuTelemetryReadCount);
                    Interlocked.Increment(ref TelemetryInstrumentation.CpuTelemetryReadCount);
                    Interlocked.Increment(ref TelemetryInstrumentation.RamTelemetryReadCount);

                    return new TelemetrySnapshot
                    {
                        IsValid = true,
                        CpuUsage = 20,
                        CpuTemp = 55,
                        GpuTemp = 50,
                        GpuInUse = true
                    };
                },
                fanOnlySampler: () =>
                {
                    Interlocked.Increment(ref ecThermalCount);
                    Interlocked.Increment(ref TelemetryInstrumentation.EcThermalReadCount);
                    return new TelemetrySnapshot
                    {
                        IsValid = true,
                        CpuTemp = 56,
                        GpuTemp = 51,
                        IsFanOnly = true
                    };
                });

            var mwSnapshots = new List<TelemetrySnapshot>();
            var ovSnapshots = new List<TelemetrySnapshot>();

            Action<TelemetrySnapshot> mwHandler = s => mwSnapshots.Add(s);
            Action<TelemetrySnapshot> ovHandler = s => ovSnapshots.Add(s);

            try
            {
                TelemetryCoordinator.SnapshotAvailable += mwHandler;
                TelemetryCoordinator.SnapshotAvailable += ovHandler;

                // --- CONDITION 1: MainWindow only visible (5 ticks) ---
                TelemetryCoordinator.SetDemand(TelemetryDemand.MainWindow, true);
                TelemetryCoordinator.SetDemand(TelemetryDemand.Overlay, false);
                TelemetryCoordinator.SetDemand(TelemetryDemand.FanController, false);

                for (int i = 0; i < 5; i++) TelemetryCoordinator.Poll();

                Assert.AreEqual(5, ecThermalCount, "Condition 1 (MW only): exactly 5 EC reads for 5 ticks");
                Assert.AreEqual(5, gpuNvmlCount, "Condition 1 (MW only): exactly 5 GPU reads for 5 ticks");
                Assert.AreEqual(5, mwSnapshots.Count, "Condition 1 (MW only): MainWindow received 5 snapshots");

                // Reset counters
                ecThermalCount = gpuNvmlCount = cpuCount = ramCount = 0;
                mwSnapshots.Clear();
                ovSnapshots.Clear();
                TelemetryInstrumentation.Reset();

                // --- CONDITION 2: Overlay only visible (5 ticks) ---
                TelemetryCoordinator.SetDemand(TelemetryDemand.MainWindow, false);
                TelemetryCoordinator.SetDemand(TelemetryDemand.Overlay, true);

                for (int i = 0; i < 5; i++) TelemetryCoordinator.Poll();

                Assert.AreEqual(5, ecThermalCount, "Condition 2 (Overlay only): exactly 5 EC reads for 5 ticks");
                Assert.AreEqual(5, gpuNvmlCount, "Condition 2 (Overlay only): exactly 5 GPU reads for 5 ticks");
                Assert.AreEqual(5, ovSnapshots.Count, "Condition 2 (Overlay only): Overlay received 5 snapshots");

                // Reset counters
                ecThermalCount = gpuNvmlCount = cpuCount = ramCount = 0;
                mwSnapshots.Clear();
                ovSnapshots.Clear();
                TelemetryInstrumentation.Reset();

                // --- CONDITION 3: Both Visible (MainWindow + Overlay) (5 ticks) ---
                // Deterministic single-producer verification: in an uncoordinated model where two consumers poll independently,
                // 10 reads would occur across 5 ticks; with the coordinator, exactly 5 reads occur (single producer eliminated duplicate polling in this deterministic harness).
                TelemetryCoordinator.SetDemand(TelemetryDemand.MainWindow, true);
                TelemetryCoordinator.SetDemand(TelemetryDemand.Overlay, true);

                for (int i = 0; i < 5; i++) TelemetryCoordinator.Poll();

                Assert.AreEqual(5, ecThermalCount, "Condition 3 (Both visible): exactly 5 EC reads (NOT 10 duplicate reads)");
                Assert.AreEqual(5, gpuNvmlCount, "Condition 3 (Both visible): exactly 5 GPU reads (NOT 10 duplicate reads)");
                Assert.AreEqual(5, mwSnapshots.Count, "Condition 3 (Both visible): MainWindow received all 5 snapshots");
                Assert.AreEqual(5, ovSnapshots.Count, "Condition 3 (Both visible): Overlay received all 5 snapshots");
                Assert.AreEqual(5, TelemetryInstrumentation.TotalSnapshotsProduced, "Coordinator produced 5 snapshots");
                Assert.AreEqual(10, TelemetryInstrumentation.TotalSnapshotsDelivered, "Coordinator delivered 10 snapshots (5 to MW, 5 to Overlay)");

                // Reset counters
                ecThermalCount = gpuNvmlCount = cpuCount = ramCount = 0;
                mwSnapshots.Clear();
                ovSnapshots.Clear();
                TelemetryInstrumentation.Reset();

                // --- CONDITION 4: Both Hidden, Auto/Max Fan mode (5 ticks) ---
                TelemetryCoordinator.SetDemand(TelemetryDemand.MainWindow, false);
                TelemetryCoordinator.SetDemand(TelemetryDemand.Overlay, false);
                TelemetryCoordinator.SetDemand(TelemetryDemand.FanController, false);

                for (int i = 0; i < 5; i++) TelemetryCoordinator.Poll();

                Assert.AreEqual(0, ecThermalCount, "Condition 4 (Both hidden, Auto/Max): 0 EC reads (100% idle)");
                Assert.AreEqual(0, gpuNvmlCount, "Condition 4 (Both hidden, Auto/Max): 0 GPU reads (100% idle)");
                Assert.AreEqual(0, TelemetryInstrumentation.TotalSnapshotsProduced, "0 snapshots produced when idle");

                // --- CONDITION 5: Both Hidden, Custom Fan curve (5 ticks) ---
                TelemetryCoordinator.SetDemand(TelemetryDemand.FanController, true);

                for (int i = 0; i < 5; i++) TelemetryCoordinator.Poll();

                Assert.AreEqual(5, ecThermalCount, "Condition 5 (Both hidden, Custom fan): exactly 5 thermal EC reads for curve safety");
                Assert.AreEqual(0, gpuNvmlCount, "Condition 5 (Both hidden, Custom fan): 0 GPU NVML reads (GPU remains asleep)");
                Assert.AreEqual(5, TelemetryInstrumentation.TotalSnapshotsProduced, "5 fan snapshots produced");
            }
            finally
            {
                TelemetryCoordinator.SnapshotAvailable -= mwHandler;
                TelemetryCoordinator.SnapshotAvailable -= ovHandler;
            }
        }

        [TestMethod]
        public void StaleThermalFallback_ReturnsInvalidTemperatures_WhenGateContended()
        {
            long mockTime = 1000 * Stopwatch.Frequency;
            int sampleCount = 0;

            TelemetryCoordinator.ConfigureSampler(
                fullSampler: () =>
                {
                    Interlocked.Increment(ref sampleCount);
                    return new TelemetrySnapshot(
                        TimestampTicks: mockTime,
                        CpuUsage: 25,
                        CpuMaxCoreUsage: 30,
                        CpuPCoreUsage: 25,
                        CpuECoreUsage: 15,
                        RamUsage: 50,
                        RamUsedMb: 8000,
                        CpuTemp: 65,
                        GpuTemp: 60,
                        CpuRpm: 2500,
                        GpuRpm: 2300,
                        CpuFanMaxRpm: 5800,
                        GpuFanMaxRpm: 5600,
                        CpuPkgPowerWatts: 45f,
                        GpuPowerWatts: 60f,
                        CpuVoltage: 1.1f,
                        GpuVoltage: 0.9f,
                        CpuClockMhz: 4500,
                        ECoreClockMhz: 3200,
                        GpuClockMhz: 2000,
                        GpuUsage: 50,
                        VramUsedMb: 2048,
                        VramTotalMb: 8192,
                        VramSpeedMhz: 7000,
                        GpuInUse: true,
                        BatteryPercent: 100f,
                        IsAcConnected: true,
                        PowerSource: PowerSourceKind.Ac,
                        IsValid: true,
                        ThermalTimestampTicks: mockTime);
                },
                fanOnlySampler: () =>
                {
                    Interlocked.Increment(ref sampleCount);
                    return new TelemetrySnapshot(
                        TimestampTicks: 0,
                        CpuUsage: null,
                        CpuMaxCoreUsage: null,
                        CpuPCoreUsage: null,
                        CpuECoreUsage: null,
                        RamUsage: null,
                        RamUsedMb: null,
                        CpuTemp: 65,
                        GpuTemp: 60,
                        CpuRpm: null,
                        GpuRpm: null,
                        CpuFanMaxRpm: 5800,
                        GpuFanMaxRpm: 5600,
                        CpuPkgPowerWatts: null,
                        GpuPowerWatts: null,
                        CpuVoltage: null,
                        GpuVoltage: null,
                        CpuClockMhz: null,
                        ECoreClockMhz: null,
                        GpuClockMhz: null,
                        GpuUsage: null,
                        VramUsedMb: null,
                        VramTotalMb: null,
                        VramSpeedMhz: null,
                        GpuInUse: false,
                        BatteryPercent: 0,
                        IsAcConnected: false,
                        PowerSource: PowerSourceKind.Ac,
                        IsValid: true,
                        IsFanOnly: true,
                        ThermalTimestampTicks: mockTime);
                },
                timestampProvider: () => mockTime);

            // Initial successful read
            var initial = TelemetryCoordinator.RequestFreshFanTemperatures();
            Assert.AreEqual(65, initial.CpuTemp);
            Assert.AreEqual(60, initial.GpuTemp);

            // Now hold the hardware gate to simulate another operation (e.g. lighting or battery charge command)
            using (var gateLease = HardwareOperationGate.TryEnterTelemetry(100))
            {
                Assert.IsNotNull(gateLease, "Should acquire gate lock");

                // Advance mock time by 1.0 second (within 3.0s safety grace interval)
                mockTime += 1 * Stopwatch.Frequency;
                var graceReading = TelemetryCoordinator.RequestFreshFanTemperatures();
                Assert.AreEqual(65, graceReading.CpuTemp, "Within grace interval, cached temperature is permitted");

                // Now advance mock time to 3.5 seconds (exceeding 3.0s MaxThermalStalenessSeconds)
                mockTime += (long)(2.5 * Stopwatch.Frequency);
                Assert.IsTrue(TelemetryCoordinator.IsThermalExpired(3.0), "Thermals must be flagged as expired");

                var staleFallbackReading = TelemetryCoordinator.RequestFreshFanTemperatures();
                Assert.AreEqual(-1, staleFallbackReading.CpuTemp, "Stale thermal read must return -1 to trigger safety fallback");
                Assert.AreEqual(-1, staleFallbackReading.GpuTemp, "Stale thermal read must return -1 to trigger safety fallback");
                Assert.IsFalse(PreySense.Mode.FanCurveProfile.ValidTemperatures(staleFallbackReading.CpuTemp, staleFallbackReading.GpuTemp),
                    "Readings must be invalid for fan curve processing");

                // Verify that emergency cooling latch is PRESERVED when -1 is passed
                var safety = new PreySense.Mode.FanCoolingSafety();
                safety.Evaluate(96, 75); // Activated emergency cooling
                bool emergencyActive = safety.Evaluate(staleFallbackReading.CpuTemp, staleFallbackReading.GpuTemp);
                Assert.IsTrue(emergencyActive, "Emergency cooling latch must remain active even during sensor failure/gate contention");
            }
        }

        [TestMethod]
        public void SeparateFreshness_FanOnlyUpdateDoesNotRefreshStaleFullTelemetry()
        {
            long mockTime = 1000 * Stopwatch.Frequency;

            TelemetryCoordinator.ConfigureSampler(
                fullSampler: () => new TelemetrySnapshot(
                    TimestampTicks: mockTime,
                    CpuUsage: 45,
                    CpuMaxCoreUsage: 60,
                    CpuPCoreUsage: 50,
                    CpuECoreUsage: 35,
                    RamUsage: 55,
                    RamUsedMb: 8800,
                    CpuTemp: 68,
                    GpuTemp: 62,
                    CpuRpm: 2800,
                    GpuRpm: 2600,
                    CpuFanMaxRpm: 5800,
                    GpuFanMaxRpm: 5600,
                    CpuPkgPowerWatts: 45f,
                    GpuPowerWatts: 85f,
                    CpuVoltage: 1.15f,
                    GpuVoltage: 0.95f,
                    CpuClockMhz: 4800,
                    ECoreClockMhz: 3600,
                    GpuClockMhz: 2100,
                    GpuUsage: 80,
                    VramUsedMb: 4000,
                    VramTotalMb: 8192,
                    VramSpeedMhz: 7000,
                    GpuInUse: true,
                    BatteryPercent: 100f,
                    IsAcConnected: true,
                    PowerSource: PowerSourceKind.Ac,
                    IsValid: true,
                    ThermalTimestampTicks: mockTime),
                fanOnlySampler: () => new TelemetrySnapshot(
                    TimestampTicks: 0,
                    CpuUsage: null,
                    CpuMaxCoreUsage: null,
                    CpuPCoreUsage: null,
                    CpuECoreUsage: null,
                    RamUsage: null,
                    RamUsedMb: null,
                    CpuTemp: 72,
                    GpuTemp: 64,
                    CpuRpm: null,
                    GpuRpm: null,
                    CpuFanMaxRpm: 5800,
                    GpuFanMaxRpm: 5600,
                    CpuPkgPowerWatts: null,
                    GpuPowerWatts: null,
                    CpuVoltage: null,
                    GpuVoltage: null,
                    CpuClockMhz: null,
                    ECoreClockMhz: null,
                    GpuClockMhz: null,
                    GpuUsage: null,
                    VramUsedMb: null,
                    VramTotalMb: null,
                    VramSpeedMhz: null,
                    GpuInUse: false,
                    BatteryPercent: 0,
                    IsAcConnected: false,
                    PowerSource: PowerSourceKind.Ac,
                    IsValid: true,
                    IsFanOnly: true,
                    ThermalTimestampTicks: mockTime),
                timestampProvider: () => mockTime);

            // Produce initial full snapshot
            TelemetryCoordinator.SetDemand(TelemetryDemand.MainWindow, true);
            TelemetryCoordinator.Poll();

            var initial = TelemetryCoordinator.CurrentSnapshot;
            Assert.AreEqual(45, initial.CpuUsage);
            Assert.AreEqual(85f, initial.GpuPowerWatts);
            Assert.AreEqual(68, initial.CpuTemp);
            Assert.IsFalse(TelemetryCoordinator.IsSnapshotExpired(), "Initial snapshot should not be expired");

            // Switch to fan-only demand (e.g. MainWindow and Overlay hidden, Custom fan curve active)
            TelemetryCoordinator.SetDemand(TelemetryDemand.MainWindow, false);
            TelemetryCoordinator.SetDemand(TelemetryDemand.FanController, true);

            // Advance time by 30 minutes (1800 seconds)
            mockTime += 1800 * Stopwatch.Frequency;

            // Perform fan-only poll
            TelemetryCoordinator.Poll();

            var fanOnlySnapshot = TelemetryCoordinator.CurrentSnapshot;

            // Thermal metrics are fresh
            Assert.AreEqual(72, fanOnlySnapshot.CpuTemp, "CPU temperature updated by fan sampler");
            Assert.AreEqual(64, fanOnlySnapshot.GpuTemp, "GPU temperature updated by fan sampler");
            Assert.IsFalse(TelemetryCoordinator.IsThermalExpired(), "Thermal metrics must be fresh");

            // Stale full telemetry metrics (CPU usage, GPU power, etc.) MUST be expired to null
            Assert.IsNull(fanOnlySnapshot.CpuUsage, "30-minute-old CPU usage must be expired to null");
            Assert.IsNull(fanOnlySnapshot.GpuPowerWatts, "30-minute-old GPU power must be expired to null");
            Assert.IsNull(fanOnlySnapshot.RamUsage, "30-minute-old RAM usage must be expired to null");
            Assert.AreEqual(0, fanOnlySnapshot.TimestampTicks, "Full telemetry TimestampTicks must be zeroed when expired");
            Assert.IsTrue(TelemetryCoordinator.IsSnapshotExpired(), "Full telemetry snapshot must be reported as expired");
        }

        [TestMethod]
        public void DemandResumption_ConcurrencyAndImmediateSample()
        {
            int pollExecutions = 0;
            TelemetryCoordinator.ConfigureSampler(
                fullSampler: () =>
                {
                    Interlocked.Increment(ref pollExecutions);
                    Thread.Sleep(5); // Simulate hardware read latency
                    return new TelemetrySnapshot { IsValid = true, CpuUsage = 15, CpuTemp = 55, GpuTemp = 50 };
                });

            TelemetryCoordinator.AutoPromptOnDemandResume = true;

            // Resuming demand triggers immediate sample
            TelemetryCoordinator.SetDemand(TelemetryDemand.Overlay, true);
            Thread.Sleep(50); // Allow threadpool task to execute

            Assert.IsTrue(pollExecutions >= 1, "Demand resumption should prompt immediate hardware sample");

            // Verify that 20 parallel threads calling Poll() cannot overlap or create concurrency bugs
            int parallelCompleted = 0;
            Parallel.For(0, 20, _ =>
            {
                TelemetryCoordinator.Poll();
                Interlocked.Increment(ref parallelCompleted);
            });

            Assert.AreEqual(20, parallelCompleted, "All parallel poll attempts completed without crashing");
            Assert.IsTrue(TelemetryCoordinator.CurrentSnapshot.IsValid, "Current snapshot is valid");
        }

        [TestMethod]
        public void ConfigureSampler_LifecycleAndReinitializationSafety()
        {
            int sampler1Calls = 0;
            int sampler2Calls = 0;

            // Initial configuration
            TelemetryCoordinator.ConfigureSampler(
                fullSampler: () =>
                {
                    Interlocked.Increment(ref sampler1Calls);
                    return new TelemetrySnapshot { IsValid = true, CpuUsage = 10, CpuTemp = 50, GpuTemp = 45 };
                });

            TelemetryCoordinator.SetDemand(TelemetryDemand.MainWindow, true);
            TelemetryCoordinator.Poll();

            Assert.AreEqual(1, sampler1Calls, "Sampler 1 executed");

            // Service disposal: Reset
            TelemetryCoordinator.Reset();

            Assert.IsFalse(TelemetryCoordinator.HasActiveDemand, "Demand reset to 0");
            Assert.IsFalse(TelemetryCoordinator.CurrentSnapshot.IsValid, "Snapshot emptied");

            // Polling after reset should do nothing
            TelemetryCoordinator.Poll();
            Assert.AreEqual(1, sampler1Calls, "Sampler 1 should not be called after reset");

            // Reinitialization with new sampler
            TelemetryCoordinator.ConfigureSampler(
                fullSampler: () =>
                {
                    Interlocked.Increment(ref sampler2Calls);
                    return new TelemetrySnapshot { IsValid = true, CpuUsage = 20, CpuTemp = 55, GpuTemp = 50 };
                });

            TelemetryCoordinator.SetDemand(TelemetryDemand.Overlay, true);
            TelemetryCoordinator.Poll();

            Assert.AreEqual(0, sampler1Calls - 1, "Old sampler was never invoked");
            Assert.AreEqual(1, sampler2Calls, "New sampler 2 cleanly took over");
        }

        [TestMethod]
        public void BackgroundPolling_IsRateLimited_ToPreventFastFlips()
        {
            int sampleCount = 0;
            long mockTicks = 10_000_000;

            TelemetryCoordinator.ConfigureSampler(
                fullSampler: () =>
                {
                    Interlocked.Increment(ref sampleCount);
                    return new TelemetrySnapshot { IsValid = true, CpuUsage = 15, CpuTemp = 50, GpuTemp = 45 };
                },
                fanOnlySampler: null,
                timestampProvider: () => mockTicks);

            TelemetryCoordinator.SetDemand(TelemetryDemand.MainWindow, true);

            // First poll produces snapshot
            TelemetryCoordinator.Poll(bypassDebounce: false);
            Assert.AreEqual(1, sampleCount, "Initial poll must execute");

            // Rapid follow-up polls within 500ms (Stopwatch.Frequency / 2 ticks)
            mockTicks += (long)(Stopwatch.Frequency * 0.5);
            TelemetryCoordinator.Poll(bypassDebounce: false);
            TelemetryCoordinator.Poll(bypassDebounce: false);
            Assert.AreEqual(1, sampleCount, "Subsequent demand polls within 1.0s must be throttled to prevent rapid updates");

            // Advance time past 1 second (1000ms + 10ms margin)
            mockTicks += (long)(Stopwatch.Frequency * 0.51); // Total > 1.0s elapsed
            TelemetryCoordinator.Poll(bypassDebounce: false);
            Assert.AreEqual(2, sampleCount, "Poll after 1.0s interval must be permitted");
        }

        [TestMethod]
        public void MasterTimer_BypassesDebounce_ButHonorsConcurrency()
        {
            int sampleCount = 0;
            long mockTicks = 20_000_000;
            var inSampler = new ManualResetEventSlim(false);
            var blockGate = new ManualResetEventSlim(false);

            TelemetryCoordinator.ConfigureSampler(
                fullSampler: () =>
                {
                    Interlocked.Increment(ref sampleCount);
                    inSampler.Set();
                    blockGate.Wait(1000);
                    return new TelemetrySnapshot { IsValid = true, CpuUsage = 25, CpuTemp = 55, GpuTemp = 50 };
                },
                fanOnlySampler: null,
                timestampProvider: () => mockTicks);

            TelemetryCoordinator.SetDemand(TelemetryDemand.MainWindow, true);

            // 1. Initial master tick produces sample
            blockGate.Set();
            TelemetryCoordinator.Poll(bypassDebounce: true);
            Assert.AreEqual(1, sampleCount);

            // 2. Authoritative master timer ticks at 995ms (slightly under 1.0s due to timer jitter)
            mockTicks += (long)(Stopwatch.Frequency * 0.995);
            TelemetryCoordinator.Poll(bypassDebounce: true);
            Assert.AreEqual(2, sampleCount, "Master timer tick must bypass debounce to prevent 2-second stutter");

            // 3. Concurrency protection: if a slow read is in-flight, MasterTimer tick must NOT overlap
            blockGate.Reset();
            inSampler.Reset();
            var task = System.Threading.Tasks.Task.Run(() => TelemetryCoordinator.Poll(bypassDebounce: true));
            Assert.IsTrue(inSampler.Wait(500), "Background task must enter sampler");

            // A second master tick arrives while the slow read is still in flight
            TelemetryCoordinator.Poll(bypassDebounce: true);
            blockGate.Set();
            task.Wait();

            Assert.AreEqual(3, sampleCount, "In-flight poll concurrency protection must prevent overlapping reads");
        }

        [TestMethod]
        public void SetDemand_DoesNotQueueRedundantPoll_IfSnapshotIsFresh()
        {
            int sampleCount = 0;
            long mockTicks = 50_000_000;

            TelemetryCoordinator.ConfigureSampler(
                fullSampler: () =>
                {
                    Interlocked.Increment(ref sampleCount);
                    return new TelemetrySnapshot
                    {
                        IsValid = true,
                        CpuUsage = 20,
                        CpuTemp = 55,
                        TimestampTicks = mockTicks,
                        ThermalTimestampTicks = mockTicks
                    };
                },
                fanOnlySampler: null,
                timestampProvider: () => mockTicks);

            TelemetryCoordinator.AutoPromptOnDemandResume = true;

            // Initially establish demand and sample
            TelemetryCoordinator.SetDemand(TelemetryDemand.MainWindow, true);
            TelemetryCoordinator.Poll(bypassDebounce: true);
            Assert.AreEqual(1, sampleCount);

            // Demand toggles within 200ms (snapshot is fresh, < 1.0s old)
            mockTicks += (long)(Stopwatch.Frequency * 0.2);
            TelemetryCoordinator.SetDemand(TelemetryDemand.MainWindow, false);
            TelemetryCoordinator.SetDemand(TelemetryDemand.MainWindow, true);

            // Give ThreadPool a short window if anything was queued
            Thread.Sleep(50);
            Assert.AreEqual(1, sampleCount, "Fresh snapshot must prevent queuing duplicate poll on resume");
        }
    }
}
