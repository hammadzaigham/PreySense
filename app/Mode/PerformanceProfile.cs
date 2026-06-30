namespace PreySense.Mode
{
    /// <summary>
    /// Represents the hardware settings associated with a specific performance mode.
    /// Each mode (Silent/Eco, Balanced, Performance, Turbo) stores its own set of limits.
    /// </summary>
    public class PerformanceProfile
    {
        public byte PowerMode { get; set; }
        public string Name { get; set; } = "Balanced";

        // CPU Power Limits
        public int CpuPl1 { get; set; } = 45;
        public int CpuPl2 { get; set; } = 65;
        public int WindowsPowerMode { get; set; } = 1; // 0 = Best power efficiency, 1 = Balanced, 2 = Best performance
        public int CpuBoost { get; set; } = 1; // 0 = Disabled, 1 = Enabled, 2 = Aggressive, etc.

        // GPU Settings
        public int GpuCoreOffset { get; set; } = 0;
        public int GpuMemoryOffset { get; set; } = 0;

        // Apply flags — each category can be independently enabled/disabled per mode
        public bool ApplyCpuLimits { get; set; } = false;
        public bool ApplyGpuLimits { get; set; } = false;
        public bool ApplyFanCurve { get; set; } = false;
        public int FanRampUp { get; set; } = 1;

        /// <summary>
        /// Creates the default profile for a given power mode code.
        /// </summary>
        public static PerformanceProfile CreateDefault(byte mode)
        {
            var profile = mode switch
            {
                0x00 or 0x06 => new PerformanceProfile // Silent / Eco
                {
                    PowerMode = mode,
                    Name = mode == 0x06 ? "Eco" : "Silent",
                    CpuPl1 = mode == 0x06 ? 45 : 55,
                    CpuPl2 = mode == 0x06 ? 50 : 140,
                    WindowsPowerMode = 0,
                    CpuBoost = 1,
                    ApplyCpuLimits = false, ApplyGpuLimits = false, ApplyFanCurve = false
                },
                0x04 => new PerformanceProfile // Performance
                {
                    PowerMode = mode,
                    Name = "Performance",
                    CpuPl1 = 75,
                    CpuPl2 = 140,
                    WindowsPowerMode = 2,
                    CpuBoost = 1,
                    ApplyCpuLimits = false, ApplyGpuLimits = false, ApplyFanCurve = false
                },
                0x05 => new PerformanceProfile // Turbo
                {
                    PowerMode = mode,
                    Name = "Turbo",
                    CpuPl1 = 85,
                    CpuPl2 = 140,
                    WindowsPowerMode = 2,
                    CpuBoost = 1,
                    ApplyCpuLimits = false, ApplyGpuLimits = true, ApplyFanCurve = false
                },
                _ => new PerformanceProfile // Balanced (default)
                {
                    PowerMode = mode,
                    Name = "Balanced",
                    CpuPl1 = 65,
                    CpuPl2 = 140,
                    WindowsPowerMode = 1,
                    CpuBoost = 1,
                    ApplyCpuLimits = false, ApplyGpuLimits = false, ApplyFanCurve = false
                }
            };

            var gpuDefaults = GetDefaultGpuOffsets(mode);
            profile.GpuCoreOffset = gpuDefaults.coreOffset;
            profile.GpuMemoryOffset = gpuDefaults.memoryOffset;

            return profile;
        }

        public static (int coreOffset, int memoryOffset) GetDefaultGpuOffsets(byte mode)
        {
            return (0, 0);
        }
    }
}
