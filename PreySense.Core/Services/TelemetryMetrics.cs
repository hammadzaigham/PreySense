namespace PreySense.Core.Services;

[Flags]
public enum TelemetryMetrics
{
    None = 0,
    CpuTemperature = 1 << 0,
    CpuFanRpm = 1 << 1,
    GpuTemperature = 1 << 2,
    GpuFanRpm = 1 << 3,
    CpuUsage = 1 << 4,
    CpuClock = 1 << 5,
    CpuVoltage = 1 << 6,
    CpuPower = 1 << 7,
    RamUsage = 1 << 8,
    RamSpeed = 1 << 9,
    GpuUsage = 1 << 10,
    GpuClock = 1 << 11,
    GpuVoltage = 1 << 12,
    GpuPower = 1 << 13,
    GpuMemory = 1 << 14,
    GpuMemoryClock = 1 << 15,
    Temperatures = CpuTemperature | GpuTemperature,
    Gpu = GpuTemperature | GpuUsage | GpuClock | GpuVoltage | GpuPower | GpuMemory | GpuMemoryClock,
    All = 0xFFFF
}

public static class TelemetrySelection
{
    public static TelemetryMetrics Cpu(string metric) => metric switch
    {
        "Temperature" => TelemetryMetrics.CpuTemperature,
        "Fan Speed" => TelemetryMetrics.CpuFanRpm,
        "Usage" or "Max Core" or "Max Core Usage" or "P-Core Usage" or "E-Core Usage" => TelemetryMetrics.CpuUsage,
        "P-Core" or "E-Core" or "Clock" => TelemetryMetrics.CpuClock,
        "Voltage" => TelemetryMetrics.CpuVoltage,
        "Power" => TelemetryMetrics.CpuPower,
        "Memory" => TelemetryMetrics.RamUsage,
        "Memory Speed" => TelemetryMetrics.RamSpeed,
        _ => TelemetryMetrics.None
    };

    public static TelemetryMetrics Gpu(string metric) => metric switch
    {
        "Temperature" => TelemetryMetrics.GpuTemperature,
        "Fan Speed" => TelemetryMetrics.GpuFanRpm,
        "Usage" => TelemetryMetrics.GpuUsage,
        "Clock" => TelemetryMetrics.GpuClock,
        "Voltage" => TelemetryMetrics.GpuVoltage,
        "Power" => TelemetryMetrics.GpuPower,
        "Memory" => TelemetryMetrics.GpuMemory,
        "Memory Speed" => TelemetryMetrics.GpuMemoryClock,
        _ => TelemetryMetrics.None
    };
}
