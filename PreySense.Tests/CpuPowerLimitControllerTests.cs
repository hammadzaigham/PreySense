using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PreySense.Core.Driver;
using PreySense.Core.Hardware;

namespace PreySense.Tests;

[TestClass]
public class CpuPowerLimitControllerTests
{
    private sealed class MockCpuDriverBackend : ICpuDriverBackend
    {
        public bool IsInitialized { get; set; } = true;
        public Dictionary<uint, ulong> MsrRegisters { get; } = new();
        public Dictionary<ulong, uint> PhysicalDwords { get; } = new();

        public int ReadMsrCallCount { get; private set; }
        public int WriteMsrCallCount { get; private set; }
        public int ReadPhysicalCallCount { get; private set; }
        public int WritePhysicalCallCount { get; private set; }

        public bool FailMsrWrites { get; set; }
        public bool FailPhysicalWrites { get; set; }
        public bool ClampingMSRReadback { get; set; }
        public ulong MsrClampedValue { get; set; }
        public bool ClampingMmioReadback { get; set; }
        public uint MmioClampedValue { get; set; }
        public bool FailRollbackWrite { get; set; }
        public Func<bool>? WriteCallback { get; set; }
        public Func<bool>? WritePhysicalCallback { get; set; }

        public bool ReadMsr(uint msr, out ulong value)
        {
            ReadMsrCallCount++;
            if (!IsInitialized)
            {
                value = 0;
                return false;
            }

            if (ClampingMSRReadback && msr == CpuPowerLimitController.MSR_PKG_POWER_LIMIT)
            {
                value = MsrClampedValue;
                return true;
            }

            return MsrRegisters.TryGetValue(msr, out value);
        }

        public bool WriteMsr(uint msr, ulong value)
        {
            WriteMsrCallCount++;
            if (!IsInitialized) return false;
            if (FailMsrWrites) return false;
            if (FailRollbackWrite) return false;
            if (WriteCallback != null && !WriteCallback()) return false;

            MsrRegisters[msr] = value;
            return true;
        }

        public bool ReadPhysicalDword(ulong physicalAddress, out uint value)
        {
            ReadPhysicalCallCount++;
            if (!IsInitialized)
            {
                value = 0;
                return false;
            }

            if (ClampingMmioReadback)
            {
                value = MmioClampedValue;
                return true;
            }

            return PhysicalDwords.TryGetValue(physicalAddress, out value);
        }

        public bool WritePhysicalDword(ulong physicalAddress, uint value)
        {
            WritePhysicalCallCount++;
            if (!IsInitialized) return false;
            if (FailPhysicalWrites) return false;
            if (FailRollbackWrite) return false;
            if (WritePhysicalCallback != null && !WritePhysicalCallback()) return false;

            PhysicalDwords[physicalAddress] = value;
            return true;
        }

        public void Dispose() { }
    }

    // Helper to create RAPL unit register value
    // Power units: bits [3:0] -> 2^-p Watts (3 = 1/8W = 125mW)
    // Time units: bits [19:16] -> 2^-t Seconds
    private static ulong MakeUnits(int powerUnitExp = 3, int timeUnitExp = 10)
    {
        return ((ulong)((uint)timeUnitExp & 0xFu) << 16) | ((uint)powerUnitExp & 0xFu);
    }

