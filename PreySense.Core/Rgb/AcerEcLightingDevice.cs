using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.Versioning;

namespace PreySense.Rgb;

/// <summary>Direct WMI EC lighting, gated by SMBIOS declarations and firmware capability replies.</summary>
[SupportedOSPlatform("windows")]
public sealed class AcerEcLightingDevice : IDisposable
{
    private static readonly object TransportLock = new();
    private readonly ManagementObject _instance;
    private readonly AcerEcLightingCapabilities _capabilities;
    private bool _disposed;
    public string Id { get; }
    public string Name { get; }
    public bool IsKeyboard => SideSelector == 0;
    public int SideSelector { get; }
    public int LedCount { get; }
    public bool ModesAffectAllLightbars => !IsKeyboard;

    private AcerEcLightingDevice(string path, AcerEcLightingCapabilities capabilities, int sideSelector, int count)
    {
        _instance = new ManagementObject(path);
        _capabilities = capabilities;
        SideSelector = sideSelector;
        LedCount = count;
        string target = sideSelector switch { 0 => "Keyboard", 1 => "Front", 2 => "Left", 4 => "Right", 8 => "Rear", _ => throw new ArgumentOutOfRangeException(nameof(sideSelector)) };
        Id = "acer-ec-" + target.ToLowerInvariant();
        Name = sideSelector == 0 ? $"Keyboard ({count} zones)" : target + " lightbar";
    }

    /// <summary>Only reads SMBIOS, WMI method metadata and GetGamingLED. No lighting writes.</summary>
    public static IReadOnlyList<AcerEcLightingDevice> Discover(out string diagnostic)
    {
        var devices = new List<AcerEcLightingDevice>();
        diagnostic = "No EC lighting declared by firmware.";
        try
        {
            AcerEcLightingCapabilities? capabilities = null;
            using (var tables = new ManagementObjectSearcher(@"root\wmi", "SELECT SMBiosData FROM MSSmBios_RawSMBiosTables"))
            using (var rows = tables.Get())
            {
                foreach (ManagementObject row in rows)
                {
                    using (row)
                        if (row["SMBiosData"] is byte[] bytes && AcerEcLightingCapabilities.FromSmbios(bytes) is { } found)
                        { capabilities = found; break; }
                }
            }
            if (capabilities is null || (!capabilities.Keyboard && !capabilities.Lightbars)) return devices;
            using var metadata = new ManagementClass(@"root\wmi:AcerGamingFunction");
            metadata.Get();
            if (!Matches(metadata, "SetGamingKBBacklight", CimType.UInt8, true, CimType.UInt32, false) ||
                !Matches(metadata, "SetGamingRgbKb", CimType.UInt64, false, CimType.UInt32, false) ||
                !Matches(metadata, "SetGamingLED", CimType.UInt8, true, CimType.UInt32, false))
            {
                diagnostic = "Acer EC lighting method schema is unsupported.";
                return devices;
            }
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM AcerGamingFunction");
            using var instances = searcher.Get();
            using var instance = instances.Cast<ManagementObject>().FirstOrDefault();
            if (instance is null) { diagnostic = "Acer gaming WMI instance is unavailable."; return devices; }
            string path = instance.Path.Path;
            if (capabilities.Keyboard) devices.Add(new(path, capabilities, 0, capabilities.KeyboardZoneCount));
            if (capabilities.Lightbars && AcerEcLightingProtocol.GetLedCommandLength(capabilities.Major, capabilities.Minor) >= 12 &&
                Matches(metadata, "GetGamingLED", CimType.UInt32, false, CimType.UInt8, true, true))
            {
                using var input = instance.GetMethodParameters("GetGamingLED");
                input["gmInput"] = AcerEcLightingProtocol.LightbarCapabilityQuery;
                using var output = instance.InvokeMethod("GetGamingLED", input, null);
                if (output is not null && output["gmReturn"] is byte status && status == 0 && output["gmOutput"] is byte[] bytes)
                {
                    foreach (int side in new[] { 1, 2, 4, 8 })
                    {
                        int count = AcerEcLightingProtocol.DecodeLightbarLedCount(capabilities.Major, capabilities.Minor, side, status, bytes);
                        if (count > 0) devices.Add(new(path, capabilities, side, count));
                    }
                }
            }
            diagnostic = devices.Count == 0 ? "No supported EC lighting targets confirmed." : $"Confirmed {devices.Count} EC lighting target(s), Acer SMBIOS {capabilities.Major}.{capabilities.Minor}.";
        }
        catch (Exception ex)
        {
            // Previously confirmed keyboard can remain usable when an optional bar query fails.
            diagnostic = "EC lighting discovery: " + ex.Message;
        }
        return devices;
    }

