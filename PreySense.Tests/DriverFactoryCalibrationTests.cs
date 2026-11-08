using PreySense.Core.Hardware;

namespace PreySense.Tests;

[TestClass]
public class DriverFactoryCalibrationTests
{
    private sealed class Hardware : ICpuDriverBackend, IPhysicalMemoryByteAccess
    {
        public Dictionary<ulong, byte> Memory = new();
        public ulong? FailAddress;
        public bool ProchotLocked;
        public bool IsInitialized => true;
        public Hardware()
        {
            byte[] signature = "Predator"u8.ToArray();
            for (int i = 0; i < signature.Length; i++) Memory[0xFE800400UL + (ulong)i] = signature[i];
            foreach (byte mode in new byte[] { 6, 0, 1, 4, 5 })
            {
                ulong start = AcerProfileApplyTransaction.ProfileAddress(mode) - 4;
                byte[] record = { 35, (byte)(45 + mode), 56, (byte)(80 + mode), 15, 20, 10 };
                for (int i = 0; i < record.Length; i++) Memory[start + (ulong)i] = record[i];
            }
        }
        public bool ReadMsr(uint msr, out ulong value)
        { value = msr == 0x1A2 ? (100UL << 16) | (7UL << 24) | (ProchotLocked ? 1UL << 31 : 0) : 0; return true; }
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
        var profiles = new DriverFactoryCalibration(hw, hw).Capture(null);
        Assert.IsNull(profiles["Turbo"].GpuWatts);
        Assert.AreEqual(20, profiles["Turbo"].GpuDeltaWatts);
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
        var hw = new Hardware();
        hw.Memory[0xFE800400] = 0;
        Assert.Throws<InvalidOperationException>(() => new DriverFactoryCalibration(hw, hw).Capture(85));
    }

    [TestMethod]
    public void UnreadableProfile_RejectsCapture()
    {
        var hw = new Hardware { FailAddress = AcerProfileApplyTransaction.ProfileAddress(5) };
        Assert.Throws<InvalidOperationException>(() => new DriverFactoryCalibration(hw, hw).Capture(85));
    }

    [TestMethod]
    public void Capture_SnapsGpuValuesToApplyLegalSteps()
    {
        var hw = new Hardware();
        foreach (byte mode in new byte[] { 6, 0, 1, 4, 5 })
        {
            ulong start = AcerProfileApplyTransaction.ProfileAddress(mode) - 4;
            hw.Memory[start + 4] = 7;  // dynamic boost → 10
            hw.Memory[start + 5] = 22; // delta → 20
            hw.Memory[start + 6] = 13; // platform offset → 15
        }
        var profiles = new DriverFactoryCalibration(hw, hw).Capture(85);
        Assert.AreEqual(20, profiles["Turbo"].GpuDeltaWatts);
        Assert.AreEqual(10, profiles["Turbo"].GpuDynamicBoostWatts);
        Assert.AreEqual(15, profiles["Turbo"].PlatformPowerOffsetWatts);
        Assert.AreEqual(115, profiles["Turbo"].GpuWatts);
        Assert.AreEqual(20, DriverFactoryCalibration.SnapGpuDelta(22));
        Assert.AreEqual(50, DriverFactoryCalibration.SnapGpuDelta(53));
        Assert.AreEqual(0, DriverFactoryCalibration.SnapGpuDynamicBoost(3));
        Assert.AreEqual(10, DriverFactoryCalibration.SnapGpuDynamicBoost(7));
        Assert.AreEqual(25, DriverFactoryCalibration.SnapGpuDynamicBoost(255));
    }
}
