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
    long ThermalTimestampTicks = 0)
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
    private static readonly System.Timers.Timer MasterTimer = new(1000) { AutoReset = true };
    private static int _demandMask;
    private static int _pollInProgress;
    private static TelemetrySnapshot _currentSnapshot = TelemetrySnapshot.Empty;
    private static long _lastPollTicks;

    private const double StaleSnapshotSeconds = 4.0;
    private const double MaxThermalStalenessSeconds = 3.0;
    private const double MinBackgroundPollIntervalSeconds = 0.850;

    private static Func<TelemetrySnapshot>? _customFullSampler;
    private static Func<TelemetrySnapshot>? _customFanOnlySampler;
    private static Func<long>? _customTimestampProvider;

    public static event Action<TelemetrySnapshot>? SnapshotAvailable;

    public static bool AutoPromptOnDemandResume { get; set; } = false;

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
    public static bool HasUiDemand => (Volatile.Read(ref _demandMask) & (int)(TelemetryDemand.MainWindow | TelemetryDemand.Overlay)) != 0;
    public static bool HasFanOnlyDemand => Volatile.Read(ref _demandMask) == (int)TelemetryDemand.FanController;
    public static TelemetryDemand ActiveDemand => (TelemetryDemand)Volatile.Read(ref _demandMask);

    static TelemetryCoordinator()
    {
        MasterTimer.Elapsed += (_, _) => Poll(force: false);
    }

    public static void SetInterval(double intervalMs)
    {
        lock (SyncLock)
        {
            MasterTimer.Interval = Math.Clamp(intervalMs, 250, 5000);
        }
    }

    public static void SetDemand(TelemetryDemand demand, bool active)
    {
        int oldMask, newMask;
        do
        {
            oldMask = Volatile.Read(ref _demandMask);
            newMask = active ? (oldMask | (int)demand) : (oldMask & ~(int)demand);
        } while (Interlocked.CompareExchange(ref _demandMask, newMask, oldMask) != oldMask);

        bool hadDemand = oldMask != 0;
        bool hasDemand = newMask != 0;
        bool hadUiDemand = (oldMask & (int)(TelemetryDemand.MainWindow | TelemetryDemand.Overlay)) != 0;
        bool hasUiDemand = (newMask & (int)(TelemetryDemand.MainWindow | TelemetryDemand.Overlay)) != 0;

        if (hasDemand && !hadDemand)
        {
            if (IsSnapshotExpired())
            {
                InvalidateCurrentSnapshot();
            }

            bool hasSampler;
            lock (SyncLock)
            {
                hasSampler = _customFullSampler != null || _customFanOnlySampler != null;
                if (hasSampler)
                {
                    MasterTimer.Start();
                }
            }

            if (hasSampler && AutoPromptOnDemandResume && IsSnapshotExpired(1.0))
            {
                ThreadPool.QueueUserWorkItem(_ => Poll(force: false));
            }
        }
        else if (!hasDemand && hadDemand)
        {
            lock (SyncLock)
            {
                MasterTimer.Stop();
            }
        }
        else if (!hadUiDemand && hasUiDemand)
        {
            // UI demand just resumed while background fan demand was already running.
            // Invalidate stale full telemetry metrics so 30-minute old metrics aren't displayed.
            if (IsSnapshotExpired())
            {
                lock (SyncLock)
                {
                    long now = _customTimestampProvider != null ? _customTimestampProvider() : Stopwatch.GetTimestamp();
                    _currentSnapshot = _currentSnapshot.ExpireFullTelemetryIfStale(now, StaleSnapshotSeconds, _customTimestampProvider);
                }
            }

            bool hasSampler;
            lock (SyncLock)
            {
                hasSampler = _customFullSampler != null;
            }

            if (hasSampler && AutoPromptOnDemandResume && IsSnapshotExpired(1.0))
            {
                ThreadPool.QueueUserWorkItem(_ => Poll(force: false));
            }
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
                MasterTimer.Start();
            }
            else if (!hasSampler)
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
            _customFullSampler = null;
            _customFanOnlySampler = null;
            _customTimestampProvider = null;
            _currentSnapshot = TelemetrySnapshot.Empty;
            _lastPollTicks = 0;
            Volatile.Write(ref _demandMask, 0);
            Interlocked.Exchange(ref _pollInProgress, 0);
            AutoPromptOnDemandResume = false;
            TelemetryInstrumentation.Reset();
        }
    }

    public static void InvalidateCurrentSnapshot()
    {
        lock (SyncLock)
        {
            _currentSnapshot = TelemetrySnapshot.Empty;
            _lastPollTicks = 0;
        }
    }

    /// <summary>
    /// Independent, thread-safe temperature query for the fan curve controller.
    /// Allows thermal safety/emergency checks to obtain fresh readings without relying on UI timers.
    /// If gate contention occurs and cached thermal readings are stale (> 3.0s), returns invalid
    /// temperatures (-1, -1) so the fan controller invokes conservative cooling fallback.
    /// </summary>
    public static HardwareTelemetry RequestFreshFanTemperatures()
    {
        using var lease = HardwareOperationGate.TryEnterTelemetry(100);
        if (lease == null)
        {
            // If gate is busy, check freshness of cached thermal reading.
            // If cached temperatures are stale (> 3.0s), return invalid readings (-1, -1)
            // so the fan controller can apply conservative cooling fallback rather than
            // blindly trusting stale temperatures.
            if (IsThermalExpired(MaxThermalStalenessSeconds))
            {
                return new HardwareTelemetry(-1, -1, -1, -1, 5800, 5600, 0, false, 0);
            }
            return CurrentSnapshot.ToHardwareTelemetry();
        }

        TelemetrySnapshot sample;
        lock (SyncLock)
        {
            if (_customFanOnlySampler != null)
            {
                sample = _customFanOnlySampler();
            }
            else if (_customFullSampler != null)
            {
                sample = _customFullSampler();
            }
            else
            {
                return CurrentSnapshot.ToHardwareTelemetry();
            }

            if (_currentSnapshot.IsValid && sample.IsFanOnly)
            {
                long now = _customTimestampProvider != null ? _customTimestampProvider() : Stopwatch.GetTimestamp();
                var baseSnapshot = _currentSnapshot.ExpireFullTelemetryIfStale(now, StaleSnapshotSeconds, _customTimestampProvider);
                _currentSnapshot = baseSnapshot.WithFanTemperatures(
                    sample.CpuTemp,
                    sample.GpuTemp,
                    sample.EffectiveThermalTicks);
            }
            else
            {
                _currentSnapshot = sample;
            }
        }
        Interlocked.Increment(ref TelemetryInstrumentation.EcThermalReadCount);
        return sample.ToHardwareTelemetry();
    }

    public static void Poll(bool force = true)
    {
        if (!HasActiveDemand)
            return;

        lock (SyncLock)
        {
            if (_customFullSampler == null && _customFanOnlySampler == null)
                return;

            if (!force && _lastPollTicks > 0)
            {
                long now = _customTimestampProvider != null ? _customTimestampProvider() : Stopwatch.GetTimestamp();
                double elapsed = _customTimestampProvider != null
                    ? (double)(now - _lastPollTicks) / Stopwatch.Frequency
                    : Stopwatch.GetElapsedTime(_lastPollTicks, now).TotalSeconds;

                if (elapsed < MinBackgroundPollIntervalSeconds)
                    return;
            }
        }

        if (Interlocked.Exchange(ref _pollInProgress, 1) != 0)
            return;

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
                    // notify subscribers with an invalid thermal snapshot so safety fallback can take over.
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
                    lock (SyncLock)
                    {
                        if (uiDemand)
                        {
                            snapshot = _customFullSampler != null ? _customFullSampler() : DefaultFullSampler();
                        }
                        else
                        {
                            snapshot = _customFanOnlySampler != null ? _customFanOnlySampler() : DefaultFanOnlySampler();
                            if (_currentSnapshot.IsValid && snapshot.IsFanOnly)
                            {
                                long now = _customTimestampProvider != null ? _customTimestampProvider() : Stopwatch.GetTimestamp();
                                var baseSnapshot = _currentSnapshot.ExpireFullTelemetryIfStale(now, StaleSnapshotSeconds, _customTimestampProvider);
                                snapshot = baseSnapshot.WithFanTemperatures(
                                    snapshot.CpuTemp,
                                    snapshot.GpuTemp,
                                    snapshot.EffectiveThermalTicks);
                            }
                        }
                        _currentSnapshot = snapshot;
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
            lock (SyncLock)
            {
                _lastPollTicks = _customTimestampProvider != null ? _customTimestampProvider() : Stopwatch.GetTimestamp();
            }

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

    private static TelemetrySnapshot DefaultFanOnlySampler()
    {
        long timestamp = _customTimestampProvider != null ? _customTimestampProvider() : Stopwatch.GetTimestamp();
        int cpuTemp = PreySense.Mode.PawnCpuPowerControl.ReadPackageTemperature() ?? -1;
        int gpuTemp = PreySense.Gpu.NvidiaGpuControl.Shared.ReadCurrentTemperature() ?? -1;

        Interlocked.Increment(ref TelemetryInstrumentation.EcThermalReadCount);

        lock (SyncLock)
        {
            if (_currentSnapshot.IsValid)
            {
                return _currentSnapshot.WithFanTemperatures(cpuTemp, gpuTemp, timestamp);
            }
        }

        return new TelemetrySnapshot(
            TimestampTicks: 0,
            CpuUsage: null,
            CpuMaxCoreUsage: null,
            CpuPCoreUsage: null,
            CpuECoreUsage: null,
            RamUsage: null,
            RamUsedMb: null,
            CpuTemp: cpuTemp,
            GpuTemp: gpuTemp,
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
            BatteryPercent: 0f,
            IsAcConnected: false,
            PowerSource: PowerSourceKind.Ac,
            IsValid: cpuTemp > 0 || gpuTemp > 0,
            IsFanOnly: true,
            ThermalTimestampTicks: timestamp);
    }

    private static TelemetrySnapshot DefaultFullSampler()
    {
        long timestamp = _customTimestampProvider != null ? _customTimestampProvider() : Stopwatch.GetTimestamp();

        // 1. CPU utilization via CpuUsageMonitor (zero-allocation native syscalls)
        var cpuSnap = CpuUsageMonitor.GetSnapshot();
        Interlocked.Increment(ref TelemetryInstrumentation.CpuTelemetryReadCount);

        // 2. RAM utilization via SystemMemoryMonitor
        var ramInfo = SystemMemoryMonitor.GetMemoryInfo();
        Interlocked.Increment(ref TelemetryInstrumentation.RamTelemetryReadCount);

        // 3. Thermal sensors & battery
        int cpuTemp = PreySense.Mode.PawnCpuPowerControl.ReadPackageTemperature() ?? -1;
        int gpuTemp = PreySense.Gpu.NvidiaGpuControl.Shared.ReadCurrentTemperature() ?? -1;
        Interlocked.Increment(ref TelemetryInstrumentation.EcThermalReadCount);

        // 4. GPU specifics
        var gpu = PreySense.Gpu.NvidiaGpuControl.Shared;
        bool gpuInUse = gpu.IsDiscreteGpuInUse();
        int? gpuUsage = null;
        int? gpuMhz = null;
        float? gpuVoltage = null;
        float? gpuPower = null;
        int? vramUsedMb = null;
        int? vramTotalMb = null;
        int? vramSpeedMhz = null;

        if (gpuInUse)
        {
            try
            {
                gpuUsage = gpu.GetGpuUse();
                gpuMhz = gpu.GetGpuClock();
                gpuVoltage = gpu.GetGpuVoltage();
                gpuPower = (float?)gpu.GetGpuPower();
                if (gpu.GetVramInfo() is { } v && v.totalMb > 0)
                {
                    vramUsedMb = (int)v.usedMb;
                    vramTotalMb = (int)v.totalMb;
                }
                vramSpeedMhz = gpu.GetGpuMemoryClock();
                Interlocked.Increment(ref TelemetryInstrumentation.GpuTelemetryReadCount);
            }
            catch { }
        }

        float? cpuVid = PreySense.Mode.PawnCpuPowerControl.ReadCoreVid();

        return new TelemetrySnapshot(
            TimestampTicks: timestamp,
            CpuUsage: cpuSnap?.TotalUsage,
            CpuMaxCoreUsage: cpuSnap?.MaxCoreUsage,
            CpuPCoreUsage: cpuSnap?.PCoreUsage,
            CpuECoreUsage: cpuSnap?.ECoreUsage,
            RamUsage: ramInfo?.UsedPercent,
            RamUsedMb: ramInfo?.UsedMb,
            CpuTemp: cpuTemp,
            GpuTemp: gpuTemp,
            CpuRpm: null, // Populated via Wmi thermal if available
            GpuRpm: null,
            CpuFanMaxRpm: 5800,
            GpuFanMaxRpm: 5600,
            CpuPkgPowerWatts: null,
            GpuPowerWatts: gpuPower,
            CpuVoltage: cpuVid,
            GpuVoltage: gpuVoltage,
            CpuClockMhz: null,
            ECoreClockMhz: null,
            GpuClockMhz: gpuMhz,
            GpuUsage: gpuUsage,
            VramUsedMb: vramUsedMb,
            VramTotalMb: vramTotalMb,
            VramSpeedMhz: vramSpeedMhz,
            GpuInUse: gpuInUse,
            BatteryPercent: 0f,
            IsAcConnected: true,
            PowerSource: PowerSourceKind.Ac,
            IsValid: true,
            IsFanOnly: false,
            ThermalTimestampTicks: timestamp);
    }
}
