using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace PreySense.Core.Hardware;

/// <summary>
/// Scoped thread affinity helper that pins the current thread to a specific logical processor index,
/// and restores the original affinity upon disposal.
/// Safe across Windows processor groups (up to 64 cores per group).
/// </summary>
public sealed class ThreadAffinityScope : IDisposable
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct GROUP_AFFINITY
    {
        public UIntPtr Mask;
        public ushort Group;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public ushort[] Reserved;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetCurrentThread();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetThreadGroupAffinity(IntPtr hThread, ref GROUP_AFFINITY groupAffinity);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetThreadGroupAffinity(
        IntPtr hThread,
        ref GROUP_AFFINITY groupAffinity,
        ref GROUP_AFFINITY previousGroupAffinity);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr SetThreadAffinityMask(IntPtr hThread, IntPtr dwThreadAffinityMask);

    private readonly bool _affinityApplied;
    private readonly bool _useGroupAffinity;
    private GROUP_AFFINITY _previousGroupAffinity;
    private IntPtr _previousLegacyMask;
    private bool _disposed;

    /// <summary>
    /// Gets whether thread affinity was successfully pinned.
    /// </summary>
    public bool IsPinned => _affinityApplied;

    /// <summary>
    /// Pins the current thread to the specified zero-based logical processor index.
    /// If logicalProcessorIndex is null or negative, no pinning occurs.
    /// </summary>
    public ThreadAffinityScope(int? logicalProcessorIndex)
    {
        if (!OperatingSystem.IsWindows() || !logicalProcessorIndex.HasValue || logicalProcessorIndex.Value < 0)
        {
            _affinityApplied = false;
            return;
        }

        int coreIndex = logicalProcessorIndex.Value;
        Thread.BeginThreadAffinity();

        IntPtr threadHandle = GetCurrentThread();
        ushort group = (ushort)(coreIndex / 64);
        int indexInGroup = coreIndex % 64;
        ulong mask = 1UL << indexInGroup;

        // Try SetThreadGroupAffinity first
        _previousGroupAffinity = new GROUP_AFFINITY { Reserved = new ushort[3] };
        var newAffinity = new GROUP_AFFINITY
        {
            Group = group,
            Mask = new UIntPtr(mask),
            Reserved = new ushort[3]
        };

        if (SetThreadGroupAffinity(threadHandle, ref newAffinity, ref _previousGroupAffinity))
        {
            _useGroupAffinity = true;
            _affinityApplied = true;
            return;
        }

        // Fallback to legacy SetThreadAffinityMask if group 0 and <= 63
        if (group == 0)
        {
            IntPtr prev = SetThreadAffinityMask(threadHandle, (IntPtr)(long)mask);
            if (prev != IntPtr.Zero)
            {
                _previousLegacyMask = prev;
                _useGroupAffinity = false;
                _affinityApplied = true;
                return;
            }
        }

        // Pinning failed, release thread affinity
        int err = Marshal.GetLastWin32Error();
        Trace.TraceWarning($"[ThreadAffinityScope] Failed to set thread affinity to core {coreIndex} (group {group}, mask 0x{mask:X}): Win32 error {err}");
        Thread.EndThreadAffinity();
        _affinityApplied = false;
    }

    public void Dispose()
    {
        if (_disposed || !_affinityApplied) return;
        _disposed = true;

        try
        {
            IntPtr threadHandle = GetCurrentThread();
            if (_useGroupAffinity)
            {
                var unused = new GROUP_AFFINITY { Reserved = new ushort[3] };
                if (!SetThreadGroupAffinity(threadHandle, ref _previousGroupAffinity, ref unused))
                {
                    int err = Marshal.GetLastWin32Error();
                    Trace.TraceWarning($"[ThreadAffinityScope] Failed to restore thread group affinity (group {_previousGroupAffinity.Group}, mask 0x{_previousGroupAffinity.Mask.ToUInt64():X}): Win32 error {err}");
                }
            }
            else if (_previousLegacyMask != IntPtr.Zero)
            {
                IntPtr res = SetThreadAffinityMask(threadHandle, _previousLegacyMask);
                if (res == IntPtr.Zero)
                {
                    int err = Marshal.GetLastWin32Error();
                    Trace.TraceWarning($"[ThreadAffinityScope] Failed to restore legacy thread affinity mask (mask 0x{_previousLegacyMask.ToInt64():X}): Win32 error {err}");
                }
            }
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"[ThreadAffinityScope] Exception during affinity restoration: {ex.Message}");
        }
        finally
        {
            Thread.EndThreadAffinity();
        }
    }
}
