using PreySense.Core.Configuration;

namespace PreySense.Rgb;

public sealed record LightingLedInfo(int Index, string Name);
public sealed record LightingModeInfo(int Value, string Name, bool SupportsSpeed = false,
    bool SupportsColor = true, bool SupportsRandomColors = false, int SpeedMinimum = 1, int SpeedMaximum = 9)
{
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
    public bool HasNamedKeys => Kind.Equals("Keyboard", StringComparison.OrdinalIgnoreCase)
        && Leds.Count(led => led.Name.Length == 1 && char.IsLetter(led.Name[0])) >= 20;
    public bool IsLightBar => Kind.Replace(" ", "").Equals("Lightbar", StringComparison.OrdinalIgnoreCase);
    public string DisplayName => Kind.Equals("Keyboard", StringComparison.OrdinalIgnoreCase)
        ? HasNamedKeys ? "Per-Key Keyboard" : "Zone Keyboard"
        : IsLightBar ? DynamicBarName() : Name;

    private string DynamicBarName()
    {
        // Preserve side labels when a controller exposes several bars.
        foreach (string side in new[] { "Front", "Left", "Right", "Rear" })
            if (Name.StartsWith(side, StringComparison.OrdinalIgnoreCase)) return $"Dynamic Bar ({side})";
        return "Dynamic Bar";
    }

    public IReadOnlyList<LightingModeInfo> GetEditorModes()
    {
        bool individualStatic = Modes.Any(mode => mode.Value == -1)
            && (HasNamedKeys || IsLightBar);
        if (!individualStatic) return Modes;
        // Keep transport IDs unchanged. The individual-color writer is the Static editor,
        // so a second uniform hardware Static entry would be redundant.
        return Modes.Where(mode => mode.Value == -1 || !mode.Name.Equals("Static", StringComparison.OrdinalIgnoreCase))
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
