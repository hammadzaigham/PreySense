using PreySense.Core.Hardware;

namespace PreySense.Tests;

[TestClass]
public class CpuProchotControllerTests
{
    private sealed class Backend : ICpuDriverBackend
    {
        public bool IsInitialized => true;
        public ulong Target = (1UL << 45) | (100UL << 16) | (5UL << 24) | 0x1234;
        public bool Supported = true, MismatchFirstWrite;
        public List<ulong> Writes = new();
        public bool ReadMsr(uint address, out ulong value)
        {
            value = address == CpuProchotController.PlatformInfoMsr ? (Supported ? 1UL << 30 : 0) : Target;
            return true;
        }
        public bool WriteMsr(uint address, ulong value)
        {
            Assert.AreEqual(CpuProchotController.TemperatureTargetMsr, address);
            Writes.Add(value);
            Target = value;
            if (MismatchFirstWrite && Writes.Count == 1)
                Target = (value & ~CpuProchotController.OffsetMask) | (11UL << 24);
            return true;
        }
        public bool ReadPhysicalDword(ulong address, out uint value) => throw new AssertFailedException("PROCHOT must not read power-limit MMIO.");
        public bool WritePhysicalDword(ulong address, uint value) => throw new AssertFailedException("PROCHOT must not write power-limit MMIO.");
        public void Dispose() { }
    }

    [TestMethod]
    public void WriteChangesOnlyOffsetBitsAndVerifiesActivationTemperature()
    {
        var backend = new Backend();
        ulong original = backend.Target;
        var control = new CpuProchotController(backend);
        Assert.IsTrue(control.TrySetOffset(10, out string status), status);
        Assert.AreEqual(original & ~CpuProchotController.OffsetMask, backend.Target & ~CpuProchotController.OffsetMask);
        Assert.AreEqual(10, control.Read()!.Value.OffsetCelsius);
        Assert.AreEqual(90, control.Read()!.Value.ActivationTemperatureCelsius);
        Assert.HasCount(1, backend.Writes);
    }

    [TestMethod]
    public void LockedOffsetIsNotWritten_AndAnAlreadyMatchingValueIsAccepted()
    {
        var backend = new Backend();
        backend.Target |= CpuProchotController.LockBit;
        var control = new CpuProchotController(backend);
        Assert.IsFalse(control.TrySetOffset(10, out string status));
        Assert.Contains("locked", status);
        Assert.IsEmpty(backend.Writes);
        Assert.IsTrue(control.TrySetOffset(5, out status), status);
        Assert.IsEmpty(backend.Writes);
    }

    [TestMethod]
    [DataRow(-1)] [DataRow(64)]
    public void InvalidOffsetDoesNotWrite(int offset)
    {
        var backend = new Backend();
        Assert.IsFalse(new CpuProchotController(backend).TrySetOffset(offset, out _));
        Assert.IsEmpty(backend.Writes);
    }

    [TestMethod]
    public void UnsupportedCpuDoesNotWrite()
    {
        var backend = new Backend { Supported = false };
        Assert.IsFalse(new CpuProchotController(backend).TrySetOffset(10, out string status));
        Assert.Contains("support", status);
        Assert.IsEmpty(backend.Writes);
    }

    [TestMethod]
    public void FailedReadbackRestoresTheOriginalOffset()
    {
        var backend = new Backend { MismatchFirstWrite = true };
        ulong original = backend.Target;
        Assert.IsFalse(new CpuProchotController(backend).TrySetOffset(10, out string status));
        Assert.Contains("restored", status);
        Assert.AreEqual(original, backend.Target);
        Assert.HasCount(2, backend.Writes);
    }
}
