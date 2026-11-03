namespace PreySense.Core.Services;

using PreySense.Gpu;

public interface ITelemetrySources
{
    CpuTelemetrySnapshot? ReadCpuUsage();
    int? ReadCpuTemperature();
    float? ReadCpuVoltage();
    float? ReadCpuPower();
    (int? Performance, int? Efficiency) ReadCpuClocks();
    (int? Percent, int? UsedMb) ReadRamUsage();
    int? ReadRamSpeed();
    (int? Cpu, int? Gpu) ReadFanRpms(bool cpu, bool gpu);
    NvidiaGpuControl.Metrics ReadGpu(TelemetryMetrics metrics);
}

/// <summary>Reads only the physical sources required by the current selection.</summary>
public static class TelemetryCollector
{
    public static TelemetrySnapshot Read(TelemetryMetrics metrics, ITelemetrySources sources, bool fanOnly = false)
    {
        bool Wants(TelemetryMetrics metric) => (metrics & metric) != 0;
        var cpu = Wants(TelemetryMetrics.CpuUsage) ? sources.ReadCpuUsage() : null;
        var cpuTemp = Wants(TelemetryMetrics.CpuTemperature) ? sources.ReadCpuTemperature() : null;
        var cpuVoltage = Wants(TelemetryMetrics.CpuVoltage) ? sources.ReadCpuVoltage() : null;
        var cpuPower = Wants(TelemetryMetrics.CpuPower) ? sources.ReadCpuPower() : null;
        var clocks = Wants(TelemetryMetrics.CpuClock) ? sources.ReadCpuClocks() : default;
        var ram = Wants(TelemetryMetrics.RamUsage) ? sources.ReadRamUsage() : default;
        var ramSpeed = Wants(TelemetryMetrics.RamSpeed) ? sources.ReadRamSpeed() : null;
        var fans = Wants(TelemetryMetrics.CpuFanRpm | TelemetryMetrics.GpuFanRpm)
            ? sources.ReadFanRpms(Wants(TelemetryMetrics.CpuFanRpm), Wants(TelemetryMetrics.GpuFanRpm)) : default;
        var gpu = Wants(TelemetryMetrics.Gpu) ? sources.ReadGpu(metrics & TelemetryMetrics.Gpu) : default;
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        return TelemetrySnapshot.Empty with
        {
            TimestampTicks = fanOnly ? 0 : now, ThermalTimestampTicks = now,
            IsValid = true, IsFanOnly = fanOnly,
            CpuUsage = cpu?.TotalUsage, CpuMaxCoreUsage = cpu?.MaxCoreUsage,
            CpuPCoreUsage = cpu?.PCoreUsage, CpuECoreUsage = cpu?.ECoreUsage,
            CpuTemp = cpuTemp, CpuRpm = fans.Cpu, GpuRpm = fans.Gpu,
            CpuVoltage = cpuVoltage, CpuPkgPowerWatts = cpuPower,
            CpuClockMhz = clocks.Performance, ECoreClockMhz = clocks.Efficiency,
            RamUsage = ram.Percent, RamUsedMb = ram.UsedMb, RamSpeedMhz = ramSpeed,
            GpuTemp = gpu.Temperature, GpuUsage = gpu.Usage, GpuClockMhz = gpu.ClockMhz,
            GpuVoltage = gpu.Voltage, GpuPowerWatts = gpu.PowerWatts,
            VramUsedMb = (int?)gpu.VramUsedMb, VramTotalMb = (int?)gpu.VramTotalMb,
            VramSpeedMhz = gpu.MemoryClockMhz, GpuInUse = gpu.InUse
        };
    }
}
