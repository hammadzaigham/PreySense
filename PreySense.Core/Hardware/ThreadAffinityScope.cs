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
public readonly struct ThreadAffinityScope : IDisposable
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct GROUP_AFFINITY
    {
        public UIntPtr Mask;
        public ushort Group;
        public ushort Reserved0;
        public ushort Reserved1;
        public ushort Reserved2;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetCurrentThread();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetThreadGroupAffinity(
        IntPtr hThread,
        ref GROUP_AFFINITY groupAffinity,
        ref GROUP_AFFINITY previousGroupAffinity);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr SetThreadAffinityMask(IntPtr hThread, IntPtr dwThreadAffinityMask);

    private readonly bool _affinityApplied;
    private readonly bool _useGroupAffinity;
    private readonly GROUP_AFFINITY _previousGroupAffinity;
    private readonly IntPtr _previousLegacyMask;

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
        _previousGroupAffinity = default;
        _previousLegacyMask = IntPtr.Zero;
        _useGroupAffinity = false;

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
        var previousGroupAffinity = default(GROUP_AFFINITY);
        var newAffinity = new GROUP_AFFINITY
        {
            Group = group,
            Mask = new UIntPtr(mask)
        };

        if (SetThreadGroupAffinity(threadHandle, ref newAffinity, ref previousGroupAffinity))
        {
            _previousGroupAffinity = previousGroupAffinity;
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
        if (!_affinityApplied) return;

        try
        {
            IntPtr threadHandle = GetCurrentThread();
            if (_useGroupAffinity)
            {
                var previousGroupAffinity = _previousGroupAffinity;
                var unused = default(GROUP_AFFINITY);
                if (!SetThreadGroupAffinity(threadHandle, ref previousGroupAffinity, ref unused))
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
