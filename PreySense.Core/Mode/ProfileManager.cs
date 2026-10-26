using System;
using System.Threading.Tasks;
using Microsoft.Win32;
using PreySense.Core.Acpi;
using PreySense.Helpers;

namespace PreySense.Mode
{
    /// <summary>
    /// Manages per-mode performance profiles, loading/saving from registry
    /// and applying hardware settings when modes are switched.
    /// </summary>
    public static class ProfileManager
    {
        private const string RegistryBase = @"SOFTWARE\PreySense\Profiles";
        public static bool IsSupportedMode(byte mode) => mode is 0 or 1 or 4 or 5 or 6;

        /// <summary>
        /// Maps a WMI power mode code to a registry-safe profile name.
        /// </summary>
        public static string ModeToProfileName(byte mode)
        {
            return mode switch
            {
                0x00 => "Quiet",
                0x06 => "Eco",
                0x04 => "Performance",
                0x05 => "Turbo",
                _ => "Balanced"
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
            _ => 1
        };

        public static byte SourceProfileMode(int index)
            => index >= 0 && index < SourceProfileModes.Length ? SourceProfileModes[index] : (byte)0x01;

        /// <summary>
        /// Loads a performance profile for the given mode from registry.
        /// Returns default values if no saved profile exists.
        /// </summary>
        public static PerformanceProfile LoadProfile(byte mode)
        {
            var profile = PerformanceProfile.CreateDefault(mode);

            try
            {
                using var key = OpenProfileKey(mode);
                if (key == null) return profile;

                profile.CpuPl1 = Math.Clamp((int)key.GetValue("CpuPl1", profile.CpuPl1), 5, CpuPowerLimitConfiguration.MaxPowerLimitWatts);
                profile.CpuPl2 = Math.Clamp((int)key.GetValue("CpuPl2", profile.CpuPl2), 5, CpuPowerLimitConfiguration.MaxPowerLimitWatts);
                profile.TauSeconds = Math.Clamp((int)key.GetValue("TauSeconds", profile.TauSeconds), 0, CpuPowerLimitConfiguration.InfiniteTauSliderValue);
                profile.CpuBoost = Math.Clamp((int)key.GetValue("CpuBoost", profile.CpuBoost), 0, 6);

                profile.ApplyCpuLimits = (int)key.GetValue("ApplyCpuLimits", 0) == 1;
                profile.ApplyGpuLimits = (int)key.GetValue("ApplyGpuLimits", 0) == 1;
                profile.ApplyFanCurve = (int)key.GetValue("ApplyFanCurve", 0) == 1;
                profile.FanRampUp = Math.Clamp((int)key.GetValue("FanRampUp", 1), 0, 10);
            }
            catch { }

            return profile;
        }

        /// <summary>
        /// Saves a performance profile for the given mode to registry.
        /// </summary>
        public static bool SaveProfile(PerformanceProfile profile)
        {
            string subKey = $@"{RegistryBase}\{ModeToProfileName(profile.PowerMode)}";

            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(subKey);
                if (key == null)
                {
                    AppLogger.Log($"ProfileManager: Failed to create registry key '{subKey}'.");
                    return false;
                }

                key.SetValue("CpuPl1", Math.Clamp(profile.CpuPl1, 5, CpuPowerLimitConfiguration.MaxPowerLimitWatts));
                key.SetValue("CpuPl2", Math.Clamp(profile.CpuPl2, 5, CpuPowerLimitConfiguration.MaxPowerLimitWatts));
                key.SetValue("TauSeconds", Math.Clamp(profile.TauSeconds, 0, CpuPowerLimitConfiguration.InfiniteTauSliderValue));
                TryDeleteValue(key, "WindowsPowerMode");
                key.SetValue("CpuBoost", profile.CpuBoost);

                key.SetValue("ApplyCpuLimits", profile.ApplyCpuLimits ? 1 : 0);
                key.SetValue("ApplyGpuLimits", profile.ApplyGpuLimits ? 1 : 0);
                key.SetValue("ApplyFanCurve", profile.ApplyFanCurve ? 1 : 0);
                key.SetValue("FanRampUp", profile.FanRampUp);
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"ProfileManager: Failed to save profile '{profile.Name}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Saves calibrated factory defaults separately from user-editable profile values.
        /// </summary>
        public static bool SaveFactoryDefaultProfile(PerformanceProfile profile)
        {
            string subKey = $@"{RegistryBase}\{ModeToProfileName(profile.PowerMode)}";

            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(subKey);
                if (key == null)
                {
                    AppLogger.Log($"ProfileManager: Failed to create registry key '{subKey}' for factory defaults.");
                    return false;
                }

                key.SetValue("DefaultCpuPl1", Math.Max(5, profile.CpuPl1));
                key.SetValue("DefaultCpuPl2", Math.Max(5, profile.CpuPl2));
                TryDeleteValue(key, "DefaultWindowsPowerMode");
                key.SetValue("DefaultCpuBoost", Math.Clamp(profile.CpuBoost, 0, 6));
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"ProfileManager: Failed to save factory defaults for '{profile.Name}': {ex.Message}");
                return false;
            }
        }

