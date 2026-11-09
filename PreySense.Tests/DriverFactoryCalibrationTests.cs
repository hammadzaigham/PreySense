using PreySense.Core.Hardware;

namespace PreySense.Tests;

[TestClass]
[DoNotParallelize]
public class DriverFactoryCalibrationTests
{
    private sealed class Hardware : ICpuDriverBackend, IPhysicalMemoryByteAccess
    {
        public Dictionary<ulong, byte> Memory = new();
        public ulong? FailAddress;
        public bool ProchotLocked;
        public bool IsInitialized => true;
        public Hardware(ulong erm4Base = AcerErm4Table.Phn16s71Base)
        {
            AcerErm4Table.SetBase(erm4Base);
            byte[] signature = "Predator"u8.ToArray();
            for (int i = 0; i < signature.Length; i++) Memory[erm4Base + (ulong)i] = signature[i];
            foreach (byte mode in new byte[] { 6, 0, 1, 4, 5 })
            {
                ulong start = erm4Base + AcerErm4Table.ModeDbOffset(mode) - 4;
                byte[] record = { 35, (byte)(45 + mode), 56, (byte)(80 + mode), 15, 20, 10 };
                for (int i = 0; i < record.Length; i++) Memory[start + (ulong)i] = record[i];
            }
        }
        public bool ReadMsr(uint msr, out ulong value)
        {
            if (msr == 0x1A2)
            {
                value = (100UL << 16) | (7UL << 24) | (ProchotLocked ? 1UL << 31 : 0);
                return true;
            }
            if (msr == CpuPowerLimitController.MSR_RAPL_POWER_UNIT)
            {
                value = 0x000A0E03UL; // 1/8 W power unit
                return true;
            }
            if (msr == CpuPowerLimitController.MSR_PKG_POWER_LIMIT)
            {
                // Static floors: PL1=55W, PL2=140W (raw = watts / 0.125)
                value = 440UL | (1UL << 15) | (1120UL << 32) | (1UL << 47);
                return true;
            }
            value = 0;
            return true;
        }
        public bool ReadPhysicalDword(ulong address, out uint value) => throw new AssertFailedException("Calibration must read stored profiles, not active CPU limits.");
        public bool ReadPhysicalByte(ulong address, out byte value)
        { value = Memory.GetValueOrDefault(address); return address != FailAddress && Memory.ContainsKey(address); }
        public bool WriteMsr(uint msr, ulong value) => throw new AssertFailedException("Calibration must not write CPU limits.");
        public bool WritePhysicalDword(ulong address, uint value) => throw new AssertFailedException("Calibration must not write CPU limits.");
        public bool WritePhysicalByte(ulong address, byte value) => throw new AssertFailedException("Calibration must not write GPU limits.");
        public bool TryReadMchbar(out ulong address) { address = 0; return false; }
        public void Dispose() { }
    }

    private sealed class LiveHardware : ICpuDriverBackend, IPhysicalMemoryByteAccess, IAcerPowerModeController
    {
        public const ulong Mchbar = 0xFED10000UL;
        public byte CurrentMode = 0x01;
        public Dictionary<byte, (int Pl1, int Pl2, byte TauEnc)> ModeLimits = new()
        {
            [6] = (45, 70, 0x6F),
            [0] = (50, 75, 0x6F),
            [1] = (85, 115, 0x6F),
            [4] = (95, 130, 0x6F),
            [5] = (115, 157, 0x6F),
        };

