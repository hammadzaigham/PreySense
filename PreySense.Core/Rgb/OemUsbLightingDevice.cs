using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace PreySense.Rgb;

/// <summary>Direct HID transport for recovered OEM keyboard products and vendor usages.</summary>
[SupportedOSPlatform("windows")]
public sealed partial class OemUsbLightingDevice : IDisposable
{
    private readonly object _gate = new();
    private readonly SafeFileHandle _handle;
    private bool _initialized;
    private bool _disposed;
    public string Id { get; }
    public string Name { get; }
    public OemUsbKeyboardFamily Family { get; }
    public ushort ProductId { get; }
    public IReadOnlyList<OemUsbKey> Keys { get; }
    public string? LastError { get; private set; }

    private OemUsbLightingDevice(string path, SafeFileHandle handle, OemUsbKeyboardFamily family, ushort productId)
    {
        Id = path;
        _handle = handle;
        Family = family;
        ProductId = productId;
        Name = $"{family} USB keyboard ({productId:X4})";
        Keys = OemUsbLightingProtocol.GetKeys(family, productId);
    }

    /// <summary>Reads identity and capabilities only. Discovery does not send reports.</summary>
    public static IReadOnlyList<OemUsbLightingDevice> Discover()
    {
        var devices = new List<OemUsbLightingDevice>();
        foreach (var path in EnekRgbClient.EnumerateAllHidPaths().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            // Bound probing to paths with vendor identities recovered from OEM status exports.
            if (!path.Contains("vid_04f2", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains("vid_0d62", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains("vid_05af", StringComparison.OrdinalIgnoreCase)) continue;
            SafeFileHandle? handle = null;
            try
            {
                handle = CreateFile(path, 0xc0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                if (handle.IsInvalid) continue;
                var attributes = new HidAttributes { Size = Marshal.SizeOf<HidAttributes>() };
                if (!HidD_GetAttributes(handle, ref attributes)) continue;
                OemUsbKeyboardFamily family;
                ushort expectedPage;
                switch (attributes.VendorId)
                {
                    case 0x04f2: family = OemUsbKeyboardFamily.Acer; expectedPage = 0xff02; break;
                    case 0x0d62: family = OemUsbKeyboardFamily.Darfon; expectedPage = 0xff01; break;
                    case 0x05af: family = OemUsbKeyboardFamily.Sunrex; expectedPage = 0xff02; break;
                    default: continue;
                }
                if (!OemUsbLightingProtocol.IsKnownProduct(family, attributes.ProductId)) continue;
                if (!HidD_GetPreparsedData(handle, out var preparsed)) continue;
                HidCaps caps;
                try
                {
                    if (HidP_GetCaps(preparsed, out caps) != 0x00110000) continue;
                }
                finally { HidD_FreePreparsedData(preparsed); }
                if (caps.UsagePage != expectedPage || caps.OutputReportByteLength != 65 || caps.FeatureReportByteLength != 9)
                    continue;
                devices.Add(new OemUsbLightingDevice(path, handle, family, attributes.ProductId));
                handle = null; // Ownership transferred.
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or ExternalException)
            { /* A removed/busy interface is not an available keyboard. */ }
            finally { handle?.Dispose(); }
        }
        return devices.AsReadOnly();
    }

    public bool ApplyColors(IReadOnlyList<Color> colors, byte brightness)
    {
        ArgumentNullException.ThrowIfNull(colors);
        lock (_gate)
        {
            if (_disposed) { LastError = "Device is closed."; return false; }
            // Shared OEM Direct mode stores brightness at offset 0x44 and copies it into the commit report.
            var level = OemUsbLightingProtocol.MapBrightnessPercent(brightness);
            var transfer = OemUsbLightingProtocol.BuildTransfer(Family, ProductId, colors, (byte)level);
            if (!_initialized)
            {
                if (!WriteOutput(OemUsbLightingProtocol.BuildInitializationReport())) return false;
                Thread.Sleep(2);
                _initialized = true;
            }
            if (!WriteFeature(transfer.BeginFeatureReport)) return false;
            Thread.Sleep(transfer.BeforeBodyDelayMs);
            foreach (var report in transfer.BodyWriteReports)
            {
                if (!WriteOutput(report)) return false;
                Thread.Sleep(transfer.BetweenWritesDelayMs);
            }
            Thread.Sleep(transfer.BeforeCommitDelayMs);
            if (!WriteFeature(transfer.CommitFeatureReport)) return false;
            LastError = null;
            return true;
        }
    }

    private bool WriteOutput(byte[] report)
    {
        var padded = new byte[65]; // hidapi pads short writes to OutputReportByteLength.
        Array.Copy(report, padded, report.Length);
        if (WriteFile(_handle, padded, padded.Length, out var written, IntPtr.Zero) && written == padded.Length) return true;
        LastError = $"USB output report failed (Win32 {Marshal.GetLastWin32Error()}).";
        return false;
    }
    private bool WriteFeature(byte[] report)
    {
        if (HidD_SetFeature(_handle, report, report.Length)) return true;
        LastError = $"USB feature report failed (Win32 {Marshal.GetLastWin32Error()}).";
        return false;
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _handle.Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidAttributes { public int Size; public ushort VendorId, ProductId, VersionNumber; }
    [StructLayout(LayoutKind.Sequential)]
    private struct HidCaps
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices;
        public ushort NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteFile(SafeFileHandle handle, byte[] buffer, int length, out int written, IntPtr overlapped);
    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetAttributes(SafeFileHandle handle, ref HidAttributes attributes);
    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr data);
    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")]
    private static extern int HidP_GetCaps(IntPtr data, out HidCaps caps);
    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_SetFeature(SafeFileHandle handle, byte[] report, int length);
}