    [TestMethod]
    [DataRow(10, 0.1)]
    [DataRow(10, 1.0)]
    [DataRow(10, 28.0)]
    [DataRow(10, 56.0)]
    [DataRow(10, 128.0)]
    [DataRow(10, 160.0)]
    [DataRow(10, 300.0)]
    [DataRow(9, 0.1)]
    [DataRow(9, 1.0)]
    [DataRow(9, 28.0)]
    [DataRow(9, 56.0)]
    [DataRow(9, 128.0)]
    [DataRow(9, 160.0)]
    [DataRow(9, 300.0)]
    [DataRow(8, 0.1)]
    [DataRow(8, 1.0)]
    [DataRow(8, 28.0)]
    [DataRow(8, 56.0)]
    [DataRow(8, 128.0)]
    [DataRow(8, 160.0)]
    [DataRow(8, 300.0)]
    public void Test_EncodeTau_LogarithmicSearch_AccuracyAcrossTimeUnits(int timeUnitExp, double seconds)
    {
        ulong units = MakeUnits(3, timeUnitExp);
        byte encoded = CpuPowerLimitController.EncodeTau(seconds, units);

        // Ensure 7-bit boundary
        Assert.IsLessThanOrEqualTo(0x7F, encoded, $"Encoded tau must fit in 7 bits (was 0x{encoded:X2})");

        // Verify decoded duration is within logarithmic quantization step
        double decoded = CpuPowerLimitController.DecodeTau(encoded, units);
        
        // For values in valid range, error shouldn't be larger than half the step between adjacent representations
        // Test that no other 7-bit value is closer to target
        double actualError = Math.Abs(decoded - seconds);
        double timeUnit = Math.Pow(2, -timeUnitExp);

        for (int i = 0; i < 128; i++)
        {
            int y = i & 0x1F;
            int f = (i >> 5) & 0x03;
            double testSec = Math.Pow(2, y) * (1.0 + f / 4.0) * timeUnit;
            double testErr = Math.Abs(testSec - seconds);
            Assert.IsLessThanOrEqualTo(testErr + 1e-9,
actualError, $"Tau encoding for {seconds}s (TU={timeUnitExp}) selected 0x{encoded:X2} (err={actualError}), but 0x{i:X2} had lower error {testErr}");
        }

        // Specifically test that 128s, 160s, 300s are NOT arbitrarily pegged to 0x7F if 0x7F is not the best
        // Max representation for TU=10 is 2^31 * 1.75 * 2^-10 = 2^21 * 1.75 = 3,670,016 seconds.
        // So 128s, 160s, 300s should NOT encode to 0x7F!
        Assert.AreNotEqual((byte)0x7F, encoded, $"Duration {seconds}s with TU={timeUnitExp} should not be pegged to 0x7F!");
    }

    [TestMethod]
    [DataRow(10)]
    [DataRow(14)]
    public void Test_EncodeTau_ZeroMapsToShortestNonZeroWindow(int timeUnitExp)
    {
        ulong units = MakeUnits(3, timeUnitExp);
        Assert.AreEqual((byte)0x01, CpuPowerLimitController.EncodeTau(0, units));
        Assert.AreEqual((byte)0x01, CpuPowerLimitConfiguration.EncodeTauSeconds(0));
        Assert.AreEqual("Off", CpuPowerLimitConfiguration.FormatTauSelection(0));
    }

    [TestMethod]
    [DataRow(10, 449.0)]
    [DataRow(10, 450.0)]
    [DataRow(10, 1000.0)]
    [DataRow(9, 449.0)]
    [DataRow(9, 500.0)]
    [DataRow(8, 449.0)]
    [DataRow(8, 999.0)]
    public void Test_EncodeTau_InfiniteSliderValue_MapsTo0x7F(int timeUnitExp, double seconds)
    {
        ulong units = MakeUnits(3, timeUnitExp);
        byte encoded = CpuPowerLimitController.EncodeTau(seconds, units);

        Assert.AreEqual(
            CpuPowerLimitConfiguration.InfiniteTauEncodedValue,
            encoded,
            $"Duration {seconds}s (>= {CpuPowerLimitConfiguration.InfiniteTauSliderValue}) with TU={timeUnitExp} must encode as 0x7F");
    }

    [TestMethod]
    public void Test_TrySetMsrPowerLimits_ValidatesBeforeWrite_RejectsOutOfRange()
    {
        var mock = new MockCpuDriverBackend();
        mock.MsrRegisters[CpuPowerLimitController.MSR_RAPL_POWER_UNIT] = MakeUnits(3, 10); // 1/8W = 0.125W
        mock.MsrRegisters[CpuPowerLimitController.MSR_PKG_POWER_LIMIT] = 0x00000000_00000000UL;

        var controller = new CpuPowerLimitController(mock);

        // Max 15-bit value = 32767 * 0.125W = 4095.875W (~4095W)
        // Requesting 5000W should fail BEFORE any writes
        bool success = controller.TrySetMsrPowerLimits(5000, 100, true, 28, out string status);

        Assert.IsFalse(success);
        Assert.Contains("exceed 15-bit MSR field limits", status, status);
        Assert.AreEqual(0, mock.WriteMsrCallCount, "No MSR write should occur when validation fails");
    }

