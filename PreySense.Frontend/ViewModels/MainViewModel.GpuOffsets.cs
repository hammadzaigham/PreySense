using System;
using System.Collections.Generic;
using PreySense.Core.Driver;
using PreySense.Core.Services;

namespace PreySense.Frontend.ViewModels;

public partial class MainViewModel
{
    // Unsaved edits only. Persisted values are read fresh until the user edits them.
    private Dictionary<int, int?> _gpuRuntimeLimits = new();
    private Dictionary<int, GpuPowerOffsets> _gpuOffsets = new();

    private Dictionary<int, int?> SafeGpuRuntimeLimits => _gpuRuntimeLimits ??= new();
    private Dictionary<int, GpuPowerOffsets> SafeGpuOffsets => _gpuOffsets ??= new();

    public static int GpuModeIndex(byte mode) => mode switch
    {
        0x06 => 0, 0x00 => 1, 0x04 => 3, 0x05 => 4, _ => 2
    };

    public static byte ModeIndexToByte(int modeIndex) => modeIndex switch
    {
        0 => 0x06,
        1 => 0x00,
        3 => 0x04,
        4 => 0x05,
        _ => 0x01
    };

    public static int ByteToModeIndex(byte mode) => mode switch
    {
        0x06 => 0,
        0x00 => 1,
        0x04 => 3,
        0x05 => 4,
        _ => 2
    };

    internal bool IsGpuHardwareModeActive(byte mode) => _hw.CurrentPowerMode == mode;

    public int? GpuRuntimeLimitForMode(byte mode) =>
        GpuRuntimeLimitForModeIndex(GpuModeIndex(mode));

    public int? GpuRuntimeLimitForModeIndex(int modeIndex)
    {
        if (SafeGpuRuntimeLimits.TryGetValue(modeIndex, out var draft)) return draft;
        var tuning = (TuningConfigurationReader?.Invoke() ?? TuningStore.GetCurrent()).Tuning;
        var profile = modeIndex switch
        {
            0 => tuning.Eco,
            1 => tuning.Quiet,
            2 => tuning.Balanced,
            3 => tuning.Performance,
            4 => tuning.Turbo,
            _ => throw new ArgumentOutOfRangeException(nameof(modeIndex))
        };
        return profile.GpuRuntimeLimitWatts;
    }

    public void StoreGpuRuntimeLimit(int modeIndex, int? limit)
    {
        SafeGpuRuntimeLimits[modeIndex] = limit;
        SetGpuRuntimeLimitForModeIndex(modeIndex, limit);
    }

    public GpuPowerOffsets? GpuOffsetsForMode(int modeIndex)
    {
        if (SafeGpuOffsets.TryGetValue(modeIndex, out var draft)) return draft;
        var profile = TuningEditorMode(modeIndex);
        return profile.HasGpuLimitValues
            ? new(profile.GpuDeltaWatts!.Value, profile.GpuDynamicBoostWatts!.Value, profile.PlatformPowerOffsetWatts!.Value) : null;
    }

    public void StoreGpuOffsets(int modeIndex, GpuPowerOffsets offsets)
    {
        if (!offsets.IsValid) throw new ArgumentOutOfRangeException(nameof(offsets));
        SafeGpuOffsets[modeIndex] = offsets;
    }
}