        public bool FailRestore;
        public bool IgnoreFirstSwitch;
        public bool FailAnyRestore;
        public bool LimitsLocked;
        private int _modeSwitchCount;
        public bool IsInitialized => true;
        public bool TryGetAcerPowerMode(out byte mode) { mode = CurrentMode; return true; }
        public bool SetAcerPowerMode(byte mode)
        {
            _modeSwitchCount++;
            if (FailRestore && _modeSwitchCount > 5 || FailAnyRestore && _modeSwitchCount > 1 && mode == 1) return false;
            if (!(IgnoreFirstSwitch && _modeSwitchCount == 1)) CurrentMode = mode;
            return ModeLimits.ContainsKey(mode);
        }
        public bool TryReadMchbar(out ulong address) { address = Mchbar; return true; }
        public bool ReadMsr(uint msr, out ulong value)
        {
            if (msr == 0x1A2) { value = (100UL << 16) | (7UL << 24); return true; }
            if (msr == CpuPowerLimitController.MSR_RAPL_POWER_UNIT) { value = 0x000A0E03UL; return true; }
            if (msr == CpuPowerLimitController.MSR_PKG_POWER_LIMIT)
            {
                value = 440UL | (1UL << 15) | (1120UL << 32) | (1UL << 47);
                return true;
            }
            if (msr == 0x1A0) { value = 0; return true; }
            value = 0;
            return true;
        }
        public bool ReadPhysicalDword(ulong address, out uint value)
        {
            value = 0;
            if (!ModeLimits.TryGetValue(CurrentMode, out var lim)) return false;
            uint pl1Raw = (uint)(lim.Pl1 * 1000 / 125);
            uint pl2Raw = (uint)(lim.Pl2 * 1000 / 125);
            if (address == Mchbar + CpuPowerLimitController.MMIO_PL1_OFFSET)
            {
                value = pl1Raw | CpuPowerLimitController.MMIO_ENABLE_BIT
                    | ((uint)lim.TauEnc << (int)CpuPowerLimitController.MMIO_TIME_SHIFT);
                return true;
            }
            if (address == Mchbar + CpuPowerLimitController.MMIO_PL2_OFFSET)
            {
                value = pl2Raw | CpuPowerLimitController.MMIO_ENABLE_BIT
                    | (LimitsLocked ? CpuPowerLimitController.MMIO_PL_LOCK_BIT : 0);
                return true;
            }
            return false;
        }
        public bool ReadPhysicalByte(ulong address, out byte value) { value = 0; return false; }
        public bool WriteMsr(uint msr, ulong value) => throw new AssertFailedException("live capture must not write MSR");
        public bool WritePhysicalDword(ulong address, uint value) => throw new AssertFailedException("live capture must not write MMIO");
        public bool WritePhysicalByte(ulong address, byte value) => throw new AssertFailedException("live capture must not write bytes");
        public void Dispose() { }
    }

    [TestMethod]
    public void LiveCapture_UsesEncodedZeroTauAndAcceptsReadOnlyLimits()
    {
        AcerErm4Table.Reset();
        int previousSettle = DriverFactoryCalibration.LiveModeSettleOverrideMilliseconds;
        DriverFactoryCalibration.LiveModeSettleOverrideMilliseconds = 0;
        try
        {
            var backend = new LiveHardware { LimitsLocked = true };
            foreach (byte mode in backend.ModeLimits.Keys.ToArray())
            {
                var limits = backend.ModeLimits[mode];
                backend.ModeLimits[mode] = (limits.Pl1, limits.Pl2, 0);
            }
            var profiles = new DriverFactoryCalibration(backend, backend).Capture(null, backend);
            Assert.AreEqual(0, profiles["Turbo"].TauSeconds);
            Assert.AreEqual((byte)1, backend.CurrentMode);
        }
        finally { DriverFactoryCalibration.LiveModeSettleOverrideMilliseconds = previousSettle; }
    }

    [TestMethod]
    public void LiveCapture_RejectsUnverifiedModeSelection()
    {
        AcerErm4Table.Reset();
        int previousSettle = DriverFactoryCalibration.LiveModeSettleOverrideMilliseconds;
        DriverFactoryCalibration.LiveModeSettleOverrideMilliseconds = 0;
        try
        {
            var backend = new LiveHardware { IgnoreFirstSwitch = true };
            var error = Assert.Throws<InvalidOperationException>(() => new DriverFactoryCalibration(backend, backend).Capture(null, backend));
            Assert.Contains("did not stick after switching", error.Message);
            Assert.AreEqual((byte)1, backend.CurrentMode);
        }
        finally { DriverFactoryCalibration.LiveModeSettleOverrideMilliseconds = previousSettle; }
    }

