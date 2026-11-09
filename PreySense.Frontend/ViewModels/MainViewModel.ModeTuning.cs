using System;
using PreySense.Core.Driver;
using PreySense.Mode;

namespace PreySense.Frontend.ViewModels;

public partial class MainViewModel
{
    // Per-mode CPU boost and Tau helpers live separately from driver tuning
    // and calibration workflows so mode editing is easier to navigate.
    public static readonly string[] CpuBoostModeNames =
    [
        "Disabled",
        "Enabled",
        "Aggressive",
        "Efficient Enabled",
        "Efficient Aggressive",
        "Aggressive At Guaranteed",
        "Efficient Aggressive At Guaranteed"
    ];

    public int CpuBoostForMode(byte mode) => mode switch
    {
        0x05 => CustomTurboCpuBoost,
        0x04 => CustomPerfCpuBoost,
        0x01 => CustomBalancedCpuBoost,
        0x06 => CustomEcoCpuBoost,
        _ => CustomQuietCpuBoost
    };

    public int CpuBoostForModeIndex(int modeIndex) => modeIndex switch
    {
        0 => CustomEcoCpuBoost,
        1 => CustomQuietCpuBoost,
        2 => CustomBalancedCpuBoost,
        3 => CustomPerfCpuBoost,
        _ => CustomTurboCpuBoost
    };

    public void SetCpuBoostForModeIndex(int modeIndex, int boostMode)
    {
        int boost = Math.Clamp(boostMode, 0, 6);
        switch (modeIndex)
        {
            case 0: CustomEcoCpuBoost = boost; break;
            case 1: CustomQuietCpuBoost = boost; break;
            case 2: CustomBalancedCpuBoost = boost; break;
            case 3: CustomPerfCpuBoost = boost; break;
            default: CustomTurboCpuBoost = boost; break;
        }
    }

    public int CpuBoostModeForMode(byte mode) => Math.Clamp(CpuBoostForMode(mode), 0, 6);

    public int CpuBoostModeForModeIndex(int index) => Math.Clamp(CpuBoostForModeIndex(index), 0, 6);

    private bool _lastCpuBoostApplySucceeded = true;

    public bool ApplyCpuBoostForMode(byte mode)
    {
        int boost = CpuBoostModeForMode(mode);
        CpuBoostMode = boost;
        _lastCpuBoostApplySucceeded = PowerLimitController.SetCpuBoost(boost);
        if (!_lastCpuBoostApplySucceeded) DiagnosticMessage = "Could not apply CPU boost.";
        return _lastCpuBoostApplySucceeded;
    }

    private async System.Threading.Tasks.Task<bool> ApplyCpuBoostForModeAsync(byte mode)
    {
        int boost = CpuBoostModeForMode(mode);
        CpuBoostMode = boost;
        bool applied = await System.Threading.Tasks.Task.Run(() => PowerLimitController.SetCpuBoost(boost));
        _lastCpuBoostApplySucceeded = applied;
        if (!applied) DiagnosticMessage = "Could not apply CPU boost.";
        return applied;
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
