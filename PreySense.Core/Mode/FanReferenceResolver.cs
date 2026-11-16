
namespace PreySense.Mode;


/// <summary>An approximate offline target range, never measured RPM or running EC state.</summary>
public sealed record FanRpmEstimate(int MinimumRpm, int MaximumRpm, string Description)
{
    public bool IsApproximate => true;
}

/// <summary>Estimates always use the bundled PHN16S-71 reference.</summary>
public sealed class FanReferenceResolution
{
    internal FanReferenceResolution() { }

    public FactoryFanTable GetTable(byte mode, int variantBit = 0) =>
        FactoryFanReference.GetTable(mode, variantBit);

    /// <summary>Unknown state: range across every reachable normal factory stage.</summary>
    public FanRpmEstimate EstimateRange(byte mode, int percent, bool gpu = false, int variantBit = 0)
    {
        var table = GetTable(mode, variantBit);
        return Range(table, 0, LastNormalStage(table), percent, gpu,
            "Approximate offline range across factory stages; running EC state is unknown.");
    }

    /// <summary>
    /// Steady rising-temperature reference for the selected sensor. The other sensor is unknown,
    /// so the upper bound includes hotter coupled stages. This excludes transient delay/fault
    /// overrides and does not reconstruct the running EC's hysteresis or stage.
    /// </summary>
    public FanRpmEstimate EstimateAtTemperature(byte mode, int temperatureC, int percent,
        bool gpu = false, int variantBit = 0, int? measuredMaximumRpm = null)
    {
        if (temperatureC is < 0 or > 125) throw new ArgumentOutOfRangeException(nameof(temperatureC));
        var table = GetTable(mode, variantBit);
        int last = LastNormalStage(table);
        int risingStage = 0;
        while (risingStage < last)
        {
            var stage = table.Stages[risingStage];
            int trip = gpu ? stage.GpuRiseC : stage.CpuRiseC;
            if (trip >= FactoryFanReference.DisabledTransitionCelsius || temperatureC < trip) break;
            risingStage++;
        }
        return Range(table, risingStage, last, percent, gpu,
            "Approximate steady rising-temperature reference; other sensor and EC hysteresis are unknown. Transition delays and overrides are excluded.", measuredMaximumRpm);
    }

    private static int LastNormalStage(FactoryFanTable table)
    {
        for (int i = 0; i < table.Stages.Count; i++)
        {
            var stage = table.Stages[i];
            if (stage.CpuRiseC >= FactoryFanReference.DisabledTransitionCelsius &&
                stage.GpuRiseC >= FactoryFanReference.DisabledTransitionCelsius) return i;
        }
        return table.Stages.Count - 1;
    }

    private static FanRpmEstimate Range(FactoryFanTable table, int first, int last,
        int percent, bool gpu, string description, int? measuredMaximumRpm = null)
    {
        int minimum = int.MaxValue, maximum = int.MinValue;
        for (int stage = first; stage <= last; stage++)
        {
            int rpm = table.EstimateCustomTargetRpm(stage, percent, gpu);
            if (measuredMaximumRpm is int measured)
            {
                if (measured is <= 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(measuredMaximumRpm));
                int baseline = Math.Min(gpu ? table.Stages[stage].GpuTargetRpm : table.Stages[stage].CpuTargetRpm, measured);
                rpm = baseline + (measured - baseline) * percent / 100;
            }
            minimum = Math.Min(minimum, rpm);
            maximum = Math.Max(maximum, rpm);
        }
        return new(minimum, maximum, description);
    }
}

/// <summary>Always uses the bundled PHN16S-71 reference; never scans user firmware.</summary>
public static class FanReferenceResolver
{
    public static FanReferenceResolution BundledFallback { get; } =
        new();
}