    [TestMethod]
    public void LiveCapture_RejectsUnverifiedOriginalModeRestoration()
    {
        AcerErm4Table.Reset();
        int previousSettle = DriverFactoryCalibration.LiveModeSettleOverrideMilliseconds;
        DriverFactoryCalibration.LiveModeSettleOverrideMilliseconds = 0;
        try
        {
            var backend = new LiveHardware { FailRestore = true };
            var error = Assert.Throws<InvalidOperationException>(() => new DriverFactoryCalibration(backend, backend).Capture(null, backend));
            Assert.Contains("Could not restore the original Acer mode after capture", error.Message);
        }
        finally { DriverFactoryCalibration.LiveModeSettleOverrideMilliseconds = previousSettle; }
    }

    [TestMethod]
    public void LiveCapture_PreservesCaptureFailureWhenRestorationAlsoFails()
    {
        AcerErm4Table.Reset();
        int previousSettle = DriverFactoryCalibration.LiveModeSettleOverrideMilliseconds;
        DriverFactoryCalibration.LiveModeSettleOverrideMilliseconds = 0;
        try
        {
            var backend = new LiveHardware { IgnoreFirstSwitch = true, FailAnyRestore = true };
            var error = Assert.Throws<InvalidOperationException>(() => new DriverFactoryCalibration(backend, backend).Capture(null, backend));
            Assert.Contains("Could not restore the original Acer mode after capture", error.Message);
            Assert.IsNotNull(error.InnerException);
            Assert.Contains("did not stick after switching", error.InnerException.Message);
        }
        finally { DriverFactoryCalibration.LiveModeSettleOverrideMilliseconds = previousSettle; }
    }

    [TestMethod]
    public void MissingGpuBaseline_DoesNotInventNominalWattage()
    {
        var hw = new Hardware();
        var calibration = new DriverFactoryCalibration(hw, hw);
        var profiles = calibration.Capture(null);
        Assert.IsNull(profiles["Turbo"].GpuWatts);
        Assert.AreEqual(20, profiles["Turbo"].GpuDeltaWatts);
        Assert.AreEqual(55, calibration.StaticCpuPl1Watts);
        Assert.AreEqual(140, calibration.StaticCpuPl2Watts);
    }

