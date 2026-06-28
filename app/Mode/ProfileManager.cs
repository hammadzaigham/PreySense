using System;
using System.Drawing;
using Microsoft.Win32;
using PreySense.Gpu;
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

        /// <summary>
        /// Maps a WMI power mode code to a registry-safe profile name.
        /// </summary>
        public static string ModeToProfileName(byte mode)
        {
            return mode switch
            {
                0x00 => "Silent",
                0x06 => "Eco",
                0x04 => "Performance",
                0x05 => "Turbo",
                _ => "Balanced"
            };
        }

        /// <summary>
        /// Loads a performance profile for the given mode from registry.
        /// Returns default values if no saved profile exists.
        /// </summary>
        public static PerformanceProfile LoadProfile(byte mode)
        {
            var profile = PerformanceProfile.CreateDefault(mode);
            string subKey = $@"{RegistryBase}\{ModeToProfileName(mode)}";

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(subKey);
                if (key == null) return profile;

                profile.CpuPl1 = Math.Clamp((int)key.GetValue("CpuPl1", profile.CpuPl1), 5, 200);
                profile.CpuPl2 = Math.Clamp((int)key.GetValue("CpuPl2", profile.CpuPl2), 5, 200);
                profile.WindowsPowerMode = Math.Clamp((int)key.GetValue("WindowsPowerMode", profile.WindowsPowerMode), 0, 2);

                profile.GpuCoreOffset = Math.Clamp((int)key.GetValue("GpuCoreOffset", profile.GpuCoreOffset), -1000, 1000);
                profile.GpuMemoryOffset = Math.Clamp((int)key.GetValue("GpuMemoryOffset", profile.GpuMemoryOffset), -1000, 3000);

                profile.ApplyCpuLimits = (int)key.GetValue("ApplyCpuLimits", 0) == 1;
                profile.ApplyGpuLimits = (int)key.GetValue("ApplyGpuLimits", 0) == 1;
                profile.ApplyFanCurve = (int)key.GetValue("ApplyFanCurve", 0) == 1;
                profile.FanRampUp = (int)key.GetValue("FanRampUp", 0);
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

                key.SetValue("CpuPl1", Math.Clamp(profile.CpuPl1, 5, 200));
                key.SetValue("CpuPl2", Math.Clamp(profile.CpuPl2, 5, 200));
                key.SetValue("WindowsPowerMode", profile.WindowsPowerMode);

                key.SetValue("GpuCoreOffset", profile.GpuCoreOffset);
                key.SetValue("GpuMemoryOffset", profile.GpuMemoryOffset);

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
                key.SetValue("DefaultWindowsPowerMode", Math.Clamp(profile.WindowsPowerMode, 0, 2));
                key.SetValue("DefaultGpuCoreOffset", profile.GpuCoreOffset);
                key.SetValue("DefaultGpuMemoryOffset", profile.GpuMemoryOffset);
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
            string subKey = $@"{RegistryBase}\{ModeToProfileName(mode)}";

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(subKey, writable: true);
                if (key == null)
                    return;

                foreach (string valueName in new[]
                {
                    "CpuPl1",
                    "CpuPl2",
                    "WindowsPowerMode",
                    "GpuCoreOffset",
                    "GpuMemoryOffset",
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
            catch (Exception ex)
            {
                AppLogger.Log($"ProfileManager: Failed to clear user profile values for mode 0x{mode:X2}: {ex.Message}");
            }
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
            string subKey = $@"{RegistryBase}\{ModeToProfileName(mode)}";

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(subKey);
                if (key == null) return profile;

                profile.CpuPl1 = Math.Max(5, (int)key.GetValue("DefaultCpuPl1", profile.CpuPl1));
                profile.CpuPl2 = Math.Max(5, (int)key.GetValue("DefaultCpuPl2", profile.CpuPl2));
                profile.WindowsPowerMode = Math.Clamp((int)key.GetValue("DefaultWindowsPowerMode", profile.WindowsPowerMode), 0, 2);
            }
            catch { }

            var gpuDefaults = PerformanceProfile.GetDefaultGpuOffsets(mode);
            profile.GpuCoreOffset = Math.Clamp(gpuDefaults.coreOffset, -1000, 1000);
            profile.GpuMemoryOffset = Math.Clamp(gpuDefaults.memoryOffset, -1000, 3000);

            profile.ApplyCpuLimits = false;
            profile.ApplyGpuLimits = false;
            profile.ApplyFanCurve = false;
            return profile;
        }

        /// <summary>
        /// Applies the hardware settings from a profile, respecting the per-category apply flags.
        /// Only applies settings where the corresponding checkbox was enabled.
        /// </summary>
        public static void ApplyProfile(string modeName, WmiController? wmi = null)
        {
            byte mode = modeName switch
            {
                "Silent" => 0x00,
                "Eco" => 0x06,
                "Performance" => 0x04,
                "Turbo" => 0x05,
                _ => 0x01
            };

            var profile = LoadProfile(mode);
            ApplyProfile(profile, wmi);
        }

        /// <summary>
        /// Applies the hardware settings from a profile, respecting the per-category apply flags.
        /// Only applies settings where the corresponding checkbox was enabled.
        /// </summary>
        public static void ApplyProfile(PerformanceProfile profile, WmiController? wmi = null)
        {
            AppLogger.Log($"ProfileManager: Applying profile '{profile.Name}' (mode 0x{profile.PowerMode:X2})");

            // Wait a second or two before applying power limit changes to stop EC from immediately overwriting them
            if (profile.ApplyCpuLimits || profile.ApplyGpuLimits)
            {
                System.Threading.Thread.Sleep(2000);
            }

            // Apply CPU power limits (only if the user has opted in)
            if (profile.ApplyCpuLimits)
            {
                AppLogger.Log($"ProfileManager: CPU PL1={profile.CpuPl1}W, PL2={profile.CpuPl2}W");
                PowerLimitController.SetCpuPowerLimits(
                    profile.CpuPl1,
                    profile.CpuPl2);
            }

            // Apply GPU offsets only when the profile explicitly enables them.
            if (profile.ApplyGpuLimits && IsDiscreteGpuAvailableForOverclock())
            {
                AppLogger.Log($"ProfileManager: applying NVAPI GPU offsets for profile '{profile.Name}' (core={profile.GpuCoreOffset:+#;-#;0}MHz, memory={profile.GpuMemoryOffset:+#;-#;0}MHz).");
                NvidiaWrapper.ApplyGpuSettings(profile.GpuCoreOffset, profile.GpuMemoryOffset);
            }
            else if (profile.ApplyGpuLimits)
            {
                AppLogger.Log($"ProfileManager: skipped NVAPI GPU offsets for profile '{profile.Name}' because GPU mode is iGPU only.");
            }

            // Fan curve is applied by the main form's telemetry timer if enabled —
            // we just need to signal whether it should be active for this mode.
            // The SettingsForm.ToggleCustomFans() handles this.
        }

        private static bool IsDiscreteGpuAvailableForOverclock()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\PreySense");
                int gpuMode = key != null ? (int)key.GetValue("GPUMode", 1)! : 1;
                return gpuMode != 0;
            }
            catch
            {
                return true;
            }
        }
    }
}
