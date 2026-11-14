using PreySense.Core.Configuration;
using PreySense.Core.Driver;
using PreySense.Core.Hardware;

namespace PreySense.Frontend.ViewModels;

public partial class MainViewModel
{
    partial void OnGpuBasePowerWattsChanged(int? value)
    {
        if (value is not int baseline) return;
        GpuPowerTargetMin = baseline;
        GpuPowerTargetMax = baseline + DriverFactoryCalibration.MaxGpuDeltaWatts
            + DriverFactoryCalibration.MaxGpuDynamicBoostWatts;
    }

    private static ModeTuningConfig ProfileForMode(TuningConfig tuning, byte mode) => mode switch
    {
        0 => tuning.Quiet, 1 => tuning.Balanced, 4 => tuning.Performance,
        5 => tuning.Turbo, 6 => tuning.Eco, _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    internal bool TryResolveGpuTarget(byte mode, int watts, out GpuPowerOffsets? offsets, out string status)
    {
        offsets = null;
        var tuning = (TuningConfigurationReader?.Invoke() ?? TuningStore.GetCurrent()).Tuning;
        var factory = tuning.FactoryDefaults.GetValueOrDefault(TuningModeName(GpuModeIndex(mode)));
        int? baseline = GpuBasePowerWatts ?? tuning.GpuBasePowerWatts;
        if (factory?.HasGpuLimitValues != true || baseline is null)
        {
            status = "GPU profile table or measured GPU baseline was not found on this laptop.";
            return false;
        }
        // At factory nominal wattage restore all three captured bytes exactly. For custom
        // wattage retain the factory platform budget and split the extra watts as in v3.0.8.
        int factoryWatts = baseline.Value + factory.GpuDeltaWatts!.Value + factory.GpuDynamicBoostWatts!.Value;
        var custom = GpuPowerOffsets.FromLegacyTarget(watts, baseline.Value);
        if (custom.DeltaWatts > DriverFactoryCalibration.MaxGpuDeltaWatts)
            custom = new(DriverFactoryCalibration.MaxGpuDeltaWatts,
                watts - baseline.Value - DriverFactoryCalibration.MaxGpuDeltaWatts);
        var target = watts == factoryWatts
            ? new GpuPowerOffsets(factory.GpuDeltaWatts.Value, factory.GpuDynamicBoostWatts.Value,
                factory.PlatformPowerOffsetWatts!.Value)
            : custom with
                { PlatformOffsetWatts = factory.PlatformPowerOffsetWatts!.Value };
        if (watts < baseline || target.DeltaWatts > DriverFactoryCalibration.MaxGpuDeltaWatts
            || target.DeltaWatts % 5 != 0 || !DriverFactoryCalibration.IsLegalGpuDynamicBoost(target.DynamicBoostWatts)
            || target.PlatformOffsetWatts is < 0 or > 255 || target.PlatformOffsetWatts % 5 != 0
            || target.NominalReferenceWatts(baseline) != watts)
        {
            status = "GPU limit is outside the supported Acer profile range or step.";
            return false;
        }
        offsets = target;
        status = "GPU target resolved from the calibrated Acer profile.";
        return true;
    }
}
