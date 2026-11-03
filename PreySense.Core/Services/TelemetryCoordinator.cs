namespace PreySense.Core.Services;

using System;
using System.Diagnostics;
using System.Threading;

[Flags]
public enum TelemetryDemand
{
    None = 0,
    Overlay = 1 << 1,
    FanController = 1 << 2
}

public readonly record struct TelemetrySnapshot(
    long TimestampTicks,
    int? CpuUsage,
    int? CpuMaxCoreUsage,
    int? CpuPCoreUsage,
    int? CpuECoreUsage,
    int? RamUsage,
    int? RamUsedMb,
    int? CpuTemp,
    int? GpuTemp,
    int? CpuRpm,
    int? GpuRpm,
    int CpuFanMaxRpm,
    int GpuFanMaxRpm,
    float? CpuPkgPowerWatts,
    float? GpuPowerWatts,
    float? CpuVoltage,
    float? GpuVoltage,
    int? CpuClockMhz,
    int? ECoreClockMhz,
    int? GpuClockMhz,
    int? GpuUsage,
    int? VramUsedMb,
    int? VramTotalMb,
    int? VramSpeedMhz,
    bool GpuInUse,
    float BatteryPercent,
    bool IsAcConnected,
    PowerSourceKind PowerSource,
    bool IsValid,
    bool IsFanOnly = false,
    long ThermalTimestampTicks = 0,
    int? RamSpeedMhz = null)
{
    public long EffectiveThermalTicks => ThermalTimestampTicks > 0 ? ThermalTimestampTicks : TimestampTicks;

    public static readonly TelemetrySnapshot Empty = new(
        TimestampTicks: 0,
        CpuUsage: null,
        CpuMaxCoreUsage: null,
        CpuPCoreUsage: null,
        CpuECoreUsage: null,
        RamUsage: null,
        RamUsedMb: null,
        CpuTemp: null,
        GpuTemp: null,
        CpuRpm: null,
        GpuRpm: null,
        CpuFanMaxRpm: 6800,
        GpuFanMaxRpm: 6800,
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
        BatteryPercent: 0f,
        IsAcConnected: false,
        PowerSource: PowerSourceKind.Ac,
        IsValid: false,
        IsFanOnly: false,
        ThermalTimestampTicks: 0);

    public TelemetrySnapshot WithFanTemperatures(int? cpuTemp, int? gpuTemp, long fanTimestampTicks) => this with
    {
        ThermalTimestampTicks = fanTimestampTicks,
        CpuTemp = cpuTemp,
        GpuTemp = gpuTemp,
        IsValid = IsValid || (cpuTemp > 0 || gpuTemp > 0),
        IsFanOnly = true
    };

    public TelemetrySnapshot ExpireFullTelemetryIfStale(long now, double maxAgeSeconds = 4.0, Func<long>? timestampProvider = null)
    {
        if (TimestampTicks <= 0) return this;
        double age = timestampProvider != null
            ? (double)(now - TimestampTicks) / Stopwatch.Frequency
            : Stopwatch.GetElapsedTime(TimestampTicks, now).TotalSeconds;

        if (age > maxAgeSeconds)
        {
            return this with
            {
                CpuUsage = null,
                CpuMaxCoreUsage = null,
                CpuPCoreUsage = null,
                CpuECoreUsage = null,
                RamUsage = null,
                RamUsedMb = null,
                CpuPkgPowerWatts = null,
                GpuPowerWatts = null,
                CpuVoltage = null,
                GpuVoltage = null,
                CpuClockMhz = null,
                ECoreClockMhz = null,
                GpuClockMhz = null,
                GpuUsage = null,
                VramUsedMb = null,
                VramTotalMb = null,
                VramSpeedMhz = null,
                TimestampTicks = 0
            };
        }
        return this;
    }

    public HardwareTelemetry ToHardwareTelemetry() => new(
        CpuTemp: CpuTemp ?? -1,
        GpuTemp: GpuTemp ?? -1,
        CpuRpm: CpuRpm ?? -1,
        GpuRpm: GpuRpm ?? -1,
        CpuFanMaxRpm: CpuFanMaxRpm > 0 ? CpuFanMaxRpm : 5800,
        GpuFanMaxRpm: GpuFanMaxRpm > 0 ? GpuFanMaxRpm : 5600,
        BatteryPercent: BatteryPercent,
        IsAcConnected: IsAcConnected,
        BatteryDischargeRateMw: 0,
        CpuPkgPowerWatts: CpuPkgPowerWatts ?? 0f,
        PowerSource: PowerSource
    );
}

