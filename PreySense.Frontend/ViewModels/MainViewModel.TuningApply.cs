using System;
using System.Threading.Tasks;
using PreySense.Core.Configuration;
using PreySense.Core.Driver;
using PreySense.Core.Hardware;
using PreySense.Core.Services;
using PreySense.Frontend.Services;
using PreySense.Mode;

namespace PreySense.Frontend.ViewModels;

public partial class MainViewModel
{
    internal readonly record struct DriverTuningRequest(byte Mode, int Pl1, int Pl2, int Tau,
        int? GpuRuntimeLimitWatts = null, int? ProchotOffsetCelsius = null,
        bool ApplyCpuLimits = true, bool RestoreMsrToStaticFloor = false, GpuPowerOffsets? GpuOffsets = null)
    {
        public bool ApplyGpuLimits => GpuOffsets is not null || GpuRuntimeLimitWatts.HasValue;
    }
    internal readonly record struct DriverTuningResult(bool Success, string Status);
    // Allows regression tests to exercise the same automatic/manual orchestration without hardware writes.
    internal Func<DriverTuningRequest, DriverTuningResult>? DriverTuningExecutor { get; set; }
    internal Func<PreySenseConfig>? TuningConfigurationReader { get; set; }
    internal ConfigurationService? TuningConfigurationStore { get; set; }
    // Prefer an explicit test store, then ProfileManager's test override, then the process singleton.
    private ConfigurationService TuningStore =>
        TuningConfigurationStore ?? ProfileManager.ConfigurationStore ?? ConfigurationService.Instance;

    private void LoadTuningProfilesFromJson()
    {
        var t = TuningStore.GetCurrent().Tuning;
        ProchotOffsetCelsius = t.ProchotOffsetCelsius;
        IsProchotOffsetLocked = t.FactoryProchotOffsetLocked == true;
        IsProchotOffsetEnabled = t.ApplyProchotOffset && !IsProchotOffsetLocked;
        EnableCpuPowerLimitControl = t.EnableCpuPowerLimitControl;
        EnableGpuPowerLimitControl = t.EnableGpuPowerLimitControl;
        EnableGpuOverclockingControl = t.EnableGpuOverclockingControl;
        EnableCpuBoostTuningControl = t.EnableCpuBoostTuningControl;

        void ApplyEditor(int index, Action<ModeTuningConfig> assign)
        {
            // Prefer saved values, then factory fallbacks — never invent 0W drafts.
            assign(TuningEditorMode(index));
        }

        ApplyEditor(0, d =>
        {
            CustomEcoPl1 = d.CpuPl1Watts ?? CustomEcoPl1; CustomEcoPl2 = d.CpuPl2Watts ?? CustomEcoPl2;
            CustomEcoTau = d.TauSeconds ?? CustomEcoTau; CustomEcoGpuWatts = d.GpuWatts ?? CustomEcoGpuWatts;
            CustomEcoCpuBoost = d.EffectiveCpuBoostMode ?? CustomEcoCpuBoost;
            CustomEcoGpuRuntimeWatts = d.GpuRuntimeLimitWatts;
        });
        ApplyEditor(1, d =>
        {
            CustomQuietPl1 = d.CpuPl1Watts ?? CustomQuietPl1; CustomQuietPl2 = d.CpuPl2Watts ?? CustomQuietPl2;
            CustomQuietTau = d.TauSeconds ?? CustomQuietTau; CustomQuietGpuWatts = d.GpuWatts ?? CustomQuietGpuWatts;
            CustomQuietCpuBoost = d.EffectiveCpuBoostMode ?? CustomQuietCpuBoost;
            CustomQuietGpuRuntimeWatts = d.GpuRuntimeLimitWatts;
        });
        ApplyEditor(2, d =>
        {
            CustomBalancedPl1 = d.CpuPl1Watts ?? CustomBalancedPl1; CustomBalancedPl2 = d.CpuPl2Watts ?? CustomBalancedPl2;
            CustomBalancedTau = d.TauSeconds ?? CustomBalancedTau; CustomBalancedGpuWatts = d.GpuWatts ?? CustomBalancedGpuWatts;
            CustomBalancedCpuBoost = d.EffectiveCpuBoostMode ?? CustomBalancedCpuBoost;
            CustomBalancedGpuRuntimeWatts = d.GpuRuntimeLimitWatts;
        });
        ApplyEditor(3, d =>
        {
            CustomPerfPl1 = d.CpuPl1Watts ?? CustomPerfPl1; CustomPerfPl2 = d.CpuPl2Watts ?? CustomPerfPl2;
            CustomPerfTau = d.TauSeconds ?? CustomPerfTau; CustomPerfGpuWatts = d.GpuWatts ?? CustomPerfGpuWatts;
            CustomPerfCpuBoost = d.EffectiveCpuBoostMode ?? CustomPerfCpuBoost;
            CustomPerfGpuRuntimeWatts = d.GpuRuntimeLimitWatts;
        });
        ApplyEditor(4, d =>
        {
            CustomTurboPl1 = d.CpuPl1Watts ?? CustomTurboPl1; CustomTurboPl2 = d.CpuPl2Watts ?? CustomTurboPl2;
            CustomTurboTau = d.TauSeconds ?? CustomTurboTau; CustomTurboGpuWatts = d.GpuWatts ?? CustomTurboGpuWatts;
            CustomTurboCpuBoost = d.EffectiveCpuBoostMode ?? CustomTurboCpuBoost;
            CustomTurboGpuRuntimeWatts = d.GpuRuntimeLimitWatts;
        });
    }

