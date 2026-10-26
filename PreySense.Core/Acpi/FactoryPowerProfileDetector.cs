using System;
using PreySense.Core.Hardware;

namespace PreySense.Core.Acpi
{
    public sealed record FactoryPowerProfileSnapshot(
        CpuPowerLimitConfiguration Cpu,
        GpuPowerLimitConfiguration Gpu,
        byte ActiveMode,
        bool CpuReadFromFirmware,
        bool GpuPowerReadFromFirmware,
        bool GpuTripReadFromFirmware,
        bool TauReadFromFirmware)
    {
        public GpuPowerOffsets? GpuOffsets { get; init; }
        public bool HasCompleteFirmwareReadback => CpuReadFromFirmware && GpuPowerReadFromFirmware
            && GpuTripReadFromFirmware && TauReadFromFirmware;
    }

    /// <summary>
    /// Captures the active firmware power profile before application settings are applied.
    /// Firmware-known defaults remain in place for inactive modes because the ACPI-WMI
    /// interface exposes only the currently selected profile.
    /// </summary>
    public static class FactoryPowerProfileDetector
    {
        public static FactoryPowerProfileSnapshot Capture(byte activeMode, byte[]? dsdtBytes)
        {
            var cpu = new CpuPowerLimitConfiguration();
            var gpu = GpuPowerLimitConfiguration.DetectFromHardware(dsdtBytes);

            bool cpuRead = false;
            bool tauRead = false;
            var live = PreySenseWmiDeviceClient.GetPowerSnapshot();
            int? tauSeconds = null;
            if (live.TauEncoded.HasValue)
            {
                tauSeconds = live.TauEncoded.Value == CpuPowerLimitConfiguration.InfiniteTauEncodedValue
                    ? CpuPowerLimitConfiguration.InfiniteTauSliderValue
                    : CpuPowerLimitConfiguration.DecodeTauSeconds(live.TauEncoded.Value);
                cpu.TauSeconds = tauSeconds.Value;
                SetCpuModeTau(cpu, activeMode, tauSeconds.Value);
                tauRead = true;
            }

            if (live.Pl1Milliwatts > 0 && live.Pl2Milliwatts > 0)
            {
                int pl1 = (int)Math.Round(live.Pl1Milliwatts / 1000.0);
                int pl2 = (int)Math.Round(live.Pl2Milliwatts / 1000.0);
                if (pl1 is >= 5 and <= CpuPowerLimitConfiguration.MaxPowerLimitWatts &&
                    pl2 >= pl1 && pl2 <= CpuPowerLimitConfiguration.MaxPowerLimitWatts)
                {
                    SetCpuMode(cpu, activeMode, pl1, pl2, tauSeconds);
                    cpuRead = true;
                }
            }

            bool gpuPowerRead = false;
            int? baseline = GpuPowerBaseline.ReadWatts();
            var offsets = baseline.HasValue ? PreySenseWmiDeviceClient.GetGpuPowerOffsets() : null;
            if (baseline.HasValue && offsets != null)
            {
                gpu.BasePowerWatts = baseline.Value;
                SetGpuModePower(gpu, activeMode, offsets.NominalReferenceWatts(baseline.Value));
                gpuPowerRead = true;
            }
            else if (!baseline.HasValue && live.GpuTargetWatts >= GpuPowerLimitConfiguration.MinimumPowerLimitWatts)
            {
                SetGpuModePower(gpu, activeMode, (int)live.GpuTargetWatts);
                gpuPowerRead = true;
            }

            bool gpuTripRead = false;
            if (live.GpuTripCelsius is >= 70 and <= 89)
            {
                SetGpuModeTrip(gpu, activeMode, (int)live.GpuTripCelsius);
                gpuTripRead = true;
            }

            return new FactoryPowerProfileSnapshot(cpu, gpu, activeMode, cpuRead, gpuPowerRead, gpuTripRead, tauRead)
            {
                GpuOffsets = offsets
            };
        }

        private static void SetCpuMode(CpuPowerLimitConfiguration config, byte mode, int pl1, int pl2, int? tauSeconds)
        {
            switch (mode)
            {
                case 0x05: config.TurboPl1 = pl1; config.TurboPl2 = pl2; break;
                case 0x04: config.PerformancePl1 = pl1; config.PerformancePl2 = pl2; break;
                case 0x01: config.BalancedPl1 = pl1; config.BalancedPl2 = pl2; break;
                case 0x06: config.EcoPl1 = pl1; config.EcoPl2 = pl2; break;
                default: config.QuietPl1 = pl1; config.QuietPl2 = pl2; break;
            }
            if (tauSeconds.HasValue)
                SetCpuModeTau(config, mode, tauSeconds.Value);
        }

        private static void SetCpuModeTau(CpuPowerLimitConfiguration config, byte mode, int tauSeconds)
        {
            switch (mode)
            {
                case 0x05: config.TurboTauSeconds = tauSeconds; break;
                case 0x04: config.PerformanceTauSeconds = tauSeconds; break;
                case 0x01: config.BalancedTauSeconds = tauSeconds; break;
                case 0x06: config.EcoTauSeconds = tauSeconds; break;
                default: config.QuietTauSeconds = tauSeconds; break;
            }
        }

        private static void SetGpuModePower(GpuPowerLimitConfiguration config, byte mode, int watts)
        {
            switch (mode)
            {
                case 0x05: config.TurboGpuWatts = watts; break;
                case 0x04: config.PerformanceGpuWatts = watts; break;
                case 0x01: config.BalancedGpuWatts = watts; break;
                case 0x06: config.EcoGpuWatts = watts; break;
                default: config.QuietGpuWatts = watts; break;
            }
        }

        private static void SetGpuModeTrip(GpuPowerLimitConfiguration config, byte mode, int temp)
        {
            switch (mode)
            {
                case 0x05: config.TurboGpuTripPointCelsius = temp; break;
                case 0x04: config.PerformanceGpuTripPointCelsius = temp; break;
                case 0x01: config.BalancedGpuTripPointCelsius = temp; break;
                case 0x06: config.EcoGpuTripPointCelsius = temp; break;
                default: config.QuietGpuTripPointCelsius = temp; break;
            }
        }
    }
}
