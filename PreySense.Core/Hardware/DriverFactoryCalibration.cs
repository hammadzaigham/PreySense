using System.Threading;
using PreySense.Core.Configuration;
using PreySense.Core.Driver;

namespace PreySense.Core.Hardware;

/// <summary>Captures mode defaults through driver reads, without writing custom limits.</summary>
public sealed class DriverFactoryCalibration(ICpuDriverBackend backend, IPhysicalMemoryByteAccess memory)
{
    /// <summary>
    /// UI/apply Delta ceiling. Hardware stores CT as a byte (0–255); Acer factory Turbo on PH16-73
    /// uses 55 W. Values above a SKU's VBIOS/TGP ceiling do not raise sustained draw on that machine.
    /// </summary>
    public const int MaxGpuDeltaWatts = 100;
    public const int MaxGpuDynamicBoostWatts = 25;

    /// <summary>Wait after Acer mode switch before reading live MMIO (EC reloads package limits).</summary>
    public const int LiveModeSettleMilliseconds = 2000;

    /// <summary>Test hook: when &gt;= 0, overrides <see cref="LiveModeSettleMilliseconds"/>.</summary>
    internal static int LiveModeSettleOverrideMilliseconds { get; set; } = -1;

    public int? ProchotOffsetCelsius { get; private set; }
    public bool? ProchotOffsetLocked { get; private set; }
    public int? TjMaxCelsius { get; private set; }
    public int? StaticCpuPl1Watts { get; private set; }
    public int? StaticCpuPl2Watts { get; private set; }

    /// <summary>True when capture used ERM4 GPU profile bytes; false for CPU-only live MMIO capture.</summary>
    public bool CapturedGpuProfiles { get; private set; }

    public Dictionary<string, ModeTuningConfig> Capture(
        int? gpuBaselineWatts,
        IAcerPowerModeController? modes = null)
    {
        if (!backend.IsInitialized)
            throw new InvalidOperationException("CPU driver is not ready.");

        CaptureProchotAndStaticCpu();
        bool? cpuBoost = backend.ReadMsr(0x1A0, out ulong miscEnable) ? (miscEnable & (1UL << 38)) == 0 : null;

        Dictionary<string, ModeTuningConfig> captured;
        if (AcerErm4Table.EnsureResolved(memory, out _))
        {
            CapturedGpuProfiles = true;
            captured = CaptureFromErm4(gpuBaselineWatts, cpuBoost);
        }
        else
        {
            if (modes is null)
                throw new InvalidOperationException(
                    "Could not read factory power limits. Acer mode switching is unavailable.");
            CapturedGpuProfiles = false;
            captured = CaptureFromLiveModes(modes, cpuBoost);
        }

        if (!new TuningConfig { FactoryDefaults = captured }.HasFactoryDefaults)
            throw new InvalidOperationException("Factory defaults are incomplete.");
        return captured;
    }

    private void CaptureProchotAndStaticCpu()
    {
        var prochot = new CpuProchotController(backend).Read();
        ProchotOffsetCelsius = prochot?.OffsetCelsius;
        ProchotOffsetLocked = prochot?.Locked;
        TjMaxCelsius = prochot?.TjMaxCelsius;
        var staticLimits = new CpuPowerLimitController(backend).ReadMsrPowerLimits();
        if (!staticLimits.Success || staticLimits.Pl1Watts < 5 || staticLimits.Pl2Watts < staticLimits.Pl1Watts)
            throw new InvalidOperationException("Could not read factory CPU power limits.");
        StaticCpuPl1Watts = staticLimits.Pl1Watts;
        StaticCpuPl2Watts = staticLimits.Pl2Watts;
    }