    private sealed class TuningModeController(MainViewModel owner) : IAcerPowerModeController
    {
        public bool TryGetAcerPowerMode(out byte mode)
        {
            try
            {
                // Some firmwares accept Set but fail Get / return unknown bytes. Prefer a live
                // WMI read, then last-known after a successful Set/Get, then the session UI mode.
                if (owner._hw.TryGetPowerMode(out mode) && mode is 0 or 1 or 4 or 5 or 6)
                    return true;
                mode = owner.ActivePowerMode;
                return mode is 0 or 1 or 4 or 5 or 6;
            }
            catch { mode = 0; return false; }
        }
        public bool SetAcerPowerMode(byte mode) => owner._hw.SetPowerMode(mode);
    }

    private DriverTuningResult ExecuteDriverTuning(DriverTuningRequest request)
    {
        if (request.Mode is not (0 or 1 or 4 or 5 or 6))
            return new(false, "Invalid power mode.");
        if (!request.ApplyCpuLimits && !request.ApplyGpuLimits && request.ProchotOffsetCelsius is null)
            return new(false, "Turn on CPU limits, GPU limits, or PROCHOT first.");
        if (request.ApplyCpuLimits &&
            (request.Pl1 is < 5 or > 300 || request.Pl2 < request.Pl1 || request.Pl2 > 300 || request.Tau is < 0 or > 449))
            return new(false, "CPU limits are out of range.");
        int minGpu = GpuPowerTargetMin > 0 ? GpuPowerTargetMin : (PreySense.Core.Driver.GpuPowerBaseline.ReadWatts() ?? 55);
        int maxGpu = GpuPowerTargetMax > 0 ? GpuPowerTargetMax : 180;
        if (request.GpuRuntimeLimitWatts is int requestedGpuWatts &&
            (requestedGpuWatts < minGpu || requestedGpuWatts > maxGpu))
            return new(false, "GPU limit is out of range.");
        if (request.ProchotOffsetCelsius is < 0 or > 63)
            return new(false, "PROCHOT offset must be between 0 and 63°C.");
        if (request.GpuOffsets is { } direct && (!direct.IsValid || direct.DeltaWatts % 5 != 0
            || !DriverFactoryCalibration.IsLegalGpuDynamicBoost(direct.DynamicBoostWatts) || direct.PlatformOffsetWatts % 5 != 0))
            return new(false, "GPU offsets are outside the supported Acer profile range or step.");
        if (DriverTuningExecutor is not null) return DriverTuningExecutor(request);
        var backend = CpuDriverRuntime.Backend ?? throw new InvalidOperationException(CpuDriverRuntime.Status);
        var cpu = CpuDriverRuntime.PowerLimits ?? throw new InvalidOperationException(CpuDriverRuntime.Status);
        var prochot = new CpuProchotController(backend);
        if (request.ProchotOffsetCelsius is int requestedOffset && !prochot.CanSetOffset(requestedOffset, out string preflight))
            return new(false, preflight);
        ulong mchbar = 0;
        if (request.ApplyCpuLimits && !backend.TryReadMchbar(out mchbar))
            return new(false, "Could not find CPU power registers.");

        // Same transaction as v3.0.8: stage DB/CT/CP while the target is inactive,
        // activate the target once, verify the bytes, then apply CPU limits last.
        if (request.ApplyGpuLimits)
        {
            var offsets = request.GpuOffsets;
            if (offsets is null && !TryResolveGpuTarget(request.Mode, request.GpuRuntimeLimitWatts!.Value, out offsets, out string status))
                return new(false, status);
            if (offsets is null || !offsets.IsValid || offsets.DeltaWatts % 5 != 0
                || !DriverFactoryCalibration.IsLegalGpuDynamicBoost(offsets.DynamicBoostWatts)
                || offsets.PlatformOffsetWatts % 5 != 0)
                return new(false, "GPU offsets are outside the supported Acer profile range or step.");
            var gpu = new AcerProfileApplyTransaction(backend, new TuningModeController(this))
                .Apply(request.Mode, offsets!.DeltaWatts, offsets.DynamicBoostWatts, (byte)offsets.PlatformOffsetWatts);
            if (!gpu.Success) return new(false, gpu.Status);
        }

        if (request.ApplyCpuLimits)
        {
            // EC transitions reload CPU limits, so CPU registers must be written last.
            var tuning = TuningStore.GetCurrent().Tuning;
            var result = cpu.ExecuteCombinedTransaction(
                mchbar, request.Pl1, request.Pl2, true, request.Tau,
                tuning.FactoryStaticCpuPl1Watts, tuning.FactoryStaticCpuPl2Watts,
                restoreMsrToStaticFloor: request.RestoreMsrToStaticFloor);
            if (!result.Success)
                return new(false, result.Status);
        }

        if (request.ProchotOffsetCelsius is int offset)
        {
            if (!prochot.TrySetOffset(offset, out string prochotStatus))
                return new(false, prochotStatus);
        }
        return new(true, "Applied and read back successfully.");
    }

