using PreySense.Core.Configuration;

namespace PreySense.Core.Hardware;

/// <summary>Captures mode defaults through driver reads, without writing custom limits.</summary>
public sealed class DriverFactoryCalibration(ICpuDriverBackend backend, IPhysicalMemoryByteAccess memory)
{
    public int? ProchotOffsetCelsius { get; private set; }
    public bool? ProchotOffsetLocked { get; private set; }

    public Dictionary<string, ModeTuningConfig> Capture(int? gpuBaselineWatts)
    {
        if (!backend.IsInitialized)
            throw new InvalidOperationException("The CPU driver is unavailable; calibration was not started.");
        if (!AcerErm4Table.EnsureResolved(memory, out _))
            throw new InvalidOperationException("The Acer hardware profile table could not be identified; defaults were not saved.");
        var prochot = new CpuProchotController(backend).Read();
        ProchotOffsetCelsius = prochot?.OffsetCelsius;
        ProchotOffsetLocked = prochot?.Locked;
        bool? cpuBoost = backend.ReadMsr(0x1A0, out ulong miscEnable) ? (miscEnable & (1UL << 38)) == 0 : null;
        var captured = new Dictionary<string, ModeTuningConfig>();
        foreach (var (name, mode) in new (string, byte)[] { ("Eco", 6), ("Quiet", 0), ("Balanced", 1), ("Performance", 4), ("Turbo", 5) })
        {
            ulong address = AcerErm4Table.ProfileAddress(mode);
            // ERM4 @ Mx base: P1, PM, PT, P2, DB, CT, CP (platform ATPP watts at record[6] / address+2).
            byte[] record = new byte[7];
            for (int i = 0; i < record.Length; i++)
                if (!memory.ReadPhysicalByte(address - 4 + (ulong)i, out record[i]))
                    throw new InvalidOperationException($"Hardware defaults could not be read for {name}.");
            for (int i = 0; i < record.Length; i++)
                if (!memory.ReadPhysicalByte(address - 4 + (ulong)i, out byte verify) || verify != record[i])
                    throw new InvalidOperationException($"The {name} profile changed during capture; defaults were not saved.");
            int gpuDelta = SnapGpuDelta(record[5]);
            int gpuDynamicBoost = SnapGpuDynamicBoost(record[4]);
            int platformOffset = SnapPlatformPowerOffset(record[6]);
            captured[name] = new ModeTuningConfig
            {
                CpuBoost = cpuBoost,
                CpuBoostMode = cpuBoost == true ? 1 : cpuBoost == false ? 0 : null,
                CpuPl1Watts = record[1], CpuPl2Watts = record[3], TauSeconds = record[2],
                GpuDeltaWatts = gpuDelta, GpuDynamicBoostWatts = gpuDynamicBoost,
                PlatformPowerOffsetWatts = platformOffset,
                GpuWatts = gpuBaselineWatts.HasValue ? gpuBaselineWatts.Value + gpuDelta + gpuDynamicBoost : null
            };
        }
        if (!new TuningConfig { FactoryDefaults = captured }.HasFactoryDefaults)
            throw new InvalidOperationException("Calibration returned incomplete or invalid defaults.");
        return captured;
    }

    /// <summary>Clamp to 0–50 W in steps of 5 (same as ModeSettingsWindow apply constraints).</summary>
    public static int SnapGpuDelta(int watts) =>
        (int)Math.Round(Math.Clamp(watts, 0, 50) / 5.0, MidpointRounding.AwayFromZero) * 5;

    /// <summary>Map to {0,10,15,20,25} (same as ModeSettingsWindow.SnapGpuDynamicBoost).</summary>
    public static int SnapGpuDynamicBoost(int watts) =>
        watts < 5 ? 0 : Math.Clamp((int)Math.Round(watts / 5.0, MidpointRounding.AwayFromZero) * 5, 10, 25);

    /// <summary>Platform power offset (CP): 0–100 W in steps of 5 (hardware byte allows up to 255).</summary>
    public static int SnapPlatformPowerOffset(int watts) =>
        (int)Math.Round(Math.Clamp(watts, 0, 100) / 5.0, MidpointRounding.AwayFromZero) * 5;
}
