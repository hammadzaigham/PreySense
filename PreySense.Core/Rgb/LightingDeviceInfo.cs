using PreySense.Core.Configuration;

namespace PreySense.Rgb;

public sealed record LightingLedInfo(int Index, string Name);
public sealed record LightingDirectionInfo(int Value, string Name)
{
    public override string ToString() => Name;
}
public sealed record LightingModeInfo(int Value, string Name, bool SupportsSpeed = false,
    bool SupportsColor = true, bool SupportsRandomColors = false, int SpeedMinimum = 1, int SpeedMaximum = 9,
    uint DirectionFlags = 0, int DefaultDirection = 0)
{
    // OEM service/USB metadata uses the OpenRGB direction capability bits and values.
    public IReadOnlyList<LightingDirectionInfo> Directions => DirectionsForFlags(DirectionFlags);
    public bool SupportsDirection => Directions.Count > 0;
    public static IReadOnlyList<LightingDirectionInfo> DirectionsForFlags(uint flags)
    {
        var values = new List<LightingDirectionInfo>();
        if ((flags & 2) != 0) { values.Add(new(0, "Left")); values.Add(new(1, "Right")); }
        if ((flags & 4) != 0) { values.Add(new(2, "Up")); values.Add(new(3, "Down")); }
        if ((flags & 8) != 0) { values.Add(new(4, "Horizontal")); values.Add(new(5, "Vertical")); }
        return values;
    }
    public int ResolveDirection(int? selected) => selected is int value && Directions.Any(d => d.Value == value)
        ? value : Directions.Any(d => d.Value == DefaultDirection) ? DefaultDirection : Directions.FirstOrDefault()?.Value ?? DefaultDirection;
    public static int NativeSpeed(int percent, int minimum, int maximum) => maximum > minimum
        ? minimum + (int)Math.Round((maximum - (double)minimum) * Math.Clamp(percent, 0, 100) / 100) : minimum;
    public int NativeSpeed(int percent) => NativeSpeed(percent, SpeedMinimum, SpeedMaximum);
    public int SpeedPercent(int level) => SpeedMaximum > SpeedMinimum
        ? (int)Math.Round((Math.Clamp(level, SpeedMinimum, SpeedMaximum) - (double)SpeedMinimum) * 100 / (SpeedMaximum - SpeedMinimum)) : 0;
    public override string ToString() => Name;
}
public sealed record LightingDeviceInfo(string Id, string Name, string Kind,
    IReadOnlyList<LightingLedInfo> Leds, IReadOnlyList<LightingModeInfo> Modes,
    string Details)
{
    /// <summary>Choose one editor for each physical laptop target while retaining transport IDs.</summary>
    public static IReadOnlyList<LightingDeviceInfo> UniqueTargets(IEnumerable<LightingDeviceInfo> devices) => devices
        .GroupBy(PhysicalTarget, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.OrderByDescending(device => device.HasNamedKeys)
            .ThenBy(device => device.Id.StartsWith("usb:", StringComparison.Ordinal) ? 0
                : device.Id.StartsWith("acer-hid-", StringComparison.Ordinal) ? 1
                : device.Id.StartsWith("acer-ec-", StringComparison.Ordinal) ? 2 : 3)
            .ThenByDescending(device => device.Id.EndsWith("-65", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(device => device.Modes.Count).ThenBy(device => device.Id, StringComparer.Ordinal).First()).ToArray();

    private static string PhysicalTarget(LightingDeviceInfo device)
    {
        if (device.Kind.Equals("Keyboard", StringComparison.OrdinalIgnoreCase)) return "keyboard";
        string name = device.Name.Replace(" ", "").Replace("-", "").ToLowerInvariant();
        if (name.Contains("turbokey") || name.Contains("modekey")) return "turbo-key";
        if (device.IsLightBar || name.Contains("lightbar") || name.Contains("dynamicbar"))
        {
            foreach (string side in new[] { "front", "left", "right", "rear" })
                if (name.Contains(side)) return "bar-" + side;
            return "bar";
        }
        if (name.Contains("logo"))
            return name.Contains("base") ? "logo-base" : "logo-cover";
        // An unknown device is not proof of a duplicate physical target.
        return device.Id;
    }
    public bool HasNamedKeys => Kind.Equals("Keyboard", StringComparison.OrdinalIgnoreCase)
        && Leds.Count(led => led.Name.Length == 1 && char.IsLetter(led.Name[0])) >= 20;
    public bool IsLightBar => Kind.Replace(" ", "").Equals("Lightbar", StringComparison.OrdinalIgnoreCase);
    public string DisplayName => Kind.Equals("Keyboard", StringComparison.OrdinalIgnoreCase)
        ? HasNamedKeys ? "Per-Key Keyboard" : "Zone Keyboard"
        : IsLightBar ? DynamicBarName()
        : Kind.Equals("Hotkey", StringComparison.OrdinalIgnoreCase)
            && (Name.Contains("Mode Key", StringComparison.OrdinalIgnoreCase) || Name.Contains("Turbo Key", StringComparison.OrdinalIgnoreCase))
                ? "Turbo Key" : Name;

    private string DynamicBarName()
    {
        // Preserve side labels when a controller exposes several bars.
        foreach (string side in new[] { "Front", "Left", "Right" })
            if (Name.StartsWith(side, StringComparison.OrdinalIgnoreCase)) return $"Dynamic Bar ({side})";
        return "Dynamic Bar";
    }

    public IReadOnlyList<LightingModeInfo> GetEditorModes()
    {
        bool individualStatic = Modes.Any(mode => mode.Value == -1)
            && (HasNamedKeys || IsLightBar);
        var modes = Modes.Any(mode => ModeKeyLighting.IsFollowMode(mode.Value))
            ? Modes : Modes.Concat([new LightingModeInfo(ModeKeyLighting.FollowOperatingMode, "Mode")]).ToArray();
        if (!individualStatic) return modes;
        // Keep transport IDs unchanged. The individual-color writer is the Static editor,
        // so a second uniform hardware Static entry would be redundant.
        return modes.Where(mode => mode.Value == -1 || !mode.Name.Equals("Static", StringComparison.OrdinalIgnoreCase))
            .Select(mode => mode.Value == -1 ? mode with { Name = "Static" } : mode).ToArray();
    }

    public override string ToString() => DisplayName;
}

public interface IExtendedLightingHardware
{
    /// <param name="refresh">
    /// Ignored. Acer lighting targets are hardwired; discovery runs once at startup
    /// and later calls return the cached device list.
    /// </param>
    IReadOnlyList<LightingDeviceInfo> DiscoverLightingDevices(bool refresh = false);
    void ApplyLightingDevice(string deviceId, DeviceLightingConfig settings);
}
