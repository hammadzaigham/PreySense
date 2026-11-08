using System;
using PreySense.Helpers;

namespace PreySense.Core.Hardware;

/// <summary>
/// Controller for CPU power limit operations, supporting bit-level register preservation,
/// hardware lock bit checks (PL_LOCK), strict request validation, and safe rollback on failure.
/// Can operate via any ICpuDriverBackend.
/// </summary>
public sealed class CpuPowerLimitController
{
    // MSR Addresses
    public const uint MSR_PLATFORM_INFO     = 0x0CE;
    public const uint MSR_TURBO_RATIO_LIMIT = 0x1AD;
    public const uint MSR_RAPL_POWER_UNIT   = 0x606;
    public const uint MSR_PKG_POWER_LIMIT   = 0x610;
    public const uint MSR_PKG_ENERGY_STATUS = 0x611;

    // Bit Masks and Constants for MSR 0x610
    public const ulong MSR_610_LOCK_BIT        = 1UL << 63; // PL_LOCK (firmware locked until reset)
    public const ulong MSR_610_WRITABLE_MASK   = 0x0001FFFF00FFFFFFUL;
    public const ulong MSR_610_PL1_POWER_MASK  = 0x7FFFUL;
    public const ulong MSR_610_PL1_ENABLE_BIT  = 1UL << 15;
    public const ulong MSR_610_PL1_CLAMP_BIT   = 1UL << 16;
    public const ulong MSR_610_PL1_TIME_SHIFT  = 17;
    public const ulong MSR_610_PL1_TIME_MASK   = 0x7FUL << 17;
    public const ulong MSR_610_PL2_POWER_SHIFT = 32;
    public const ulong MSR_610_PL2_POWER_MASK  = 0x7FFFUL << 32;
    public const ulong MSR_610_PL2_ENABLE_BIT  = 1UL << 47;
    public const ulong MSR_610_PL2_CLAMP_BIT   = 1UL << 48;
    public const ulong MSR_610_PL2_TIME_SHIFT  = 49;
    public const ulong MSR_610_PL2_TIME_MASK   = 0x7FUL << 49;

    // MMIO Constants (MCHBAR + 0x59A0 / 0x59A4)
    public const uint MMIO_PL1_OFFSET          = 0x59A0;
    public const uint MMIO_PL2_OFFSET          = 0x59A4;
    public const uint MMIO_PL_LOCK_BIT         = 1U << 31; // Bit 31 in 0x59A4
    public const uint MMIO_POWER_MASK          = 0x7FFFU;
    public const uint MMIO_ENABLE_BIT          = 1U << 15;
    public const uint MMIO_CLAMP_BIT           = 1U << 16;
    public const uint MMIO_TIME_SHIFT          = 17;
    public const uint MMIO_TIME_MASK           = 0x7FU << 17;

    // Maximum 15-bit hardware limit (0..32767)
    public const uint MAX_15BIT_RAW_LIMIT      = 0x7FFF;
    public const uint MMIO_STEP_MW             = 125U;
    public const uint MMIO_MAX_MW              = MAX_15BIT_RAW_LIMIT * MMIO_STEP_MW; // 4,095,875 mW (~4095.8W)

    public readonly record struct PowerLimitsSnapshot(
        bool Success,
        int Pl1Watts,
        int Pl2Watts,
        bool Pl1Enabled,
        bool Pl2Enabled,
        bool Pl1Clamped,
        bool Pl2Clamped,
        byte Pl1TauRaw,
        bool MsrLocked,
        ulong RawMsrValue);

    public readonly record struct CombinedTransactionResult(
        bool Success,
        bool MsrUpdated,
        bool MmioUpdated,
        bool RollbackAttempted,
        bool RollbackSucceeded,
        string Status);

    private readonly ICpuDriverBackend _backend;