public static class TelemetryInstrumentation
{
    public static long EcThermalReadCount;
    public static long GpuTelemetryReadCount;
    public static long CpuTelemetryReadCount;
    public static long RamTelemetryReadCount;
    public static long TotalSnapshotsProduced;
    public static long TotalSnapshotsDelivered;

    public static void Reset()
    {
        Interlocked.Exchange(ref EcThermalReadCount, 0);
        Interlocked.Exchange(ref GpuTelemetryReadCount, 0);
        Interlocked.Exchange(ref CpuTelemetryReadCount, 0);
        Interlocked.Exchange(ref RamTelemetryReadCount, 0);
        Interlocked.Exchange(ref TotalSnapshotsProduced, 0);
        Interlocked.Exchange(ref TotalSnapshotsDelivered, 0);
    }
}

/// <summary>
/// Central, demand-driven telemetry coordinator for PreySense.
/// Replaces duplicate polling timers in MainWindow and HardwareOverlay with a single,
/// synchronized hardware sampling producer that distributes immutable snapshots to subscribers.
/// </summary>
public static class TelemetryCoordinator
{
    private static readonly object SyncLock = new();
    private static readonly System.Timers.Timer MasterTimer = new(1000) { AutoReset = false };
    private static int _demandMask;
    private static int _pollInProgress;
    private static int _cycleInProgress;
    private static TelemetrySnapshot _currentSnapshot = TelemetrySnapshot.Empty;
    private static long _lastPollTicks;

    private const double StaleSnapshotSeconds = 4.0;
    private const double MaxThermalStalenessSeconds = 3.0;
    private const double MinDemandPollIntervalSeconds = 1.0;

    private static Func<TelemetrySnapshot>? _customFullSampler;
    private static Func<TelemetrySnapshot>? _customFanOnlySampler;
    private static Func<long>? _customTimestampProvider;

    /// <summary>
    /// Shared hardware telemetry snapshot for fan control, power transitions, and both UIs.
    /// Fired immediately upon completion of a hardware poll cycle.
    /// </summary>
    public static event Action<TelemetrySnapshot>? SnapshotAvailable;

    // Lightweight overlay visibility/FPS work shares the same timer, even while sensors are idle.
    public static event Action? Heartbeat;
    private static bool _overlayActive;
    private static TelemetryMetrics _overlayMetrics;

    public static TelemetryMetrics RequestedMetrics
    {
        get
        {
            lock (SyncLock)
            {
                var demand = (TelemetryDemand)_demandMask;
                var metrics = TelemetryMetrics.None;
                if (demand.HasFlag(TelemetryDemand.Overlay)) metrics |= _overlayMetrics;
                if (demand.HasFlag(TelemetryDemand.FanController))
                    metrics |= TelemetryMetrics.Temperatures;
                return metrics;
            }
        }
    }

    public static void SetOverlayMetrics(TelemetryMetrics metrics)
    {
        lock (SyncLock) _overlayMetrics = metrics;
    }


    public static TelemetrySnapshot CurrentSnapshot
    {
        get
        {
            lock (SyncLock)
            {
                if (_currentSnapshot.IsValid)
                {
                    long now = _customTimestampProvider != null ? _customTimestampProvider() : Stopwatch.GetTimestamp();
                    return _currentSnapshot.ExpireFullTelemetryIfStale(now, StaleSnapshotSeconds, _customTimestampProvider);
                }
                return _currentSnapshot;
            }
        }
    }

    public static bool IsSnapshotExpired(double maxAgeSeconds = StaleSnapshotSeconds)
    {
        lock (SyncLock)
        {
            if (!_currentSnapshot.IsValid || _currentSnapshot.TimestampTicks <= 0)
                return true;

            long now = _customTimestampProvider != null ? _customTimestampProvider() : Stopwatch.GetTimestamp();
            double ageSeconds = _customTimestampProvider != null
                ? (double)(now - _currentSnapshot.TimestampTicks) / Stopwatch.Frequency
                : Stopwatch.GetElapsedTime(_currentSnapshot.TimestampTicks, now).TotalSeconds;

            return ageSeconds > maxAgeSeconds;
        }
    }

