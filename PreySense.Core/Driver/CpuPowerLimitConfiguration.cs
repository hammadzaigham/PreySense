using System;

namespace PreySense.Core.Driver
{
    public class CpuPowerLimitConfiguration
    {
        private const double RaplTimeUnitSeconds = 1.0 / 1024.0;
        public const int InfiniteTauSliderValue = 449;
        public const byte InfiniteTauEncodedValue = 0x7F;
        public const int FactoryTauSeconds = 56;

        // Equal slider notches. 56 stays between 32 and 64 so the factory window is exact.
        // The notch after 256 is infinity.
        public static readonly int[] TauSliderSteps =
        [
            0, 1, 2, 4, 8, 16, 32, FactoryTauSeconds, 64, 128, 256, InfiniteTauSliderValue
        ];

        public static int TauSliderMaximum => TauSliderSteps.Length - 1;

        public static int SnapTauSelection(int seconds)
        {
            if (seconds >= InfiniteTauSliderValue || seconds > 256)
                return InfiniteTauSliderValue;

            int best = TauSliderSteps[0];
            int bestDistance = int.MaxValue;
            foreach (int step in TauSliderSteps)
            {
                if (step >= InfiniteTauSliderValue)
                    continue;
                int distance = Math.Abs(step - seconds);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = step;
                }
            }
            return best;
        }

        public static int TauStepIndex(int seconds)
        {
            int snapped = SnapTauSelection(seconds);
            for (int i = 0; i < TauSliderSteps.Length; i++)
            {
                if (TauSliderSteps[i] == snapped)
                    return i;
            }
            return 0;
        }

        public static int TauSecondsForIndex(int index) =>
            TauSliderSteps[Math.Clamp(index, 0, TauSliderSteps.Length - 1)];

        public static byte EncodeTauSelection(int sliderValue) =>
            sliderValue >= InfiniteTauSliderValue ? InfiniteTauEncodedValue : EncodeTauSeconds(sliderValue);

        public static string FormatTauSelection(int sliderValue) =>
            sliderValue <= 0 ? "Off"
            : sliderValue >= InfiniteTauSliderValue ? "∞"
            : $"{sliderValue} s";

        public static byte EncodeTauSeconds(int seconds)
        {
            // "Off" (0) is the shortest stored RAPL window — never write encoded 0 (no-op).
            if (seconds <= 0)
                return 0x01;

            double target = Math.Clamp(seconds, 1, 65535);
            byte best = 0;
            double bestError = double.MaxValue;
            for (int fraction = 0; fraction < 4; fraction++)
            {
                for (int exponent = 0; exponent < 32; exponent++)
                {
                    double candidate = Math.Pow(2, exponent) * (1.0 + fraction / 4.0) * RaplTimeUnitSeconds;
                    double error = Math.Abs(candidate - target);
                    if (error < bestError)
                    {
                        bestError = error;
                        best = (byte)((fraction << 5) | exponent);
                    }
                }
            }
            return best;
        }

        public static int DecodeTauSeconds(byte encoded)
        {
            int exponent = encoded & 0x1F;
            int fraction = (encoded >> 5) & 0x03;
            return (int)Math.Round(Math.Pow(2, exponent) * (1.0 + fraction / 4.0) * RaplTimeUnitSeconds);
        }
        // Factory AC Hardware Constants from PHN16S-71 ERM4 EC Table (0xFE8004A0)
        // Quiet:    55W / 140W
        // Eco:      45W / 50W
        // Balanced: 65W / 140W
        // Perf:     75W / 140W
        // Turbo:    85W / 140W
        // Tau:      0x6F (56 s). RAPL window = 2^Y * (1 + F/4) / 1024.
        public const int FactoryQuietPl1 = 55;
        public const int FactoryQuietPl2 = 140;
        // Acer Eco M5 profile, verified by forcing a firmware profile reload and
        // reading the firmware CPU power table on the verified hardware.
        public const int FactoryEcoPl1 = 45;
        public const int FactoryEcoPl2 = 50;

        public const int FactoryBalancedPl1 = 65;
        public const int FactoryBalancedPl2 = 140;

        public const int FactoryPerformancePl1 = 75;
        public const int FactoryPerformancePl2 = 140;

        public const int FactoryTurboPl1 = 85;
        public const int FactoryTurboPl2 = 140;

        public const int MaxPowerLimitWatts = 300;