    [TestMethod]
    public void Test_TrySetMmioPowerLimits_ValidatesBeforeWrite_PreventsTruncation()
    {
        var mock = new MockCpuDriverBackend();
        ulong mchbar = 0xFED10000UL;
        mock.PhysicalDwords[mchbar + CpuPowerLimitController.MMIO_PL1_OFFSET] = 0x00000100;
        mock.PhysicalDwords[mchbar + CpuPowerLimitController.MMIO_PL2_OFFSET] = 0x00000200;

        var controller = new CpuPowerLimitController(mock);

        // In 15 bits: 32767 * 125mW = 4,095,875 mW.
        // If we request 5,000,000 mW (5000W):
        // Old code did: pl1Raw = 5000000 / 125 = 40000 (0x9C40), and then masked (orig & ~0xFFFF) | (pl1Raw & 0x7FFF)
        // which wrote 0x1C40 (7232 units = 904W), silently truncating!
        bool success = controller.TrySetMmioPowerLimits(mchbar, 5000000U, 45000U, 0, out string status);

        Assert.IsFalse(success);
        Assert.Contains("exceed maximum 15-bit range", status, status);
        Assert.AreEqual(0, mock.WritePhysicalCallCount, "No MMIO write should occur when validation fails");
    }

    [TestMethod]
    public void Test_TrySetMsrPowerLimits_PreservesReservedBits()
    {
        var mock = new MockCpuDriverBackend();
        mock.MsrRegisters[CpuPowerLimitController.MSR_RAPL_POWER_UNIT] = MakeUnits(3, 10);
        // Original MSR has bits outside writable mask set: e.g. bit 28, bit 55
        ulong reservedBits = (1UL << 28) | (1UL << 55);
        mock.MsrRegisters[CpuPowerLimitController.MSR_PKG_POWER_LIMIT] = reservedBits;

        var controller = new CpuPowerLimitController(mock);
        bool success = controller.TrySetMsrPowerLimits(45, 65, true, 28, out string status);

        Assert.IsTrue(success, status);
        ulong writtenMsr = mock.MsrRegisters[CpuPowerLimitController.MSR_PKG_POWER_LIMIT];
        Assert.AreEqual(reservedBits, writtenMsr & reservedBits, "Reserved bits must be strictly preserved");
    }

    [TestMethod]
    public void Test_TrySetMmioPowerLimits_PreservesReservedBits()
    {
        var mock = new MockCpuDriverBackend();
        ulong mchbar = 0xFED10000UL;
        // In 0x59A0: bits [31:24] are reserved
        // In 0x59A4: bits [30:17] are reserved
        uint reserved1 = 0xAB000000U;
        uint reserved2 = 0x55500000U;
        mock.PhysicalDwords[mchbar + CpuPowerLimitController.MMIO_PL1_OFFSET] = reserved1;
        mock.PhysicalDwords[mchbar + CpuPowerLimitController.MMIO_PL2_OFFSET] = reserved2;

        var controller = new CpuPowerLimitController(mock);
        bool success = controller.TrySetMmioPowerLimits(mchbar, 45000U, 65000U, 0x22, out string status);

        Assert.IsTrue(success, status);
        uint written1 = mock.PhysicalDwords[mchbar + CpuPowerLimitController.MMIO_PL1_OFFSET];
        uint written2 = mock.PhysicalDwords[mchbar + CpuPowerLimitController.MMIO_PL2_OFFSET];

        Assert.AreEqual(reserved1, written1 & 0xFF000000U, "PL1 reserved bits [31:24] must be preserved");
        Assert.AreEqual(reserved2 & ~CpuPowerLimitController.MMIO_PL_LOCK_BIT, written2 & 0x7FFE0000U, "PL2 reserved bits [30:17] must be preserved");
        Assert.AreEqual(0U, written1 & CpuPowerLimitController.MMIO_CLAMP_BIT, "MMIO must not force-set PL1 clamp");
        Assert.AreEqual(0U, written2 & CpuPowerLimitController.MMIO_CLAMP_BIT, "MMIO must not force-set PL2 clamp");
    }

