using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace PreySense.Rgb;

/// <summary>Descriptor-validated Acer KYD100 targets advertised by A1 and confirmed by A3.</summary>
[SupportedOSPlatform("windows")]
public sealed class AcerHidLightingDevice : IDisposable
{
    private static readonly object TransportLock = AcerLightingTransportGate.Sync;
    private readonly SafeFileHandle _handle;
    private readonly int _reportLength;
    private readonly IReadOnlyDictionary<ushort, int> _updateWidths;
    public string Id { get; }
    public string Name { get; }
    public string Kind { get; }
    public byte Target { get; }
    public int LedCount { get; }
    public byte MaximumSpeed { get; }
    public IReadOnlyList<AcerHidLightingMode> Modes { get; }

    private AcerHidLightingDevice(string path, int reportLength, IReadOnlyDictionary<ushort, int> updateWidths, AcerHidLightingBaseInfo info)
    {
        _handle = Open(path);
        if (_handle.IsInvalid) { _handle.Dispose(); throw new InvalidOperationException("Cannot open Acer lighting HID target."); }
        _reportLength = reportLength;
        _updateWidths = updateWidths;
        Target = info.Target;
        LedCount = info.LedCount;
        MaximumSpeed = info.MaximumSpeed;
        Modes = AcerHidLightingProtocol.GetModes(info.EffectMask);
        Kind = Target switch { 0x21 => "Keyboard", >= 0x20 and <= 0x25 => "Lightbar", 0x64 or 0x65 => "Hotkey", _ => "Logo" };
        Name = Target switch { 0x20 => "Lightbar", 0x21 => "Keyboard", 0x22 => "Front Lightbar", 0x23 => "Left Lightbar",
            0x24 => "Right Lightbar", 0x25 => "Rear Lightbar", 0x64 => "Turbo Key", 0x65 => "Mode Key",
            0x80 => "Logo", 0x83 => "Cover Logo", 0x84 => "Base Logo", _ => "Acer lighting" };
        // Path hash distinguishes multiple controllers without exposing device instance paths in the UI.
        string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(path.ToLowerInvariant())))[..12];
        Id = $"acer-hid-{hash}-{Target:X2}";
    }

    /// <summary>Only reads descriptors and A1/A3. A2 selects the advertised target for the A3 query; no A4 lighting updates are sent.</summary>
    public static IReadOnlyList<AcerHidLightingDevice> Discover(out string diagnostic)
    {
        var devices = new List<AcerHidLightingDevice>();
        var errors = new List<string>();
        // Hold the shared gate only around each A2→sleep→A3 probe so live keyboard frames
        // are not blocked for the entire SetupDi walk.
        foreach (string path in EnekRgbClient.EnumerateAllHidPaths())
        {
            try
            {
                using var handle = Open(path);
                if (handle.IsInvalid || !HidD_GetPreparsedData(handle, out IntPtr pp)) continue;
                try
                {
                    var caps = new byte[64];
                    if (HidP_GetCaps(pp, caps) < 0 || BitConverter.ToUInt16(caps, 2) != 0xFF5A) continue;
                    int length = BitConverter.ToUInt16(caps, 8);
                    if (length is < 2 or > 4096) continue;
                    ushort count = BitConverter.ToUInt16(caps, 60);
                    if (count is 0 or > 256) continue;
                    var valueCaps = new byte[count * 72];
                    if (HidP_GetValueCaps(2, valueCaps, ref count, pp) < 0) continue;
                    var fields = ReadWidths(valueCaps, count);
                    if (!fields.TryGetValue(0xA4, out var update) || !fields.TryGetValue(0xA3, out var infoWidths) ||
                        !fields.TryGetValue(0xA2, out var query) || !query.TryGetValue(0x21, out int selectorWidth) || selectorWidth != 1 ||
                        !update.TryGetValue(0x21, out selectorWidth) || selectorWidth != 1) continue;
                    // Static updates require mode, brightness, RGB and a bounded zone mask.
                    if (new ushort[] { 0x41, 0x43, 0x45, 0x46, 0x47, 0x48 }.Any(u => !update.ContainsKey(u))) continue;
                    byte[]? attributes;
                    lock (TransportLock) attributes = GetFeature(handle, 0xA1, length);
                    if (attributes is null) continue;
                    foreach (byte target in AcerHidLightingCapabilities.ParseAttributeReport(attributes))
                    {
                        AcerHidLightingBaseInfo? info;
                        lock (TransportLock)
                        {
                            var request = new byte[length]; request[0] = 0xA2; request[1] = target;
                            if (!HidD_SetFeature(handle, request, length)) continue;
                            Thread.Sleep(20);
                            var response = GetFeature(handle, 0xA3, length);
                            info = response is null ? null : AcerHidLightingProtocol.ParseBaseInfo(response, target, infoWidths);
                        }
                        if (info is null || (info.EffectMask & 2) == 0) continue;
                        int maskWidth = update[0x48];
                        if (info.LedCount > maskWidth * 8) continue;
                        // Validate all field widths and total size before exposing a writable target.
                        AcerHidLightingProtocol.CreateUpdateReport(length, target, 2, 0, 0, 0, 0, 0, 0, 1, update);
                        devices.Add(new(path, length, update, info));
                    }
                }
                finally { HidD_FreePreparsedData(pp); }
            }
            catch (Exception ex) { errors.Add(ex.Message); }
        }
        diagnostic = devices.Count > 0 ? $"Confirmed {devices.Count} advertised Acer HID lighting target(s)." :
            errors.Count > 0 ? "Acer HID lighting discovery: " + errors[0] : "No supported Acer HID lighting targets advertised.";
        return devices;
    }

    private static Dictionary<byte, Dictionary<ushort, int>> ReadWidths(byte[] caps, int count)
    {
        var fields = new Dictionary<byte, Dictionary<ushort, int>>();
        for (int i = 0; i < count; i++)
        {
            int offset = i * 72;
            if (BitConverter.ToUInt16(caps, offset) != 0xFF5A || caps[offset + 12] != 0) continue;
            int bits = BitConverter.ToUInt16(caps, offset + 18);
            int repeats = BitConverter.ToUInt16(caps, offset + 20);
            if (bits is < 8 or > 32 || bits % 8 != 0 || repeats != 1) continue;
            byte report = caps[offset + 2]; ushort usage = BitConverter.ToUInt16(caps, offset + 56);
            if (!fields.TryGetValue(report, out var usages)) fields[report] = usages = new();
            if (!usages.TryAdd(usage, bits / 8)) throw new InvalidOperationException("Ambiguous Acer HID descriptor fields.");
        }
        return fields;
    }

    public void ApplyColors(ReadOnlySpan<uint> colors, byte brightness)
    {
        if (colors.Length != LedCount) throw new ArgumentException("Supply one packed 0x00BBGGRR color per confirmed zone or LED.", nameof(colors));
        // Group identical colors into one report with a combined zone mask (same idea as ENEK 0x0F).
        // No inter-zone sleep: pacing here held the shared HID lock and stalled the software engine.
        lock (TransportLock)
        {
            Span<(uint Color, ushort Mask)> groups = stackalloc (uint, ushort)[colors.Length];
            int groupCount = 0;
            for (int i = 0; i < colors.Length; i++)
            {
                uint color = colors[i];
                ushort bit = (ushort)(1 << i);
                int existing = -1;
                for (int g = 0; g < groupCount; g++)
                {
                    if (groups[g].Color == color) { existing = g; break; }
                }
                if (existing >= 0) groups[existing].Mask |= bit;
                else groups[groupCount++] = (color, bit);
            }

            for (int g = 0; g < groupCount; g++)
            {
                uint color = groups[g].Color;
                byte red = (byte)color, green = (byte)(color >> 8), blue = (byte)(color >> 16);
                byte reportBrightness = brightness;
                // Mode Key / Turbo Key Static ignores the brightness byte and follows RGB level.
                if (IsHotkeyTarget)
                    (red, green, blue, reportBrightness) = BakeStaticBrightness(brightness, red, green, blue);
                Send(2, 0, 0, reportBrightness, red, green, blue, groups[g].Mask);
            }
        }
    }

    public void ApplyMode(byte mode, byte speed, byte brightness, byte direction, byte red, byte green, byte blue)
    {
        if (!Modes.Any(m => m.Value == mode)) throw new ArgumentOutOfRangeException(nameof(mode), "The target does not advertise this verified effect.");
        if (speed > MaximumSpeed) throw new ArgumentOutOfRangeException(nameof(speed));
        // Static/effects on single-LED targets (Mode Key, Cover Logo) require a non-zero zone mask,
        // matching ApplyColors and ENEK cover writes (mask 0x01). zone=0 leaves brightness ineffective.
        ushort zoneMask = LedCount >= 16 ? (ushort)0xFFFF : (ushort)((1 << LedCount) - 1);
        // Breathing/Neon honor the brightness byte as peak intensity. Mode Key Static does not —
        // bake the slider into RGB so Static dimming matches the other effects.
        if (mode == 2 && IsHotkeyTarget)
            (red, green, blue, brightness) = BakeStaticBrightness(brightness, red, green, blue);
        lock (TransportLock) Send(mode, speed, direction, brightness, red, green, blue, zoneMask);
    }

    private bool IsHotkeyTarget => Target is 0x64 or 0x65;

    private static (byte Red, byte Green, byte Blue, byte Brightness) BakeStaticBrightness(
        byte brightness, byte red, byte green, byte blue)
    {
        if (brightness == 0) return (0, 0, 0, 0);
        float factor = Math.Clamp(brightness / 100f, 0f, 1f);
        return (
            (byte)Math.Clamp((int)Math.Round(red * factor), 0, 255),
            (byte)Math.Clamp((int)Math.Round(green * factor), 0, 255),
            (byte)Math.Clamp((int)Math.Round(blue * factor), 0, 255),
            100);
    }

    private void Send(byte mode, byte speed, byte direction, byte brightness, byte red, byte green, byte blue, ushort zone)
    {
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
        byte[] packet = AcerHidLightingProtocol.CreateUpdateReport(_reportLength, Target, mode, speed, direction, brightness, red, green, blue, zone, _updateWidths);
        if (!HidD_SetFeature(_handle, packet, packet.Length)) throw new InvalidOperationException($"Acer HID lighting update failed (Win32 {Marshal.GetLastWin32Error()}).");
    }

    public void Dispose() { lock (TransportLock) _handle.Dispose(); }
    private static SafeFileHandle Open(string path) => CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
    private static byte[]? GetFeature(SafeFileHandle handle, byte id, int length)
    {
        var report = new byte[length]; report[0] = id;
        return HidD_GetFeature(handle, report, length) ? report : null;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr data);
    [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, [Out] byte[] caps);
    [DllImport("hid.dll")] private static extern int HidP_GetValueCaps(int type, [Out] byte[] caps, ref ushort count, IntPtr data);
    [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_GetFeature(SafeFileHandle handle, [In, Out] byte[] report, int length);
    [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_SetFeature(SafeFileHandle handle, byte[] report, int length);
}
