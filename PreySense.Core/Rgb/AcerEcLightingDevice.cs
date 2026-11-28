using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.Versioning;
using System.Threading;

namespace PreySense.Rgb;

/// <summary>Direct WMI EC lighting, gated by SMBIOS declarations and firmware capability replies.</summary>
[SupportedOSPlatform("windows")]
public sealed class AcerEcLightingDevice : IDisposable
{
    internal const int LightbarCommitRounds = 3;
    internal const int LightbarCommitGapMs = 65;

    private static readonly object TransportLock = new();
    private readonly AcerEcLightingCapabilities _capabilities;
    private readonly Action<string, object> _invoke;
    private readonly Func<uint, (bool Success, byte[] Output)>? _getBacklight;
    private readonly Action<int> _sleep;
    private readonly Action? _disposeAction;
    private bool _disposed;
    public string Id { get; }
    public string Name { get; }
    public bool IsKeyboard => SideSelector == 0;
    public int SideSelector { get; }
    public int LedCount { get; }
    public bool ModesAffectAllLightbars => !IsKeyboard;

    private AcerEcLightingDevice(string path, AcerEcLightingCapabilities capabilities, int sideSelector, int count)
        : this(capabilities, sideSelector, count, null, null, null)
    {
        var instance = new ManagementObject(path);
        _invoke = (method, value) => InvokeWmi(instance, method, value);
        _getBacklight = slot => TryInvokeGetBacklight(instance, slot);
        _disposeAction = instance.Dispose;
    }

    internal AcerEcLightingDevice(AcerEcLightingCapabilities capabilities, int sideSelector, int count,
        Action<string, object>? invoke, Action<int>? sleep = null,
        Func<uint, (bool Success, byte[] Output)>? getBacklight = null)
    {
        _capabilities = capabilities;
        SideSelector = sideSelector;
        LedCount = count;
        _invoke = invoke ?? ((_, _) => { });
        _getBacklight = getBacklight;
        _sleep = sleep ?? Thread.Sleep;
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
        var colorValues = colors.ToArray();
        lock (TransportLock)
        {
            if (IsKeyboard)
            {
                var mask = AcerEcLightingProtocol.CreateKeyboardLedMask(_capabilities.Major, _capabilities.Minor, colorValues);
                _invoke("SetGamingLED", mask);
                _invoke("SetGamingKBBacklight", AcerEcLightingProtocol.CreateKeyboardBrightness(brightness));
                for (int i = 0; i < colorValues.Length; i++)
                {
                    uint color = colorValues[i];
                    byte red = (byte)color, green = (byte)(color >> 8), blue = (byte)(color >> 16);
                    // WMI Invoke is synchronous; no artificial sleep — pacing held the shared HID/EC
                    // lock and made software-effect frames stall on the fallback path.
                    _invoke("SetGamingRgbKb", AcerEcLightingProtocol.CreateKeyboardZoneColor(i, red, green, blue));
                }
                return;
            }

            var barMask = AcerEcLightingProtocol.CreateLightbarLedMask(_capabilities.Major, _capabilities.Minor, SideSelector, colorValues);
            var barBrightness = AcerEcLightingProtocol.CreateLightbarBrightness(brightness);
            CommitLightbar(() =>
            {
                _invoke("SetGamingLED", barMask);
                _invoke("SetGamingKBBacklight", barBrightness);
                for (int i = 0; i < colorValues.Length; i++)
                {
                    uint color = colorValues[i];
                    byte red = (byte)color, green = (byte)(color >> 8), blue = (byte)(color >> 16);
                    _invoke("SetGamingRgbKb", AcerEcLightingProtocol.CreateLightbarColor(SideSelector, i, red, green, blue));
                }
            });
        }
    }

    public void ApplyMode(byte mode, byte speed, byte brightness, byte direction, byte red, byte green, byte blue)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (TransportLock)
        {
            if (IsKeyboard)
            {
                _invoke("SetGamingKBBacklight", AcerEcLightingProtocol.CreateKeyboardMode(mode, speed, brightness, direction, red, green, blue));
                return;
            }

            Span<uint> allEnabled = stackalloc uint[LedCount];
            allEnabled.Fill(1u);
            var barMask = AcerEcLightingProtocol.CreateLightbarLedMask(_capabilities.Major, _capabilities.Minor, SideSelector, allEnabled);
            var barMode = AcerEcLightingProtocol.CreateLightbarMode(mode, speed, brightness, direction, red, green, blue);
            CommitLightbar(() =>
            {
                _invoke("SetGamingLED", barMask);
                _invoke("SetGamingKBBacklight", barMode);
            });
        }
    }

    public bool TryReadBrightness(out int brightness)
    {
        brightness = 0;
        if (_disposed || _getBacklight is null) return false;
        lock (TransportLock)
        {
            if (_disposed) return false;
            uint[] slots = IsKeyboard ? [1u] : [1u, 2u, 3u];
            foreach (uint slot in slots)
            {
                try
                {
                    var (success, output) = _getBacklight(slot);
                    if (success && output.Length >= 3)
                    {
                        brightness = Math.Clamp((int)output[2], 0, 100);
                        return true;
                    }
                }
                catch { }
            }
            return false;
        }
    }

    private static (bool Success, byte[] Output) TryInvokeGetBacklight(ManagementObject instance, uint slot)
    {
        try
        {
            using var input = instance.GetMethodParameters("GetGamingKBBacklight");
            input["gmInput"] = slot;
            using var output = instance.InvokeMethod("GetGamingKBBacklight", input, null);
            if (output is not null && output["gmReturn"] is byte status && status == 0 && output["gmOutput"] is byte[] bytes)
                return (true, bytes);
        }
        catch { }
        return (false, Array.Empty<byte>());
    }

    private void CommitLightbar(Action round)
    {
        // PH16-71 lightbar firmware only commits color/mode changes reliably when repeated 3 times, 65 ms apart.
        for (int i = 0; i < LightbarCommitRounds; i++)
        {
            if (i > 0) _sleep(LightbarCommitGapMs);
            round();
        }
    }

    private static void InvokeWmi(ManagementObject instance, string method, object value)
    {
        using var input = instance.GetMethodParameters(method);
        input["gmInput"] = value;
        using var output = instance.InvokeMethod(method, input, null);
        if (output is null || output["gmOutput"] is null || (Convert.ToUInt64(output["gmOutput"]) & 0xFF) != 0)
            throw new InvalidOperationException($"Acer EC {method} rejected the lighting update.");
    }

    public void Dispose()
    {
        lock (TransportLock)
        {
            if (_disposed) return;
            _disposed = true;
            _disposeAction?.Invoke();
        }
    }
}
