using System;
using Microsoft.Win32;

namespace PreySense.Core.Acpi;

public static class GpuPowerBaseline
{
    public const string RegistryPath = @"SOFTWARE\PreySense\FactoryProfiles";
    public const string ValueName = "GpuBasePowerWatts";
    public const int LegacyFallbackWatts = 85;
    public const string VerifiedDefaultSource = "NVML default power management limit";
    private static readonly object Gate = new();
    public static int? FromMilliwatts(uint value)
    {
        if (value < 1000 || value > GpuPowerLimitConfiguration.MaximumPowerLimitWatts * 1000U) return null;
        return (int)Math.Round(value / 1000.0, MidpointRounding.AwayFromZero);
    }
    public static int? ReadWatts()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
            return VerifiedSavedWatts(key?.GetValue(ValueName) as int?,
                key?.GetValue("GpuBasePowerSource") as string);
        }
        catch { return null; }
    }
    // Older builds saved the minimum constraint as the baseline, without a source.
    // Require evidence that the stored value came from the default-limit API.
    public static int? VerifiedSavedWatts(int? watts, string? source) =>
        watts is > 0 and <= GpuPowerLimitConfiguration.MaximumPowerLimitWatts
            && string.Equals(source, VerifiedDefaultSource, StringComparison.Ordinal) ? watts : null;

    public static int? SaveOnce(int watts)
    {
        if (watts is <= 0 or > GpuPowerLimitConfiguration.MaximumPowerLimitWatts) return null;
        lock (Gate)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
            if (!VerifiedSavedWatts(key.GetValue(ValueName) as int?,
                key.GetValue("GpuBasePowerSource") as string).HasValue)
            {
                key.SetValue(ValueName, watts, RegistryValueKind.DWord);
                key.SetValue("GpuBasePowerSource", VerifiedDefaultSource);
            }
            return ReadWatts();
        }
    }
}
