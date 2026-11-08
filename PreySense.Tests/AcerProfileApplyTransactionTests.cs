using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PreySense.Core.Hardware;

namespace PreySense.Tests;

[TestClass]
[DoNotParallelize]
public class AcerProfileApplyTransactionTests
{
    private sealed class Fake : IPhysicalMemoryByteAccess, IAcerPowerModeController
    {
        public byte Mode = 5;
        public readonly Dictionary<ulong, byte> Bytes = new();
        public readonly List<string> Events = new();
        public bool FailNextWrite;
        public bool ReadPhysicalByte(ulong address, out byte value) => Bytes.TryGetValue(address, out value);
        public bool WritePhysicalByte(ulong address, byte value)
        {
            Events.Add($"write:{Mode}");
            if (FailNextWrite) { FailNextWrite = false; return false; }
            Bytes[address] = value;
            return true;
        }
        public bool TryGetAcerPowerMode(out byte mode) { mode = Mode; return true; }
        public bool SetAcerPowerMode(byte mode) { Mode = mode; Events.Add($"mode:{mode}"); return true; }
        public void Dispose() { }
    }

    [TestInitialize]
    public void InitErm4() => AcerErm4Table.SetBase(AcerErm4Table.Phn16s71Base);

    private static void SeedSignature(Fake fake, ulong erm4Base = AcerErm4Table.Phn16s71Base)
    {
        AcerErm4Table.SetBase(erm4Base);
        ReadOnlySpan<byte> signature = "Predator"u8;
        for (int i = 0; i < signature.Length; i++)
            fake.Bytes[erm4Base + (ulong)i] = signature[i];
    }

    [TestMethod]
    [DataRow(0)] [DataRow(1)] [DataRow(4)] [DataRow(5)] [DataRow(6)]
    public void Apply_WhenAlreadyOnSelected_LeavesThenReturns(int mode)
    {
        var fake = new Fake { Mode = (byte)mode };
        SeedSignature(fake);
        ulong address = AcerProfileApplyTransaction.ProfileAddress((byte)mode);
        fake.Bytes[address] = 15; fake.Bytes[address + 1] = 40; fake.Bytes[address + 2] = 45;
        var result = new AcerProfileApplyTransaction(fake, fake).Apply((byte)mode, 30, 25, 20);
        Assert.IsTrue(result.Success, result.Status);
        byte staging = AcerProfileApplyTransaction.StagingMode((byte)mode);
        CollectionAssert.AreEqual(
            new[] { $"mode:{staging}", $"write:{staging}", $"write:{staging}", $"write:{staging}", $"mode:{mode}" },
            fake.Events);
        Assert.AreEqual((byte)25, fake.Bytes[address]);
        Assert.AreEqual((byte)30, fake.Bytes[address + 1]);
        Assert.AreEqual((byte)20, fake.Bytes[address + 2]);
        Assert.AreEqual((byte)mode, fake.Mode);
    }

    [TestMethod]
    public void Apply_WhenOnDifferentMode_WritesThenSwitchesToSelected()
    {
        var fake = new Fake { Mode = 0x05 }; // Turbo live, applying Quiet
        SeedSignature(fake);
        ulong address = AcerProfileApplyTransaction.ProfileAddress(0);
        fake.Bytes[address] = 15; fake.Bytes[address + 1] = 40; fake.Bytes[address + 2] = 25;
        var result = new AcerProfileApplyTransaction(fake, fake).Apply(0, 0, 0, 35);
        Assert.IsTrue(result.Success, result.Status);
        CollectionAssert.AreEqual(
            new[] { "write:5", "write:5", "write:5", "mode:0" },
            fake.Events);
        Assert.AreEqual((byte)0, fake.Bytes[address]);
        Assert.AreEqual((byte)0, fake.Bytes[address + 1]);
        Assert.AreEqual((byte)35, fake.Bytes[address + 2]);
        Assert.AreEqual((byte)0, fake.Mode);
    }

    [TestMethod]
    public void WriteFailure_RestoresBytesAndOriginalMode()
    {
        var fake = new Fake { FailNextWrite = true };
        SeedSignature(fake);
        ulong address = AcerProfileApplyTransaction.ProfileAddress(5);
        fake.Bytes[address] = 15; fake.Bytes[address + 1] = 40; fake.Bytes[address + 2] = 35;
        var result = new AcerProfileApplyTransaction(fake, fake).Apply(5, 30, 25, 20);
        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Status, "Rollback verified");
        Assert.AreEqual((byte)15, fake.Bytes[address]);
        Assert.AreEqual((byte)40, fake.Bytes[address + 1]);
        Assert.AreEqual((byte)35, fake.Bytes[address + 2]);
        Assert.AreEqual((byte)5, fake.Mode);
    }

    [TestMethod]
    public void UnsupportedSliderValue_DoesNotSwitchOrWrite()
    {
        var fake = new Fake();
        var result = new AcerProfileApplyTransaction(fake, fake).Apply(5, 55, 5);
        Assert.IsFalse(result.Success);
        Assert.IsEmpty(fake.Events);
    }

    [TestMethod]
    public void PlatformOffset_IsErM4CpAtProfileAddressPlusTwo()
    {
        // Quiet DB @ A4, CT @ A5, CP @ A6 — same byte calibration reads as record[6].
        Assert.AreEqual(0xFE8004A4UL, AcerProfileApplyTransaction.ProfileAddress(0));
        Assert.AreEqual(0xFE8004A6UL, AcerProfileApplyTransaction.ProfileAddress(0) + 2);
    }
}