        public static void ClearUserProfileValues(byte mode)
        {
            try
            {
                foreach (string name in ProfileKeyAliases(mode))
                {
                    using var key = Registry.CurrentUser.OpenSubKey($@"{RegistryBase}\{name}", writable: true);
                    if (key == null)
                        continue;

                    foreach (string valueName in new[]
                    {
                        "CpuPl1",
                        "CpuPl2",
                        "WindowsPowerMode",
                        "DefaultWindowsPowerMode",
                        "CpuBoost",
                        "GpuCoreOffset",
                        "GpuMemoryOffset",
                        "DefaultGpuCoreOffset",
                        "DefaultGpuMemoryOffset",
                        "ApplyCpuLimits",
                        "ApplyGpuLimits",
                        "ApplyFanCurve",
                        "FanRampUp"
                    })
                    {
                        TryDeleteValue(key, valueName);
                    }

                    for (int i = 0; i < 15; i++)
                    {
                        TryDeleteValue(key, $"CpuCurve_{i}_Y");
                        TryDeleteValue(key, $"GpuCurve_{i}_Y");
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"ProfileManager: Failed to clear user profile values for mode 0x{mode:X2}: {ex.Message}");
            }
        }

        private static RegistryKey? OpenProfileKey(byte mode, bool writable = false)
        {
            foreach (string name in ProfileKeyAliases(mode))
            {
                RegistryKey? key = Registry.CurrentUser.OpenSubKey($@"{RegistryBase}\{name}", writable);
                if (key != null)
                    return key;
            }

            return null;
        }

        private static string[] ProfileKeyAliases(byte mode)
        {
            return mode == 0x00
                ? new[] { "Quiet", "Silent" }
                : new[] { ModeToProfileName(mode) };
        }

        private static void TryDeleteValue(RegistryKey key, string name)
        {
            try
            {
                if (key.GetValue(name) != null)
                    key.DeleteValue(name);
            }
            catch { }
        }

        /// <summary>
        /// Loads calibrated factory defaults for a mode. Falls back to compiled defaults
        /// when calibration has not stored a value yet.
        /// </summary>
        public static PerformanceProfile LoadFactoryDefaultProfile(byte mode)
        {
            var profile = PerformanceProfile.CreateDefault(mode);

            try
            {
                using var key = OpenProfileKey(mode);
                if (key == null) return profile;

                profile.CpuPl1 = Math.Max(5, (int)key.GetValue("DefaultCpuPl1", profile.CpuPl1));
                profile.CpuPl2 = Math.Max(5, (int)key.GetValue("DefaultCpuPl2", profile.CpuPl2));
                profile.CpuBoost = Math.Clamp((int)key.GetValue("DefaultCpuBoost", profile.CpuBoost), 0, 6);
            }
            catch { }

            profile.ApplyCpuLimits = false;
            profile.ApplyGpuLimits = false;
            profile.ApplyFanCurve = false;
            return profile;
        }

        /// <summary>
        /// Applies the hardware settings from a profile, respecting the per-category apply flags.
        /// Only applies settings where the corresponding checkbox was enabled.
        /// </summary>
        public static Task ApplyProfileAsync(string modeName, WmiController? wmi = null)
        {
            byte mode = modeName switch
            {
                "Quiet" or "Silent" => 0x00,
                "Eco" => 0x06,
                "Performance" => 0x04,
                "Turbo" => 0x05,
                _ => 0x01
            };

            return ApplyProfileAsync(LoadProfile(mode), wmi);
        }

        /// <summary>
        /// Applies the hardware settings from a profile, respecting the per-category apply flags.
        /// Only applies settings where the corresponding checkbox was enabled.
        /// </summary>
        public static Task ApplyProfileAsync(PerformanceProfile profile, WmiController? wmi = null)
        {
            AppLogger.Log($"ProfileManager: Applying profile '{profile.Name}' (mode 0x{profile.PowerMode:X2})");
            var factoryDefaults = LoadFactoryDefaultProfile(profile.PowerMode);
            bool cpuLimitsDifferFromFactory =
                profile.CpuPl1 != factoryDefaults.CpuPl1 ||
                profile.CpuPl2 != factoryDefaults.CpuPl2;

            if (profile.ApplyCpuLimits && cpuLimitsDifferFromFactory)
            {
                AppLogger.Log($"ProfileManager: CPU PL1={profile.CpuPl1}W, PL2={profile.CpuPl2}W");
                PowerLimitController.SetCpuPowerLimits(
                    profile.CpuPl1,
                    profile.CpuPl2);
            }

            PowerLimitController.SetCpuBoost(profile.CpuBoost);
            return Task.CompletedTask;
        }
    }
}