    [TestMethod]
    public void Test_TrySetMmioPowerLimits_PreservesExistingClampBits()
    {
        var mock = new MockCpuDriverBackend();
        ulong mchbar = 0xFED10000UL;
        uint orig1 = CpuPowerLimitController.MMIO_CLAMP_BIT; // clamp already on
        uint orig2 = CpuPowerLimitController.MMIO_CLAMP_BIT;
        mock.PhysicalDwords[mchbar + CpuPowerLimitController.MMIO_PL1_OFFSET] = orig1;
        mock.PhysicalDwords[mchbar + CpuPowerLimitController.MMIO_PL2_OFFSET] = orig2;

        var controller = new CpuPowerLimitController(mock);
        Assert.IsTrue(controller.TrySetMmioPowerLimits(mchbar, 55000U, 140000U, 0x6F, out string status), status);

        uint written1 = mock.PhysicalDwords[mchbar + CpuPowerLimitController.MMIO_PL1_OFFSET];
        uint written2 = mock.PhysicalDwords[mchbar + CpuPowerLimitController.MMIO_PL2_OFFSET];
        Assert.AreEqual(CpuPowerLimitController.MMIO_CLAMP_BIT, written1 & CpuPowerLimitController.MMIO_CLAMP_BIT);
        Assert.AreEqual(CpuPowerLimitController.MMIO_CLAMP_BIT, written2 & CpuPowerLimitController.MMIO_CLAMP_BIT);
        Assert.AreEqual(55000U / 125U, written1 & CpuPowerLimitController.MMIO_POWER_MASK);
        Assert.AreEqual(140000U / 125U, written2 & CpuPowerLimitController.MMIO_POWER_MASK);
        Assert.AreNotEqual(0U, written1 & CpuPowerLimitController.MMIO_ENABLE_BIT);
        Assert.AreNotEqual(0U, written2 & CpuPowerLimitController.MMIO_ENABLE_BIT);
    }

    [TestMethod]
    public void Test_Rollback_OnReadbackMismatch_VerifiedAndReported()
    {
        var mock = new MockCpuDriverBackend();
        mock.MsrRegisters[CpuPowerLimitController.MSR_RAPL_POWER_UNIT] = MakeUnits(3, 10);
        ulong originalMsr = 0x00000100_00000200UL;
        mock.MsrRegisters[CpuPowerLimitController.MSR_PKG_POWER_LIMIT] = originalMsr;

        // Simulate firmware clamping or rejecting write
        mock.ClampingMSRReadback = true;
        mock.MsrClampedValue = 0x00000050_00000050UL; // Clamped to lower value

        var controller = new CpuPowerLimitController(mock);
        bool success = controller.TrySetMsrPowerLimits(45, 65, true, 28, out string status);

        Assert.IsFalse(success);
        Assert.Contains("Rollback verified successfully", status, status);
    }

    [TestMethod]
    public void Test_Rollback_FailureDistinctlyReported()
    {
        var mock = new MockCpuDriverBackend();
        mock.MsrRegisters[CpuPowerLimitController.MSR_RAPL_POWER_UNIT] = MakeUnits(3, 10);
        mock.MsrRegisters[CpuPowerLimitController.MSR_PKG_POWER_LIMIT] = 0x00000100_00000200UL;

        // First write succeeds, but readback mismatch triggers rollback, during which rollback write fails
        var controller = new CpuPowerLimitController(mock);
        mock.ClampingMSRReadback = true;
        mock.MsrClampedValue = 0x00000050_00000050UL; // Readback mismatch

        // When rollback write is attempted, simulate failure
        // We set FailMsrWrites after initial write or trigger on rollback
        int writeCount = 0;
        // In our mock, let's configure write count check:
        // 1st write = normal write (succeeds)
        // 2nd write = rollback write (fails)
        mock.WriteCallback = () =>
        {
            writeCount++;
            if (writeCount > 1)
            {
                return false;
            }
            return true;
        };

        bool success = controller.TrySetMsrPowerLimits(45, 65, true, 28, out string status);

        Assert.IsFalse(success);
        Assert.IsTrue(status.Contains("CRITICAL") && status.Contains("rollback verification FAILED"), status);
    }

