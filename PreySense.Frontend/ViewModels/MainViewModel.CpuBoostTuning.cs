namespace PreySense.Frontend.ViewModels;

public partial class MainViewModel
{
    private async Task<bool> ApplyCpuBoostOnlyAsync(byte mode, bool persist, bool persistApplySwitches)
    {
        if (mode is not (0 or 1 or 4 or 5 or 6))
        {
            DiagnosticMessage = "Invalid power mode.";
            return false;
        }
        if (!await TryEnterModeSwitchGateAsync()) return false;
        IsBusy = true;
        try
        {
            // A draft for an inactive mode is saved without changing the current Windows policy.
            // Mode switches restore it later when this independent section is enabled.
            bool active = _hw.CurrentPowerMode == mode;
            if (active && !await WriteCpuBoostAsync(mode)) return false;
            if (persist)
            {
                int boost = CpuBoostModeForMode(mode);
                TuningStore.Update(config =>
                {
                    var profile = ProfileForMode(config.Tuning, mode);
                    profile.ApplyCpuBoost = persistApplySwitches;
                    profile.CpuBoostMode = boost;
                    profile.CpuBoost = boost > 0;
                    profile.ApplyCpuLimits = profile.ApplyGpuLimits = profile.ApplyGpuClockOffsets = false;
                    profile.GpuRuntimeLimitWatts = null;
                    config.Tuning.ApplyProchotOffset = false;
                });
            }
            DiagnosticMessage = persist ? "CPU boost saved." : active ? "CPU boost applied." : "Select this power mode before applying CPU boost.";
            return persist || active;
        }
        catch (Exception ex) { DiagnosticMessage = $"Could not apply CPU boost: {ex.Message}"; return false; }
        finally { IsBusy = false; ExitModeSwitchGate(); }
    }
}