    public CpuPowerLimitController(ICpuDriverBackend backend)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <summary>
    /// Reads and parses current static CPU power limits from MSR 0x610.
    /// </summary>
    public PowerLimitsSnapshot ReadMsrPowerLimits()
    {
        if (!_backend.IsInitialized)
            return default;

        if (!_backend.ReadMsr(MSR_RAPL_POWER_UNIT, out ulong unitRaw) ||
            !_backend.ReadMsr(MSR_PKG_POWER_LIMIT, out ulong msr610))
        {
            return default;
        }

        double powerUnit = Math.Pow(2, -(int)(unitRaw & 0xF));
        bool isLocked = (msr610 & MSR_610_LOCK_BIT) != 0;

        uint pl1Raw = (uint)(msr610 & MSR_610_PL1_POWER_MASK);
        uint pl2Raw = (uint)((msr610 & MSR_610_PL2_POWER_MASK) >> 32);

        int pl1Watts = (int)Math.Round(pl1Raw * powerUnit);
        int pl2Watts = (int)Math.Round(pl2Raw * powerUnit);

        bool pl1En = (msr610 & MSR_610_PL1_ENABLE_BIT) != 0;
        bool pl1Clamp = (msr610 & MSR_610_PL1_CLAMP_BIT) != 0;
        byte pl1Tau = (byte)((msr610 & MSR_610_PL1_TIME_MASK) >> (int)MSR_610_PL1_TIME_SHIFT);

        bool pl2En = (msr610 & MSR_610_PL2_ENABLE_BIT) != 0;
        bool pl2Clamp = (msr610 & MSR_610_PL2_CLAMP_BIT) != 0;

        return new PowerLimitsSnapshot(
            Success: true,
            Pl1Watts: pl1Watts,
            Pl2Watts: pl2Watts,
            Pl1Enabled: pl1En,
            Pl2Enabled: pl2En,
            Pl1Clamped: pl1Clamp,
            Pl2Clamped: pl2Clamp,
            Pl1TauRaw: pl1Tau,
            MsrLocked: isLocked,
            RawMsrValue: msr610);
    }