    public static bool IsThermalExpired(double maxAgeSeconds = MaxThermalStalenessSeconds)
    {
        lock (SyncLock)
        {
            if (!_currentSnapshot.IsValid)
                return true;

            long ticks = _currentSnapshot.EffectiveThermalTicks;
            if (ticks <= 0) return true;

            long now = _customTimestampProvider != null ? _customTimestampProvider() : Stopwatch.GetTimestamp();
            double ageSeconds = _customTimestampProvider != null
                ? (double)(now - ticks) / Stopwatch.Frequency
                : Stopwatch.GetElapsedTime(ticks, now).TotalSeconds;

            return ageSeconds > maxAgeSeconds;
        }
    }

    public static bool HasActiveDemand => Volatile.Read(ref _demandMask) != 0;
    public static bool HasUiDemand => (Volatile.Read(ref _demandMask) & (int)TelemetryDemand.Overlay) != 0;
    public static bool HasFanOnlyDemand => HasActiveDemand && !HasUiDemand;
    public static TelemetryDemand ActiveDemand => (TelemetryDemand)Volatile.Read(ref _demandMask);

    static TelemetryCoordinator()
    {
        // Timer ticks and explicit requests use the same rate and concurrency limits.
        MasterTimer.Elapsed += (_, _) =>
        {
            if (Interlocked.Exchange(ref _cycleInProgress, 1) != 0) return;
            try
            {
                Heartbeat?.Invoke();
                Poll();
            }
            catch (Exception ex) { AppLogger.Log($"Telemetry cycle failed: {ex.Message}"); }
            finally
            {
                Volatile.Write(ref _cycleInProgress, 0);
                ScheduleNextMasterTick();
            }
        };
    }

    private static void ScheduleNextMasterTick()
    {
        lock (SyncLock)
        {
            if (Volatile.Read(ref _cycleInProgress) == 0 && !MasterTimer.Enabled &&
                (_overlayActive || (HasActiveDemand && (_customFullSampler != null || _customFanOnlySampler != null))))
            {
                MasterTimer.Start();
            }
        }
    }

    public static void SetOverlayActive(bool active)
    {
        lock (SyncLock)
        {
            _overlayActive = active;
            if (active) ScheduleNextMasterTick();
            else if (!HasActiveDemand) MasterTimer.Stop();
        }
    }

    public static void SetDemand(TelemetryDemand demand, bool active)
    {
        lock (SyncLock)
        {
            int oldMask = _demandMask;
            int newMask = active ? oldMask | (int)demand : oldMask & ~(int)demand;
            if (oldMask == newMask) return;
            Volatile.Write(ref _demandMask, newMask);
            if (oldMask == 0 && newMask != 0) ScheduleNextMasterTick();
            else if (newMask == 0 && !_overlayActive) MasterTimer.Stop();
        }
    }

    public static void ConfigureSampler(
        Func<TelemetrySnapshot>? fullSampler,
        Func<TelemetrySnapshot>? fanOnlySampler = null,
        Func<long>? timestampProvider = null)
    {
        lock (SyncLock)
        {
            _customFullSampler = fullSampler;
            _customFanOnlySampler = fanOnlySampler;
            _customTimestampProvider = timestampProvider;

            bool hasSampler = fullSampler != null || fanOnlySampler != null;
            if (HasActiveDemand && hasSampler)
            {
                ScheduleNextMasterTick();
            }
            else if (!hasSampler && !_overlayActive)
            {
                MasterTimer.Stop();
            }
        }
    }

    public static void Reset()
    {
        lock (SyncLock)
        {
            MasterTimer.Stop();

            SnapshotAvailable = null;
            Heartbeat = null;
            _overlayActive = false;
            _overlayMetrics = TelemetryMetrics.None;
            _customFullSampler = null;
            _customFanOnlySampler = null;
            _customTimestampProvider = null;
            _currentSnapshot = TelemetrySnapshot.Empty;
            _lastPollTicks = 0;
            Volatile.Write(ref _demandMask, 0);
            Interlocked.Exchange(ref _pollInProgress, 0);
            TelemetryInstrumentation.Reset();
        }
    }

    public static void InvalidateCurrentSnapshot()
    {
        lock (SyncLock)
        {
            _currentSnapshot = TelemetrySnapshot.Empty;
        }
    }

    /// <summary>Reads the shared thermal snapshot without issuing hardware queries.</summary>
    public static HardwareTelemetry GetFanTemperaturesSnapshot() =>
        IsThermalExpired(MaxThermalStalenessSeconds)
            ? new HardwareTelemetry(-1, -1, -1, -1, 5800, 5600, 0, false, 0)
            : CurrentSnapshot.ToHardwareTelemetry();