    [TestMethod]
    public void Test_CombinedTransaction_AtomicCoordinatedRollbackOnMmioFailure()
    {
        var mock = new MockCpuDriverBackend();
        ulong mchbar = 0xFED10000UL;
        ulong origMsr = 0x00000100_00000200UL;
        mock.MsrRegisters[CpuPowerLimitController.MSR_RAPL_POWER_UNIT] = MakeUnits(3, 10);
        mock.MsrRegisters[CpuPowerLimitController.MSR_PKG_POWER_LIMIT] = origMsr;

        uint origMmio1 = 0x00000100U;
        uint origMmio2 = 0x00000200U;
        mock.PhysicalDwords[mchbar + CpuPowerLimitController.MMIO_PL1_OFFSET] = origMmio1;
        mock.PhysicalDwords[mchbar + CpuPowerLimitController.MMIO_PL2_OFFSET] = origMmio2;

        // In this test, initial MMIO write fails, triggering rollback.
        int mmioWriteCount = 0;
        mock.WritePhysicalCallback = () =>
        {
            mmioWriteCount++;
            // The 1st physical write fails (target PL1 write)
            if (mmioWriteCount == 1) return false;
            // Rollback writes (and any subsequent) succeed
            return true;
        };

        var controller = new CpuPowerLimitController(mock);
        var result = controller.ExecuteCombinedTransaction(mchbar, 45, 65, true, 28);

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.RollbackAttempted);
        Assert.IsTrue(result.RollbackSucceeded);
        Assert.IsFalse(result.MsrUpdated);
        Assert.IsFalse(result.MmioUpdated);

        // MSR should be restored back to original
        Assert.AreEqual(origMsr, mock.MsrRegisters[CpuPowerLimitController.MSR_PKG_POWER_LIMIT], "MSR must be rolled back when MMIO fails");
    }

    [TestMethod]
    public void Test_CombinedTransaction_SucceedsWhenBothSucceed()
    {
        var mock = new MockCpuDriverBackend();
        ulong mchbar = 0xFED10000UL;
        ulong origMsr = 0x00000100_00000200UL;
        mock.MsrRegisters[CpuPowerLimitController.MSR_RAPL_POWER_UNIT] = MakeUnits(3, 10);
        mock.MsrRegisters[CpuPowerLimitController.MSR_PKG_POWER_LIMIT] = origMsr;

        mock.PhysicalDwords[mchbar + CpuPowerLimitController.MMIO_PL1_OFFSET] = 0x00000100U;
        mock.PhysicalDwords[mchbar + CpuPowerLimitController.MMIO_PL2_OFFSET] = 0x00000200U;

        var controller = new CpuPowerLimitController(mock);
        var result = controller.ExecuteCombinedTransaction(mchbar, 45, 65, true, 28);

        Assert.IsTrue(result.Success, result.Status);
        Assert.IsTrue(result.MsrUpdated);
        Assert.IsTrue(result.MmioUpdated);
        Assert.IsFalse(result.RollbackAttempted);
    }

    [TestMethod]
    public void Test_CombinedTransaction_SkipsMsrWhenAtOrBelowStaticFloors()
    {
        var mock = new MockCpuDriverBackend();
        ulong mchbar = 0xFED10000UL;
        ulong origMsr = 0x00000100_00000200UL;
        mock.MsrRegisters[CpuPowerLimitController.MSR_RAPL_POWER_UNIT] = MakeUnits(3, 10);
        mock.MsrRegisters[CpuPowerLimitController.MSR_PKG_POWER_LIMIT] = origMsr;
        mock.PhysicalDwords[mchbar + CpuPowerLimitController.MMIO_PL1_OFFSET] = 0x00000100U;
        mock.PhysicalDwords[mchbar + CpuPowerLimitController.MMIO_PL2_OFFSET] = 0x00000200U;

        var controller = new CpuPowerLimitController(mock);
        var result = controller.ExecuteCombinedTransaction(mchbar, 45, 65, true, 28, staticPl1FloorWatts: 55, staticPl2FloorWatts: 140);

        Assert.IsTrue(result.Success, result.Status);
        Assert.IsFalse(result.MsrUpdated);
        Assert.IsTrue(result.MmioUpdated);
        Assert.AreEqual(origMsr, mock.MsrRegisters[CpuPowerLimitController.MSR_PKG_POWER_LIMIT]);
        Assert.AreEqual(0, mock.WriteMsrCallCount);
    }
}
