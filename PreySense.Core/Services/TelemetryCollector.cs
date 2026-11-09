namespace PreySense.Core.Services;

using PreySense.Gpu;

public readonly record struct CpuClockSnapshot(int? PerformanceMhz, int? EfficiencyMhz, int? ActiveMhz, int? EffectiveMhz);

public interface ITelemetrySources
{
    CpuTelemetrySnapshot? ReadCpuUsage();
    int? ReadCpuTemperature();
    float? ReadCpuVoltage();
    float? ReadCpuPower();
    (int? Performance, int? Efficiency) ReadCpuClocks();
    (int? ActiveMhz, int? EffectiveMhz) ReadCpuActiveEffectiveClocks() => default;
    CpuClockSnapshot ReadCpuClockSnapshot(TelemetryMetrics requested)
    {
        var clocks = (requested & TelemetryMetrics.CpuClock) != 0 ? ReadCpuClocks() : default;
        var active = ReadCpuActiveEffectiveClocks();
        return new(clocks.Performance, clocks.Efficiency, active.ActiveMhz, active.EffectiveMhz);
    }
    (int? Pl1Watts, int? Pl2Watts) ReadCpuPowerLimits() => default;
    (int? Percent, int? UsedMb) ReadRamUsage();
    int? ReadRamSpeed();
    string? ReadDisplayGpuName() => null;
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
        var cpuLimits = Wants(TelemetryMetrics.CpuPowerLimit) ? sources.ReadCpuPowerLimits() : default;
        var clocks = Wants(TelemetryMetrics.CpuClock | TelemetryMetrics.CpuEffectiveClock)
            ? sources.ReadCpuClockSnapshot(metrics) : default;
        var ram = Wants(TelemetryMetrics.RamUsage) ? sources.ReadRamUsage() : default;
        var ramSpeed = Wants(TelemetryMetrics.RamSpeed) ? sources.ReadRamSpeed() : null;
        var fans = Wants(TelemetryMetrics.CpuFanRpm | TelemetryMetrics.GpuFanRpm)
            ? sources.ReadFanRpms(Wants(TelemetryMetrics.CpuFanRpm), Wants(TelemetryMetrics.GpuFanRpm)) : default;
        var gpu = Wants(TelemetryMetrics.Gpu) ? sources.ReadGpu(metrics & TelemetryMetrics.Gpu) : default;
        string? displayGpu = Wants(TelemetryMetrics.DisplayAdapter) ? sources.ReadDisplayGpuName() : null;
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        return TelemetrySnapshot.Empty with
        {
            TimestampTicks = fanOnly ? 0 : now, ThermalTimestampTicks = now,
            DisplayGpuName = displayGpu,
            IsValid = true, IsFanOnly = fanOnly,
            CpuUsage = cpu?.TotalUsage, CpuMaxCoreUsage = cpu?.MaxCoreUsage,
            CpuPCoreUsage = cpu?.PCoreUsage, CpuECoreUsage = cpu?.ECoreUsage,
            CpuTemp = cpuTemp, CpuRpm = fans.Cpu, GpuRpm = fans.Gpu,
            CpuVoltage = cpuVoltage, CpuPkgPowerWatts = cpuPower,
            CpuPl1Watts = cpuLimits.Pl1Watts, CpuPl2Watts = cpuLimits.Pl2Watts,
            CpuClockMhz = clocks.PerformanceMhz,
            ECoreClockMhz = clocks.EfficiencyMhz,
            CpuActiveFrequencyMhz = clocks.ActiveMhz,
            CpuEffectiveFrequencyMhz = clocks.EffectiveMhz,
            RamUsage = ram.Percent, RamUsedMb = ram.UsedMb, RamSpeedMhz = ramSpeed,
            GpuTemp = gpu.Temperature, GpuHotspotTemp = gpu.HotspotTemperature, GpuMemoryTemp = gpu.MemoryTemperature, GpuUsage = gpu.Usage, GpuClockMhz = gpu.ClockMhz,
            GpuActiveClockMhz = gpu.ActiveClockMhz,
            GpuVoltage = gpu.Voltage, GpuPowerWatts = gpu.PowerWatts, GpuPowerLimitWatts = gpu.PowerLimitWatts,
            VramUsedMb = (int?)gpu.VramUsedMb, VramTotalMb = (int?)gpu.VramTotalMb,
            VramSpeedMhz = gpu.MemoryClockMhz, GpuThrottleReasons = gpu.ThrottleReasons, GpuInUse = gpu.InUse
        };
    }
}
