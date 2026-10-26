using System;

namespace PreySense.Core.Acpi;

/// <summary>Independent AC NVPCF offsets, in watts. ERM4 stores each in one byte.</summary>
public sealed record GpuPowerOffsets(int DeltaWatts, int DynamicBoostWatts)
{
    public const int MaximumOffsetWatts = 255;
    public bool IsValid => DeltaWatts is >= 0 and <= MaximumOffsetWatts
        && DynamicBoostWatts is >= 0 and <= MaximumOffsetWatts;

    // Arithmetic reference using the calibrated baseline, not an enforced GPU limit.
    public int NominalReferenceWatts(int baseWatts = 85) => baseWatts + DeltaWatts + DynamicBoostWatts;

    public static GpuPowerOffsets FromLegacyTarget(int targetWatts, int baseWatts = 85)
    {
        int extra = Math.Clamp(targetWatts - baseWatts, 0, MaximumOffsetWatts);
        int boost = extra > 15 ? 15 : 0;
        return new(extra - boost, boost);
    }
}
