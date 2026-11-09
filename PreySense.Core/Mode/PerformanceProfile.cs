using PreySense.Core.Driver;

namespace PreySense.Mode
{
    /// <summary>
    /// Represents the hardware settings associated with a specific performance mode.
    /// Each mode (Quiet/Eco, Balanced, Performance, Turbo) stores its own set of limits.
    /// </summary>
    public class PerformanceProfile
    {
        public byte PowerMode { get; set; }
        public string Name { get; set; } = "Balanced";

        // CPU Power Limits
        public int CpuPl1 { get; set; } = 45;
        public int CpuPl2 { get; set; } = 65;
        public int TauSeconds { get; set; } = 56;
        public int CpuBoost { get; set; } = 1; // 0 = Disabled, 1 = Enabled, 2 = Aggressive, etc.

        public int? ExtendedCpuBoost { get; set; }

        // Apply flags — CPU/GPU independently enabled/disabled per mode (fans use Fans.*)
        public bool ApplyCpuLimits { get; set; } = false;
        public bool ApplyGpuLimits { get; set; } = false;

        /// <summary>
        /// Creates the default profile for a given power mode code.
        /// </summary>
        public static PerformanceProfile CreateDefault(byte mode)
        {
            var profile = mode switch
            {
                0x00 => new PerformanceProfile
                {
                    PowerMode = mode,
                    Name = "Quiet",
                    CpuPl1 = CpuPowerLimitConfiguration.FactoryQuietPl1,
                    CpuPl2 = CpuPowerLimitConfiguration.FactoryQuietPl2,
                    TauSeconds = CpuPowerLimitConfiguration.FactoryTauSeconds,
                    CpuBoost = 1,
                    ApplyCpuLimits = false, ApplyGpuLimits = false
                },
                0x06 => new PerformanceProfile
                {
                    PowerMode = mode,
                    Name = "Eco",
                    CpuPl1 = CpuPowerLimitConfiguration.FactoryEcoPl1,
                    CpuPl2 = CpuPowerLimitConfiguration.FactoryEcoPl2,
                    TauSeconds = CpuPowerLimitConfiguration.FactoryTauSeconds,
                    CpuBoost = 1,
                    ApplyCpuLimits = false, ApplyGpuLimits = false
                },
                0x04 => new PerformanceProfile
                {
                    PowerMode = mode,
                    Name = "Performance",
                    CpuPl1 = CpuPowerLimitConfiguration.FactoryPerformancePl1,
                    CpuPl2 = CpuPowerLimitConfiguration.FactoryPerformancePl2,
                    TauSeconds = CpuPowerLimitConfiguration.FactoryTauSeconds,
                    CpuBoost = 1,
                    ApplyCpuLimits = false, ApplyGpuLimits = false
                },
                0x05 => new PerformanceProfile
                {
                    PowerMode = mode,
                    Name = "Turbo",
                    CpuPl1 = CpuPowerLimitConfiguration.FactoryTurboPl1,
                    CpuPl2 = CpuPowerLimitConfiguration.FactoryTurboPl2,
                    TauSeconds = CpuPowerLimitConfiguration.FactoryTauSeconds,
                    CpuBoost = 1,
                    ApplyCpuLimits = false, ApplyGpuLimits = false
                },
                0x01 => new PerformanceProfile
                {
                    PowerMode = mode,
                    Name = "Balanced",
                    CpuPl1 = CpuPowerLimitConfiguration.FactoryBalancedPl1,
                    CpuPl2 = CpuPowerLimitConfiguration.FactoryBalancedPl2,
                    TauSeconds = CpuPowerLimitConfiguration.FactoryTauSeconds,
                    CpuBoost = 1,
                    ApplyCpuLimits = false, ApplyGpuLimits = false
                },
                _ => throw new ArgumentOutOfRangeException(nameof(mode))
            };

            return profile;
        }
    }
}
