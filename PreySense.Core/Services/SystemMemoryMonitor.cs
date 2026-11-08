namespace PreySense.Core.Services;

using System.Diagnostics;
using System.Runtime.InteropServices;

public readonly record struct SystemMemoryInfo(int UsedPercent, int UsedMb, int TotalMb);

/// <summary>
/// Thread-safe, throttled system memory monitor using Windows GlobalMemoryStatusEx().
/// Decoupled from hardware I/O and EC/GPU operation gates.
/// </summary>
public static class SystemMemoryMonitor
{
    public delegate bool GlobalMemoryStatusExDelegate(ref MEMORYSTATUSEX lpBuffer);

    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    private static readonly object SyncLock = new();
    private static long _lastSampleTimestamp;
    private static SystemMemoryInfo? _cachedMemoryInfo;
    private const double MinSampleSeconds = 0.200; // 200 ms throttle

    private static Func<long>? _customTimestampProvider;
    private static GlobalMemoryStatusExDelegate _globalMemoryStatusExImpl = Win32GlobalMemoryStatusEx;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    private static bool Win32GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer) =>
        GlobalMemoryStatusEx(ref buffer);

    private static long CurrentTimestamp() =>
        _customTimestampProvider != null ? _customTimestampProvider() : Stopwatch.GetTimestamp();

    private static double ElapsedSeconds(long startTimestamp, long endTimestamp)
    {
        if (_customTimestampProvider != null)
            return (double)(endTimestamp - startTimestamp) / Stopwatch.Frequency;
        return Stopwatch.GetElapsedTime(startTimestamp, endTimestamp).TotalSeconds;
    }

    /// <summary>
    /// Returns the current system RAM usage metrics, cached for up to 200 ms.
    /// Thread-safe and decoupled from hardware operation gates.
    /// </summary>
    public static SystemMemoryInfo? GetMemoryInfo()
    {
        lock (SyncLock)
        {
            long now = CurrentTimestamp();

            if (_cachedMemoryInfo.HasValue && _lastSampleTimestamp != 0)
            {
                double elapsed = ElapsedSeconds(_lastSampleTimestamp, now);
                if (elapsed < MinSampleSeconds)
                {
                    return _cachedMemoryInfo;
                }
            }

            var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (!_globalMemoryStatusExImpl(ref status))
            {
                _cachedMemoryInfo = null;
                return null;
            }

            int totalMb = (int)(status.ullTotalPhys / (1024 * 1024));
            int availMb = (int)(status.ullAvailPhys / (1024 * 1024));
            int usedMb = Math.Max(0, totalMb - availMb);
            int percent = (int)status.dwMemoryLoad;

            _cachedMemoryInfo = new SystemMemoryInfo(percent, usedMb, totalMb);
            _lastSampleTimestamp = now;
            return _cachedMemoryInfo;
        }
    }

    public static void ConfigureForTesting(Func<long>? timestampProvider, GlobalMemoryStatusExDelegate? memoryStatusProvider)
    {
        lock (SyncLock)
        {
            _customTimestampProvider = timestampProvider;
            _globalMemoryStatusExImpl = memoryStatusProvider ?? Win32GlobalMemoryStatusEx;
            _cachedMemoryInfo = null;
            _lastSampleTimestamp = 0;
        }
    }

    public static void Reset()
    {
        lock (SyncLock)
        {
            _customTimestampProvider = null;
            _globalMemoryStatusExImpl = Win32GlobalMemoryStatusEx;
            _cachedMemoryInfo = null;
            _lastSampleTimestamp = 0;
        }
    }
}