        // Custom Target Limits (Watts)
        public int QuietPl1 { get; set; } = FactoryQuietPl1;
        public int QuietPl2 { get; set; } = FactoryQuietPl2;
        public int EcoPl1 { get; set; } = FactoryEcoPl1;
        public int EcoPl2 { get; set; } = FactoryEcoPl2;

        public int BalancedPl1 { get; set; } = FactoryBalancedPl1;
        public int BalancedPl2 { get; set; } = FactoryBalancedPl2;

        public int PerformancePl1 { get; set; } = FactoryPerformancePl1;
        public int PerformancePl2 { get; set; } = FactoryPerformancePl2;

        public int TurboPl1 { get; set; } = FactoryTurboPl1;
        public int TurboPl2 { get; set; } = FactoryTurboPl2;

        public int QuietTauSeconds { get; set; } = FactoryTauSeconds;
        public int EcoTauSeconds { get; set; } = FactoryTauSeconds;
        public int BalancedTauSeconds { get; set; } = FactoryTauSeconds;
        public int PerformanceTauSeconds { get; set; } = FactoryTauSeconds;
        public int TurboTauSeconds { get; set; } = FactoryTauSeconds;

        /// <summary>Last captured or applied window. Mode-specific fields are the stored values.</summary>
        public int TauSeconds { get; set; } = FactoryTauSeconds;

        /// <summary>
        /// Validates values against Intel Raptor Lake / Arrow Lake mobile thermal specifications
        /// and PreySense driver-tuning limits (up to 300W max, PL1 <= PL2).
        /// </summary>
        public bool Validate(out string errorMessage)
        {
            if (QuietPl1 < 10 || QuietPl1 > MaxPowerLimitWatts || QuietPl2 < QuietPl1 || QuietPl2 > MaxPowerLimitWatts)
            {
                errorMessage = $"Quiet limits out of range (PL1: 10-{MaxPowerLimitWatts}W, PL2: PL1-{MaxPowerLimitWatts}W).";
                return false;
            }
            if (EcoPl1 < 10 || EcoPl1 > MaxPowerLimitWatts || EcoPl2 < EcoPl1 || EcoPl2 > MaxPowerLimitWatts)
            {
                errorMessage = $"Eco limits out of range (PL1: 10-{MaxPowerLimitWatts}W, PL2: PL1-{MaxPowerLimitWatts}W).";
                return false;
            }
            if (BalancedPl1 < 10 || BalancedPl1 > MaxPowerLimitWatts || BalancedPl2 < BalancedPl1 || BalancedPl2 > MaxPowerLimitWatts)
            {
                errorMessage = $"Balanced limits out of range (PL1: 10-{MaxPowerLimitWatts}W, PL2: PL1-{MaxPowerLimitWatts}W).";
                return false;
            }
            if (PerformancePl1 < 10 || PerformancePl1 > MaxPowerLimitWatts || PerformancePl2 < PerformancePl1 || PerformancePl2 > MaxPowerLimitWatts)
            {
                errorMessage = $"Performance limits out of range (PL1: 10-{MaxPowerLimitWatts}W, PL2: PL1-{MaxPowerLimitWatts}W).";
                return false;
            }
            if (TurboPl1 < 10 || TurboPl1 > MaxPowerLimitWatts || TurboPl2 < TurboPl1 || TurboPl2 > MaxPowerLimitWatts)
            {
                errorMessage = $"Turbo limits out of range (PL1: 10-{MaxPowerLimitWatts}W, PL2: PL1-{MaxPowerLimitWatts}W).";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        public void ResetToDefaults()
        {
            QuietPl1 = FactoryQuietPl1;
            QuietPl2 = FactoryQuietPl2;
            EcoPl1 = FactoryEcoPl1;
            EcoPl2 = FactoryEcoPl2;
            BalancedPl1 = FactoryBalancedPl1;
            BalancedPl2 = FactoryBalancedPl2;
            PerformancePl1 = FactoryPerformancePl1;
            PerformancePl2 = FactoryPerformancePl2;
            TurboPl1 = FactoryTurboPl1;
            TurboPl2 = FactoryTurboPl2;
            QuietTauSeconds = FactoryTauSeconds;
            EcoTauSeconds = FactoryTauSeconds;
            BalancedTauSeconds = FactoryTauSeconds;
            PerformanceTauSeconds = FactoryTauSeconds;
            TurboTauSeconds = FactoryTauSeconds;
            TauSeconds = FactoryTauSeconds;
        }
    }
}
