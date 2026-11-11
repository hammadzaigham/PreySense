using System.Drawing;
using PreySense.Rgb;

namespace PreySense.Tests;

[TestClass]
public class OemUsbLightingProtocolTests
{
    [TestMethod]
    public void AcerAndDarfonUseReservedByteThenRgbInOriginalKeyOrder()
    {
        foreach (var (family, pid) in new[] { (OemUsbKeyboardFamily.Acer, (ushort)0x0117), (OemUsbKeyboardFamily.Darfon, (ushort)0xba51) })
        {
            var frame = OemUsbLightingProtocol.BuildStaticFrame(family, pid, [Color.FromArgb(0x12, 0x34, 0x56), Color.FromArgb(0xab, 0xcd, 0xef)]);
            Assert.HasCount(512, frame);
            CollectionAssert.AreEqual(new byte[] { 0, 0x12, 0x34, 0x56, 0, 0xab, 0xcd, 0xef }, frame[..8]);
            Assert.IsTrue(frame.Skip(8).All(b => b == 0));
        }
    }

    [TestMethod]
    public void SunrexMapsEscapeAndControlToFirmwareSlots()
    {
        var keys = OemUsbLightingProtocol.GetKeys(OemUsbKeyboardFamily.Sunrex, 0x666a);
        Assert.AreEqual(new OemUsbKey(0, "Left Control", 5), keys.Single(k => k.Index == 0));
        Assert.AreEqual(new OemUsbKey(5, "Escape", 0), keys.Single(k => k.Index == 5));
        var colors = new Color[6];
        colors[0] = Color.FromArgb(1, 2, 3);
        colors[5] = Color.FromArgb(4, 5, 6);
        var frame = OemUsbLightingProtocol.BuildStaticFrame(OemUsbKeyboardFamily.Sunrex, 0x666a, colors);
        Assert.HasCount(504, frame);
        CollectionAssert.AreEqual(new byte[] { 0, 4, 5, 6 }, frame[..4]);
        CollectionAssert.AreEqual(new byte[] { 0, 1, 2, 3 }, frame[20..24]);
    }

    [TestMethod]
    public void TransferMatchesOemFeatureCommandsAndChunkBoundary()
    {
        var transfer = OemUsbLightingProtocol.BuildTransfer(OemUsbKeyboardFamily.Sunrex, 0x767a, [], 50);
        CollectionAssert.AreEqual(new byte[] { 0, 0x12, 0, 0, 8, 0, 0, 0, 0xe5 }, transfer.BeginFeatureReport);
        CollectionAssert.AreEqual(new byte[] { 0, 8, 2, 0x33, 5, 50, 8, 1, 0x82 }, transfer.CommitFeatureReport);
        Assert.HasCount(8, transfer.BodyWriteReports);
        Assert.IsTrue(transfer.BodyWriteReports.Take(7).All(r => r.Length == 65));
        Assert.HasCount(57, transfer.BodyWriteReports[7]);
        Assert.AreEqual(15, transfer.BeforeBodyDelayMs);
        Assert.AreEqual(2, transfer.BetweenWritesDelayMs);
        Assert.AreEqual(15, transfer.BeforeCommitDelayMs);
        var acer = OemUsbLightingProtocol.BuildTransfer(OemUsbKeyboardFamily.Acer, 0x0117, [], 0);
        Assert.IsTrue(acer.BodyWriteReports.All(r => r.Length == 65));
        Assert.AreEqual((byte)0xb4, acer.CommitFeatureReport[^1]);
        Assert.AreEqual(20, acer.BeforeBodyDelayMs);
    }

    [TestMethod]
    public void UnknownProductsAndNontransmittedSlotsAreNotAdvertised()
    {
        Assert.IsFalse(OemUsbLightingProtocol.IsKnownProduct(OemUsbKeyboardFamily.Sunrex, 0xffff));
        Assert.Throws<NotSupportedException>(() => OemUsbLightingProtocol.BuildStaticFrame(OemUsbKeyboardFamily.Sunrex, 0xffff, []));
        Assert.Throws<ArgumentException>(() => OemUsbLightingProtocol.BuildStaticFrame(OemUsbKeyboardFamily.Acer, 0x0117, new Color[128]));
        foreach (var key in OemUsbLightingProtocol.GetKeys(OemUsbKeyboardFamily.Sunrex, 0x666c))
        {
            Assert.IsTrue(key.Slot is >= 0 and < 126);
            Assert.AreNotEqual("Unused", key.Name);
        }
        var initial = OemUsbLightingProtocol.BuildInitializationReport();
        Assert.HasCount(65, initial);
        CollectionAssert.AreEqual(new byte[] { 0, 0x41, 1 }, initial[..3]);
    }
}
