using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PreySense.Core.Driver;
using PreySense.Core.Configuration;
using PreySense.Core.Hardware;
using PreySense.Core.Services;

namespace PreySense.Frontend.ViewModels;

public partial class MainViewModel
{
    private bool _tuningEnablePending;
    private bool _updatingTuningEnabled;
    private void SetTuningEnabledState(bool enabled)
    {
        _updatingTuningEnabled = true;
        try { IsHardwareTuningEnabled = enabled; }
        finally { _updatingTuningEnabled = false; }
    }

    private int? _factoryProchotOffset;
    private bool? _factoryProchotOffsetLocked;
    internal Task TuningEnableTask { get; private set; } = Task.CompletedTask;
    internal Func<Dictionary<string, ModeTuningConfig>>? CaptureTuningDefaults { get; set; }

    internal async Task CaptureMissingFactoryDefaultsAsync()
    {
        if (TuningStore.GetCurrent().Tuning.HasFactoryDefaults || _disposed) return;
        bool gateHeld = false;
        try
        {
            await _modeSwitchGate.WaitAsync();
            gateHeld = true;
            if (TuningStore.GetCurrent().Tuning.HasFactoryDefaults || _disposed) return;
            var defaults = await Task.Run(CaptureDefaultsWithDriver);
            if (_disposed) return;
            if (!new TuningConfig { FactoryDefaults = defaults }.HasFactoryDefaults)
                throw new InvalidOperationException("Calibration did not capture valid defaults for all five modes.");
            TuningStore.Update(cfg =>
            {
                cfg.Tuning.FactoryDefaults = defaults;
                if (_factoryProchotOffset.HasValue) cfg.Tuning.FactoryProchotOffsetCelsius = _factoryProchotOffset;
                if (_factoryProchotOffsetLocked.HasValue) cfg.Tuning.FactoryProchotOffsetLocked = _factoryProchotOffsetLocked;
            });
            // Constructor may have left the switch off when defaults were missing at startup.
            if (TuningStore.GetCurrent().General.HardwareTuningEnabled
                && TuningStore.GetCurrent().Tuning.HasFactoryDefaults)
                SetTuningEnabledState(true);
        }
        catch (Exception ex)
        {
            DiagnosticMessage = $"Factory defaults capture failed: {ex.Message}";
        }
        finally { if (gateHeld) _modeSwitchGate.Release(); }
    }

    private async Task InitializeTuningAndSessionAsync()
    {
        if (_hw is not MockHardwareService)
        {
            await RefreshProchotLockStateAsync();
            await CaptureMissingFactoryDefaultsAsync();
        }
        if (!_disposed) await InitializeSessionAsync();
    }

    private Dictionary<string, ModeTuningConfig> CaptureDefaultsWithDriver()
    {
        if (CaptureTuningDefaults is not null) return CaptureTuningDefaults();
        if (!GpuBasePowerWatts.HasValue)
        {
            int? detectedGpu = PreySense.Gpu.NvmlHelper.GetGpuDefaultPowerLimitWatts();
            if (detectedGpu.HasValue)
            {
                int? saved = GpuPowerBaseline.SaveOnce(detectedGpu.Value);
                if (saved.HasValue) GpuBasePowerWatts = saved.Value;
            }
        }
        var backend = CpuDriverRuntime.Backend ?? throw new InvalidOperationException(CpuDriverRuntime.Status);
        var calibration = new DriverFactoryCalibration(backend, backend);
        var profiles = calibration.Capture(GpuBasePowerWatts);
        _factoryProchotOffset = calibration.ProchotOffsetCelsius;
        _factoryProchotOffsetLocked = calibration.ProchotOffsetLocked;
        return profiles;
    }

    private async Task EnableTuningAsync()
    {
        if (_tuningEnablePending) return;
        _tuningEnablePending = true;
        bool gateHeld = false;
        try
        {
            await _modeSwitchGate.WaitAsync();
            gateHeld = true;
            if (_disposed)
            {
                SetTuningEnabledState(false);
                try { TuningStore.Update(cfg => cfg.General.HardwareTuningEnabled = false); } catch { }
                DiagnosticMessage = "Tuning remains off. PreySense is shutting down.";
                return;
            }

            IsBusy = true;
            if (!TuningStore.GetCurrent().Tuning.HasFactoryDefaults)
            {
                SetTuningEnabledState(false);
                try { TuningStore.Update(cfg => cfg.General.HardwareTuningEnabled = false); } catch { }
                DiagnosticMessage = "Tuning remains off. Factory defaults are not available yet.";
                return;
            }
            TuningStore.Update(cfg => cfg.General.HardwareTuningEnabled = true);
            SetTuningEnabledState(true);
            DiagnosticMessage = "Tuning enabled.";
        }
        catch (Exception ex)
        {
            SetTuningEnabledState(false);
            try { TuningStore.Update(cfg => cfg.General.HardwareTuningEnabled = false); } catch { }
            DiagnosticMessage = $"Tuning remains off. {ex.Message}";
        }
        finally
        {
            if (gateHeld) { IsBusy = false; _modeSwitchGate.Release(); }
            _tuningEnablePending = false;
        }
    }

    private static string TuningModeName(int index) => index switch
    {
        0 => "Eco", 1 => "Quiet", 2 => "Balanced", 3 => "Performance", 4 => "Turbo",
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    internal ModeTuningConfig SavedTuningMode(int index)
    {
        var tuning = (TuningConfigurationReader?.Invoke() ?? TuningStore.GetCurrent()).Tuning;
        return index switch { 0 => tuning.Eco, 1 => tuning.Quiet, 2 => tuning.Balanced,
            3 => tuning.Performance, 4 => tuning.Turbo, _ => throw new ArgumentOutOfRangeException(nameof(index)) };
    }

    internal ModeTuningConfig TuningEditorMode(int index)
    {
        var saved = SavedTuningMode(index);
        var factory = FactoryTuningMode(index);
        return new ModeTuningConfig
        {
            CpuPl1Watts = saved.CpuPl1Watts ?? factory?.CpuPl1Watts,
            CpuPl2Watts = saved.CpuPl2Watts ?? factory?.CpuPl2Watts,
            TauSeconds = saved.TauSeconds ?? factory?.TauSeconds,
            CpuBoost = saved.CpuBoost ?? factory?.CpuBoost,
            CpuBoostMode = saved.EffectiveCpuBoostMode ?? factory?.EffectiveCpuBoostMode,
            GpuWatts = saved.GpuWatts ?? factory?.GpuWatts,
            GpuDeltaWatts = saved.GpuDeltaWatts ?? factory?.GpuDeltaWatts,
            GpuDynamicBoostWatts = saved.GpuDynamicBoostWatts ?? factory?.GpuDynamicBoostWatts,
            PlatformPowerOffsetWatts = saved.PlatformPowerOffsetWatts ?? factory?.PlatformPowerOffsetWatts,
            ApplyCpuLimits = saved.ApplyCpuLimits,
            ApplyGpuLimits = saved.ApplyGpuLimits,
            ApplyFanCurve = saved.ApplyFanCurve
        };
    }

    internal int? TuningEditorProchotOffset =>
        ProchotOffsetCelsius ?? TuningStore.GetCurrent().Tuning.FactoryProchotOffsetCelsius;

    internal ModeTuningConfig? FactoryTuningMode(int index) =>
        TuningStore.GetCurrent().Tuning.FactoryDefaults.GetValueOrDefault(TuningModeName(index));
}
