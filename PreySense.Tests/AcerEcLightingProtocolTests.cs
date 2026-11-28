using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PreySense.Core.Configuration;
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

    [TestMethod]
    public void EcLightbarExposesPh1671EffectsWithZeroToTenSpeedAndStaticFallback()
    {
        var modes = OemLightingController.BuildEcModes(isKeyboard: false, ledCount: 3);
        Assert.AreEqual(ModeKeyLighting.FollowOperatingMode, modes[0].Value);
        Assert.AreEqual(-1, modes[1].Value);

        var effects = modes.Skip(2).ToArray();
        CollectionAssert.AreEqual(
            new[] { (1, "Breathing"), (2, "Neon"), (3, "Rainbow"), (4, "Wave"), (5, "Ripple"), (6, "Scanner"), (7, "Strobe") },
            effects.Select(m => (m.Value, m.Name)).ToArray());
        Assert.IsTrue(effects.All(m => m.SupportsSpeed && m.SpeedMinimum == 0 && m.SpeedMaximum == 10 && m.DefaultDirection == 1));

        var singleLedModes = OemLightingController.BuildEcModes(isKeyboard: false, ledCount: 1);
        Assert.IsFalse(singleLedModes.Any(m => m.Value == -1));
        Assert.HasCount(8, singleLedModes);
    }

    [TestMethod]
    public void LightbarWritesUseThreeCommitRoundsSpacedSixtyFiveMsApartWhileKeyboardWritesOnce()
    {
        var caps = new AcerEcLightingCapabilities(2, 90, Keyboard: true, Lightbars: true, KeyboardZoneCount: 4);
        var calls = new List<(string Method, object Value)>();
        var sleeps = new List<int>();

        using var bar = new AcerEcLightingDevice(caps, sideSelector: 8, count: 3, (m, v) => calls.Add((m, v)), sleeps.Add);
        bar.ApplyColors([0x000000FFu, 0x0000FF00u, 0x00FF0000u], 80);
        Assert.HasCount(15, calls); // 3 rounds * (SetGamingLED + SetGamingKBBacklight + 3 * SetGamingRgbKb)
        CollectionAssert.AreEqual(new[] { 65, 65 }, sleeps);
        CollectionAssert.AreEqual(new byte[] { 0x10, 0, 0, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0x15, 0, 0 }, (byte[])calls[0].Value);
        CollectionAssert.AreEqual(new byte[] { 0x10, 0, 0, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0x15, 0, 0 }, (byte[])calls[5].Value);
        CollectionAssert.AreEqual(new byte[] { 0x10, 0, 0, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0x15, 0, 0 }, (byte[])calls[10].Value);

        calls.Clear();
        sleeps.Clear();
        bar.ApplyMode(3, 5, 75, 1, 0xFF, 0x00, 0xEE);
        Assert.HasCount(6, calls); // 3 rounds * (SetGamingLED + SetGamingKBBacklight)
        CollectionAssert.AreEqual(new[] { 65, 65 }, sleeps);
        Assert.AreEqual("SetGamingLED", calls[0].Method);
        CollectionAssert.AreEqual(new byte[] { 0x10, 0, 0, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0x15, 0, 0 }, (byte[])calls[0].Value);
        Assert.AreEqual("SetGamingKBBacklight", calls[1].Method);
        CollectionAssert.AreEqual(new byte[] { 3, 5, 75, 0, 1, 0xFF, 0x00, 0xEE, 3, 2, 0, 0, 0, 0, 0, 0 }, (byte[])calls[1].Value);

        calls.Clear();
        sleeps.Clear();
        using var keyboard = new AcerEcLightingDevice(caps, sideSelector: 0, count: 4, (m, v) => calls.Add((m, v)), sleeps.Add);
        keyboard.ApplyColors([1u, 2u, 3u, 4u], 100);
        keyboard.ApplyMode(1, 5, 100, 1, 0xFF, 0, 0);
        Assert.HasCount(7, calls); // 6 for single-round ApplyColors + 1 for single-round ApplyMode
        Assert.IsEmpty(sleeps);
    }

    [TestMethod]
    public void TryReadBrightnessReadsGetGamingKBBacklightSlotTwoForLightbar()
    {
        var capabilities = new AcerEcLightingCapabilities(1, 0, true, true, 4);
        var bar = new AcerEcLightingDevice(capabilities, 8, 3, (_, _) => { }, null, slot =>
            slot == 2 ? (true, new byte[] { 0x00, 0x00, 75, 0x00, 0x00, 0xFF, 0x00, 0x00, 0x03 }) : (false, Array.Empty<byte>()));

        Assert.IsTrue(bar.TryReadBrightness(out int brightness));
        Assert.AreEqual(75, brightness);
    }
}
