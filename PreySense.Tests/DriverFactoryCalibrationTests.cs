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
        public void Dispose() { }
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
    public void MissingSignature_RejectsCapture()
    {
        AcerErm4Table.Reset();
        var hw = new Hardware();
        hw.Memory[AcerErm4Table.Phn16s71Base] = 0;
        AcerErm4Table.Reset();
        Assert.Throws<InvalidOperationException>(() => new DriverFactoryCalibration(hw, hw).Capture(85));
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