    [TestMethod]
    public void Capture_ReadsDistinctStoredModesWithoutWritingLimits()
    {
        var hw = new Hardware();
        var result = new DriverFactoryCalibration(hw, hw).Capture(85);
        Assert.HasCount(5, result);
        Assert.AreEqual(50, result["Turbo"].CpuPl1Watts);
        Assert.AreEqual(45, result["Quiet"].CpuPl1Watts);
        Assert.AreEqual(51, result["Eco"].CpuPl1Watts);
        Assert.AreEqual(56, result["Turbo"].TauSeconds);
        Assert.AreEqual(20, result["Turbo"].GpuDeltaWatts);
        Assert.AreEqual(10, result["Turbo"].PlatformPowerOffsetWatts);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Capture_ReadsGlobalProchotOffset(bool locked)
    {
        var hw = new Hardware();
        hw.ProchotLocked = locked;
        var calibration = new DriverFactoryCalibration(hw, hw);
        calibration.Capture(85);
        Assert.AreEqual(7, calibration.ProchotOffsetCelsius);
        Assert.AreEqual(locked, calibration.ProchotOffsetLocked);
    }

    [TestMethod]
    public void MissingSignature_WithoutModeController_RejectsCapture()
    {
        AcerErm4Table.Reset();
        var hw = new Hardware();
        hw.Memory[AcerErm4Table.Phn16s71Base] = 0;
        AcerErm4Table.Reset();
        Assert.Throws<InvalidOperationException>(() => new DriverFactoryCalibration(hw, hw).Capture(85));
    }

    [TestMethod]
    public void MissingErm4_CapturesCpuOnlyFromLiveMmio()
    {
        AcerErm4Table.Reset();
        int previousSettle = DriverFactoryCalibration.LiveModeSettleOverrideMilliseconds;
        DriverFactoryCalibration.LiveModeSettleOverrideMilliseconds = 0;
        try
        {
            var hw = new LiveHardware();
            var calibration = new DriverFactoryCalibration(hw, hw);
            // Force no ERM4 by leaving memory empty (no Predator signature).
            var profiles = calibration.Capture(85, hw);
            Assert.IsFalse(calibration.CapturedGpuProfiles);
            Assert.IsTrue(new PreySense.Core.Configuration.TuningConfig { FactoryDefaults = profiles }.HasFactoryDefaults);
            Assert.IsFalse(new PreySense.Core.Configuration.TuningConfig { FactoryDefaults = profiles }.HasFactoryGpuProfiles);
            Assert.AreEqual(115, profiles["Turbo"].CpuPl1Watts);
            Assert.AreEqual(157, profiles["Turbo"].CpuPl2Watts);
            Assert.AreEqual(85, profiles["Balanced"].CpuPl1Watts);
            Assert.IsNull(profiles["Turbo"].GpuDeltaWatts);
            Assert.IsNull(profiles["Turbo"].GpuWatts);
            Assert.AreEqual(0x01, hw.CurrentMode, "Capture must restore the original Acer mode.");
        }
        finally
        {
            DriverFactoryCalibration.LiveModeSettleOverrideMilliseconds = previousSettle;
            AcerErm4Table.Reset();
        }
    }

    [TestMethod]
    public void Capture_ResolvesAlternateErm4Base()
    {
        AcerErm4Table.Reset();
        var hw = new Hardware(AcerErm4Table.Phn1671Base);
        AcerErm4Table.Reset(); // force discovery from candidates
        var result = new DriverFactoryCalibration(hw, hw).Capture(85);
        Assert.AreEqual(AcerErm4Table.Phn1671Base, AcerErm4Table.ResolvedBase);
        Assert.AreEqual(50, result["Turbo"].CpuPl1Watts);
    }

    [TestMethod]
    public void UnreadableProfile_RejectsCapture()
    {
        var hw = new Hardware { FailAddress = AcerErm4Table.ProfileAddress(5) };
        Assert.Throws<InvalidOperationException>(() => new DriverFactoryCalibration(hw, hw).Capture(85));
    }

    [TestMethod]
    public void Capture_SnapsGpuValuesToApplyLegalSteps()
    {
        var hw = new Hardware();
        foreach (byte mode in new byte[] { 6, 0, 1, 4, 5 })
        {
            ulong start = AcerProfileApplyTransaction.ProfileAddress(mode) - 4;
            hw.Memory[start + 4] = 7;  // dynamic boost → 5
            hw.Memory[start + 5] = 22; // delta → 20
            hw.Memory[start + 6] = 13; // platform offset → 15
        }
        var profiles = new DriverFactoryCalibration(hw, hw).Capture(85);
        Assert.AreEqual(20, profiles["Turbo"].GpuDeltaWatts);
        Assert.AreEqual(5, profiles["Turbo"].GpuDynamicBoostWatts);
        Assert.AreEqual(15, profiles["Turbo"].PlatformPowerOffsetWatts);
        Assert.AreEqual(110, profiles["Turbo"].GpuWatts);
        Assert.AreEqual(20, DriverFactoryCalibration.SnapGpuDelta(22));
        Assert.AreEqual(55, DriverFactoryCalibration.SnapGpuDelta(53));
        Assert.AreEqual(100, DriverFactoryCalibration.SnapGpuDelta(255));
        Assert.AreEqual(0, DriverFactoryCalibration.SnapGpuDynamicBoost(2));
        Assert.AreEqual(5, DriverFactoryCalibration.SnapGpuDynamicBoost(3));
        Assert.AreEqual(5, DriverFactoryCalibration.SnapGpuDynamicBoost(5));
        Assert.AreEqual(5, DriverFactoryCalibration.SnapGpuDynamicBoost(7));
        Assert.AreEqual(25, DriverFactoryCalibration.SnapGpuDynamicBoost(255));
        Assert.IsTrue(DriverFactoryCalibration.IsLegalGpuDynamicBoost(5));
    }
}