    /// <summary>
    /// Applies static power limits to MSR 0x610 with bit preservation, lock verification,
    /// strict input validation, and verified rollback to the original register state if readback verification fails.
    /// </summary>
    public bool TrySetMsrPowerLimits(
        int pl1Watts,
        int pl2Watts,
        bool pl2Enable,
        double tauSeconds,
        out string status)
    {
        status = string.Empty;

        if (!_backend.IsInitialized)
        {
            status = "Driver backend is not initialized.";
            return false;
        }

        // Tau 0 means shortest RAPL window (EncodeTau → 0x01); only reject negatives.
        if (pl1Watts <= 0 || pl2Watts <= 0 || tauSeconds < 0)
        {
            status = $"Invalid power or tau parameters: PL1={pl1Watts}W, PL2={pl2Watts}W, Tau={tauSeconds}s.";
            return false;
        }

        // 1. Read existing MSR values
        if (!_backend.ReadMsr(MSR_RAPL_POWER_UNIT, out ulong units) ||
            !_backend.ReadMsr(MSR_PKG_POWER_LIMIT, out ulong originalMsr))
        {
            status = "Failed to read MSR 0x606 or 0x610.";
            return false;
        }

        // 2. Check PL_LOCK bit 63
        if ((originalMsr & MSR_610_LOCK_BIT) != 0)
        {
            status = "MSR 0x610 is locked by BIOS firmware (bit 63 PL_LOCK set). MSR writes cannot take effect until reboot/unlock.";
            return false;
        }

        // 3. Compute power units & validate before write (do not mask or truncate)
        double powerUnit = Math.Pow(2, -(int)(units & 0xF));
        double pl1RawExact = pl1Watts / powerUnit;
        double pl2RawExact = pl2Watts / powerUnit;
        long pl1Raw = (long)Math.Round(pl1RawExact);
        long pl2Raw = (long)Math.Round(pl2RawExact);

        if (pl1Raw < 0 || pl1Raw > MAX_15BIT_RAW_LIMIT || pl2Raw < 0 || pl2Raw > MAX_15BIT_RAW_LIMIT)
        {
            status = $"Requested limits (PL1={pl1Watts}W [raw={pl1Raw}], PL2={pl2Watts}W [raw={pl2Raw}]) exceed 15-bit MSR field limits (max {MAX_15BIT_RAW_LIMIT}).";
            return false;
        }

        byte tau = EncodeTau(tauSeconds, units);

        // 4. Preserve reserved bits and clamp; only rewrite power / enable / tau.
        ulong targetMsr = originalMsr & ~MSR_610_WRITABLE_MASK;
        ulong pl1Clamp = originalMsr & MSR_610_PL1_CLAMP_BIT;
        ulong pl2Clamp = originalMsr & MSR_610_PL2_CLAMP_BIT;

        // PL1: bits [14:0]=Power, bit 15=Enable, bit 16=Clamp (unchanged), bits [23:17]=TimeWindow
        targetMsr |= ((ulong)pl1Raw & MSR_610_PL1_POWER_MASK)
                  | MSR_610_PL1_ENABLE_BIT
                  | pl1Clamp
                  | (((ulong)tau & 0x7FUL) << (int)MSR_610_PL1_TIME_SHIFT);

        // PL2: bits [46:32]=Power, bit 47=Enable, bit 48=Clamp (unchanged)
        targetMsr |= (((ulong)pl2Raw & 0x7FFFUL) << (int)MSR_610_PL2_POWER_SHIFT) | pl2Clamp;
        if (pl2Enable)
            targetMsr |= MSR_610_PL2_ENABLE_BIT;

        // 5. Perform the write
        if (!_backend.WriteMsr(MSR_PKG_POWER_LIMIT, targetMsr))
        {
            status = "Driver write to MSR 0x610 failed.";
            return false;
        }

        // 6. Verify via immediate readback
        if (!_backend.ReadMsr(MSR_PKG_POWER_LIMIT, out ulong readback))
        {
            bool rbOk = RollbackMsrVerified(originalMsr);
            status = rbOk
                ? "Failed to read back MSR 0x610 after write. Rollback verified successfully."
                : "CRITICAL: Failed to read back MSR 0x610 after write and rollback verification FAILED.";
            return false;
        }

        bool matched = (readback & MSR_610_WRITABLE_MASK) == (targetMsr & MSR_610_WRITABLE_MASK);
        if (!matched)
        {
            bool rbOk = RollbackMsrVerified(originalMsr);
            status = rbOk
                ? $"MSR 0x610 readback mismatch (Wrote: 0x{targetMsr:X16}, Read: 0x{readback:X16}). Firmware may have rejected or clamped values. Rollback verified successfully."
                : $"CRITICAL: MSR 0x610 readback mismatch (Wrote: 0x{targetMsr:X16}, Read: 0x{readback:X16}) and rollback verification FAILED.";
            return false;
        }

        status = $"MSR 0x610 limits verified: PL1={pl1Watts}W, PL2={pl2Watts}W (Tau={tauSeconds}s/0x{tau:X2}).";
        return true;
    }

    /// <summary>
    /// Reads MMIO power limits directly from MCHBAR space if MCHBAR base address is provided.
    /// </summary>
    public bool TryReadMmioPowerLimits(ulong mchbarBase, out uint pl1Mw, out uint pl2Mw, out bool isLocked)
    {
        pl1Mw = 0;
        pl2Mw = 0;
        isLocked = false;

        if (mchbarBase == 0 || !_backend.IsInitialized)
            return false;

        if (!_backend.ReadPhysicalDword(mchbarBase + MMIO_PL1_OFFSET, out uint reg1) ||
            !_backend.ReadPhysicalDword(mchbarBase + MMIO_PL2_OFFSET, out uint reg2))
        {
            return false;
        }

        isLocked = (reg2 & MMIO_PL_LOCK_BIT) != 0;

        // Units in MCHBAR: 1 unit = 1/8 Watt = 125 mW
        uint pl1Raw = reg1 & MMIO_POWER_MASK;
        uint pl2Raw = reg2 & MMIO_POWER_MASK;

        pl1Mw = pl1Raw * MMIO_STEP_MW;
        pl2Mw = pl2Raw * MMIO_STEP_MW;
        return true;
    }

