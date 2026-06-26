using System;
using Microsoft.Win32;
using PreySense;
using PreySense.Helpers;

namespace PreySense.Battery
{
    public static class BatteryControl
    {
        private const string RegistryKeyPath = @"SOFTWARE\PreySense";
        private const string BatteryLimitValueName = "BatteryLimit";
        private const int BatteryLimitDisabled = 0;
        private const int BatteryLimitEnabled = 1;

        public static int GetBatteryLimit()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
                return key == null ? BatteryLimitDisabled : Convert.ToInt32(key.GetValue(BatteryLimitValueName, BatteryLimitDisabled));
            }
            catch
            {
                return BatteryLimitDisabled;
            }
        }

        public static void SaveBatteryLimit(int mode)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
                key?.SetValue(BatteryLimitValueName, NormalizeMode(mode), RegistryValueKind.DWord);
            }
            catch
            {
                // Intentionally ignore registry write failures.
            }
        }

        public static bool ApplyBatteryLimit(WmiController wmi, int mode)
        {
            int normalizedMode = NormalizeMode(mode);
            try
            {
                bool applied = wmi.SetBatteryMode(normalizedMode);
                AppLogger.Log($"BatteryControl: {(applied ? "applied" : "failed to apply")} battery charge limit mode {normalizedMode} ({GetLimitLabel(normalizedMode)}).");
                return applied;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"BatteryControl: failed to apply battery charge limit mode {normalizedMode}: {ex.Message}");
                return false;
            }
        }

        public static bool SetBatteryLimit(WmiController wmi, int mode)
        {
            int normalizedMode = NormalizeMode(mode);
            SaveBatteryLimit(normalizedMode);
            return ApplyBatteryLimit(wmi, normalizedMode);
        }

        private static int NormalizeMode(int mode) => mode == BatteryLimitEnabled ? BatteryLimitEnabled : BatteryLimitDisabled;

        private static string GetLimitLabel(int mode) => mode == BatteryLimitEnabled ? "80%" : "100%";
    }
}
