namespace PreySense.Core.Services;

using System;
using System.Diagnostics;
using System.Threading;

[Flags]
public enum TelemetryDemand
{
    None = 0,
    MainWindow = 1 << 0,
    Overlay = 1 << 1,
    FanController = 1 << 2
}

public readonly record struct FrameTelemetry(double? FramesPerSecond, double? FrameTimeMilliseconds, int? ProcessId);

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
    int? RamSpeedMhz = null,
    float? GpuHotspotTemp = null,
    float? GpuPowerLimitWatts = null,
    ulong? GpuThrottleReasons = null,
    int? CpuActiveFrequencyMhz = null,
    int? CpuEffectiveFrequencyMhz = null,
    int? CpuPl1Watts = null,
    int? CpuPl2Watts = null,
    float? GpuMemoryTemp = null,
    int? GpuActiveClockMhz = null,
    double? FramesPerSecond = null,
    double? FrameTimeMilliseconds = null,
    int? FrameProcessId = null,
    string? DisplayGpuName = null)
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
        GpuHotspotTemp: null,
        CpuRpm: null,
        GpuRpm: null,
        CpuFanMaxRpm: -1,
        GpuFanMaxRpm: -1,
        CpuPkgPowerWatts: null,
        GpuPowerWatts: null,
        GpuPowerLimitWatts: null,
        CpuVoltage: null,
        GpuVoltage: null,
        CpuClockMhz: null,
        ECoreClockMhz: null,
        CpuActiveFrequencyMhz: null,
        CpuEffectiveFrequencyMhz: null,
        CpuPl1Watts: null,
        CpuPl2Watts: null,
        GpuClockMhz: null,
        GpuUsage: null,
        VramUsedMb: null,
        VramTotalMb: null,
        VramSpeedMhz: null,
        GpuThrottleReasons: null,
        GpuInUse: false,
        BatteryPercent: float.NaN,
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
                CpuPl1Watts = null,
                CpuPl2Watts = null,
                GpuPowerWatts = null,
                GpuPowerLimitWatts = null,
                CpuVoltage = null,
                GpuVoltage = null,
                CpuClockMhz = null,
                ECoreClockMhz = null,
                CpuActiveFrequencyMhz = null,
                CpuEffectiveFrequencyMhz = null,
                GpuClockMhz = null,
                GpuActiveClockMhz = null,
                GpuUsage = null,
                GpuHotspotTemp = null,
                GpuMemoryTemp = null,
                GpuThrottleReasons = null,
                VramUsedMb = null,
                VramTotalMb = null,
                VramSpeedMhz = null,
                FramesPerSecond = null,
                FrameTimeMilliseconds = null,
                FrameProcessId = null,
                DisplayGpuName = null,
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
        CpuFanMaxRpm: CpuFanMaxRpm,
        GpuFanMaxRpm: GpuFanMaxRpm,
        BatteryPercent: BatteryPercent,
        IsAcConnected: IsAcConnected,
        BatteryDischargeRateMw: 0,
        CpuPkgPowerWatts: CpuPkgPowerWatts,
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
    private static Func<TelemetryMetrics, bool, TelemetrySnapshot>? _metricSampler;
    private static Func<FrameTelemetry>? _frameSampler;

    /// <summary>
    /// Shared hardware telemetry snapshot for fan control, power transitions, and both UIs.
    /// Fired immediately upon completion of a hardware poll cycle.
    /// </summary>
    public static event Action<TelemetrySnapshot>? SnapshotAvailable;

    // Lightweight overlay visibility/FPS work shares the same timer, even while sensors are idle.
    public static event Action? Heartbeat;
    private static volatile bool _overlayActive;
    private static TelemetryMetrics _overlayMetrics;
    public const TelemetryMetrics MainWindowMetrics = TelemetryMetrics.Temperatures | TelemetryMetrics.CpuFanRpm | TelemetryMetrics.GpuFanRpm | TelemetryMetrics.DisplayAdapter;

    public static TelemetryMetrics RequestedMetrics
    {
        get
        {
            lock (SyncLock)
            {
                var demand = (TelemetryDemand)_demandMask;
                var metrics = _overlayActive ? TelemetryMetrics.FrameRate : TelemetryMetrics.None;
                if (demand.HasFlag(TelemetryDemand.MainWindow)) metrics |= MainWindowMetrics;
                if (demand.HasFlag(TelemetryDemand.Overlay)) metrics |= _overlayMetrics;
                if (demand.HasFlag(TelemetryDemand.FanController))
                    metrics |= TelemetryMetrics.Temperatures;
                return metrics;
            }
        }
    }

    public static void SetOverlayMetrics(TelemetryMetrics metrics)
    {
        lock (SyncLock)
        {
            _overlayMetrics = metrics & TelemetryMetrics.Hardware;
            ScheduleNextMasterTick();
        }
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

    public static bool HasActiveDemand => Volatile.Read(ref _demandMask) != 0 || _overlayActive;
    public static bool HasUiDemand => _overlayActive || (Volatile.Read(ref _demandMask) & (int)(TelemetryDemand.MainWindow | TelemetryDemand.Overlay)) != 0;
    public static bool HasFanOnlyDemand => HasActiveDemand && !HasUiDemand;
    public static TelemetryDemand ActiveDemand => (TelemetryDemand)Volatile.Read(ref _demandMask);

    static TelemetryCoordinator()
    {
        // Timer ticks and explicit requests use the same rate and concurrency limits.
        MasterTimer.Elapsed += (_, _) => RunMasterCycle();
    }

    internal static void RunMasterCycle()
    {
        if (Interlocked.Exchange(ref _cycleInProgress, 1) != 0) return;
        try
        {
            var heartbeat = Heartbeat;
            if (heartbeat != null)
                foreach (Action subscriber in heartbeat.GetInvocationList())
                {
                    try { subscriber(); }
                    catch (Exception ex) { AppLogger.Log($"Telemetry heartbeat failed: {ex.Message}"); }
                }
            // An overlay refresh failure must not prevent temperature sampling for custom fans.
            Poll();
        }
        catch (Exception ex) { AppLogger.Log($"Telemetry cycle failed: {ex.Message}"); }
        finally
        {
            Volatile.Write(ref _cycleInProgress, 0);
            ScheduleNextMasterTick();
        }
    }

    private static void ScheduleNextMasterTick()
    {
        lock (SyncLock)
        {
            bool needsHardware = (RequestedMetrics & TelemetryMetrics.Hardware) != TelemetryMetrics.None &&
                (_metricSampler != null || _customFullSampler != null || _customFanOnlySampler != null);
            if (!_overlayActive && !needsHardware) { MasterTimer.Stop(); return; }
            if (Volatile.Read(ref _cycleInProgress) == 0 && !MasterTimer.Enabled) MasterTimer.Start();
        }
    }

    // Active means the visible overlay actually requests FPS, not merely that a window exists.
    public static void SetOverlayActive(bool active)
    {
        lock (SyncLock)
        {
            _overlayActive = active;
            ScheduleNextMasterTick();
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
            ScheduleNextMasterTick();
        }
    }

    public static void ConfigureMetricSampler(Func<TelemetryMetrics, bool, TelemetrySnapshot> sampler, Func<long>? timestampProvider = null)
    {
        ArgumentNullException.ThrowIfNull(sampler);
        lock (SyncLock)
        {
            _metricSampler = sampler;
            _customFullSampler = _customFanOnlySampler = null;
            _customTimestampProvider = timestampProvider;
            ScheduleNextMasterTick();
        }
    }

    public static void ConfigureFrameSampler(Func<FrameTelemetry>? sampler)
    {
        lock (SyncLock)
        {
            _frameSampler = sampler;
            ScheduleNextMasterTick();
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

            _metricSampler = null;
            ScheduleNextMasterTick();
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
            _metricSampler = null;
            _frameSampler = null;
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
            ? new HardwareTelemetry(-1, -1, -1, -1, -1, -1, float.NaN, false, 0, PowerSource: PowerSourceKind.Unknown)
            : CurrentSnapshot.ToHardwareTelemetry();

    /// <summary>Samples once at most per second, including explicit requests and timer ticks.</summary>
    public static void Poll()
    {
        if (!HasActiveDemand || RequestedMetrics == TelemetryMetrics.None) return;
        if (Interlocked.Exchange(ref _pollInProgress, 1) != 0) return;

        Func<TelemetrySnapshot>? fullSampler;
        Func<TelemetrySnapshot>? fanOnlySampler;
        Func<TelemetryMetrics, bool, TelemetrySnapshot>? metricSampler;
        Func<FrameTelemetry>? frameSampler;
        TelemetryMetrics metrics;
        bool uiDemand;
        bool wantsFrames;
        Func<long>? timestampProvider;
        lock (SyncLock)
        {
            timestampProvider = _customTimestampProvider;
            metrics = RequestedMetrics;
            wantsFrames = (metrics & TelemetryMetrics.FrameRate) != 0;
            metrics &= TelemetryMetrics.Hardware;
            uiDemand = HasUiDemand;
            fullSampler = _customFullSampler;
            fanOnlySampler = _customFanOnlySampler;
            metricSampler = _metricSampler;
            frameSampler = wantsFrames ? _frameSampler : null;
            if ((!wantsFrames && metrics == TelemetryMetrics.None) ||
                (!wantsFrames && metricSampler == null && fullSampler == null && fanOnlySampler == null))
            {
                Volatile.Write(ref _pollInProgress, 0);
                return;
            }
            long now = _customTimestampProvider != null ? _customTimestampProvider() : Stopwatch.GetTimestamp();
            if (_lastPollTicks > 0 && (double)(now - _lastPollTicks) / Stopwatch.Frequency < MinDemandPollIntervalSeconds)
            {
                Volatile.Write(ref _pollInProgress, 0);
                return;
            }
        }

        var snapshot = TelemetrySnapshot.Empty;
        bool producedSnapshot = false;
        bool sampled = false;
        try
        {
            if (metrics != TelemetryMetrics.None && (metricSampler != null || fullSampler != null || fanOnlySampler != null))
            {
                using var lease = HardwareOperationGate.TryEnterTelemetry(50);
                if (lease == null)
                {
                    lock (SyncLock)
                    {
                        snapshot = _currentSnapshot;
                        if (IsThermalExpired(MaxThermalStalenessSeconds))
                        {
                            long now = _customTimestampProvider != null ? _customTimestampProvider() : Stopwatch.GetTimestamp();
                            snapshot = snapshot.ExpireFullTelemetryIfStale(now, StaleSnapshotSeconds, _customTimestampProvider)
                                .WithFanTemperatures(-1, -1, 0) with { IsValid = false };
                            producedSnapshot = true;
                        }
                    }
                }
                else
                {
                    sampled = true;
                    snapshot = metricSampler != null ? metricSampler(metrics, !uiDemand)
                        : uiDemand ? (fullSampler ?? fanOnlySampler!)() : (fanOnlySampler ?? fullSampler!)();
                    if (!uiDemand && snapshot.IsFanOnly)
                    {
                        lock (SyncLock)
                        {
                            if (_currentSnapshot.IsValid)
                            {
                                long now = _customTimestampProvider != null ? _customTimestampProvider() : Stopwatch.GetTimestamp();
                                snapshot = _currentSnapshot.ExpireFullTelemetryIfStale(now, StaleSnapshotSeconds, _customTimestampProvider)
                                    .WithFanTemperatures(snapshot.CpuTemp, snapshot.GpuTemp, snapshot.EffectiveThermalTicks);
                            }
                        }
                    }
                    producedSnapshot = true;
                }
            }
            if (wantsFrames)
            {
                sampled = true;
                // FPS counters and hardware sensors are sampled only here, on the same one-second path.
                FrameTelemetry frame = default;
                try { frame = frameSampler?.Invoke() ?? default; }
                catch (Exception ex) { AppLogger.Error($"Frame telemetry failed: {ex}"); }
                snapshot = snapshot with
                {
                    FramesPerSecond = frame.FramesPerSecond,
                    FrameTimeMilliseconds = frame.FrameTimeMilliseconds,
                    FrameProcessId = frame.ProcessId,
                    TimestampTicks = snapshot.TimestampTicks > 0 ? snapshot.TimestampTicks : timestampProvider?.Invoke() ?? Stopwatch.GetTimestamp(),
                    IsValid = snapshot.IsValid || metrics == TelemetryMetrics.None,
                    IsFanOnly = false
                };
                producedSnapshot = true;
            }
            else snapshot = snapshot with { FramesPerSecond = null, FrameTimeMilliseconds = null, FrameProcessId = null };
            if (producedSnapshot)
            {
                lock (SyncLock)
                {
                    _currentSnapshot = snapshot;
                    // A busy hardware gate only invalidates stale data; it does not consume a sampling interval.
                    if (sampled) _lastPollTicks = _customTimestampProvider != null ? _customTimestampProvider() : Stopwatch.GetTimestamp();
                }
                Interlocked.Increment(ref TelemetryInstrumentation.TotalSnapshotsProduced);
            }
        }
        finally { Volatile.Write(ref _pollInProgress, 0); }

        if (!producedSnapshot) return;
        var handler = SnapshotAvailable;
        if (handler == null) return;
        foreach (Action<TelemetrySnapshot> subscriber in handler.GetInvocationList())
        {
            try
            {
                subscriber(snapshot);
                Interlocked.Increment(ref TelemetryInstrumentation.TotalSnapshotsDelivered);
            }
            catch (Exception ex) { Debug.WriteLine($"[TelemetryCoordinator] Subscriber failed: {ex.Message}"); }
        }
    }
}