    /// <summary>
    /// Writes package power limits through the Acer CPWR MMIO layout (MCHBAR+0x59A0/0x59A4),
    /// matching the former ACPI PREYDEV method: PPL = mW/125, set PL1E/PL2E, optional PL1T,
    /// preserve clamp/lock/reserved bits, then verify readback.
    /// </summary>
    public bool TrySetMmioPowerLimits(ulong mchbarBase, uint pl1Mw, uint pl2Mw, byte tauEncoded, out string status)
    {
        status = string.Empty;

        if (mchbarBase == 0 || !_backend.IsInitialized)
        {
            status = "Invalid MCHBAR base or backend not initialized.";
            return false;
        }

        // ACPI CPWR units: 1 register unit = 125 mW (1/8 W). Same as PREYDEV LocalN / 0x7D.
        if (pl1Mw == 0 || pl2Mw == 0)
        {
            status = $"Invalid MMIO power limits (PL1={pl1Mw}mW, PL2={pl2Mw}mW). Limits must be greater than 0.";
            return false;
        }

        if (pl1Mw > MMIO_MAX_MW || pl2Mw > MMIO_MAX_MW)
        {
            status = $"Requested MMIO limits (PL1={pl1Mw}mW, PL2={pl2Mw}mW) exceed maximum 15-bit range ({MMIO_MAX_MW}mW).";
            return false;
        }

        uint pl1Raw = pl1Mw / MMIO_STEP_MW;
        uint pl2Raw = pl2Mw / MMIO_STEP_MW;

        if (pl1Raw > MAX_15BIT_RAW_LIMIT || pl2Raw > MAX_15BIT_RAW_LIMIT)
        {
            status = $"Calculated MMIO raw units exceed 15-bit field (PL1 raw={pl1Raw}, PL2 raw={pl2Raw}).";
            return false;
        }

        if (!_backend.ReadPhysicalDword(mchbarBase + MMIO_PL1_OFFSET, out uint orig1) ||
            !_backend.ReadPhysicalDword(mchbarBase + MMIO_PL2_OFFSET, out uint orig2))
        {
            status = "Failed to read MMIO registers at MCHBAR + 0x59A0/0x59A4.";
            return false;
        }

        if ((orig2 & MMIO_PL_LOCK_BIT) != 0)
        {
            status = "MMIO power limits are hardware-locked (bit 31 at MCHBAR+0x59A4). MMIO writes blocked.";
            return false;
        }

        // Acer CPWR Field at GMHB()+0x5000:
        //   0x9A0: PPL1 15, PL1E 1, CLP1 1, PL1T 7
        //   0x9A4: PPL2 15, PL2E 1, CLP2 1, PL2T 7
        // PREYDEV set enable + power (+ PL1T); clamp bits were left alone.
        uint new1 = orig1 & ~((MMIO_POWER_MASK) | MMIO_ENABLE_BIT | MMIO_TIME_MASK);
        new1 |= (pl1Raw & MMIO_POWER_MASK) | MMIO_ENABLE_BIT;
        if (tauEncoded != 0)
            new1 |= ((uint)tauEncoded & 0x7FU) << (int)MMIO_TIME_SHIFT;
        else
            new1 |= orig1 & MMIO_TIME_MASK;

        uint new2 = orig2 & ~(MMIO_POWER_MASK | MMIO_ENABLE_BIT);
        new2 |= (pl2Raw & MMIO_POWER_MASK) | MMIO_ENABLE_BIT;

        if (!_backend.WritePhysicalDword(mchbarBase + MMIO_PL1_OFFSET, new1) ||
            !_backend.WritePhysicalDword(mchbarBase + MMIO_PL2_OFFSET, new2))
        {
            bool rbOk = RollbackMmioVerified(mchbarBase, orig1, orig2);
            status = rbOk
                ? "Failed to write MMIO registers. Rollback verified successfully."
                : "CRITICAL: Failed to write MMIO registers and rollback verification FAILED.";
            return false;
        }

        if (!_backend.ReadPhysicalDword(mchbarBase + MMIO_PL1_OFFSET, out uint verify1) ||
            !_backend.ReadPhysicalDword(mchbarBase + MMIO_PL2_OFFSET, out uint verify2))
        {
            bool rbOk = RollbackMmioVerified(mchbarBase, orig1, orig2);
            status = rbOk
                ? "Failed to verify MMIO registers after write. Rollback verified successfully."
                : "CRITICAL: Failed to verify MMIO registers after write and rollback verification FAILED.";
            return false;
        }

        bool match1 = (verify1 & MMIO_POWER_MASK) == pl1Raw
            && (verify1 & MMIO_ENABLE_BIT) != 0
            && (tauEncoded == 0 || ((verify1 & MMIO_TIME_MASK) >> (int)MMIO_TIME_SHIFT) == (tauEncoded & 0x7FU));
        bool match2 = (verify2 & MMIO_POWER_MASK) == pl2Raw
            && (verify2 & MMIO_ENABLE_BIT) != 0;
        if (!match1 || !match2)
        {
            bool rbOk = RollbackMmioVerified(mchbarBase, orig1, orig2);
            status = rbOk
                ? $"MMIO readback differed from ACPI CPWR targets (PL1 read=0x{verify1:X8}, PL2 read=0x{verify2:X8}). Rollback verified successfully."
                : "CRITICAL: MMIO readback differed from target values and rollback verification FAILED.";
            return false;
        }

        status = $"ACPI CPWR MMIO verified: PL1={pl1Mw / 1000}W, PL2={pl2Mw / 1000}W.";
        return true;
    }

