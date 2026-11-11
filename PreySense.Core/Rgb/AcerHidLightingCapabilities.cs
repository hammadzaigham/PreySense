using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace PreySense.Rgb;

/// <summary>Reads the Acer KYD100 A1 target advertisement. It never sends a lighting update.</summary>
public static class AcerHidLightingCapabilities
{
    private static readonly object Gate = new();
    private static IReadOnlySet<byte> _cached = FrozenSet<byte>.Empty;
    private static int _generation = -1;

    public static IReadOnlySet<byte> ParseAttributeReport(ReadOnlySpan<byte> report)
    {
        if (report.Length < 2 || report[0] != 0xA1 || report[1] > report.Length - 2)
            return FrozenSet<byte>.Empty;
        var targets = new HashSet<byte>();
        foreach (byte target in report.Slice(2, report[1]))
            if (target is 0x20 or 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x64 or 0x65 or 0x80 or 0x83 or 0x84)
                targets.Add(target);
        return targets.ToFrozenSet();
    }

    [SupportedOSPlatform("windows")]
    public static IReadOnlySet<byte> GetSupportedTargets(bool refresh = false)
    {
        lock (Gate)
        {
            // Acer KYD targets are hardwired for the laptop lifetime — probe once.
            // refresh / HID-arrival generation are ignored so hotplug noise cannot re-scan.
            if (_generation >= 0)
                return _cached;
            var targets = new HashSet<byte>();
            foreach (string path in EnekRgbClient.EnumerateAllHidPaths())
            {
                try
                {
                    using var handle = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                    if (handle.IsInvalid || !HidD_GetPreparsedData(handle, out IntPtr pp)) continue;
                    try
                    {
                        var caps = new byte[64];
                        if (HidP_GetCaps(pp, caps) < 0 || BitConverter.ToUInt16(caps, 2) != 0xFF5A) continue;
                        int length = BitConverter.ToUInt16(caps, 8);
                        if (length is < 2 or > 4096) continue;
                        var report = new byte[length];
                        report[0] = 0xA1;
                        if (HidD_GetFeature(handle, report, report.Length)) targets.UnionWith(ParseAttributeReport(report));
                    }
                    finally { HidD_FreePreparsedData(pp); }
                }
                catch { /* Unreadable interfaces do not establish lighting capabilities. */ }
            }
            _cached = targets.ToFrozenSet();
            _generation = 0;
            return _cached;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr data);
    [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, [Out] byte[] caps);
    [DllImport("hid.dll")] private static extern bool HidD_GetFeature(SafeFileHandle handle, [In, Out] byte[] report, int length);
}
