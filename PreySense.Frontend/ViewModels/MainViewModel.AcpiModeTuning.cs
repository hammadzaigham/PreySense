using System;
using PreySense.Core.Acpi;
using PreySense.Mode;

namespace PreySense.Frontend.ViewModels;

public partial class MainViewModel
{
    // Per-mode CPU boost and Tau helpers live separately from ACPI installation
    // and calibration workflows so mode editing is easier to navigate.
    public bool CpuBoostForMode(byte mode) => mode switch
    {
        0x05 => CustomTurboCpuBoost,
        0x04 => CustomPerfCpuBoost,
        0x01 => CustomBalancedCpuBoost,
        0x06 => CustomEcoCpuBoost,
        _ => CustomQuietCpuBoost
    };

    public bool CpuBoostForModeIndex(int modeIndex) => modeIndex switch
    {
        0 => CustomEcoCpuBoost,
        1 => CustomQuietCpuBoost,
        2 => CustomBalancedCpuBoost,
        3 => CustomPerfCpuBoost,
        _ => CustomTurboCpuBoost
    };

    public void SetCpuBoostForModeIndex(int modeIndex, bool enabled)
    {
        switch (modeIndex)
        {
            case 0: CustomEcoCpuBoost = enabled; break;
            case 1: CustomQuietCpuBoost = enabled; break;
            case 2: CustomBalancedCpuBoost = enabled; break;
            case 3: CustomPerfCpuBoost = enabled; break;
            default: CustomTurboCpuBoost = enabled; break;
        }
    }

    public void ApplyCpuBoostForMode(byte mode)
    {
        int boost = CpuBoostForMode(mode) ? 1 : 0;
        if (CpuBoostMode != boost)
            CpuBoostMode = boost;
        else
            PowerLimitController.SetCpuBoost(boost);
    }

    public int TauForMode(byte mode) => Math.Clamp(mode switch
    {
        0x05 => CustomTurboTau,
        0x04 => CustomPerfTau,
        0x01 => CustomBalancedTau,
        0x06 => CustomEcoTau,
        _ => CustomQuietTau
    }, 0, CpuPowerLimitConfiguration.InfiniteTauSliderValue);

    public int TauForModeIndex(int modeIndex) => modeIndex switch
    {
        0 => CustomEcoTau,
        1 => CustomQuietTau,
        2 => CustomBalancedTau,
        3 => CustomPerfTau,
        _ => CustomTurboTau
    };

    public void SetTauForModeIndex(int modeIndex, int tauSeconds)
    {
        int tau = Math.Clamp(tauSeconds, 0, CpuPowerLimitConfiguration.InfiniteTauSliderValue);
        switch (modeIndex)
        {
            case 0: CustomEcoTau = tau; break;
            case 1: CustomQuietTau = tau; break;
            case 2: CustomBalancedTau = tau; break;
            case 3: CustomPerfTau = tau; break;
            default: CustomTurboTau = tau; break;
        }
        TauSeconds = tau;
    }

    public int EffectivePlatformPl1Limit => CpuPowerLimitConfiguration.MaxPowerLimitWatts;

    public void ClampCpuPl1ToPlatformLimit()
    {
        Pl1Max = EffectivePlatformPl1Limit;
        CustomQuietPl1 = LimitPl1ToPlatform(CustomQuietPl1);
        CustomEcoPl1 = LimitPl1ToPlatform(CustomEcoPl1);
        CustomBalancedPl1 = LimitPl1ToPlatform(CustomBalancedPl1);
        CustomPerfPl1 = LimitPl1ToPlatform(CustomPerfPl1);
        CustomTurboPl1 = LimitPl1ToPlatform(CustomTurboPl1);
        Pl1Watts = LimitPl1ToPlatform(Pl1Watts);
        CustomQuietPl2 = LimitPl2(CustomQuietPl1, CustomQuietPl2);
        CustomEcoPl2 = LimitPl2(CustomEcoPl1, CustomEcoPl2);
        CustomBalancedPl2 = LimitPl2(CustomBalancedPl1, CustomBalancedPl2);
        CustomPerfPl2 = LimitPl2(CustomPerfPl1, CustomPerfPl2);
        CustomTurboPl2 = LimitPl2(CustomTurboPl1, CustomTurboPl2);
        Pl2Watts = LimitPl2(Pl1Watts, Pl2Watts);
    }

    public int LimitPl1ToPlatform(int pl1Watts) =>
        Math.Clamp(pl1Watts, 10, EffectivePlatformPl1Limit);

    public int LimitPl2(int pl1Watts, int pl2Watts) =>
        Math.Clamp(pl2Watts, LimitPl1ToPlatform(pl1Watts),
            CpuPowerLimitConfiguration.MaxPowerLimitWatts);
}