    private static bool Matches(ManagementClass metadata, string name, CimType inputType, bool inputArray, CimType outputType, bool outputArray, bool hasStatus = false)
    {
        var method = metadata.Methods.Cast<MethodData>().FirstOrDefault(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (method?.InParameters is null || method.OutParameters is null) return false;
        bool Property(ManagementBaseObject parameters, string property, CimType type, bool array) =>
            parameters.Properties.Cast<PropertyData>().Any(p => p.Name.Equals(property, StringComparison.OrdinalIgnoreCase) && p.Type == type && p.IsArray == array);
        return Property(method.InParameters, "gmInput", inputType, inputArray) &&
               Property(method.OutParameters, "gmOutput", outputType, outputArray) &&
               (!hasStatus || Property(method.OutParameters, "gmReturn", CimType.UInt8, false));
    }

    /// <param name="colors">Exactly LedCount packed colors, 0x00BBGGRR.</param>
    public void ApplyColors(ReadOnlySpan<uint> colors, byte brightness)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (colors.Length != LedCount) throw new ArgumentException("Supply exactly one color per confirmed zone or LED.", nameof(colors));
        lock (TransportLock)
        {
            var mask = IsKeyboard
                ? AcerEcLightingProtocol.CreateKeyboardLedMask(_capabilities.Major, _capabilities.Minor, colors)
                : AcerEcLightingProtocol.CreateLightbarLedMask(_capabilities.Major, _capabilities.Minor, SideSelector, colors);
            Invoke("SetGamingLED", mask);
            Invoke("SetGamingKBBacklight", IsKeyboard ? AcerEcLightingProtocol.CreateKeyboardBrightness(brightness) : AcerEcLightingProtocol.CreateLightbarBrightness(brightness));
            for (int i = 0; i < colors.Length; i++)
            {
                uint color = colors[i];
                byte red = (byte)color, green = (byte)(color >> 8), blue = (byte)(color >> 16);
                ulong value = IsKeyboard ? AcerEcLightingProtocol.CreateKeyboardZoneColor(i, red, green, blue)
                    : AcerEcLightingProtocol.CreateLightbarColor(SideSelector, i, red, green, blue);
                // WMI Invoke is synchronous; no artificial sleep — pacing held the shared HID/EC
                // lock and made software-effect frames stall on the fallback path.
                Invoke("SetGamingRgbKb", value);
            }
        }
    }

    public void ApplyMode(byte mode, byte speed, byte brightness, byte direction, byte red, byte green, byte blue)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (TransportLock)
            Invoke("SetGamingKBBacklight", IsKeyboard
                ? AcerEcLightingProtocol.CreateKeyboardMode(mode, speed, brightness, direction, red, green, blue)
                : AcerEcLightingProtocol.CreateLightbarMode(mode, speed, brightness, direction, red, green, blue));
    }

    private void Invoke(string method, object value)
    {
        using var input = _instance.GetMethodParameters(method);
        input["gmInput"] = value;
        using var output = _instance.InvokeMethod(method, input, null);
        if (output is null || output["gmOutput"] is null || (Convert.ToUInt64(output["gmOutput"]) & 0xFF) != 0)
            throw new InvalidOperationException($"Acer EC {method} rejected the lighting update.");
    }

    public void Dispose()
    {
        lock (TransportLock)
        {
            if (_disposed) return;
            _disposed = true;
            _instance.Dispose();
        }
    }
}
