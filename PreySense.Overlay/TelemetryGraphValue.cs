using PreySense.Core.Services;

namespace PreySense.Overlay;

public static class TelemetryGraphValue
{
    public static float Read(TelemetrySnapshot snapshot, string metric, bool gpu)
    {
        float? value = metric switch
        {
            "Usage" => gpu ? snapshot.GpuUsage : snapshot.CpuUsage,
            "Max Core" or "Max Core Usage" => snapshot.CpuMaxCoreUsage,
            "P-Core Usage" => snapshot.CpuPCoreUsage,
            "E-Core Usage" => snapshot.CpuECoreUsage,
            "Temperature" => gpu ? snapshot.GpuTemp : snapshot.CpuTemp,
            "P-Core" => snapshot.CpuClockMhz,
            "E-Core" => snapshot.ECoreClockMhz,
            "Clock" => gpu ? snapshot.GpuClockMhz : snapshot.CpuClockMhz,
            "Voltage" => gpu ? snapshot.GpuVoltage : snapshot.CpuVoltage,
            "Power" => gpu ? snapshot.GpuPowerWatts : snapshot.CpuPkgPowerWatts,
            "Memory" => gpu ? snapshot.VramUsedMb : snapshot.RamUsedMb,
            "Memory Speed" => gpu ? snapshot.VramSpeedMhz : snapshot.RamSpeedMhz,
            "Fan Speed" => gpu ? snapshot.GpuRpm : snapshot.CpuRpm,
            _ => null
        };
        return Math.Max(0, value ?? 0);
    }
}
