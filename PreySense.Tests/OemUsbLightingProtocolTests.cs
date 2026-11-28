using System.Drawing;
using PreySense.Rgb;

namespace PreySense.Tests;

[TestClass]
public class OemUsbLightingProtocolTests
{
    [TestMethod]
    public void AcerAndDarfonUseReservedByteThenRgbInOriginalKeyOrder()
    {
        // Per-key output applies the floored gamma 2.2 LED curve; 0x12 (18) maps to 0 while higher channels remain non-zero.
        var c0 = Color.FromArgb(0x12, 0x34, 0x56);
        var c1 = Color.FromArgb(0xab, 0xcd, 0xef);
        byte[] expectedPrefix =
        [
            0, OemUsbLightingProtocol.ApplyLedGamma(c0.R), OemUsbLightingProtocol.ApplyLedGamma(c0.G), OemUsbLightingProtocol.ApplyLedGamma(c0.B),
            0, OemUsbLightingProtocol.ApplyLedGamma(c1.R), OemUsbLightingProtocol.ApplyLedGamma(c1.G), OemUsbLightingProtocol.ApplyLedGamma(c1.B)
        ];
        foreach (var (family, pid) in new[] { (OemUsbKeyboardFamily.Acer, (ushort)0x0117), (OemUsbKeyboardFamily.Acer, (ushort)0x011a), (OemUsbKeyboardFamily.Acer, (ushort)0x0119), (OemUsbKeyboardFamily.Darfon, (ushort)0xba51) })
        {
            var frame = OemUsbLightingProtocol.BuildStaticFrame(family, pid, [c0, c1]);
            Assert.HasCount(512, frame);
            CollectionAssert.AreEqual(expectedPrefix, frame[..8]);
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
        colors[0] = Color.FromArgb(64, 128, 255);
        colors[5] = Color.FromArgb(80, 160, 220);
        var frame = OemUsbLightingProtocol.BuildStaticFrame(OemUsbKeyboardFamily.Sunrex, 0x666a, colors);
        Assert.HasCount(504, frame);
        CollectionAssert.AreEqual(
            new byte[] { 0, OemUsbLightingProtocol.ApplyLedGamma(80), OemUsbLightingProtocol.ApplyLedGamma(160), OemUsbLightingProtocol.ApplyLedGamma(220) },
            frame[..4]);
        CollectionAssert.AreEqual(
            new byte[] { 0, OemUsbLightingProtocol.ApplyLedGamma(64), OemUsbLightingProtocol.ApplyLedGamma(128), OemUsbLightingProtocol.ApplyLedGamma(255) },
            frame[20..24]);
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
        foreach (var pid in new ushort[] { 0x0117, 0x011a, 0x0119 })
        {
            var acer = OemUsbLightingProtocol.BuildTransfer(OemUsbKeyboardFamily.Acer, pid, [], 0);
            Assert.IsTrue(acer.BodyWriteReports.All(r => r.Length == 65));
            Assert.AreEqual((byte)0xb4, acer.CommitFeatureReport[^1]);
            Assert.AreEqual(0, acer.BeforeBodyDelayMs);
            Assert.AreEqual(0, acer.BetweenWritesDelayMs);
            Assert.AreEqual(0, acer.BeforeCommitDelayMs);
        }
    }

    [TestMethod]
    public void ChiconyPids011AAnd0119SharePh1671LayoutAndModes()
    {
        var baseKeys = OemUsbLightingProtocol.GetKeys(OemUsbKeyboardFamily.Acer, 0x0117);
        var baseModes = OemUsbLightingProtocol.GetSupportedModes(OemUsbKeyboardFamily.Acer, 0x0117);
        foreach (var pid in new ushort[] { 0x011a, 0x0119 })
        {
            Assert.IsTrue(OemUsbLightingProtocol.IsKnownProduct(OemUsbKeyboardFamily.Acer, pid));
            CollectionAssert.AreEqual(baseKeys.ToArray(), OemUsbLightingProtocol.GetKeys(OemUsbKeyboardFamily.Acer, pid).ToArray());
            CollectionAssert.AreEqual(baseModes.ToArray(), OemUsbLightingProtocol.GetSupportedModes(OemUsbKeyboardFamily.Acer, pid).ToArray());
        }
    }

    [TestMethod]
    public void GammaCurveSuppressesLowValuesAndPreservesEndpointsAndMonotonicity()
    {
        Assert.AreEqual((byte)0, OemUsbLightingProtocol.ApplyLedGamma(0));
        Assert.AreEqual((byte)255, OemUsbLightingProtocol.ApplyLedGamma(255));
        for (var i = 1; i <= 20; i++)
            Assert.AreEqual((byte)0, OemUsbLightingProtocol.ApplyLedGamma((byte)i), $"Expected 0 at {i}");
        Assert.IsGreaterThanOrEqualTo((byte)1, OemUsbLightingProtocol.ApplyLedGamma(21));
        Assert.IsTrue(OemUsbLightingProtocol.ApplyLedGamma(128) is >= 50 and <= 60);
        for (var i = 1; i < 256; i++)
            Assert.IsGreaterThanOrEqualTo(OemUsbLightingProtocol.ApplyLedGamma((byte)(i - 1)), OemUsbLightingProtocol.ApplyLedGamma((byte)i), $"Monotonicity failed at {i}");

        var frame = OemUsbLightingProtocol.BuildStaticFrame(OemUsbKeyboardFamily.Acer, 0x0117, [Color.FromArgb(10, 128, 255)]);
        Assert.AreEqual((byte)0, frame[1]);
        Assert.AreEqual(OemUsbLightingProtocol.ApplyLedGamma(128), frame[2]);
        Assert.AreEqual((byte)255, frame[3]);
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
