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
    GpuHotspot = 1 << 16,
    GpuThrottleReasons = 1 << 17,
    GpuPowerLimit = 1 << 18,
    CpuEffectiveClock = 1 << 19,
    CpuPowerLimit = 1 << 20,
    GpuMemoryTemperature = 1 << 21,
    GpuActiveClock = 1 << 22,
    Temperatures = CpuTemperature | GpuTemperature,
    Gpu = GpuTemperature | GpuUsage | GpuActiveClock | GpuVoltage | GpuPower | GpuMemory | GpuMemoryClock | GpuHotspot | GpuThrottleReasons | GpuPowerLimit | GpuMemoryTemperature,
    FrameRate = 1 << 23,
    DisplayAdapter = 1 << 24,
    Hardware = 0x7FFFFF | DisplayAdapter,
    All = Hardware | FrameRate
}

public static class TelemetrySelection
{
    public static string NormalizeCpu(string? metric) => Cpu(metric ?? "None") != TelemetryMetrics.None ? metric! : "None";
    public static string NormalizeGpu(string? metric) => metric == "Active Clock" ? "Clock" : Gpu(metric ?? "None") != TelemetryMetrics.None ? metric! : "None";

    public static TelemetryMetrics Cpu(string metric) => metric switch
    {
        "Temperature" => TelemetryMetrics.CpuTemperature,
        "Fan Speed" => TelemetryMetrics.CpuFanRpm,
        "Usage" => TelemetryMetrics.CpuUsage,
        "Active Clock" => TelemetryMetrics.CpuClock,
        "Effective Clock" => TelemetryMetrics.CpuEffectiveClock,
        "Voltage" or "VID" or "CPU VID" => TelemetryMetrics.CpuVoltage,
        "Power" => TelemetryMetrics.CpuPower,
        "Power Limit" or "PL1" or "PL2" => TelemetryMetrics.CpuPowerLimit,
        "Memory" => TelemetryMetrics.RamUsage,
        "Memory Speed" => TelemetryMetrics.RamSpeed,
        _ => TelemetryMetrics.None
    };

    public static TelemetryMetrics Gpu(string metric) => metric switch
    {
        "Temperature" => TelemetryMetrics.GpuTemperature,
        "Hotspot" or "Hotspot Temperature" => TelemetryMetrics.GpuHotspot,
        "Memory Temperature" => TelemetryMetrics.GpuMemoryTemperature,
        "Fan Speed" => TelemetryMetrics.GpuFanRpm,
        "Usage" => TelemetryMetrics.GpuUsage,
        "Clock" or "Active Clock" => TelemetryMetrics.GpuActiveClock,
        "Voltage" => TelemetryMetrics.GpuVoltage,
        "Power" => TelemetryMetrics.GpuPower,
        "Power Limit" => TelemetryMetrics.GpuPowerLimit,
        "PerfCap Reason" => TelemetryMetrics.GpuThrottleReasons,
        "Memory" => TelemetryMetrics.GpuMemory,
        "Memory Speed" => TelemetryMetrics.GpuMemoryClock,
        _ => TelemetryMetrics.None
    };
}