    /// <summary>
    /// Executes a combined atomic transaction applying MSR and/or MMIO power limits
    /// with coordinated rollback if either fails.
    /// When <paramref name="staticPl1FloorWatts"/> / <paramref name="staticPl2FloorWatts"/> are set
    /// and both requested limits stay at or below those calibrated MSR floors, only MMIO is written.
    /// </summary>
    public CombinedTransactionResult ExecuteCombinedTransaction(
        ulong mchbarBase,
        int pl1Watts,
        int pl2Watts,
        bool pl2Enable,
        double tauSeconds,
        int? staticPl1FloorWatts = null,
        int? staticPl2FloorWatts = null)
    {
        if (!_backend.IsInitialized)
        {
            return new CombinedTransactionResult(
                Success: false,
                MsrUpdated: false,
                MmioUpdated: false,
                RollbackAttempted: false,
                RollbackSucceeded: true,
                Status: "Backend is not initialized.");
        }

        // Tau 0 means shortest RAPL window (EncodeTau → 0x01); only reject negatives.
        if (pl1Watts <= 0 || pl2Watts <= 0 || tauSeconds < 0)
        {
            return new CombinedTransactionResult(
                Success: false,
                MsrUpdated: false,
                MmioUpdated: false,
                RollbackAttempted: false,
                RollbackSucceeded: true,
                Status: $"Invalid parameters: PL1={pl1Watts}W, PL2={pl2Watts}W, Tau={tauSeconds}s.");
        }

        // Validate MMIO range before any action
        uint pl1Mw = (uint)pl1Watts * 1000U;
        uint pl2Mw = (uint)pl2Watts * 1000U;
        if (pl1Mw > MMIO_MAX_MW || pl2Mw > MMIO_MAX_MW)
        {
            return new CombinedTransactionResult(
                Success: false,
                MsrUpdated: false,
                MmioUpdated: false,
                RollbackAttempted: false,
                RollbackSucceeded: true,
                Status: $"Requested limits exceed maximum 15-bit hardware range ({MMIO_MAX_MW}mW).");
        }

        bool skipMsr = staticPl1FloorWatts is int floorPl1
            && staticPl2FloorWatts is int floorPl2
            && pl1Watts <= floorPl1
            && pl2Watts <= floorPl2;

        // 1. Snapshot original MSR state (units always needed for Tau encoding)
        if (!_backend.ReadMsr(MSR_RAPL_POWER_UNIT, out ulong units) ||
            !_backend.ReadMsr(MSR_PKG_POWER_LIMIT, out ulong origMsr))
        {
            return new CombinedTransactionResult(
                Success: false,
                MsrUpdated: false,
                MmioUpdated: false,
                RollbackAttempted: false,
                RollbackSucceeded: true,
                Status: "Failed to read initial MSR state.");
        }

        if (!skipMsr && (origMsr & MSR_610_LOCK_BIT) != 0)
        {
            return new CombinedTransactionResult(
                Success: false,
                MsrUpdated: false,
                MmioUpdated: false,
                RollbackAttempted: false,
                RollbackSucceeded: true,
                Status: "MSR 0x610 is locked by BIOS (bit 63). Transaction aborted.");
        }

        // 2. Snapshot original MMIO state
        bool mmioRequested = mchbarBase != 0;
        if (skipMsr && !mmioRequested)
        {
            return new CombinedTransactionResult(
                Success: false,
                MsrUpdated: false,
                MmioUpdated: false,
                RollbackAttempted: false,
                RollbackSucceeded: true,
                Status: "Static MSR floors skip MSR writes, but MCHBAR/MMIO is unavailable.");
        }

        uint origMmio1 = 0;
        uint origMmio2 = 0;

        if (mmioRequested)
        {
            if (!_backend.ReadPhysicalDword(mchbarBase + MMIO_PL1_OFFSET, out origMmio1) ||
                !_backend.ReadPhysicalDword(mchbarBase + MMIO_PL2_OFFSET, out origMmio2))
            {
                return new CombinedTransactionResult(
                    Success: false,
                    MsrUpdated: false,
                    MmioUpdated: false,
                    RollbackAttempted: false,
                    RollbackSucceeded: true,
                    Status: "Failed to read initial MMIO registers.");
            }

            if ((origMmio2 & MMIO_PL_LOCK_BIT) != 0)
            {
                return new CombinedTransactionResult(
                    Success: false,
                    MsrUpdated: false,
                    MmioUpdated: false,
                    RollbackAttempted: false,
                    RollbackSucceeded: true,
                    Status: "MMIO registers are locked (bit 31). Transaction aborted.");
            }
        }

        byte tauEncoded = EncodeTau(tauSeconds, units);

        // 3. Apply MSR power limits when raising above calibrated static floors
        if (!skipMsr)
        {
            if (!TrySetMsrPowerLimits(pl1Watts, pl2Watts, pl2Enable, tauSeconds, out string msrStatus))
            {
                return new CombinedTransactionResult(
                    Success: false,
                    MsrUpdated: false,
                    MmioUpdated: false,
                    RollbackAttempted: true,
                    RollbackSucceeded: msrStatus.Contains("Rollback verified successfully"),
                    Status: $"MSR update failed: {msrStatus}");
            }
        }

        // 4. If MMIO is configured, apply MMIO power limits
        if (mmioRequested)
        {
            if (!TrySetMmioPowerLimits(mchbarBase, pl1Mw, pl2Mw, tauEncoded, out string mmioStatus))
            {
                // MMIO failed -> coordinated rollback: restore MSR when it was written
                bool msrRollbackOk = skipMsr || RollbackMsrVerified(origMsr);
                bool mmioRollbackOk = mmioStatus.Contains("Rollback verified successfully")
                    || mmioStatus.Contains("Failed to write MMIO registers. Rollback verified successfully.")
                    || !mmioStatus.Contains("CRITICAL");
                bool totalRollbackOk = msrRollbackOk && mmioRollbackOk;

                return new CombinedTransactionResult(
                    Success: false,
                    MsrUpdated: false,
                    MmioUpdated: false,
                    RollbackAttempted: true,
                    RollbackSucceeded: totalRollbackOk,
                    Status: totalRollbackOk
                        ? $"MMIO update failed ({mmioStatus}). Coordinated rollback verified successfully."
                        : $"CRITICAL: MMIO update failed ({mmioStatus}). Coordinated rollback FAILED (MSR rb: {msrRollbackOk}, MMIO rb: {mmioRollbackOk}).");
            }
        }

        return new CombinedTransactionResult(
            Success: true,
            MsrUpdated: !skipMsr,
            MmioUpdated: mmioRequested,
            RollbackAttempted: false,
            RollbackSucceeded: true,
            Status: skipMsr
                ? $"MMIO limits applied (MSR unchanged at/below static floors): PL1={pl1Watts}W, PL2={pl2Watts}W, Tau={tauSeconds}s."
                : mmioRequested
                    ? $"Combined MSR and MMIO limits applied and verified: PL1={pl1Watts}W, PL2={pl2Watts}W, Tau={tauSeconds}s."
                    : $"MSR limits applied and verified: PL1={pl1Watts}W, PL2={pl2Watts}W, Tau={tauSeconds}s (MMIO bypassed).");
    }

