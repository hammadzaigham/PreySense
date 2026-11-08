namespace PreySense.Core.Services;

using System.Diagnostics;
using System.Runtime.InteropServices;

public readonly record struct CpuTelemetrySnapshot(
    int TotalUsage,
    int? MaxCoreUsage,
    int? PCoreUsage,
    int? ECoreUsage,
    int LogicalProcessorCount);

/// <summary>
/// Thread-safe, monotonic monitor for aggregate CPU utilization, Max Core utilization,
/// and P-Core vs E-Core cluster utilization using native Windows NT performance queries.
/// Decoupled from hardware I/O and EC/GPU operation gates.
/// </summary>
public static class CpuUsageMonitor
{
    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION
    {
        public long IdleTime;
        public long KernelTime;
        public long UserTime;
        public long DpcTime;
        public long InterruptTime;
        public uint InterruptCount;
    }

    public delegate bool GetSystemTimesDelegate(out long idleTime, out long kernelTime, out long userTime);
    public delegate bool GetProcessorPerformanceDelegate(SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION[] coreInfo);

    private static readonly object SyncLock = new();

    private static readonly int ProcessorCount = Math.Max(1, Environment.ProcessorCount);
    private static readonly int PerfInfoStructSize = Marshal.SizeOf<SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION>();
    private static readonly int PerfInfoBufferSize = PerfInfoStructSize * ProcessorCount;
    private static readonly IntPtr PerfInfoBuffer = Marshal.AllocHGlobal(PerfInfoBufferSize);

    private static readonly SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION[] _lastCoreTimes = new SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION[ProcessorCount];
    private static readonly SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION[] _currentCoreTimes = new SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION[ProcessorCount];
    private static readonly bool[] _detectedECoreMap = BuildECoreMap(ProcessorCount);

    private static long _lastIdleTime;
    private static long _lastKernelTime;
    private static long _lastUserTime;
    private static long _lastSampleTimestamp;
    private static long _lastValidSampleTimestamp;
    private static int? _cachedUsage;
    private static CpuTelemetrySnapshot? _cachedSnapshot;
    private static bool _hasBaseline;
    private static bool? _perCoreSource;

    // Default policy thresholds (in seconds)
    private const double DefaultMinSampleSeconds = 0.500;       // 500 ms: prevents out-of-phase 1s UI timers from splitting samples
    private const double DefaultStaleSampleSeconds = 4.0;       // 4 seconds: expire reading if no fresh sample
    private const double DefaultSuspendThresholdSeconds = 15.0; // 15 seconds: sleep/hibernation re-baseline

    private static double _minSampleSeconds = DefaultMinSampleSeconds;
    private static double _staleSampleSeconds = DefaultStaleSampleSeconds;
    private static double _suspendThresholdSeconds = DefaultSuspendThresholdSeconds;

    private static Func<long>? _customTimestampProvider;
    private static GetSystemTimesDelegate? _customSystemTimesImpl;
    private static GetProcessorPerformanceDelegate? _customProcessorPerformanceImpl;
    private static bool[]? _customECoreMap;
    private static int? _customProcessorCount;

