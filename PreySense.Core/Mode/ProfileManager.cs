using System;
using PreySense.Core.Driver;
using PreySense.Core.Configuration;
using PreySense.Helpers;

namespace PreySense.Mode
{
    /// <summary>
    /// Manages per-mode performance profiles, loading/saving from preysense.json
    /// and applying hardware settings when modes are switched.
    /// </summary>
    public static class ProfileManager
    {
        /// <summary>Optional store override for tests; production always uses <see cref="ConfigurationService.Instance"/>.</summary>
        public static ConfigurationService? ConfigurationStore { get; set; }

        private static ConfigurationService Store => ConfigurationStore ?? ConfigurationService.Instance;

        public static bool IsSupportedMode(byte mode) => mode is 0 or 1 or 4 or 5 or 6;

        /// <summary>
        /// Maps a WMI power mode code to a profile name.
        /// </summary>
        public static string ModeToProfileName(byte mode)
        {
            return mode switch
            {
                0x00 => "Quiet",
                0x06 => "Eco",
                0x04 => "Performance",
                0x05 => "Turbo",
                0x01 => "Balanced",
                _ => throw new ArgumentOutOfRangeException(nameof(mode))
            };
        }

        public static readonly string[] SourceProfileNames = { "Quiet", "Balanced", "Performance", "Turbo", "Eco" };

        private static readonly byte[] SourceProfileModes = { 0x00, 0x01, 0x04, 0x05, 0x06 };

        public static int SourceProfileIndex(byte mode) => mode switch
        {
            0x00 => 0,
            0x01 => 1,
            0x04 => 2,
            0x05 => 3,
            0x06 => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };

        public static byte SourceProfileMode(int index)
            => index >= 0 && index < SourceProfileModes.Length ? SourceProfileModes[index] : throw new ArgumentOutOfRangeException(nameof(index));

        private static ModeTuningConfig GetModeTuningConfig(PreySenseConfig cfg, byte mode) => mode switch
        {
            0x00 => cfg.Tuning.Quiet,
            0x06 => cfg.Tuning.Eco,
            0x04 => cfg.Tuning.Performance,
            0x05 => cfg.Tuning.Turbo,
            _ => cfg.Tuning.Balanced
        };

        /// <summary>
        /// Loads a performance profile for the given mode from preysense.json.
        /// Returns default values if no saved profile exists.
        /// </summary>
        public static PerformanceProfile LoadProfile(byte mode)
        {
            var profile = PerformanceProfile.CreateDefault(mode);

            try
            {
                var cfg = Store.GetCurrent();
                var modeCfg = GetModeTuningConfig(cfg, mode);

                if (modeCfg.CpuPl1Watts.HasValue)
                    profile.CpuPl1 = Math.Clamp(modeCfg.CpuPl1Watts.Value, 5, CpuPowerLimitConfiguration.MaxPowerLimitWatts);
                if (modeCfg.CpuPl2Watts.HasValue)
                    profile.CpuPl2 = Math.Clamp(modeCfg.CpuPl2Watts.Value, 5, CpuPowerLimitConfiguration.MaxPowerLimitWatts);
                if (modeCfg.TauSeconds.HasValue)
                    profile.TauSeconds = Math.Clamp(modeCfg.TauSeconds.Value, 0, CpuPowerLimitConfiguration.InfiniteTauSliderValue);
                if (modeCfg.EffectiveCpuBoostMode is int boost)
                {
                    int onOff = boost > 0 ? 1 : 0;
                    profile.CpuBoost = onOff;
                    profile.ExtendedCpuBoost = onOff;
                }

                profile.ApplyCpuLimits = modeCfg.ApplyCpuLimits;
                profile.ApplyGpuLimits = modeCfg.ApplyGpuLimits;
                profile.ApplyFanCurve = modeCfg.ApplyFanCurve;
                profile.FanRampUp = Math.Clamp(modeCfg.FanRampUp, 0, 10);
            }
            catch { }

            return profile;
        }

        /// <summary>
        /// Saves a performance profile for the given mode to preysense.json.
        /// </summary>
        public static bool SaveProfile(PerformanceProfile profile)
        {
            try
            {
                Store.Update(cfg =>
                {
                    var modeCfg = GetModeTuningConfig(cfg, profile.PowerMode);
                    modeCfg.CpuPl1Watts = Math.Clamp(profile.CpuPl1, 5, CpuPowerLimitConfiguration.MaxPowerLimitWatts);
                    modeCfg.CpuPl2Watts = Math.Clamp(profile.CpuPl2, 5, CpuPowerLimitConfiguration.MaxPowerLimitWatts);
                    modeCfg.TauSeconds = Math.Clamp(profile.TauSeconds, 0, CpuPowerLimitConfiguration.InfiniteTauSliderValue);
                    int boost = (profile.ExtendedCpuBoost ?? profile.CpuBoost) > 0 ? 1 : 0;
                    modeCfg.CpuBoostMode = boost;
                    modeCfg.CpuBoost = boost > 0;

                    modeCfg.ApplyCpuLimits = profile.ApplyCpuLimits;
                    modeCfg.ApplyGpuLimits = profile.ApplyGpuLimits;
                    modeCfg.ApplyFanCurve = profile.ApplyFanCurve;
                    modeCfg.FanRampUp = profile.FanRampUp;
                });
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"ProfileManager: Failed to save profile '{profile.Name}': {ex.Message}");
                return false;
            }
        }

        public static void ClearUserProfileValues(byte mode)
        {
            try
            {
                Store.Update(cfg =>
                {
                    var modeCfg = GetModeTuningConfig(cfg, mode);
                    modeCfg.CpuPl1Watts = null;
                    modeCfg.CpuPl2Watts = null;
                    modeCfg.TauSeconds = null;
                    modeCfg.CpuBoost = null;
                    modeCfg.CpuBoostMode = null;
                    modeCfg.GpuWatts = null;
                    modeCfg.GpuDeltaWatts = null;
                    modeCfg.GpuDynamicBoostWatts = null;
                    modeCfg.PlatformPowerOffsetWatts = null;
                    modeCfg.ApplyCpuLimits = false;
                    modeCfg.ApplyGpuLimits = false;
                    modeCfg.ApplyFanCurve = false;
                    modeCfg.FanRampUp = 1;
                });
            }
            catch (Exception ex)
            {
                AppLogger.Log($"ProfileManager: Failed to clear user profile values for mode 0x{mode:X2}: {ex.Message}");
            }
        }
    }
}