    /// <summary>
    /// Writes back the original MSR value and verifies via readback.
    /// </summary>
    public bool RollbackMsrVerified(ulong originalMsr)
    {
        if (!_backend.WriteMsr(MSR_PKG_POWER_LIMIT, originalMsr))
            return false;

        if (!_backend.ReadMsr(MSR_PKG_POWER_LIMIT, out ulong readback))
            return false;

        return (readback & MSR_610_WRITABLE_MASK) == (originalMsr & MSR_610_WRITABLE_MASK);
    }

    /// <summary>
    /// Writes back original MMIO values and verifies via readback.
    /// </summary>
    public bool RollbackMmioVerified(ulong mchbarBase, uint orig1, uint orig2)
    {
        bool w1 = _backend.WritePhysicalDword(mchbarBase + MMIO_PL1_OFFSET, orig1);
        bool w2 = _backend.WritePhysicalDword(mchbarBase + MMIO_PL2_OFFSET, orig2);
        if (!w1 || !w2) return false;

        if (!_backend.ReadPhysicalDword(mchbarBase + MMIO_PL1_OFFSET, out uint read1) ||
            !_backend.ReadPhysicalDword(mchbarBase + MMIO_PL2_OFFSET, out uint read2))
        {
            return false;
        }

        return read1 == orig1 && read2 == orig2;
    }

