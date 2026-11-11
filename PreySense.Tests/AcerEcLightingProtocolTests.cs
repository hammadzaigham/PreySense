using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PreySense.Rgb;

namespace PreySense.Tests;

[TestClass]
public class AcerEcLightingProtocolTests
{
    [TestMethod]
    public void ModeAndBrightnessMatchOemByteOrder()
    {
        CollectionAssert.AreEqual(new byte[] { 5, 2, 80, 0, 1, 0x12, 0x34, 0x56, 3, 1, 0, 0, 0, 0, 0, 0 },
            AcerEcLightingProtocol.CreateKeyboardMode(5, 2, 80, 1, 0x12, 0x34, 0x56));
        var bar = AcerEcLightingProtocol.CreateLightbarMode(5, 2, 80, 1, 0x12, 0x34, 0x56);
        Assert.AreEqual((byte)2, bar[9]);
        CollectionAssert.AreEqual(new byte[] { 0, 0, 100, 0, 0, 0, 0, 0, 3, 2, 0, 0, 0, 0, 0, 0 },
            AcerEcLightingProtocol.CreateLightbarBrightness(100));
    }

    [TestMethod]
    public void ColorWordsKeepSideAndZoneBitsSeparate()
    {
        Assert.AreEqual(0x0000000056341208UL, AcerEcLightingProtocol.CreateKeyboardZoneColor(3, 0x12, 0x34, 0x56));
        Assert.AreEqual(0x0000800456341200UL, AcerEcLightingProtocol.CreateLightbarColor(4, 7, 0x12, 0x34, 0x56));
    }

    [TestMethod]
    public void LedCommandsRespectSmbiosVersionBoundaries()
    {
        Assert.AreEqual(8, AcerEcLightingProtocol.GetLedCommandLength(2, 85));
        Assert.AreEqual(12, AcerEcLightingProtocol.GetLedCommandLength(2, 86));
        Assert.AreEqual(12, AcerEcLightingProtocol.GetLedCommandLength(2, 90));
        Assert.AreEqual(16, AcerEcLightingProtocol.GetLedCommandLength(2, 91));
        Assert.AreEqual(16, AcerEcLightingProtocol.GetLedCommandLength(3, 0));
        CollectionAssert.AreEqual(new byte[] { 8, 0, 0, 0, 0, 5, 0, 0, 0, 0, 0, 0 },
            AcerEcLightingProtocol.CreateKeyboardLedMask(2, 86, [1u, 0u, 1u, 0u]));
        CollectionAssert.AreEqual(new byte[] { 0x10, 0, 0, 0, 0, 0, 0xFF, 0x11, 0xFF, 0xFF, 0, 0 },
            AcerEcLightingProtocol.CreateLightbarLedMask(2, 90, 2, [1u, 0u, 1u, 0u]));
        CollectionAssert.AreEqual(new byte[] { 0x10, 0, 0, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 1, 0x40, 0, 0 },
            AcerEcLightingProtocol.CreateLightbarLedMask(2, 91, 8, [1u, 0u, 0u, 0u, 0u, 0u, 0u, 1u]));
        CollectionAssert.AreEqual(new byte[] { 0x10, 0, 0, 0, 0, 0, 0xF5, 0xFF },
            AcerEcLightingProtocol.CreateLightbarLedMask(2, 85, 1, [1u]));
    }

    [TestMethod]
    public void CapabilityDecodeUsesRawOutputWithoutPrependedReturnByte()
    {
        var old = new byte[10];
        old[5] = 0xF0; // Front two present, two absent.
        Assert.AreEqual(2, AcerEcLightingProtocol.DecodeLightbarLedCount(2, 90, 1, 0, old));
        var current = new byte[15];
        Array.Fill(current, (byte)0xFF);
        current[11] = 0xFC; // Rear first LED present.
        Assert.AreEqual(1, AcerEcLightingProtocol.DecodeLightbarLedCount(2, 91, 8, 0, current));
        Assert.AreEqual(0, AcerEcLightingProtocol.DecodeLightbarLedCount(2, 91, 8, 1, []));
        Assert.Throws<ArgumentException>(() => AcerEcLightingProtocol.DecodeLightbarLedCount(2, 91, 8, 0, []));
    }

    [TestMethod]
    public void InvalidSelectorsAndUnprovenZoneRangesAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AcerEcLightingProtocol.CreateKeyboardZoneColor(4, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => AcerEcLightingProtocol.CreateLightbarColor(3, 0, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => AcerEcLightingProtocol.CreateLightbarLedMask(2, 90, 1, new uint[5]));
    }
}
