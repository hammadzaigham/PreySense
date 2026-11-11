using System;

namespace PreySense.Mode;

public enum FanControlMode { Auto = 0, Max = 1, Custom = 2 }

public sealed class FanCurveProfile
{
    public const int StageCount = 12;
    public static bool ValidTemperatures(int cpu, int gpu) => cpu is > 0 and <= 125;
    public static int CoolingTemperature(int cpu, int gpu) => gpu is > 0 and <= 125 ? Math.Max(cpu, gpu) : cpu;

    public FanControlMode ControlMode { get; set; } = FanControlMode.Auto;
    public int RampUpSeconds { get; set; } = 2;
    public FanStage[] Stages { get; set; } = DefaultStages(1);

    /// <summary>Factory ZBJ-KQ EC 1.08 variant-0 stage tables (RPM targets, rise/fall °C).</summary>
    public static FanStage[] DefaultStages(byte mode) => mode switch
    {
        5 => Clone(Turbo),
        4 => Clone(Performance),
        0 or 6 => Clone(QuietEco),
        _ => Clone(Balanced)
    };

    public static FanCurveProfile CreateDefault(byte mode) => new()
    {
        ControlMode = FanControlMode.Auto,
        RampUpSeconds = 0,
        Stages = DefaultStages(mode)
    };

    public FanCurveProfile Copy() => new()
    {
        ControlMode = ControlMode,
        RampUpSeconds = RampUpSeconds,
        Stages = Clone(Stages)
    };

    public void Normalize()
    {
        RampUpSeconds = Math.Clamp(RampUpSeconds, 0, 5);
        if (!Enum.IsDefined(ControlMode)) ControlMode = FanControlMode.Auto;
        Stages = NormalizeStages(Stages, 1);
    }

    public void Normalize(byte mode)
    {
        RampUpSeconds = Math.Clamp(RampUpSeconds, 0, 5);
        if (!Enum.IsDefined(ControlMode)) ControlMode = FanControlMode.Auto;
        Stages = NormalizeStages(Stages, mode);
    }

    private static FanStage[] NormalizeStages(FanStage[]? source, byte mode)
    {
        var result = DefaultStages(mode);
        if (source is not { Length: > 0 }) return result;
        for (int i = 0; i < StageCount && i < source.Length; i++)
        {
            result[i] = source[i]?.Copy() ?? result[i];
            result[i].Normalize();
        }
        // Compact storage ends at a terminal plateau. Never restore factory trips
        // into its omitted tail: a hand-edited shorter curve must stay terminal.
        if (source.Length < StageCount)
        {
            var terminal = result[source.Length - 1];
            terminal.CpuRiseC = terminal.GpuRiseC = FanStage.DisabledTripCelsius;
            terminal.Normalize();
            for (int i = source.Length; i < StageCount; i++) result[i] = terminal.Copy();
        }
        return result;
    }

    private static FanStage[] Clone(FanStage[] source)
    {
        var copy = new FanStage[source.Length];
        for (int i = 0; i < source.Length; i++) copy[i] = source[i].Copy();
        return copy;
    }

    private static FanStage S(int cpuRise, int cpuFall, int gpuRise, int gpuFall, int cpuRpm, int gpuRpm) => new()
    {
        CpuRiseC = cpuRise, CpuFallC = cpuFall, GpuRiseC = gpuRise, GpuFallC = gpuFall,
        CpuTargetRpm = cpuRpm, GpuTargetRpm = gpuRpm
    };

    // Quiet and Eco share the same EC table.
    private static readonly FanStage[] QuietEco =
    [
        S(57, 0, 57, 0, 0, 0),
        S(64, 40, 62, 40, 1800, 1850),
        S(70, 56, 66, 56, 1970, 1980),
        S(76, 65, 71, 61, 2150, 2150),
        S(81, 71, 75, 65, 2350, 2350),
        S(127, 77, 80, 70, 2550, 2550),
        S(127, 127, 127, 74, 2850, 2850),
        S(127, 127, 127, 127, 2850, 2850),
        S(127, 127, 127, 127, 2850, 2850),
        S(127, 127, 127, 127, 2850, 2850),
        S(127, 127, 127, 127, 2850, 2850),
        S(127, 127, 127, 127, 6400, 6400),
    ];

