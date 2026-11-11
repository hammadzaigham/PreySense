using PreySense.Rgb;

namespace PreySense.Tests;

[TestClass]
public class AcerHidLightingProtocolTests
{
    [TestMethod]
    [DataRow(0x64, 4)] [DataRow(0x64, 5)] [DataRow(0x65, 4)] [DataRow(0x65, 5)]
    public void SingleLedModeKey_ExposesAnimationSpeedWhenAdvertised(int target, int mode)
    {
        Assert.IsTrue(AcerHidLightingProtocol.SupportsAnimationSpeed((byte)target, 1, 9, (byte)mode));
        Assert.IsFalse(AcerHidLightingProtocol.SupportsAnimationSpeed((byte)target, 1, 0, (byte)mode));
        Assert.IsFalse(AcerHidLightingProtocol.SupportsAnimationSpeed((byte)target, 1, 9, 2));
    }

    [TestMethod]
    public void RealKeyboardCapabilityReplyUsesDescriptorWidthsAndOemFieldOrder()
    {
        var widths = new Dictionary<ushort, int> { [0x34] = 1, [0x32] = 1, [0x35] = 1, [0x31] = 4 };
        var info = AcerHidLightingProtocol.ParseBaseInfo([0xA3, 0x21, 1, 9, 4, 0xFB, 7, 0, 0, 0, 0], 0x21, widths);
        Assert.IsNotNull(info);
        Assert.AreEqual(4, info.LedCount);
        Assert.AreEqual(0x7FBu, info.EffectMask);
        CollectionAssert.AreEqual(new byte[] { 2, 4, 5, 7, 8, 9, 10, 11 }, AcerHidLightingProtocol.GetModes(info.EffectMask).Select(m => m.Value).ToArray());
        Assert.IsNull(AcerHidLightingProtocol.ParseBaseInfo([0xA3, 0x83, 1, 5, 1, 0x3B, 0, 0, 0], 0x21, widths));
        Assert.IsNull(AcerHidLightingProtocol.ParseBaseInfo([0xA3, 0x21, 1, 9, 4, 0xFB], 0x21, widths));
    }

    [TestMethod]
    public void TargetAndSixteenBitZoneMaskAreSerializedWithoutChangingColorOrder()
    {
        var widths = new Dictionary<ushort, int> { [0x41] = 1, [0x43] = 1, [0x42] = 1, [0x44] = 1,
            [0x45] = 1, [0x46] = 1, [0x47] = 1, [0x48] = 2 };
        var packet = AcerHidLightingProtocol.CreateUpdateReport(11, 0x22, 2, 5, 0, 25, 0x11, 0x22, 0x33, 0x0102, widths);
        CollectionAssert.AreEqual(new byte[] { 0xA4, 0x22, 2, 25, 5, 0, 0x11, 0x22, 0x33, 2, 1 }, packet);
        Assert.Throws<ArgumentException>(() => AcerHidLightingProtocol.CreateUpdateReport(10, 0x22, 2, 5, 0, 25, 1, 2, 3, 1, widths));
    }

    [TestMethod]
    public void ModeKeyStaticUpdateIncludesBrightnessAndNonZeroZoneMask()
    {
        // Mode Key (0x65) is a single LED; Static must address zone bit 0.
        // Firmware ignores the brightness byte in Static and follows RGB level, so callers bake
        // slider brightness into RGB and send brightness=100 (e.g. 40% of #FF0080 → #660033).
        var widths = new Dictionary<ushort, int> { [0x41] = 1, [0x43] = 1, [0x42] = 1, [0x44] = 1,
            [0x45] = 1, [0x46] = 1, [0x47] = 1, [0x48] = 2 };
        var packet = AcerHidLightingProtocol.CreateUpdateReport(11, 0x65, 2, 0, 0, 100, 0x66, 0x00, 0x33, 0x0001, widths);
        CollectionAssert.AreEqual(new byte[] { 0xA4, 0x65, 2, 100, 0, 0, 0x66, 0x00, 0x33, 1, 0 }, packet);
    }
}