    private static int EffectiveProcessorCount => _customProcessorCount ?? _customECoreMap?.Length ?? ProcessorCount;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out long lpIdleTime, out long lpKernelTime, out long lpUserTime);

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(
        int SystemInformationClass,
        IntPtr SystemInformation,
        int SystemInformationLength,
        out int ReturnLength);

    private const int RelationProcessorCore = 0;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetLogicalProcessorInformationEx(int relationship, IntPtr buffer, ref uint returnedLength);

    private static long CurrentTimestamp() =>
        _customTimestampProvider != null ? _customTimestampProvider() : Stopwatch.GetTimestamp();

    private static double ElapsedSeconds(long startTimestamp, long endTimestamp)
    {
        if (_customTimestampProvider != null)
        {
            return (double)(endTimestamp - startTimestamp) / Stopwatch.Frequency;
        }
        return Stopwatch.GetElapsedTime(startTimestamp, endTimestamp).TotalSeconds;
    }

    /// <summary>
    /// Pre-establishes the baseline CPU time counters during app startup so the first UI sample
    /// does not have to wait a full cycle to produce valid utilization numbers.
    /// </summary>
    public static void Warmup()
    {
        lock (SyncLock)
        {
            if (!_hasBaseline)
            {
                if (_customProcessorPerformanceImpl != null)
                {
                    if (_customProcessorPerformanceImpl(_lastCoreTimes))
                    {
                        _lastSampleTimestamp = CurrentTimestamp();
                        _hasBaseline = true;
                        _perCoreSource = true;
                    }
                }
                else if (_customSystemTimesImpl != null)
                {
                    if (TrySampleSystemTimes(out long idle, out long kernel, out long user))
                    {
                        _lastIdleTime = idle;
                        _lastKernelTime = kernel;
                        _lastUserTime = user;
                        _lastSampleTimestamp = CurrentTimestamp();
                        _hasBaseline = true;
                        _perCoreSource = false;
                    }
                }
                else if (TrySampleNativeCores(_lastCoreTimes))
                {
                    _lastSampleTimestamp = CurrentTimestamp();
                    _hasBaseline = true;
                        _perCoreSource = true;
                }
                else if (TrySampleSystemTimes(out long idle, out long kernel, out long user))
                {
                    _lastIdleTime = idle;
                    _lastKernelTime = kernel;
                    _lastUserTime = user;
                    _lastSampleTimestamp = CurrentTimestamp();
                    _hasBaseline = true;
                        _perCoreSource = false;
                }
            }
        }
    }

    /// <summary>
    /// Explicitly invalidates the cached CPU usage and baseline (e.g., when resuming from system suspend/sleep).
    /// </summary>
    public static void InvalidateBaseline()
    {
        lock (SyncLock)
        {
            _hasBaseline = false;
            _cachedUsage = null;
            _cachedSnapshot = null;
        }
    }

    /// <summary>
    /// Returns the full CPU telemetry snapshot including TotalUsage, MaxCoreUsage,
    /// and P-Core vs E-Core cluster utilization.
    /// </summary>
    public static CpuTelemetrySnapshot? GetSnapshot(bool aggregateOnly = false)
    {
        lock (SyncLock)
        {
            long now = CurrentTimestamp();

            int effectiveCount = EffectiveProcessorCount;

            // Check if native per-core performance is available
            bool hasPerCore = !aggregateOnly && (_customProcessorPerformanceImpl != null ||
                              (_customSystemTimesImpl == null && TrySampleNativeCores(_currentCoreTimes)));

            if (_perCoreSource != hasPerCore)
            {
                InvalidateSample();
                _perCoreSource = hasPerCore;
            }

            if (hasPerCore)
            {
                if (_customProcessorPerformanceImpl != null && !_customProcessorPerformanceImpl(_currentCoreTimes))
                {
                    return InvalidateSample();
                }

                if (!_hasBaseline)
                {
                    Array.Copy(_currentCoreTimes, _lastCoreTimes, effectiveCount);
                    _lastSampleTimestamp = now;
                    _hasBaseline = true;
                    return _cachedSnapshot;
                }

                double elapsedSeconds = ElapsedSeconds(_lastSampleTimestamp, now);

                if (elapsedSeconds < _minSampleSeconds)
                {
                    return GetOrExpireCachedSnapshot(now);
                }

                if (elapsedSeconds > _suspendThresholdSeconds)
                {
                    Array.Copy(_currentCoreTimes, _lastCoreTimes, effectiveCount);
                    _lastSampleTimestamp = now;
                    _cachedUsage = null;
                    _cachedSnapshot = null;
                    return null;
                }

                int maxCore = 0;
                long sumTotalDelta = 0;
                long sumIdleDelta = 0;
                int pCoreSum = 0, pCoreCount = 0;
                int eCoreSum = 0, eCoreCount = 0;
                bool[]? eMap = _customECoreMap ?? (_customProcessorCount == null ? _detectedECoreMap : null);

                for (int i = 0; i < effectiveCount; i++)
                {
                    long deltaIdle = _currentCoreTimes[i].IdleTime - _lastCoreTimes[i].IdleTime;
                    long deltaTotal = (_currentCoreTimes[i].KernelTime - _lastCoreTimes[i].KernelTime) +
                                      (_currentCoreTimes[i].UserTime - _lastCoreTimes[i].UserTime);

                    int corePct = deltaTotal > 0
                        ? Math.Clamp((int)Math.Round((1.0 - (double)deltaIdle / deltaTotal) * 100.0), 0, 100)
                        : 0;

                    if (corePct > maxCore) maxCore = corePct;
                    sumTotalDelta += deltaTotal;
                    sumIdleDelta += deltaIdle;

                    bool isECore = (eMap != null && i < eMap.Length) && eMap[i];
                    if (eMap is null || i >= eMap.Length) { }
                    else if (isECore)
                    {
                        eCoreSum += corePct;
                        eCoreCount++;
                    }
                    else
                    {
                        pCoreSum += corePct;
                        pCoreCount++;
                    }

                    _lastCoreTimes[i] = _currentCoreTimes[i];
                }

                _lastSampleTimestamp = now;

                if (sumTotalDelta <= 0)
                {
                    return GetOrExpireCachedSnapshot(now);
                }

                int totalUsage = Math.Clamp((int)Math.Round((1.0 - (double)sumIdleDelta / sumTotalDelta) * 100.0), 0, 100);
                int? pCoreUsage = pCoreCount > 0 ? (int)Math.Round((double)pCoreSum / pCoreCount) : null;
                int? eCoreUsage = eCoreCount > 0 ? (int)Math.Round((double)eCoreSum / eCoreCount) : null;

                _cachedUsage = totalUsage;
                _cachedSnapshot = new CpuTelemetrySnapshot(totalUsage, maxCore, pCoreUsage, eCoreUsage, effectiveCount);
                _lastValidSampleTimestamp = now;
                return _cachedSnapshot;
            }

            // Fallback path using aggregate GetSystemTimes()
            if (!TrySampleSystemTimes(out long idle, out long kernel, out long user))
            {
                return InvalidateSample();
            }

            if (!_hasBaseline)
            {
                _lastIdleTime = idle;
                _lastKernelTime = kernel;
                _lastUserTime = user;
                _lastSampleTimestamp = now;
                _hasBaseline = true;
                return _cachedSnapshot;
            }

            double elapsed = ElapsedSeconds(_lastSampleTimestamp, now);

            if (elapsed < _minSampleSeconds)
            {
                return GetOrExpireCachedSnapshot(now);
            }

            if (elapsed > _suspendThresholdSeconds)
            {
                _lastIdleTime = idle;
                _lastKernelTime = kernel;
                _lastUserTime = user;
                _lastSampleTimestamp = now;
                _cachedUsage = null;
                _cachedSnapshot = null;
                return null;
            }

            long totalDeltaIdle = idle - _lastIdleTime;
            long totalDelta = (kernel - _lastKernelTime) + (user - _lastUserTime);

            _lastIdleTime = idle;
            _lastKernelTime = kernel;
            _lastUserTime = user;
            _lastSampleTimestamp = now;

            if (totalDelta <= 0)
            {
                return GetOrExpireCachedSnapshot(now);
            }

            int aggregateUsage = Math.Clamp((int)Math.Round((1.0 - (double)totalDeltaIdle / totalDelta) * 100.0), 0, 100);
            _cachedUsage = aggregateUsage;
            _cachedSnapshot = new CpuTelemetrySnapshot(aggregateUsage, null, null, null, ProcessorCount);
            _lastValidSampleTimestamp = now;
            return _cachedSnapshot;
        }
    }

    /// <summary>
    /// Returns aggregate total CPU utilization percentage [0..100].
    /// </summary>
    public static int? GetCpuUsage() => GetSnapshot()?.TotalUsage;

    /// <summary>
    /// Returns the utilization percentage of the single most heavily loaded logical processor.
    /// Crucial for detecting single-threaded game engine bottlenecks on high-core-count processors.
    /// </summary>
    public static int? GetMaxCoreUsage() => GetSnapshot()?.MaxCoreUsage;

    /// <summary>
    /// Returns the average utilization percentage of the Performance Core (P-core) cluster.
    /// </summary>
    public static int? GetPCoreUsage() => GetSnapshot()?.PCoreUsage;

    /// <summary>
    /// Returns the average utilization percentage of the Efficient Core (E-core) cluster.
    /// </summary>
    public static int? GetECoreUsage() => GetSnapshot()?.ECoreUsage;

    private static bool TrySampleNativeCores(SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION[] destination)
    {
        try
        {
            int status = NtQuerySystemInformation(8, PerfInfoBuffer, PerfInfoBufferSize, out int retLength);
            if (status != 0 || retLength < PerfInfoBufferSize)
                return false;

            unsafe
            {
                var ptr = (SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION*)PerfInfoBuffer;
                for (int i = 0; i < ProcessorCount; i++)
                {
                    destination[i] = ptr[i];
                }
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TrySampleSystemTimes(out long idle, out long kernel, out long user)
    {
        if (_customSystemTimesImpl != null)
        {
            return _customSystemTimesImpl(out idle, out kernel, out user);
        }
        return GetSystemTimes(out idle, out kernel, out user);
    }

    private static CpuTelemetrySnapshot? InvalidateSample()
    {
        _cachedSnapshot = null;
        _cachedUsage = null;
        _hasBaseline = false;
        return null;
    }

    private static CpuTelemetrySnapshot? GetOrExpireCachedSnapshot(long now)
    {
        if (_cachedSnapshot.HasValue)
        {
            double validAgeSeconds = ElapsedSeconds(_lastValidSampleTimestamp, now);
            if (validAgeSeconds > _staleSampleSeconds)
            {
                _cachedUsage = null;
                _cachedSnapshot = null;
            }
        }
        return _cachedSnapshot;
    }

    private static bool[] BuildECoreMap(int logicalCount)
    {
        var isECore = new bool[Math.Max(0, logicalCount)];
        if (logicalCount <= 0)
            return isECore;

        try
        {
            uint length = 0;
            GetLogicalProcessorInformationEx(RelationProcessorCore, IntPtr.Zero, ref length);
            if (length == 0)
                return Array.Empty<bool>();

            IntPtr buffer = Marshal.AllocHGlobal((int)length);
            try
            {
                if (!GetLogicalProcessorInformationEx(RelationProcessorCore, buffer, ref length))
                    return Array.Empty<bool>();

                byte minClass = byte.MaxValue;
                byte maxClass = 0;
                var cores = new List<(byte Efficiency, ushort Group, ulong Mask)>();
                int offset = 0;
                while (offset + 32 <= (int)length)
                {
                    int size = Marshal.ReadInt32(buffer, offset + 4);
                    if (size < 32)
                        break;

                    byte efficiency = Marshal.ReadByte(buffer, offset + 9);
                    ushort groupCount = (ushort)Marshal.ReadInt16(buffer, offset + 30);
                    int maskOffset = offset + 32;
                    for (int g = 0; g < groupCount && maskOffset + 16 <= offset + size; g++)
                    {
                        ulong mask = (ulong)Marshal.ReadInt64(buffer, maskOffset);
                        ushort group = (ushort)Marshal.ReadInt16(buffer, maskOffset + 8);
                        cores.Add((efficiency, group, mask));
                        if (efficiency < minClass) minClass = efficiency;
                        if (efficiency > maxClass) maxClass = efficiency;
                        maskOffset += 16;
                    }

                    offset += size;
                }

                if (minClass == maxClass)
                    return isECore;

                foreach (var core in cores)
                {
                    if (core.Efficiency != minClass || core.Group != 0)
                        continue;

                    for (int bit = 0; bit < 64 && bit < logicalCount; bit++)
                    {
                        if ((core.Mask & (1UL << bit)) != 0)
                            isECore[bit] = true;
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            return Array.Empty<bool>();
        }

        return isECore;
    }

    public static void ConfigureForTesting(
        Func<long>? timestampProvider,
        GetSystemTimesDelegate? systemTimesProvider,
        double minSampleSec = DefaultMinSampleSeconds,
        double staleSec = DefaultStaleSampleSeconds,
        double suspendSec = DefaultSuspendThresholdSeconds,
        GetProcessorPerformanceDelegate? processorPerformanceProvider = null,
        bool[]? customECoreMap = null,
        int? processorCount = null)
    {
        lock (SyncLock)
        {
            _customTimestampProvider = timestampProvider;
            _customSystemTimesImpl = systemTimesProvider;
            _customProcessorPerformanceImpl = processorPerformanceProvider;
            _customECoreMap = customECoreMap;
            _customProcessorCount = processorCount ?? customECoreMap?.Length;
            _minSampleSeconds = minSampleSec;
            _staleSampleSeconds = staleSec;
            _suspendThresholdSeconds = suspendSec;
            ResetInternal();
        }
    }

    public static void Reset()
    {
        lock (SyncLock)
        {
            _customTimestampProvider = null;
            _customSystemTimesImpl = null;
            _customProcessorPerformanceImpl = null;
            _customECoreMap = null;
            _customProcessorCount = null;
            _minSampleSeconds = DefaultMinSampleSeconds;
            _staleSampleSeconds = DefaultStaleSampleSeconds;
            _suspendThresholdSeconds = DefaultSuspendThresholdSeconds;
            ResetInternal();
        }
    }

    private static void ResetInternal()
    {
        _hasBaseline = false;
        _perCoreSource = null;
        _cachedUsage = null;
        _cachedSnapshot = null;
        _lastIdleTime = 0;
        _lastKernelTime = 0;
        _lastUserTime = 0;
        _lastSampleTimestamp = 0;
        _lastValidSampleTimestamp = 0;
        Array.Clear(_lastCoreTimes, 0, _lastCoreTimes.Length);
        Array.Clear(_currentCoreTimes, 0, _currentCoreTimes.Length);
    }
}
