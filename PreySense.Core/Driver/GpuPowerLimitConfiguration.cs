namespace PreySense.Core.Driver;

/// <summary>Validation bounds, not detected hardware limits or factory defaults.</summary>
public static class GpuPowerLimitConfiguration
{
    public const int MinimumPowerLimitWatts = 5;
    public const int MaximumPowerLimitWatts = 999;
}
