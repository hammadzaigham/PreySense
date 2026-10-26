using PreySense.Core.Acpi;

namespace PreySense.Mode
{
    public sealed class GameProfile
    {
        public string ExeName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public byte PowerMode { get; set; } = 0x04;
        public int CpuPl1 { get; set; } = 75;
        public int CpuPl2 { get; set; } = 140;
        public int TauSeconds { get; set; } = CpuPowerLimitConfiguration.FactoryTauSeconds;
        // Nullable for migration of profiles that only stored a nominal total.
        public int? GpuDeltaWatts { get; set; }
        public int? GpuDynamicBoostWatts { get; set; }

        public GpuPowerOffsets ResolveGpuOffsets(int baselineWatts)
        {
            if (GpuDeltaWatts is int delta && GpuDynamicBoostWatts is int boost)
                return new(Math.Clamp(delta, 0, 255), Math.Clamp(boost, 0, 255));
            return GpuPowerOffsets.FromLegacyTarget(GpuPowerWatts, baselineWatts);
        }

        public int GpuPowerWatts { get; set; } = 130;
        public int GpuThermalTripCelsius { get; set; } = GpuPowerLimitConfiguration.FactoryGpuThermalTripPointCelsius;
        public bool CpuBoostEnabled { get; set; } = true;
        public bool Enabled { get; set; } = true;

        public string ProcessName
        {
            get
            {
                string name = ExeName.Trim();
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    name = name[..^4];
                return name;
            }
        }

        public string ModeTitle => PowerMode switch
        {
            0x00 => "Quiet",
            0x01 => "Balanced",
            0x04 => "Performance",
            0x05 => "Turbo",
            0x06 => "Eco",
            _ => "Balanced"
        };
    }
}
