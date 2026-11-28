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
    }
}