    /// <summary>Samples once at most per second, including explicit requests and timer ticks.</summary>
    public static void Poll()
    {
        if (!HasActiveDemand || RequestedMetrics == TelemetryMetrics.None)
            return;

        if (Interlocked.Exchange(ref _pollInProgress, 1) != 0)
            return;

        Func<TelemetrySnapshot>? fullSampler;
        Func<TelemetrySnapshot>? fanOnlySampler;

        lock (SyncLock)
        {
            fullSampler = _customFullSampler;
            fanOnlySampler = _customFanOnlySampler;

            if (fullSampler == null && fanOnlySampler == null)
            {
                Volatile.Write(ref _pollInProgress, 0);
                return;
            }

            if (_lastPollTicks > 0)
            {
                long now = _customTimestampProvider != null ? _customTimestampProvider() : Stopwatch.GetTimestamp();
                double elapsed = _customTimestampProvider != null
                    ? (double)(now - _lastPollTicks) / Stopwatch.Frequency
                    : Stopwatch.GetElapsedTime(_lastPollTicks, now).TotalSeconds;

                if (elapsed < MinDemandPollIntervalSeconds)
                {
                    Volatile.Write(ref _pollInProgress, 0);
                    return;
                }
            }
        }

        TelemetrySnapshot snapshot;
        bool uiDemand = HasUiDemand;
        bool producedSnapshot = false;

        try
        {
            // Confine hardware gate lock strictly to the hardware sampling phase
            using (var lease = HardwareOperationGate.TryEnterTelemetry(50))
            {
                if (lease == null)
                {
                    // If gate is contended and thermal readings have expired (> 3.0s),
                    // notify subscribers with an invalid thermal snapshot so fan curves skip the update.
                    if (IsThermalExpired(MaxThermalStalenessSeconds))
                    {
                        lock (SyncLock)
                        {
                            long now = _customTimestampProvider != null ? _customTimestampProvider() : Stopwatch.GetTimestamp();
                            snapshot = _currentSnapshot
                                .ExpireFullTelemetryIfStale(now, StaleSnapshotSeconds, _customTimestampProvider)
                                .WithFanTemperatures(-1, -1, 0) with { IsValid = false };
                            _currentSnapshot = snapshot;
                        }
                        producedSnapshot = true;
                    }
                    else
                    {
                        return;
                    }
                }
                else
                {
                    if (uiDemand)
                    {
                        snapshot = (fullSampler ?? fanOnlySampler!)();
                    }
                    else
                    {
                        snapshot = (fanOnlySampler ?? fullSampler!)();
                        if (snapshot.IsFanOnly)
                        {
                            lock (SyncLock)
                            {
                                if (_currentSnapshot.IsValid)
                                {
                                    long now = _customTimestampProvider != null ? _customTimestampProvider() : Stopwatch.GetTimestamp();
                                    var baseSnapshot = _currentSnapshot.ExpireFullTelemetryIfStale(now, StaleSnapshotSeconds, _customTimestampProvider);
                                    snapshot = baseSnapshot.WithFanTemperatures(
                                        snapshot.CpuTemp,
                                        snapshot.GpuTemp,
                                        snapshot.EffectiveThermalTicks);
                                }
                            }
                        }
                    }

                    lock (SyncLock)
                    {
                        _currentSnapshot = snapshot;
                        _lastPollTicks = _customTimestampProvider != null ? _customTimestampProvider() : Stopwatch.GetTimestamp();
                    }

                    Interlocked.Increment(ref TelemetryInstrumentation.TotalSnapshotsProduced);
                    producedSnapshot = true;
                }
            }
        }
        finally
        {
            Volatile.Write(ref _pollInProgress, 0);
        }

        if (producedSnapshot)
        {
            // Publish completed snapshot to subscribers outside of any hardware locks or gates
            var handler = SnapshotAvailable;
            if (handler != null)
            {
                foreach (Action<TelemetrySnapshot> subscriber in handler.GetInvocationList())
                {
                    try
                    {
                        subscriber(snapshot);
                        Interlocked.Increment(ref TelemetryInstrumentation.TotalSnapshotsDelivered);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[TelemetryCoordinator] Subscriber failed: {ex.Message}");
                    }
                }
            }

        }
    }
}
