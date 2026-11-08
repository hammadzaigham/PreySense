using System;
using PreySense.Core.Configuration;

namespace PreySense.Core.Driver;

public static class GpuPowerBaseline
{
    private static readonly object Gate = new();
    public static int? FromMilliwatts(uint value)
    {
        if (value < 1000 || value > GpuPowerLimitConfiguration.MaximumPowerLimitWatts * 1000U) return null;
        return (int)Math.Round(value / 1000.0, MidpointRounding.AwayFromZero);
    }
    public static int? ReadWatts()
    {
        try
        {
            var config = ConfigurationService.Instance.GetCurrent();
            return VerifiedSavedWatts(config.Tuning.GpuBasePowerWatts);
        }
        catch { return null; }
    }

    public static int? VerifiedSavedWatts(int? watts) =>
        watts is > 0 and <= GpuPowerLimitConfiguration.MaximumPowerLimitWatts ? watts : null;

    public static int? SaveOnce(int watts)
    {
        if (watts is <= 0 or > GpuPowerLimitConfiguration.MaximumPowerLimitWatts) return null;
        lock (Gate)
        {
            var config = ConfigurationService.Instance.GetCurrent();
            if (!VerifiedSavedWatts(config.Tuning.GpuBasePowerWatts).HasValue)
            {
                ConfigurationService.Instance.Update(cfg =>
                {
                    cfg.Tuning.GpuBasePowerWatts = watts;
                });
            }
            return ReadWatts();
        }
    }
}
