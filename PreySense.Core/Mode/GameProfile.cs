using System.Text.Json.Serialization;

namespace PreySense.Mode;

/// <summary>A game selects an existing operating-mode profile, including its saved tuning and fans.</summary>
public sealed class GameProfile
{
    public string ExecutablePath { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public byte PowerMode { get; set; } = 5;
    public bool Enabled { get; set; } = true;
    public bool ApplyCpuLimits { get; set; }
    public int? CpuPl1Watts { get; set; }
    public int? CpuPl2Watts { get; set; }
    public int? TauSeconds { get; set; }
    public bool ApplyGpuLimits { get; set; }
    public int? GpuPowerWatts { get; set; }
    public int? GpuDeltaWatts { get; set; }
    public int? GpuDynamicBoostWatts { get; set; }
    public int? PlatformPowerOffsetWatts { get; set; }
    [JsonIgnore] public bool HasGpuOffsets => GpuDeltaWatts.HasValue && GpuDynamicBoostWatts.HasValue && PlatformPowerOffsetWatts.HasValue;
    public bool ApplyCpuBoost { get; set; }
    public int? CpuBoostMode { get; set; }
    public GameProfile Copy() => (GameProfile)MemberwiseClone();
    [JsonIgnore] public string ExeName => Path.GetFileName(ExecutablePath);
    [JsonIgnore] public string ModeTitle => ProfileManager.ModeToProfileName(PowerMode);
}

public static class GameProfileStore
{
    public static string NormalizeExecutablePath(string path)
    {
        try { return Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch { return ""; }
    }

    /// <summary>Use the selected executable's full path, never another executable with the same name.</summary>
    public static GameProfile? FindMatchingProfile(string? executablePath, IReadOnlyList<GameProfile> profiles)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return null;
        string path = NormalizeExecutablePath(executablePath);
        if (path.Length == 0) return null;
        return profiles.FirstOrDefault(profile => profile.Enabled && ProfileManager.IsSupportedMode(profile.PowerMode)
            && !string.IsNullOrWhiteSpace(profile.ExecutablePath)
            && string.Equals(NormalizeExecutablePath(profile.ExecutablePath), path, StringComparison.OrdinalIgnoreCase));
    }
}
