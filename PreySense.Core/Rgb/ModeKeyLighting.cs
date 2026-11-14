using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PreySense.Core.Configuration;

namespace PreySense.Rgb;

/// <summary>
/// Mode Key "Mode" lighting follows the active Acer operating mode color.
/// Palette recovered from PHN16S-71 BIOS V1.26 EC firmware, lighting state 5.
/// Explicit RGB writes are used; firmware OPMODE is not required.
/// Only non-default overrides are persisted.
/// </summary>
public static class ModeKeyLighting
{
    public const int FollowOperatingMode = -2;

    /// <summary>OEM Mode Key flash settle; independent of the operating-mode cooldown.</summary>
    public static TimeSpan EcModeTransitionDelay { get; } = TimeSpan.FromMilliseconds(1550);

    public static readonly IReadOnlyList<(byte PowerMode, string Name, string DefaultHex)> Slots =
    [
        (0x06, "Eco", "#10FF10"),
        (0x00, "Quiet", "#A0FF90"),
        (0x01, "Balanced", "#00AEC7"),
        (0x04, "Performance", "#2E09D0"),
        (0x05, "Turbo", "#C700FF"),
    ];

    public static readonly IReadOnlyDictionary<byte, string> DefaultPowerModeColors =
        Slots.ToDictionary(s => s.PowerMode, s => s.DefaultHex);

    public static string Key(byte powerMode) => powerMode.ToString("X2");

    public static string NormalizeHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return "#00E5FF";
        string value = hex.Trim();
        if (value.Length == 6 && value[0] != '#') value = "#" + value;
        try
        {
            var c = System.Drawing.ColorTranslator.FromHtml(value);
            return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        }
        catch { return "#00E5FF"; }
    }

    public static bool HexEquals(string? a, string? b) =>
        string.Equals(NormalizeHex(a), NormalizeHex(b), StringComparison.OrdinalIgnoreCase);

    public static bool IsDefault(byte powerMode, string? colorHex) =>
        DefaultPowerModeColors.TryGetValue(powerMode, out var def) && HexEquals(colorHex, def);

    /// <summary>Effective display/apply color: stored override or default. Never invents a stored override.</summary>
    public static string ColorFor(DeviceLightingConfig settings, byte powerMode)
    {
        if (settings.PowerModeColors != null
            && settings.PowerModeColors.TryGetValue(Key(powerMode), out var stored)
            && !string.IsNullOrWhiteSpace(stored)
            && !IsDefault(powerMode, stored))
            return NormalizeHex(stored);
        if (DefaultPowerModeColors.TryGetValue(powerMode, out var def)) return def;
        return NormalizeHex(settings.ColorHex);
    }

    /// <summary>Keep only colors that differ from defaults (never-set / default → omitted).</summary>
    public static Dictionary<string, string> OnlyCustomOverrides(IReadOnlyDictionary<string, string>? colors)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (colors == null) return result;
        foreach (var pair in colors)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value)) continue;
            if (!byte.TryParse(pair.Key, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte mode)) continue;
            if (!DefaultPowerModeColors.ContainsKey(mode)) continue;
            if (IsDefault(mode, pair.Value)) continue;
            result[Key(mode)] = NormalizeHex(pair.Value);
        }
        return result;
    }

    public static Dictionary<string, string> EnsureDefaults(IReadOnlyDictionary<string, string>? stored)
    {
        var colors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var slot in Slots)
            colors[Key(slot.PowerMode)] = slot.DefaultHex;
        foreach (var pair in OnlyCustomOverrides(stored))
            colors[pair.Key] = pair.Value;
        return colors;
    }

    /// <summary>Keyboard Mode uses the same defaults and override rules as the Mode Key.</summary>
    public static (string Z1, string Z2, string Z3, string Z4) GetZoneColors(byte powerMode, DeviceLightingConfig? settings = null)
    {
        string color = ColorFor(settings ?? new DeviceLightingConfig(), powerMode);
        return (color, color, color, color);
    }

    public static DeviceLightingConfig ResolveForApply(DeviceLightingConfig settings, byte powerMode)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Mode != FollowOperatingMode) return settings;
        var (z1, z2, z3, z4) = GetZoneColors(powerMode, settings);
        return new DeviceLightingConfig
        {
            Mode = FollowOperatingMode,
            Brightness = settings.Brightness,
            Speed = settings.Speed,
            ColorHex = ColorFor(settings, powerMode),
            IndividualColors = false,
            RandomColors = false,
            LedColors = new() { [0] = z1, [1] = z2, [2] = z3, [3] = z4 },
            PowerModeColors = OnlyCustomOverrides(settings.PowerModeColors),
        };
    }

    /// <summary>Application Mode colors always come from the single shared palette.</summary>
    public static string SharedColorFor(IReadOnlyDictionary<string, string>? colors, byte powerMode) =>
        ColorFor(new DeviceLightingConfig { PowerModeColors = OnlyCustomOverrides(colors) }, powerMode);

    public static DeviceLightingConfig ResolveForApply(DeviceLightingConfig settings, byte powerMode,
        IReadOnlyDictionary<string, string>? sharedColors, int? sharedBrightness = null)
    {
        if (!IsFollowMode(settings.Mode)) return settings;
        return ResolveForApply(new DeviceLightingConfig
        {
            Mode = settings.Mode, Brightness = Math.Clamp(sharedBrightness ?? settings.Brightness, 0, 100), Speed = settings.Speed,
            PowerModeColors = OnlyCustomOverrides(sharedColors)
        }, powerMode);
    }

    public static bool IsFollowMode(int mode) => mode == FollowOperatingMode;
}
