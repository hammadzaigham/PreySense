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

        public static readonly string[] SourceProfileNames = { "Eco", "Quiet", "Balanced", "Performance", "Turbo" };

        private static readonly byte[] SourceProfileModes = { 0x06, 0x00, 0x01, 0x04, 0x05 };

        public static int SourceProfileIndex(byte mode) => mode switch
        {
            0x06 => 0,
            0x00 => 1,
            0x01 => 2,
            0x04 => 3,
            0x05 => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };

        public static byte SourceProfileMode(int index)
            => index >= 0 && index < SourceProfileModes.Length ? SourceProfileModes[index] : throw new ArgumentOutOfRangeException(nameof(index));

        /// <summary>Battery source-profile fallback when no dropdown selection is set.</summary>
        public static byte DefaultBatterySourceMode => 0x06;

        /// <summary>Resolves a battery source-profile index, defaulting to Eco when unset.</summary>
        public static int BatterySourceProfileIndexOrDefault(int index)
            => index >= 0 && index < SourceProfileModes.Length ? index : SourceProfileIndex(DefaultBatterySourceMode);

        /// <summary>Resolves a battery source-profile mode, defaulting to Eco when unset.</summary>
        public static byte BatterySourceProfileModeOrDefault(int index)
            => SourceProfileMode(BatterySourceProfileIndexOrDefault(index));

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
                    profile.CpuBoost = boost;
                    profile.ExtendedCpuBoost = boost;
                }

                profile.ApplyCpuLimits = modeCfg.ApplyCpuLimits;
                profile.GpuRuntimeLimitWatts = modeCfg.GpuRuntimeLimitWatts;
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
                    int boost = Math.Clamp(profile.ExtendedCpuBoost ?? profile.CpuBoost, 0, 6);
                    modeCfg.CpuBoostMode = boost;
                    modeCfg.CpuBoost = boost > 0;

                    modeCfg.ApplyCpuLimits = profile.ApplyCpuLimits;
                    modeCfg.GpuRuntimeLimitWatts = profile.GpuRuntimeLimitWatts;
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
                    modeCfg.GpuRuntimeLimitWatts = null;
                    modeCfg.ApplyCpuLimits = false;
                });
            }
            catch (Exception ex)
            {
                AppLogger.Log($"ProfileManager: Failed to clear user profile values for mode 0x{mode:X2}: {ex.Message}");
            }
        }
    }
}
