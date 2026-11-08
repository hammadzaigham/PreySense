using PreySense.Core.Driver;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PreySense.Frontend.ViewModels
{
    public partial class MainViewModel
    {
        public void ResetModeToCapturedFactoryDefaults(int modeIndex)
        {
            var tuning = TuningStore.GetCurrent().Tuning;
            if (!tuning.HasFactoryDefaults || !tuning.FactoryDefaults.TryGetValue(TuningModeName(modeIndex), out var profile)
                || profile.CpuPl1Watts is null || profile.CpuPl2Watts is null || profile.TauSeconds is null
                || profile.GpuDeltaWatts is null || profile.GpuDynamicBoostWatts is null || profile.PlatformPowerOffsetWatts is null)
            {
                DiagnosticMessage = "Calibrate tuning before resetting factory defaults.";
                return;
            }
            int pl1 = profile.CpuPl1Watts.Value, pl2 = profile.CpuPl2Watts.Value;
            switch (modeIndex)
            {
                case 0: CustomEcoPl1 = pl1; CustomEcoPl2 = pl2; CustomEcoGpuWatts = profile.GpuWatts ?? 0; break;
                case 1: CustomQuietPl1 = pl1; CustomQuietPl2 = pl2; CustomQuietGpuWatts = profile.GpuWatts ?? 0; break;
                case 2: CustomBalancedPl1 = pl1; CustomBalancedPl2 = pl2; CustomBalancedGpuWatts = profile.GpuWatts ?? 0; break;
                case 3: CustomPerfPl1 = pl1; CustomPerfPl2 = pl2; CustomPerfGpuWatts = profile.GpuWatts ?? 0; break;
                case 4: CustomTurboPl1 = pl1; CustomTurboPl2 = pl2; CustomTurboGpuWatts = profile.GpuWatts ?? 0; break;
            }
            SetTauForModeIndex(modeIndex, profile.TauSeconds.Value);
            if (profile.EffectiveCpuBoostMode is int boost)
                SetCpuBoostForModeIndex(modeIndex, boost > 0);
            if (GpuBasePowerWatts.HasValue)
                StoreGpuOffsets(modeIndex, new(profile.GpuDeltaWatts.Value, profile.GpuDynamicBoostWatts.Value, profile.PlatformPowerOffsetWatts.Value));

            // Restore factory PROCHOT value in the editor; leave the apply switch off.
            ProchotOffsetCelsius = tuning.FactoryProchotOffsetCelsius;
            IsProchotOffsetLocked = tuning.FactoryProchotOffsetLocked == true;
            IsProchotOffsetEnabled = false;
        }
        // Power Limits (PL1/PL2/Turbo Time Limit - Informational / Mode Target)
        [ObservableProperty] private int _pl1Watts = 55;
        [ObservableProperty] private int _pl2Watts = 130;
        [ObservableProperty] private int _pl1Max = CpuPowerLimitConfiguration.MaxPowerLimitWatts;
        [ObservableProperty] private int _pl2Max = CpuPowerLimitConfiguration.MaxPowerLimitWatts;
        [ObservableProperty] private int _tauSeconds = CpuPowerLimitConfiguration.FactoryTauSeconds; // Active window written to hardware
        [ObservableProperty] private int _customQuietTau = CpuPowerLimitConfiguration.FactoryTauSeconds;
        [ObservableProperty] private int _customEcoTau = CpuPowerLimitConfiguration.FactoryTauSeconds;
        [ObservableProperty] private int _customBalancedTau = CpuPowerLimitConfiguration.FactoryTauSeconds;
        [ObservableProperty] private int _customPerfTau = CpuPowerLimitConfiguration.FactoryTauSeconds;
        [ObservableProperty] private int _customTurboTau = CpuPowerLimitConfiguration.FactoryTauSeconds;
        [ObservableProperty] private bool _customQuietCpuBoost = true;
        [ObservableProperty] private bool _customEcoCpuBoost = true;
        [ObservableProperty] private bool _customBalancedCpuBoost = true;
        [ObservableProperty] private bool _customPerfCpuBoost = true;
        [ObservableProperty] private bool _customTurboCpuBoost = true;

        // Driver tuning enable policy
        [ObservableProperty] private bool _isHardwareTuningEnabled = false;
        // Per-mode power limits
        [ObservableProperty] private int _customQuietPl1 = CpuPowerLimitConfiguration.FactoryQuietPl1;
        [ObservableProperty] private int _customQuietPl2 = CpuPowerLimitConfiguration.FactoryQuietPl2;
        [ObservableProperty] private int _customEcoPl1 = CpuPowerLimitConfiguration.FactoryEcoPl1;
        [ObservableProperty] private int _customEcoPl2 = CpuPowerLimitConfiguration.FactoryEcoPl2;
        [ObservableProperty] private int _customBalancedPl1 = CpuPowerLimitConfiguration.FactoryBalancedPl1;
        [ObservableProperty] private int _customBalancedPl2 = CpuPowerLimitConfiguration.FactoryBalancedPl2;
        [ObservableProperty] private int _customPerfPl1 = CpuPowerLimitConfiguration.FactoryPerformancePl1;
        [ObservableProperty] private int _customPerfPl2 = CpuPowerLimitConfiguration.FactoryPerformancePl2;
        [ObservableProperty] private int _customTurboPl1 = CpuPowerLimitConfiguration.FactoryTurboPl1;
        [ObservableProperty] private int _customTurboPl2 = CpuPowerLimitConfiguration.FactoryTurboPl2;

        public void RefreshLocalizedText()
        {
            OnPropertyChanged(nameof(EcoOrQuietModeName));
            UpdateModeName(ActivePowerMode);
        }

    }
}
