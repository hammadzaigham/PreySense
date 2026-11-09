using System;
using System.Collections.Generic;
using PreySense.Core.Driver;

namespace PreySense.Frontend.ViewModels;

public partial class MainViewModel
{
    // Unsaved edits only. Persisted values are read fresh until the user edits them.
    private readonly Dictionary<int, GpuPowerOffsets> _gpuOffsets = new();
    public static int GpuModeIndex(byte mode) => mode switch
    {
        0x06 => 0, 0x00 => 1, 0x04 => 3, 0x05 => 4, _ => 2
    };

    internal bool IsGpuHardwareModeActive(byte mode) => _hw.CurrentPowerMode == mode;

    public GpuPowerOffsets? GpuOffsetsForMode(int modeIndex)
    {
        if (_gpuOffsets.TryGetValue(modeIndex, out var draft)) return draft;
        var tuning = (TuningConfigurationReader?.Invoke() ?? TuningStore.GetCurrent()).Tuning;
        var profile = modeIndex switch
        {
            0 => tuning.Eco, 1 => tuning.Quiet, 2 => tuning.Balanced,
            3 => tuning.Performance, 4 => tuning.Turbo, _ => throw new ArgumentOutOfRangeException(nameof(modeIndex))
        };
        return profile.GpuDeltaWatts.HasValue && profile.GpuDynamicBoostWatts.HasValue && profile.PlatformPowerOffsetWatts.HasValue
            ? new(profile.GpuDeltaWatts.Value, profile.GpuDynamicBoostWatts.Value, profile.PlatformPowerOffsetWatts.Value) : null;
    }

    public void StoreGpuOffsets(int modeIndex, GpuPowerOffsets offsets)
    {
        if (!offsets.IsValid) throw new ArgumentOutOfRangeException(nameof(offsets));
        _gpuOffsets[modeIndex] = offsets;
    }
}
