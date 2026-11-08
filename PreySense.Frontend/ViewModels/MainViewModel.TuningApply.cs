using System;
using System.Threading.Tasks;
using PreySense.Core.Configuration;
using PreySense.Core.Hardware;
using PreySense.Mode;

namespace PreySense.Frontend.ViewModels;

public partial class MainViewModel
{
    internal readonly record struct DriverTuningRequest(byte Mode, int Pl1, int Pl2, int Tau,
        PreySense.Core.Driver.GpuPowerOffsets Offsets, int? ProchotOffsetCelsius = null);
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
        if (request.Mode is not (0 or 1 or 4 or 5 or 6) ||
            request.Pl1 is < 5 or > 300 || request.Pl2 < request.Pl1 || request.Pl2 > 300 || request.Tau is < 0 or > 449 || request.ProchotOffsetCelsius is < 0 or > 63 ||
            request.Offsets.DeltaWatts is < 0 or > 50 || request.Offsets.DeltaWatts % 5 != 0 ||
            request.Offsets.DynamicBoostWatts is not (0 or 10 or 15 or 20 or 25) ||
            request.Offsets.PlatformOffsetWatts is < 0 or > 100 || request.Offsets.PlatformOffsetWatts % 5 != 0)
            return new(false, "Invalid tuning profile; nothing was written.");
        if (DriverTuningExecutor is not null) return DriverTuningExecutor(request);
        var backend = CpuDriverRuntime.Backend ?? throw new InvalidOperationException(CpuDriverRuntime.Status);
        var cpu = CpuDriverRuntime.PowerLimits ?? throw new InvalidOperationException(CpuDriverRuntime.Status);
        var prochot = new CpuProchotController(backend);
        if (request.ProchotOffsetCelsius is int requestedOffset && !prochot.CanSetOffset(requestedOffset, out string preflight))
            return new(false, preflight + " No tuning values were written.");
        if (!backend.TryReadMchbar(out ulong mchbar))
            return new(false, "Cannot discover the supported CPU MCHBAR; nothing was written.");
        // Tau 0 is the shortest RAPL window (EncodeTau maps it to 0x01; encoded 0 is a no-op).
        var gpu = new AcerProfileApplyTransaction(backend, new TuningModeController(this))
            .Apply(request.Mode, request.Offsets.DeltaWatts, request.Offsets.DynamicBoostWatts, (byte)request.Offsets.PlatformOffsetWatts);
        if (!gpu.Success) return new(false, gpu.Status);
        // EC transitions reload CPU limits, so CPU registers must be written last.
        var result = cpu.ExecuteCombinedTransaction(mchbar, request.Pl1, request.Pl2, true, request.Tau);
        if (result.Success && request.ProchotOffsetCelsius is int offset)
        {
            if (!prochot.TrySetOffset(offset, out string prochotStatus))
                return new(false, $"Apply failed: {prochotStatus}");
            return new(true, "Applied and readback successfully.");
        }
        return new(result.Success, result.Success ? "Applied and readback successfully."
            : $"Apply failed: {result.Status}");
    }

    internal DriverTuningResult ApplySavedDriverTuning(byte mode)
    {
        var tuning = (TuningConfigurationReader?.Invoke() ?? TuningStore.GetCurrent()).Tuning;
        var profile = mode switch
        {
            0 => tuning.Quiet, 1 => tuning.Balanced, 4 => tuning.Performance,
            5 => tuning.Turbo, 6 => tuning.Eco, _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
        if (!profile.HasValues)
            return _hw.SetPowerMode(mode) ? new(true, "Acer mode selected; no custom profile is saved.")
                : new(false, "The Acer mode could not be activated.");
        return ExecuteDriverTuning(new(mode, profile.CpuPl1Watts!.Value, profile.CpuPl2Watts!.Value, profile.TauSeconds!.Value,
            new(profile.GpuDeltaWatts!.Value, profile.GpuDynamicBoostWatts!.Value, profile.PlatformPowerOffsetWatts!.Value),
            tuning.ApplyProchotOffset && !IsProchotOffsetLocked ? tuning.ProchotOffsetCelsius : null));
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
                profile.FanRampUp = 1;
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
        PreySense.Core.Driver.GpuPowerOffsets offsets, bool persist, int? prochotOffsetCelsius = null, bool applyProchotOffset = false)
    {
        applyProchotOffset &= !IsProchotOffsetLocked;
        if (applyProchotOffset && prochotOffsetCelsius is null)
        {
            DiagnosticMessage = "Enter a global PROCHOT offset before applying.";
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
            AppLogger.Log($"[Tuning] Apply mode=0x{mode:X2} PL1={pl1} PL2={pl2} Tau={tau} Delta={offsets.DeltaWatts} DB={offsets.DynamicBoostWatts} CP={offsets.PlatformOffsetWatts}");
            var applied = await Task.Run(() => ExecuteDriverTuning(new(mode, pl1, pl2, tau, offsets, applyProchotOffset ? prochotOffsetCelsius : null)));
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
