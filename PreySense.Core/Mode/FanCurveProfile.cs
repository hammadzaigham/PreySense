using System;

namespace PreySense.Mode;

public enum FanControlMode { Auto = 0, Max = 1, Custom = 2 }

public sealed class FanCurveProfile
{
    public const int MinimumTemperature = 40;
    public const int MaximumTemperature = 100;
    public const int TemperatureStep = 5;
    public const int PointCount = (MaximumTemperature - MinimumTemperature) / TemperatureStep + 1;
    public static bool ValidTemperatures(int cpu, int gpu) => cpu is > 0 and <= 125;
    public static int CoolingTemperature(int cpu, int gpu) => gpu is > 0 and <= 125 ? Math.Max(cpu, gpu) : cpu;
    public FanControlMode ControlMode { get; set; } = FanControlMode.Auto;
    public int RampUpSeconds { get; set; } = 2;
    public int[] Cpu { get; set; } = DefaultCurve();
    public int[] Gpu { get; set; } = DefaultCurve(gpu: true);

    // Starting presets derived from the extracted ZBJ-KQ EC 1.08 RPM stages.
    // Percent commands are not calibrated RPM control; see docs/fan_control.md.
    // Shared-temperature stage selection, 20% minimum, 100% from 95°C to 100°C.
    public static int[] DefaultCurve(byte mode = 1, bool gpu = false) => (mode, gpu) switch
    {
        (5, false) => [20, 20, 20, 35, 38, 58, 73, 73, 73, 73, 81, 100, 100],
        (5, true) => [20, 20, 20, 37, 40, 61, 77, 77, 77, 77, 86, 100, 100],
        (4, false) => [20, 20, 29, 35, 38, 47, 57, 64, 64, 64, 64, 100, 100],
        (4, true) => [20, 20, 31, 37, 40, 50, 61, 68, 68, 68, 68, 100, 100],
        (0 or 6, false) => [20, 20, 20, 20, 26, 29, 32, 38, 42, 42, 42, 100, 100],
        (0 or 6, true) => [20, 20, 20, 20, 29, 31, 34, 40, 45, 45, 45, 100, 100],
        (_, false) => [20, 20, 26, 26, 29, 35, 38, 47, 53, 53, 53, 100, 100],
        (_, true) => [20, 20, 29, 29, 31, 37, 40, 50, 56, 56, 56, 100, 100]
    };
    public static FanCurveProfile CreateDefault(byte mode) => new()
    {
        ControlMode = FanControlMode.Auto,
        RampUpSeconds = 2,
        Cpu = DefaultCurve(mode),
        Gpu = DefaultCurve(mode, gpu: true)
    };
    public static int PointIndex(int temperature) =>
        Math.Clamp((temperature - MinimumTemperature) / TemperatureStep, 0, PointCount - 1);
    public FanCurveProfile Copy() => new()
    {
        ControlMode = ControlMode, RampUpSeconds = RampUpSeconds,
        Cpu = (int[])Cpu.Clone(), Gpu = (int[])Gpu.Clone()
    };
    public void Normalize()
    {
        RampUpSeconds = Math.Clamp(RampUpSeconds, 0, 5);
        if (!Enum.IsDefined(ControlMode)) ControlMode = FanControlMode.Auto;
        Cpu = NormalizeCurve(Cpu);
        Gpu = NormalizeCurve(Gpu, gpu: true);
    }
    private static int[] NormalizeCurve(int[]? source, bool gpu = false)
    {
        var result = DefaultCurve(gpu: gpu);
        for (int i = 0; i < result.Length; i++)
            result[i] = Math.Max(i == 0 ? 0 : result[i - 1],
                source != null && i < source.Length ? Math.Clamp(source[i], 0, 100) : result[i]);
        return result;
    }
    public static void SetPoint(int[] curve, int index, int value)
    {
        int minimum = index == 0 ? 0 : curve[index - 1];
        int maximum = index == curve.Length - 1 ? 100 : curve[index + 1];
        curve[index] = Math.Clamp(value, minimum, maximum);
    }
}

public static class FanCurveStorage
{
    private static PreySense.Core.Configuration.ModeFanCurveConfig GetModeFanConfig(PreySense.Core.Configuration.PreySenseConfig cfg, byte mode) => mode switch
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
            if (fanMode.CpuCurve != null && fanMode.CpuCurve.Length == FanCurveProfile.PointCount)
                Array.Copy(fanMode.CpuCurve, profile.Cpu, FanCurveProfile.PointCount);
            if (fanMode.GpuCurve != null && fanMode.GpuCurve.Length == FanCurveProfile.PointCount)
                Array.Copy(fanMode.GpuCurve, profile.Gpu, FanCurveProfile.PointCount);
        }
        catch { }
        profile.Normalize();
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
        saved.Normalize();
        try
        {
            PreySense.Core.Configuration.ConfigurationService.Instance.Update(cfg =>
            {
                var fanMode = GetModeFanConfig(cfg, mode);
                fanMode.ControlMode = (int)saved.ControlMode;
                fanMode.RampUpSeconds = saved.RampUpSeconds;
                fanMode.CpuCurve = (int[])saved.Cpu.Clone();
                fanMode.GpuCurve = (int[])saved.Gpu.Clone();
            });
        }
        catch { }
    }
}