    private static readonly FanStage[] Balanced =
    [
        S(56, 0, 50, 0, 0, 0),
        S(65, 40, 56, 40, 1800, 1850),
        S(72, 55, 62, 51, 1970, 1980),
        S(78, 64, 68, 57, 2350, 2350),
        S(84, 71, 73, 63, 2550, 2550),
        S(89, 77, 77, 69, 3180, 3170),
        S(127, 83, 127, 74, 3600, 3600),
        S(127, 127, 127, 127, 3600, 3600),
        S(127, 127, 127, 127, 3600, 3600),
        S(127, 127, 127, 127, 3600, 3600),
        S(127, 127, 127, 127, 3600, 3600),
        S(127, 127, 127, 127, 6400, 6400),
    ];

    private static readonly FanStage[] Performance =
    [
        S(50, 0, 50, 0, 0, 0),
        S(54, 40, 56, 40, 1970, 1980),
        S(60, 51, 61, 51, 2350, 2350),
        S(65, 55, 66, 57, 2550, 2550),
        S(70, 61, 71, 62, 3180, 3170),
        S(75, 66, 76, 67, 3900, 3900),
        S(127, 71, 127, 72, 4350, 4350),
        S(127, 127, 127, 127, 4350, 4350),
        S(127, 127, 127, 127, 4350, 4350),
        S(127, 127, 127, 127, 4350, 4350),
        S(127, 127, 127, 127, 4350, 4350),
        S(127, 127, 127, 127, 6400, 6400),
    ];

    private static readonly FanStage[] Turbo =
    [
        S(55, 0, 52, 0, 0, 0),
        S(60, 40, 55, 40, 2150, 2150),
        S(65, 56, 58, 53, 2350, 2350),
        S(70, 61, 61, 56, 2550, 2550),
        S(74, 66, 64, 59, 3180, 3170),
        S(78, 71, 67, 62, 3950, 3900),
        S(95, 75, 86, 65, 4950, 4950),
        S(127, 85, 127, 78, 5500, 5500),
        S(127, 127, 127, 127, 5500, 5500),
        S(127, 127, 127, 127, 5500, 5500),
        S(127, 127, 127, 127, 5500, 5500),
        S(127, 127, 127, 127, 6400, 6400),
    ];
}

public static class FanCurveStorage
{
    private static PreySense.Core.Configuration.ModeFanCurveConfig GetModeFanConfig(
        PreySense.Core.Configuration.PreySenseConfig cfg, byte mode) => mode switch
    {
        0 => cfg.Fans.Quiet,
        6 => cfg.Fans.Eco,
        4 => cfg.Fans.Performance,
        5 => cfg.Fans.Turbo,
        _ => cfg.Fans.Balanced
    };

    public static FanCurveProfile Load(byte mode)
    {
        var profile = FanCurveProfile.CreateDefault(mode);
        try
        {
            var cfg = PreySense.Core.Configuration.ConfigurationService.Instance.GetCurrent();
            var fanMode = GetModeFanConfig(cfg, mode);
            profile.ControlMode = (FanControlMode)Math.Clamp(fanMode.ControlMode, 0, 2);
            profile.RampUpSeconds = Math.Clamp(fanMode.RampUpSeconds, 0, 5);
            if (fanMode.Stages is { Length: > 0 } stages)
                profile.Stages = stages.Select(stage => stage?.ToFanStage() ?? new FanStage
                {
                    CpuRiseC = FanStage.DisabledTripCelsius,
                    GpuRiseC = FanStage.DisabledTripCelsius
                }).ToArray();
        }
        catch { }
        profile.Normalize(mode);
        return profile;
    }

    public static FanControlMode DecodeControlMode(int? saved, bool legacyEnabled, bool legacyMax, int? legacyGlobalMode)
    {
        if (saved.HasValue)
            return saved.Value is >= 0 and <= 2 ? (FanControlMode)saved.Value : FanControlMode.Auto;
        if (legacyGlobalMode == 0 || !legacyEnabled) return FanControlMode.Auto;
        return legacyMax ? FanControlMode.Max : FanControlMode.Custom;
    }

    public static void Save(byte mode, FanCurveProfile profile)
    {
        var saved = profile.Copy();
        saved.Normalize(mode);
        PreySense.Core.Configuration.ConfigurationService.Instance.Update(cfg =>
        {
            var fanMode = GetModeFanConfig(cfg, mode);
            fanMode.ControlMode = (int)saved.ControlMode;
            fanMode.RampUpSeconds = saved.RampUpSeconds;
            fanMode.Stages = saved.Stages.Select(stage =>
                new PreySense.Core.Configuration.FanStageConfig(stage)).ToArray();
        });
    }
}
