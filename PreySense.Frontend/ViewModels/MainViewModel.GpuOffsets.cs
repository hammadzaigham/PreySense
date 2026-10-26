using System;
using System.Collections.Generic;
using Microsoft.Win32;
using PreySense.Core.Acpi;
using PreySense.Helpers;

namespace PreySense.Frontend.ViewModels;

public partial class MainViewModel
{
    private readonly Dictionary<int, GpuPowerOffsets> _gpuOffsets = new();
    private static readonly string[] GpuOffsetModes = { "Eco", "Quiet", "Balanced", "Perf", "Turbo" };
    private const string GpuOffsetsKey = @"SOFTWARE\PreySense\GpuOffsets";
    public static int GpuModeIndex(byte mode) => mode switch
    {
        0x06 => 0, 0x00 => 1, 0x04 => 3, 0x05 => 4, _ => 2
    };

    internal bool IsGpuHardwareModeActive(byte mode) => _hw.CurrentPowerMode == mode;

    public GpuPowerOffsets GpuOffsetsForMode(int index, int targetWatts)
    {
        if (_gpuOffsets.TryGetValue(index, out var cached)) return cached;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(GpuOffsetsKey);
            string name = GpuOffsetModes[index];
            if (key?.GetValue(name + "Delta") is int delta
                && key.GetValue(name + "Boost") is int boost)
            {
                var saved = new GpuPowerOffsets(delta, boost);
                if (saved.IsValid) return _gpuOffsets[index] = saved;
            }
        }
        catch (Exception ex) { AppLogger.Log($"GPU offset load failed: {ex.Message}"); }
        using var factory = Registry.CurrentUser.OpenSubKey(FactoryProfilesRegistryPath);
        string nameOfMode = GpuOffsetModes[index];
        if (factory?.GetValue(nameOfMode + "GpuDelta") is int factoryDelta
            && factory.GetValue(nameOfMode + "GpuBoost") is int factoryBoost)
        {
            var captured = new GpuPowerOffsets(factoryDelta, factoryBoost);
            if (captured.IsValid && captured.NominalReferenceWatts(GpuBasePowerWatts) == targetWatts)
                return _gpuOffsets[index] = captured;
        }
        return GpuPowerOffsets.FromLegacyTarget(targetWatts, GpuBasePowerWatts);
    }

    public void StoreGpuOffsets(int index, GpuPowerOffsets offsets)
    {
        if (!offsets.IsValid) throw new ArgumentOutOfRangeException(nameof(offsets));
        _gpuOffsets[index] = offsets;
    }

    private void SaveGpuOffsets()
    {
        using var key = Registry.CurrentUser.CreateSubKey(GpuOffsetsKey);
        foreach (var (index, value) in _gpuOffsets)
        {
            string name = GpuOffsetModes[index];
            key.SetValue(name + "Delta", value.DeltaWatts);
            key.SetValue(name + "Boost", value.DynamicBoostWatts);
        }
    }

    private bool ApplyGpuModePower(byte mode, int targetWatts)
    {
        int index = GpuModeIndex(mode);
        var offsets = GpuOffsetsForMode(index, targetWatts);
        return PreySense.Core.Hardware.PreySenseWmiDeviceClient.SetGpuPowerOffsets(offsets);
    }

    private void ResetGpuOffsets(int index)
    {
        _gpuOffsets.Remove(index);
        using var key = Registry.CurrentUser.OpenSubKey(GpuOffsetsKey, writable: true);
        foreach (string suffix in new[] { "Delta", "Boost" })
            key?.DeleteValue(GpuOffsetModes[index] + suffix, throwOnMissingValue: false);
        using var factory = Registry.CurrentUser.OpenSubKey(FactoryProfilesRegistryPath);
        string name = GpuOffsetModes[index];
        if (factory?.GetValue(name + "GpuDelta") is int delta
            && factory.GetValue(name + "GpuBoost") is int boost)
        {
            var value = new GpuPowerOffsets(delta, boost);
            if (value.IsValid) _gpuOffsets[index] = value;
        }
    }

    private static void CaptureFactoryGpuOffsets(byte mode, GpuPowerOffsets offsets)
    {
        using var key = Registry.CurrentUser.CreateSubKey(FactoryProfilesRegistryPath);
        int savedMask = key.GetValue("CalibratedModes") is int saved ? saved : 0;
        int bit = 1 << GpuModeIndex(mode);
        if (key.GetValue("CalibrationLocked") is int locked && locked == 1 || (savedMask & bit) != 0) return;
        string name = GpuOffsetModes[GpuModeIndex(mode)];
        if (key.GetValue(name + "GpuDelta") != null || key.GetValue(name + "GpuBoost") != null) return;
        key.SetValue(name + "GpuDelta", offsets.DeltaWatts);
        key.SetValue(name + "GpuBoost", offsets.DynamicBoostWatts);
    }
}