    private Dictionary<string, ModeTuningConfig> CaptureFromErm4(int? gpuBaselineWatts, bool? cpuBoost)
    {
        var captured = new Dictionary<string, ModeTuningConfig>();
        foreach (var (name, mode) in ModeList)
        {
            ulong address = AcerErm4Table.ProfileAddress(mode);
            // ERM4 @ Mx base: P1, PM, PT, P2, DB, CT, CP (platform ATPP watts at record[6] / address+2).
            byte[] record = new byte[7];
            for (int i = 0; i < record.Length; i++)
                if (!memory.ReadPhysicalByte(address - 4 + (ulong)i, out record[i]))
                    throw new InvalidOperationException($"Could not read factory defaults for {name}.");
            for (int i = 0; i < record.Length; i++)
                if (!memory.ReadPhysicalByte(address - 4 + (ulong)i, out byte verify) || verify != record[i])
                    throw new InvalidOperationException($"{name} changed while reading; try again.");
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
        return captured;
    }

    private Dictionary<string, ModeTuningConfig> CaptureFromLiveModes(
        IAcerPowerModeController modes, bool? cpuBoost)
    {
        if (!backend.TryReadMchbar(out ulong mchbar) || mchbar == 0)
            throw new InvalidOperationException("Could not find CPU power registers.");

        if (!modes.TryGetAcerPowerMode(out byte originalMode) || !ModeList.Any(item => item.Mode == originalMode))
            throw new InvalidOperationException("Could not read the current Acer mode.");

        var cpu = new CpuPowerLimitController(backend);
        if (!backend.ReadMsr(CpuPowerLimitController.MSR_RAPL_POWER_UNIT, out ulong units))
            throw new InvalidOperationException("Could not read CPU power units.");

        var captured = new Dictionary<string, ModeTuningConfig>();
        Exception? captureFailure = null;
        try
        {
            foreach (var (name, mode) in ModeList)
            {
                if (!modes.SetAcerPowerMode(mode))
                    throw new InvalidOperationException($"Could not switch to {name} for capture.");
                WaitForModeSettle();
                if (!modes.TryGetAcerPowerMode(out byte confirmedMode) || confirmedMode != mode)
                    throw new InvalidOperationException($"{name} did not stick after switching; try again.");
                // A write lock does not prevent reading the factory limits.
                if (!cpu.TryReadMmioPowerLimits(mchbar, out uint pl1Mw, out uint pl2Mw, out _, out byte tauEncoded))
                    throw new InvalidOperationException($"Could not read CPU limits for {name}.");
                int pl1 = (int)(pl1Mw / 1000U);
                int pl2 = (int)(pl2Mw / 1000U);
                if (pl1 < 5 || pl2 < pl1 || pl2 > 300)
                    throw new InvalidOperationException($"{name} CPU limits are invalid (PL1={pl1}W, PL2={pl2}W).");
                int tau = (int)Math.Round(CpuPowerLimitController.DecodeTau(tauEncoded, units));
                tau = CpuPowerLimitConfiguration.SnapTauSelection(Math.Clamp(tau, 0, CpuPowerLimitConfiguration.InfiniteTauSliderValue));
                captured[name] = new ModeTuningConfig
                {
                    CpuBoost = cpuBoost,
                    CpuBoostMode = cpuBoost == true ? 1 : cpuBoost == false ? 0 : null,
                    CpuPl1Watts = pl1,
                    CpuPl2Watts = pl2,
                    TauSeconds = tau
                };
            }
        }
        catch (Exception ex)
        {
            captureFailure = ex;
            throw;
        }
        finally
        {
            bool restored = modes.SetAcerPowerMode(originalMode);
            if (restored) WaitForModeSettle();
            if (!restored || !modes.TryGetAcerPowerMode(out byte restoredMode) || restoredMode != originalMode)
                throw new InvalidOperationException("Could not restore the original Acer mode after capture.", captureFailure);
        }

        return captured;
    }

    private static void WaitForModeSettle()
    {
        int settleMilliseconds = LiveModeSettleOverrideMilliseconds >= 0
            ? LiveModeSettleOverrideMilliseconds : LiveModeSettleMilliseconds;
        if (settleMilliseconds > 0) Thread.Sleep(settleMilliseconds);
    }

    private static readonly (string Name, byte Mode)[] ModeList =
    [
        ("Eco", 6), ("Quiet", 0), ("Balanced", 1), ("Performance", 4), ("Turbo", 5)
    ];

    /// <summary>Clamp to 0–100 W in steps of 5 (same as ModeSettingsWindow apply constraints).</summary>
    public static int SnapGpuDelta(int watts) =>
        (int)Math.Round(Math.Clamp(watts, 0, MaxGpuDeltaWatts) / 5.0, MidpointRounding.AwayFromZero) * 5;
    /// <summary>Clamp to 0–25 W in steps of 5 (includes 5 W; same as ModeSettingsWindow).</summary>
    public static int SnapGpuDynamicBoost(int watts) =>
        (int)Math.Round(Math.Clamp(watts, 0, MaxGpuDynamicBoostWatts) / 5.0, MidpointRounding.AwayFromZero) * 5;

    public static bool IsLegalGpuDynamicBoost(int watts) =>
        watts is >= 0 and <= MaxGpuDynamicBoostWatts && watts % 5 == 0;

    /// <summary>Platform power offset (CP): 0–100 W in steps of 5 (hardware byte allows up to 255).</summary>
    public static int SnapPlatformPowerOffset(int watts) =>
        (int)Math.Round(Math.Clamp(watts, 0, 100) / 5.0, MidpointRounding.AwayFromZero) * 5;
}