    internal DriverTuningResult ApplySavedDriverTuning(byte mode)
    {
        var tuning = (TuningConfigurationReader?.Invoke() ?? TuningStore.GetCurrent()).Tuning;
        var profile = mode switch
        {
            0 => tuning.Quiet, 1 => tuning.Balanced, 4 => tuning.Performance,
            5 => tuning.Turbo, 6 => tuning.Eco, _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
        bool applyCpuLimits = tuning.EnableCpuPowerLimitControl && profile.ApplyCpuLimits;
        int? prochot = tuning.ApplyProchotOffset && !IsProchotOffsetLocked ? tuning.ProchotOffsetCelsius : null;
        int? gpuRuntime = tuning.EnableGpuPowerLimitControl && !profile.ApplyGpuLimits ? profile.GpuRuntimeLimitWatts : null;
        var gpuOffsets = tuning.EnableGpuPowerLimitControl && profile.ApplyGpuLimits && profile.HasGpuLimitValues
            ? new GpuPowerOffsets(profile.GpuDeltaWatts!.Value, profile.GpuDynamicBoostWatts!.Value, profile.PlatformPowerOffsetWatts!.Value) : null;
        if (tuning.EnableGpuPowerLimitControl && profile.ApplyGpuLimits && gpuOffsets is null)
            return new(false, "Set all GPU offsets before applying.");
        bool applyClockOffsets = tuning.EnableGpuOverclockingControl && profile.ApplyGpuClockOffsets;
        if (applyClockOffsets && !profile.HasGpuClockValues)
            return new(false, "Set GPU core and memory offsets before applying.");
        DriverTuningResult ApplyProfile(DriverTuningRequest request) => ExecuteRequestedTuning(request with { GpuOffsets = gpuOffsets },
            applyClockOffsets, profile.HasGpuClockValues
                ? new(profile.GpuCoreClockOffsetMhz!.Value, profile.GpuMemoryClockOffsetMhz!.Value) : null);
        if (!applyCpuLimits && !gpuRuntime.HasValue && gpuOffsets is null)
        {
            if (!_hw.SetPowerMode(mode))
                return new(false, "Could not switch Acer mode.");
            return ApplyProfile(new(mode, 0, 0, 0, null, prochot, false));
        }
        if (!profile.HasValues)
            return _hw.SetPowerMode(mode) ? new(true, "Acer mode selected.")
                : new(false, "Could not switch Acer mode.");
        if (!_hw.SetPowerMode(mode))
            return new(false, "Could not switch Acer mode.");
        return ApplyProfile(new(mode,
            profile.CpuPl1Watts ?? 0, profile.CpuPl2Watts ?? 0, profile.TauSeconds ?? 0,
            gpuRuntime, prochot, applyCpuLimits));
    }

    internal DriverTuningResult RestoreSavedPowerMode(byte mode)
    {
        try
        {
            if (IsHardwareTuningEnabled) return ApplySavedDriverTuning(mode);
            return _hw.SetPowerMode(mode) ? new(true, "Acer mode restored.")
                : new(false, "Could not switch Acer mode.");
        }
        catch (Exception ex) { return new(false, ex.Message); }
    }

    partial void OnIsHardwareTuningEnabledChanged(bool value)
    {
        if (_updatingTuningEnabled) return;
        if (!value)
        {
            try
            {
                ClearUserTuningProfilesKeepFactory();
                TuningStore.Update(cfg => cfg.General.HardwareTuningEnabled = false);
            }
            catch (Exception ex) { DiagnosticMessage = $"Could not save tuning setting: {ex.Message}"; }
            ResetAllTuningEditorsToFactory();
            DiagnosticMessage = "Tuning disabled.";
            return;
        }
        // Enabling only flips the flag; factory capture happens at startup, never here.
        SetTuningEnabledState(false);
        if (_tuningEnablePending) return;
        TuningEnableTask = EnableTuningAsync();
    }

    private void ClearUserTuningProfilesKeepFactory()
    {
        TuningStore.Update(cfg =>
        {
            foreach (var profile in new[] { cfg.Tuning.Eco, cfg.Tuning.Quiet, cfg.Tuning.Balanced, cfg.Tuning.Performance, cfg.Tuning.Turbo })
            {
                profile.CpuPl1Watts = null;
                profile.CpuPl2Watts = null;
                profile.TauSeconds = null;
                profile.CpuBoost = null;
                profile.CpuBoostMode = null;
                profile.ApplyCpuBoost = false;
                profile.GpuWatts = null;
                profile.GpuRuntimeLimitWatts = null;
                profile.GpuDeltaWatts = profile.GpuDynamicBoostWatts = profile.PlatformPowerOffsetWatts = null;
                profile.ApplyCpuLimits = false;
                profile.ApplyGpuLimits = false;
                profile.ApplyGpuClockOffsets = false;
                profile.GpuCoreClockOffsetMhz = null;
                profile.GpuMemoryClockOffsetMhz = null;
            }
            cfg.Tuning.ProchotOffsetCelsius = null;
            cfg.Tuning.ApplyProchotOffset = false;
        });
    }

    private void ResetAllTuningEditorsToFactory()
    {
        for (int i = 0; i < 5; i++)
            ResetModeToCapturedFactoryDefaults(i);
        var tuning = TuningStore.GetCurrent().Tuning;
        ProchotOffsetCelsius = tuning.FactoryProchotOffsetCelsius;
        IsProchotOffsetLocked = tuning.FactoryProchotOffsetLocked == true;
        IsProchotOffsetEnabled = false;
    }

    internal async Task<bool> ApplyTuningProfileAsync(byte mode, int pl1, int pl2, int tau,
        int? gpuRuntimeLimitWatts, bool persist, int? prochotOffsetCelsius = null, bool applyProchotOffset = false,
        bool applyCpuLimits = true, bool persistApplySwitches = true,
        bool restoreMsrToStaticFloor = false,
        PreySense.Gpu.GpuClockOffsets? clockOffsets = null, bool applyGpuClockOffsets = false, bool applyCpuBoost = false, GpuPowerOffsets? gpuOffsets = null)
    {
        applyProchotOffset &= !IsProchotOffsetLocked;
        if (applyProchotOffset && prochotOffsetCelsius is null)
        {
            DiagnosticMessage = "Enter a PROCHOT offset, or turn its switch off.";
            return false;
        }
        if (!IsHardwareTuningEnabled || IsBusy)
        {
            DiagnosticMessage = IsBusy ? "Tuning is busy. Try again in a moment." : "Tuning is unavailable until factory defaults are ready.";
            AppLogger.Error($"[Tuning] {DiagnosticMessage}");
            return false;
        }
        if (!applyCpuLimits && !gpuRuntimeLimitWatts.HasValue && gpuOffsets is null && !applyProchotOffset && !applyGpuClockOffsets && applyCpuBoost)
            return await ApplyCpuBoostOnlyAsync(mode, persist, persistApplySwitches);
        if (!applyCpuLimits && !gpuRuntimeLimitWatts.HasValue && gpuOffsets is null && !applyProchotOffset && !applyGpuClockOffsets)
        {
            if (!persist) { DiagnosticMessage = "Turn on CPU limits, GPU limits, GPU overclocking, or PROCHOT first."; return false; }
            try
            {
                TuningStore.Update(cfg =>
                {
                    var profile = mode switch { 0 => cfg.Tuning.Quiet, 1 => cfg.Tuning.Balanced,
                        4 => cfg.Tuning.Performance, 5 => cfg.Tuning.Turbo, 6 => cfg.Tuning.Eco,
                        _ => throw new ArgumentOutOfRangeException(nameof(mode)) };
                    profile.ApplyCpuLimits = profile.ApplyGpuClockOffsets = profile.ApplyCpuBoost = false;
                    profile.GpuRuntimeLimitWatts = null;
                    profile.ApplyGpuLimits = false;
                    cfg.Tuning.ApplyProchotOffset = false;
                });
                DiagnosticMessage = "Saved. Disabled tuning sections were skipped.";
                return true;
            }
            catch (Exception ex) { DiagnosticMessage = $"Could not save tuning: {ex.Message}"; return false; }
        }
        CancelPendingModeSwitch();
        if (!await TryEnterModeSwitchGateAsync())
        {
            DiagnosticMessage = "Tuning stays off — PreySense is shutting down.";
            return false;
        }
        IsBusy = true;
        try
        {
            AppLogger.Log($"[Tuning] Apply mode=0x{mode:X2} CPU={(applyCpuLimits ? $"{pl1}/{pl2}/τ{tau}" : "off")} GPU={(gpuRuntimeLimitWatts.HasValue ? $"{gpuRuntimeLimitWatts}W" : "OEM dynamic")}");
            var applied = await Task.Run(() => ExecuteRequestedTuning(new(mode, pl1, pl2, tau, gpuRuntimeLimitWatts,
                applyProchotOffset ? prochotOffsetCelsius : null, applyCpuLimits, restoreMsrToStaticFloor, gpuOffsets),
                applyGpuClockOffsets, clockOffsets));
            if (!applied.Success) throw new InvalidOperationException(applied.Status);
            ActivePowerMode = await Task.Run(() => _hw.CurrentPowerMode);
            UpdateModeName(ActivePowerMode);
            NotifyModeSelectionsChanged();
            bool boostOk = !applyCpuBoost || _hw.CurrentPowerMode != mode || await WriteCpuBoostAsync(mode);
            if (persist)
            {
                int boost = CpuBoostModeForMode(mode);
                bool saveCpu = persistApplySwitches && applyCpuLimits;
                bool saveProchot = persistApplySwitches && applyProchotOffset;
                TuningStore.Update(cfg =>
                {
                    var profile = mode switch { 0 => cfg.Tuning.Quiet, 1 => cfg.Tuning.Balanced,
                        4 => cfg.Tuning.Performance, 5 => cfg.Tuning.Turbo, 6 => cfg.Tuning.Eco,
                        _ => throw new ArgumentOutOfRangeException(nameof(mode)) };
                    profile.ApplyCpuLimits = saveCpu;
                    profile.ApplyGpuClockOffsets = persistApplySwitches && applyGpuClockOffsets;
                    if (applyGpuClockOffsets)
                    {
                        profile.GpuCoreClockOffsetMhz = clockOffsets!.Value.CoreMhz;
                        profile.GpuMemoryClockOffsetMhz = clockOffsets.Value.MemoryMhz;
                    }
                    profile.CpuPl1Watts = pl1; profile.CpuPl2Watts = pl2; profile.TauSeconds = tau;
                    cfg.Tuning.ProchotOffsetCelsius = prochotOffsetCelsius;
                    cfg.Tuning.ApplyProchotOffset = saveProchot;
                    profile.GpuRuntimeLimitWatts = gpuOffsets is null && persistApplySwitches ? gpuRuntimeLimitWatts : null;
                    profile.ApplyGpuLimits = persistApplySwitches && gpuOffsets is not null;
                    if (gpuOffsets is not null)
                    {
                        profile.GpuDeltaWatts = gpuOffsets.DeltaWatts;
                        profile.GpuDynamicBoostWatts = gpuOffsets.DynamicBoostWatts;
                        profile.PlatformPowerOffsetWatts = gpuOffsets.PlatformOffsetWatts;
                    }
                    profile.ApplyCpuBoost = persistApplySwitches && applyCpuBoost;
                    if (applyCpuBoost)
                    {
                        profile.CpuBoostMode = boost;
                        profile.CpuBoost = boost > 0;
                    }
                    profile.GpuWatts = gpuRuntimeLimitWatts ?? profile.GpuWatts;
                });
                if (!persistApplySwitches)
                    IsProchotOffsetEnabled = false;
            }
            DiagnosticMessage = boostOk
                ? (persist ? "Applied and saved." : "Applied and read back successfully.")
                : "Applied, but CPU boost failed.";
            AppLogger.Log($"[Tuning] {DiagnosticMessage}");
            return boostOk;
        }
        catch (Exception ex)
        {
            DiagnosticMessage = ex.Message.StartsWith("Apply failed", StringComparison.OrdinalIgnoreCase)
                ? ex.Message
                : $"Could not apply: {ex.Message}";
            AppLogger.Error($"[Tuning] {ex}");
            try { ActivePowerMode = await Task.Run(() => _hw.CurrentPowerMode); UpdateModeName(ActivePowerMode); NotifyModeSelectionsChanged(); } catch { }
            return false;
        }
        finally { IsBusy = false; ExitModeSwitchGate(); }
    }
}
