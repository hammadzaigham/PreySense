using System.Runtime.Versioning;

namespace PreySense.Helpers
{
    /// <summary>
    /// Predator Sense keyboard RGB modes and AcerService effect mapping.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static class RgbProfile
    {
        public const int ModeCount = 8;

        public const int WaveModeIndex = 3;

        public const int StepCount = 5;

        public static readonly string[] UiModeNames =
        {
            "Static", "Breathing", "Neon", "Wave", "Ripple", "Zoom", "Snake", "Disco"
        };

        /// <summary>
        /// AcerService EffectNames index for each UI mode (0..7).
        /// Matches Predator Sense KB_Zone_Default: STATIC, BREATHING, NEON, WAVE,
        /// SHIFTING (UI: Ripple), ZOOM, METEOR (UI: Snake), TWINKLING (UI: Disco).
        /// </summary>
        public static readonly int[] ModeToServiceEffectIndex = { 0, 1, 5, 2, 13, 14, 15, 16 };

        public static int GetServiceEffectIndex(int mode) =>
            mode is >= 0 and < ModeCount ? ModeToServiceEffectIndex[mode] : 0;

        public static string GetServiceEffectName(int mode, string[] serviceEffectNames) =>
            serviceEffectNames[GetServiceEffectIndex(mode)];

        /// <summary>Normalize saved/UI level to AcerService scale (1–5).</summary>
        public static int NormalizeStepLevel(int saved) =>
            saved switch
            {
                > 10 => Math.Clamp((saved + 10) / 20, 1, StepCount),
                > StepCount => Math.Clamp((saved + 1) / 2, 1, StepCount),
                _ => Math.Clamp(saved, 1, StepCount)
            };

        public static int ScaleBrightnessToService(int brightness) =>
            NormalizeStepLevel(brightness);

        /// <summary>Predator Sense subindex zone 2 fallback for device 0 lighting.</summary>
        public static string GetSubindexSecondary(string effect) => effect switch
        {
            "WAVE" => "NEON",
            "SHIFTING" => "STATIC",
            "ZOOM" => "STATIC",
            "METEOR" => "STATIC",
            "TWINKLING" => "BREATHING",
            _ => effect
        };

        /// <summary>Map legacy EffectNames indices saved before mode-index fix.</summary>
        public static int NormalizeSavedMode(int mode, int version = 1)
        {
            if (version >= 2)
            {
                if (mode is >= 0 and < ModeCount)
                    return mode;

                return mode switch
                {
                    5 => 2,   // NEON
                    14 => 5,  // ZOOM
                    32 => 7,  // DISCO
                    _ => 0
                };
            }

            return mode switch
            {
                0 => 0,
                1 => 1,
                2 => 3,   // WAVE
                3 => 6,   // SNAKE
                4 => 4,
                5 => 2,   // NEON
                14 => 5,  // ZOOM
                32 => 7,  // DISCO
                >= 0 and < ModeCount => mode,
                _ => 0
            };
        }
    }
}