    /// <summary>
    /// Encodes a time window in seconds into RAPL logarithmic format using MSR_RAPL_POWER_UNIT.
    /// Form: Time = 2^Y * (1 + F/4) * 2^(-Time_Units)
    /// where Y = bits [4:0] (0..31), F = bits [6:5] (0..3).
    /// </summary>
    public static byte EncodeTau(double seconds, ulong units)
    {
        // Encoded 0 leaves the prior RAPL time window unchanged on this platform.
        // Slider "Off" (0) means the shortest programmable window (encoded 0x01).
        if (seconds <= 0)
            return 0x01;
        if (seconds >= PreySense.Core.Driver.CpuPowerLimitConfiguration.InfiniteTauSliderValue)
            return PreySense.Core.Driver.CpuPowerLimitConfiguration.InfiniteTauEncodedValue;

        int timeUnitExp = (int)((units >> 16) & 0xF);
        double timeUnit = Math.Pow(2, -timeUnitExp);

        double bestError = double.MaxValue;
        byte best = 0x01;

        // 7-bit field: Y is bits [4:0] (0..31), F is bits [6:5] (0..3)
        // Skip encoded 0 — it is a no-op; search 1..127 only.
        for (int encoded = 1; encoded < 128; encoded++)
        {
            int y = encoded & 0x1F;
            int f = (encoded >> 5) & 0x03;

            double actualSeconds = Math.Pow(2, y) * (1.0 + f / 4.0) * timeUnit;
            double error = Math.Abs(actualSeconds - seconds);

            if (error < bestError)
            {
                bestError = error;
                best = (byte)encoded;
            }
        }

        return best;
    }

    /// <summary>
    /// Decodes a 7-bit RAPL time window value into seconds using MSR_RAPL_POWER_UNIT.
    /// </summary>
    public static double DecodeTau(byte encodedTau, ulong units)
    {
        int timeUnitExp = (int)((units >> 16) & 0xF);
        double timeUnit = Math.Pow(2, -timeUnitExp);

        int y = encodedTau & 0x1F;
        int f = (encodedTau >> 5) & 0x03;

        return Math.Pow(2, y) * (1.0 + f / 4.0) * timeUnit;
    }
}

