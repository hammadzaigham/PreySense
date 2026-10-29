using System;
using System.Collections.Concurrent;
using Microsoft.Win32;

namespace PreySense.Overlay
{
    public static class AppConfig
    {
        private const string RegistryKeyPath = @"SOFTWARE\PreySense";
        private const string Missing = "\u0000";
        private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.Ordinal);

        private static string Read(string name)
        {
            if (Cache.TryGetValue(name, out string? cached))
                return cached;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
                string value = key?.GetValue(name)?.ToString() ?? Missing;
                Cache[name] = value;
                return value;
            }
            catch
            {
                Cache[name] = Missing;
                return Missing;
            }
        }

        public static bool Exists(string name)
        {
            try
            {
                return Read(name) != Missing;
            }
            catch
            {
                return false;
            }
        }

        public static int Get(string name, int empty = -1)
        {
            try
            {
                string value = Read(name);
                if (value != Missing && int.TryParse(value, out int result))
                {
                    return result;
                }
            }
            catch { }
            return empty;
        }

        public static bool Is(string name)
        {
            return Get(name) == 1;
        }

        public static string GetString(string name, string empty = "")
        {
            try
            {
                string value = Read(name);
                return value == Missing ? empty : value;
            }
            catch
            {
                return empty;
            }
        }

        public static void Set(string name, int value)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
                key?.SetValue(name, value, RegistryValueKind.DWord);
                Cache[name] = value.ToString();
            }
            catch { }
        }

        public static void Set(string name, string value)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
                key?.SetValue(name, value, RegistryValueKind.String);
                Cache[name] = value;
            }
            catch { }
        }

        public static bool IsOverlay()
        {
            return Is("overlay");
        }

        public static bool IsOverlayGameOnly()
        {
            return Is("overlay_game_only");
        }

        public static bool ShouldRunOverlay()
        {
            return IsOverlay() || IsOverlayGameOnly();
        }

        public static string DefaultOverlayColumn(int oneBasedIndex) => oneBasedIndex switch
        {
            1 => "Usage|Usage",
            2 => "Temperature|Temperature",
            3 => "P-Core|Clock",
            4 => "Voltage|Voltage",
            5 => "Power|Power",
            6 => "Memory|Memory",
            7 => "Memory Speed|Memory Speed",
            8 => "None|None",
            _ => "None|None"
        };

        public static (string Cpu, string Gpu) ParseOverlayColumn(string? stored)
        {
            if (string.IsNullOrWhiteSpace(stored))
                return ("None", "None");

            int split = stored.IndexOf('|');
            if (split >= 0)
                return (NormalizeMetric(stored[..split]), NormalizeMetric(stored[(split + 1)..]));

            return stored switch
            {
                "Usage" => ("Usage", "Usage"),
                "Temperature" => ("Temperature", "Temperature"),
                "Clock" => ("P-Core", "Clock"),
                "Voltage" => ("Voltage", "Voltage"),
                "Power" => ("Power", "Power"),
                "Memory" => ("Memory", "Memory"),
                "Memory Speed" => ("Memory Speed", "Memory Speed"),
                "Fan Speed" => ("Fan Speed", "Fan Speed"),
                "None" => ("None", "None"),
                _ => ("None", "None")
            };
        }

        public static void EnsureDefaultOverlayColumns()
        {
            int version = Get("overlay_col_defaults", 0);
            if (version >= 5)
                return;
            if (version == 4)
            {
                // Remove the old fan defaults, but preserve an intentional
                // user selection of Fan Speed.
                if (GetString("overlay_col_8") == "Fan Speed|Fan Speed")
                    Set("overlay_col_8", DefaultOverlayColumn(8));
                Set("overlay_col_defaults", 5);
                return;
            }
            if (version == 3)
            {
                string voltageColumn = GetString("overlay_col_4");
                Set("overlay_col_4", UpgradeDefaultVoltageColumn(voltageColumn));
                Set("overlay_col_defaults", 4);
                return;
            }

            bool untouched = true;
            var current = new string[8];
            for (int i = 1; i <= 8; i++)
            {
                current[i - 1] = GetString($"overlay_col_{i}");
                if (!string.IsNullOrEmpty(current[i - 1]) && current[i - 1] != LegacyDefaultColumn(i))
                    untouched = false;
            }

            for (int i = 1; i <= 8; i++)
            {
                string key = $"overlay_col_{i}";
                if (untouched || string.IsNullOrEmpty(current[i - 1]))
                    Set(key, DefaultOverlayColumn(i));
                else if (!current[i - 1].Contains('|'))
                    Set(key, MigrateLegacyColumn(current[i - 1]));
            }

            Set("overlay_col_defaults", 4);
        }

        public static string UpgradeDefaultVoltageColumn(string current) =>
            string.IsNullOrEmpty(current) || current == "E-Core|Voltage"
                ? DefaultOverlayColumn(4) : current;

        private static string LegacyDefaultColumn(int oneBasedIndex) => oneBasedIndex switch
        {
            1 => "Usage",
            2 => "Temperature",
            3 => "Voltage",
            4 => "Clock",
            5 => "Power",
            6 => "Memory",
            7 => "Memory Speed",
            8 => "Fan Speed",
            _ => "None"
        };

        private static string MigrateLegacyColumn(string value) => value switch
        {
            "Usage" => "Usage|Usage",
            "Temperature" => "Temperature|Temperature",
            "Clock" => "P-Core|Clock",
            "Voltage" => "Voltage|Voltage",
            "Power" => "Power|Power",
            "Memory" => "Memory|Memory",
            "Memory Speed" => "Memory Speed|Memory Speed",
            "Fan Speed" => "Fan Speed|Fan Speed",
            "None" => "None|None",
            _ => "None|None"
        };

        private static string NormalizeMetric(string metric)
        {
            string trimmed = metric.Trim();
            return trimmed switch
            {
                "Usage" or "Temperature" or "P-Core" or "E-Core" or "Clock" or "Voltage"
                    or "Power" or "Memory" or "Memory Speed" or "Fan Speed" => trimmed,
                _ => "None"
            };
        }
    }
}
