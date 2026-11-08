using PreySense.Core.Services;

namespace PreySense.Overlay;

public static class TelemetryGraphValue
{
    public static float Read(TelemetrySnapshot snapshot, string metric, bool gpu)
    {
        float? value = metric switch
        {
            "Usage" => gpu ? snapshot.GpuUsage : snapshot.CpuUsage,
            "Temperature" => gpu ? snapshot.GpuTemp : snapshot.CpuTemp,
            "Hotspot" => snapshot.GpuHotspotTemp,
            "Memory Temperature" => snapshot.GpuMemoryTemp,
            "Clock" or "Active Clock" => gpu ? snapshot.GpuActiveClockMhz : snapshot.CpuActiveFrequencyMhz,
            "Effective Clock" => gpu ? null : snapshot.CpuEffectiveFrequencyMhz,
            "Voltage" or "CPU VID" => gpu ? snapshot.GpuVoltage : snapshot.CpuVoltage,
            "Power" => gpu ? snapshot.GpuPowerWatts : snapshot.CpuPkgPowerWatts,
            "Power Limit" => gpu ? snapshot.GpuPowerLimitWatts : snapshot.CpuPl1Watts,
            "PL1" => snapshot.CpuPl1Watts,
            "PL2" => snapshot.CpuPl2Watts,
            "Memory" => gpu ? snapshot.VramUsedMb : snapshot.RamUsedMb,
            "Memory Speed" => gpu ? snapshot.VramSpeedMhz : snapshot.RamSpeedMhz,
            "Fan Speed" => gpu ? snapshot.GpuRpm : snapshot.CpuRpm,
            _ => null
        };
        return value.HasValue && float.IsFinite(value.Value) && value.Value >= 0 ? value.Value : float.NaN;
    }
}
