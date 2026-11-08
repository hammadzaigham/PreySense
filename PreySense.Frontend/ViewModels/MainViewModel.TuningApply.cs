using System;
using System.Threading.Tasks;
using PreySense.Core.Configuration;
using PreySense.Core.Hardware;
using PreySense.Mode;

namespace PreySense.Frontend.ViewModels;

public partial class MainViewModel
{
    internal readonly record struct DriverTuningRequest(byte Mode, int Pl1, int Pl2, int Tau,
        PreySense.Core.Driver.GpuPowerOffsets Offsets, int? ProchotOffsetCelsius = null,
        bool ApplyCpuLimits = true, bool ApplyGpuLimits = true);
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
        });
        ApplyEditor(1, d =>
        {
            CustomQuietPl1 = d.CpuPl1Watts ?? CustomQuietPl1; CustomQuietPl2 = d.CpuPl2Watts ?? CustomQuietPl2;
            CustomQuietTau = d.TauSeconds ?? CustomQuietTau; CustomQuietGpuWatts = d.GpuWatts ?? CustomQuietGpuWatts;
            CustomQuietCpuBoost = d.EffectiveCpuBoostMode ?? CustomQuietCpuBoost;
        });
        ApplyEditor(2, d =>
        {
            CustomBalancedPl1 = d.CpuPl1Watts ?? CustomBalancedPl1; CustomBalancedPl2 = d.CpuPl2Watts ?? CustomBalancedPl2;
            CustomBalancedTau = d.TauSeconds ?? CustomBalancedTau; CustomBalancedGpuWatts = d.GpuWatts ?? CustomBalancedGpuWatts;
            CustomBalancedCpuBoost = d.EffectiveCpuBoostMode ?? CustomBalancedCpuBoost;
        });
        ApplyEditor(3, d =>
        {
            CustomPerfPl1 = d.CpuPl1Watts ?? CustomPerfPl1; CustomPerfPl2 = d.CpuPl2Watts ?? CustomPerfPl2;
            CustomPerfTau = d.TauSeconds ?? CustomPerfTau; CustomPerfGpuWatts = d.GpuWatts ?? CustomPerfGpuWatts;
            CustomPerfCpuBoost = d.EffectiveCpuBoostMode ?? CustomPerfCpuBoost;
        });
        ApplyEditor(4, d =>
        {
            CustomTurboPl1 = d.CpuPl1Watts ?? CustomTurboPl1; CustomTurboPl2 = d.CpuPl2Watts ?? CustomTurboPl2;
            CustomTurboTau = d.TauSeconds ?? CustomTurboTau; CustomTurboGpuWatts = d.GpuWatts ?? CustomTurboGpuWatts;
            CustomTurboCpuBoost = d.EffectiveCpuBoostMode ?? CustomTurboCpuBoost;
        });
    }

    private sealed class TuningModeController(MainViewModel owner) : IAcerPowerModeController
    {
        public bool TryGetAcerPowerMode(out byte mode)
        {
            try { return owner._hw.TryGetPowerMode(out mode) && mode is 0 or 1 or 4 or 5 or 6; }
            catch { mode = 0; return false; }
        }
        public bool SetAcerPowerMode(byte mode) => owner._hw.SetPowerMode(mode);
    }

    private DriverTuningResult ExecuteDriverTuning(DriverTuningRequest request)
    {
        if (request.Mode is not (0 or 1 or 4 or 5 or 6))
            return new(false, "Invalid tuning profile; nothing was written.");
        if (!request.ApplyCpuLimits && !request.ApplyGpuLimits)
            return new(false, "CPU and GPU power limits are both off; nothing was written.");
        if (request.ApplyCpuLimits &&
            (request.Pl1 is < 5 or > 300 || request.Pl2 < request.Pl1 || request.Pl2 > 300 || request.Tau is < 0 or > 449))
            return new(false, "Invalid CPU power limits; nothing was written.");
        if (request.ApplyGpuLimits &&
            (request.Offsets.DeltaWatts is < 0
                || request.Offsets.DeltaWatts > DriverFactoryCalibration.MaxGpuDeltaWatts
                || request.Offsets.DeltaWatts % 5 != 0 ||
                !DriverFactoryCalibration.IsLegalGpuDynamicBoost(request.Offsets.DynamicBoostWatts) ||
                request.Offsets.PlatformOffsetWatts is < 0 or > 100 || request.Offsets.PlatformOffsetWatts % 5 != 0))
            return new(false, "Invalid GPU power limits; nothing was written.");
        if (request.ProchotOffsetCelsius is < 0 or > 63)
            return new(false, "Invalid PROCHOT offset; nothing was written.");
        if (DriverTuningExecutor is not null) return DriverTuningExecutor(request);
        var backend = CpuDriverRuntime.Backend ?? throw new InvalidOperationException(CpuDriverRuntime.Status);
        var cpu = CpuDriverRuntime.PowerLimits ?? throw new InvalidOperationException(CpuDriverRuntime.Status);
        var prochot = new CpuProchotController(backend);
        if (request.ProchotOffsetCelsius is int requestedOffset && !prochot.CanSetOffset(requestedOffset, out string preflight))
            return new(false, preflight + " No tuning values were written.");
        ulong mchbar = 0;
        if (request.ApplyCpuLimits && !backend.TryReadMchbar(out mchbar))
            return new(false, "Cannot discover the supported CPU MCHBAR; nothing was written.");

        if (request.ApplyGpuLimits)
        {
            var gpu = new AcerProfileApplyTransaction(backend, new TuningModeController(this))
                .Apply(request.Mode, request.Offsets.DeltaWatts, request.Offsets.DynamicBoostWatts, (byte)request.Offsets.PlatformOffsetWatts);
            if (!gpu.Success) return new(false, gpu.Status);
        }
        else if (!_hw.SetPowerMode(request.Mode))
            return new(false, "The Acer mode could not be activated.");

        if (request.ApplyCpuLimits)
        {
            // EC transitions reload CPU limits, so CPU registers must be written last.
            var tuning = TuningStore.GetCurrent().Tuning;
            var result = cpu.ExecuteCombinedTransaction(
                mchbar, request.Pl1, request.Pl2, true, request.Tau,
                tuning.FactoryStaticCpuPl1Watts, tuning.FactoryStaticCpuPl2Watts);
            if (!result.Success)
                return new(false, $"Apply failed: {result.Status}");
        }

        if (request.ProchotOffsetCelsius is int offset)
        {
            if (!prochot.TrySetOffset(offset, out string prochotStatus))
                return new(false, $"Apply failed: {prochotStatus}");
        }
        return new(true, "Applied and readback successfully.");
    }

    internal DriverTuningResult ApplySavedDriverTuning(byte mode)
    {
        var tuning = (TuningConfigurationReader?.Invoke() ?? TuningStore.GetCurrent()).Tuning;
        var profile = mode switch
        {
            0 => tuning.Quiet, 1 => tuning.Balanced, 4 => tuning.Performance,
            5 => tuning.Turbo, 6 => tuning.Eco, _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
        if (!profile.ApplyCpuLimits && !profile.ApplyGpuLimits)
            return _hw.SetPowerMode(mode) ? new(true, "Acer mode selected; no custom profile is saved.")
                : new(false, "The Acer mode could not be activated.");
        if (!profile.HasValues)
            return _hw.SetPowerMode(mode) ? new(true, "Acer mode selected; no custom profile is saved.")
                : new(false, "The Acer mode could not be activated.");
        return ExecuteDriverTuning(new(mode,
            profile.CpuPl1Watts ?? 0, profile.CpuPl2Watts ?? 0, profile.TauSeconds ?? 0,
            new(profile.GpuDeltaWatts ?? 0, profile.GpuDynamicBoostWatts ?? 0, profile.PlatformPowerOffsetWatts ?? 0),
            tuning.ApplyProchotOffset && !IsProchotOffsetLocked ? tuning.ProchotOffsetCelsius : null,
            profile.ApplyCpuLimits, profile.ApplyGpuLimits));
    }

    internal DriverTuningResult RestoreSavedPowerMode(byte mode)
    {
        try
        {
            if (IsHardwareTuningEnabled) return ApplySavedDriverTuning(mode);
            return _hw.SetPowerMode(mode) ? new(true, "Acer mode restored.")
                : new(false, "The saved Acer mode could not be activated.");
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
            catch (Exception ex) { DiagnosticMessage = $"Could not save the tuning preference: {ex.Message}"; }
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
                profile.GpuWatts = null;
                profile.GpuDeltaWatts = null;
                profile.GpuDynamicBoostWatts = null;
                profile.PlatformPowerOffsetWatts = null;
                profile.ApplyCpuLimits = false;
                profile.ApplyGpuLimits = false;
                profile.ApplyFanCurve = false;
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
        PreySense.Core.Driver.GpuPowerOffsets offsets, bool persist, int? prochotOffsetCelsius = null, bool applyProchotOffset = false,
        bool applyCpuLimits = true, bool applyGpuLimits = true)
    {
        applyProchotOffset &= !IsProchotOffsetLocked;
        if (applyProchotOffset && prochotOffsetCelsius is null)
        {
            DiagnosticMessage = "Enter a global PROCHOT offset before applying.";
            return false;
        }
        if (!applyCpuLimits && !applyGpuLimits)
        {
            DiagnosticMessage = "Turn on CPU or GPU power limits before applying.";
            return false;
        }
        if (!IsHardwareTuningEnabled || IsBusy)
        {
            DiagnosticMessage = IsBusy ? "Tuning is busy; wait for the current operation." : "Enable Hardware Tuning before applying settings.";
            AppLogger.Error($"[Tuning] {DiagnosticMessage}");
            return false;
        }
        CancelPendingModeSwitch();
        await _modeSwitchGate.WaitAsync();
        IsBusy = true;
        try
        {
            AppLogger.Log($"[Tuning] Apply mode=0x{mode:X2} CPU={(applyCpuLimits ? $"{pl1}/{pl2}/τ{tau}" : "off")} GPU={(applyGpuLimits ? $"Δ{offsets.DeltaWatts}/DB{offsets.DynamicBoostWatts}/CP{offsets.PlatformOffsetWatts}" : "off")}");
            var applied = await Task.Run(() => ExecuteDriverTuning(new(mode, pl1, pl2, tau, offsets,
                applyProchotOffset ? prochotOffsetCelsius : null, applyCpuLimits, applyGpuLimits)));
            if (!applied.Success) throw new InvalidOperationException(applied.Status);
            ActivePowerMode = _hw.CurrentPowerMode;
            UpdateModeName(ActivePowerMode);
            NotifyModeSelectionsChanged();
            bool boostOk = ApplyCpuBoostForMode(mode);
            if (persist)
            {
                int boost = CpuBoostModeForMode(mode);
                TuningStore.Update(cfg =>
                {
                    var profile = mode switch { 0 => cfg.Tuning.Quiet, 1 => cfg.Tuning.Balanced,
                        4 => cfg.Tuning.Performance, 5 => cfg.Tuning.Turbo, 6 => cfg.Tuning.Eco,
                        _ => throw new ArgumentOutOfRangeException(nameof(mode)) };
                    profile.ApplyCpuLimits = applyCpuLimits;
                    profile.ApplyGpuLimits = applyGpuLimits;
                    profile.CpuPl1Watts = pl1; profile.CpuPl2Watts = pl2; profile.TauSeconds = tau;
                    cfg.Tuning.ProchotOffsetCelsius = prochotOffsetCelsius;
                    cfg.Tuning.ApplyProchotOffset = applyProchotOffset;
                    profile.GpuDeltaWatts = offsets.DeltaWatts; profile.GpuDynamicBoostWatts = offsets.DynamicBoostWatts;
                    profile.PlatformPowerOffsetWatts = offsets.PlatformOffsetWatts;
                    profile.CpuBoostMode = boost;
                    profile.CpuBoost = boost > 0;
                    profile.GpuWatts = offsets.NominalReferenceWatts(GpuBasePowerWatts);
                });
            }
            DiagnosticMessage = boostOk
                ? (persist ? "Applied and saved." : "Applied and readback successfully.")
                : "Applied, but CPU boost failed.";
            AppLogger.Log($"[Tuning] {DiagnosticMessage}");
            return boostOk;
        }
        catch (Exception ex)
        {
            DiagnosticMessage = $"Apply failed: {ex.Message}";
            AppLogger.Error($"[Tuning] {ex}");
            try { ActivePowerMode = _hw.CurrentPowerMode; UpdateModeName(ActivePowerMode); NotifyModeSelectionsChanged(); } catch { }
            return false;
        }
        finally { IsBusy = false; _modeSwitchGate.Release(); }
    }
}
