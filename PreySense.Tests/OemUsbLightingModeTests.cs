using System.Drawing;
using PreySense.Rgb;

namespace PreySense.Tests;

[TestClass]
public class OemUsbLightingModeTests
{
    [TestMethod]
    public void BrightnessPercentMapsToRecoveredFiftyStepRange()
    {
        Assert.AreEqual((byte)0, OemUsbLightingProtocol.MapBrightnessPercent(0));
        Assert.AreEqual((byte)30, OemUsbLightingProtocol.MapBrightnessPercent(60));
        Assert.AreEqual((byte)50, OemUsbLightingProtocol.MapBrightnessPercent(100));
        Assert.AreEqual((byte)50, OemUsbLightingProtocol.MapBrightnessPercent(255));
        Assert.Throws<ArgumentOutOfRangeException>(() => OemUsbLightingProtocol.BuildTransfer(OemUsbKeyboardFamily.Acer, 0x0117, [], 100));
    }

    [TestMethod]
    public void FamilySpecificWaveIdsAndSpeedBoundsStaySeparate()
    {
        var acer = OemUsbLightingProtocol.GetSupportedModes(OemUsbKeyboardFamily.Acer, 0x0117).Single(m => m.Name == "WAVE");
        var darfon = OemUsbLightingProtocol.GetSupportedModes(OemUsbKeyboardFamily.Darfon, 0xba51).Single(m => m.Name == "WAVE");
        var sunrex = OemUsbLightingProtocol.GetSupportedModes(OemUsbKeyboardFamily.Sunrex, 0x767a).Single(m => m.Name == "WAVE");
        Assert.AreEqual((byte)3, acer.Id);
        Assert.AreEqual((byte)4, darfon.Id);
        Assert.AreEqual((byte)3, sunrex.Id);
        Assert.AreEqual(11, acer.SpeedMax);
        Assert.AreEqual(9, sunrex.SpeedMax);
        Assert.IsTrue(acer.SupportsDirection);
        Assert.Throws<ArgumentOutOfRangeException>(() => OemUsbLightingProtocol.BuildModeTransfer(OemUsbKeyboardFamily.Sunrex, 0x767a, 3, Color.Red, 100, 10));
        Assert.Throws<NotSupportedException>(() => OemUsbLightingProtocol.BuildModeTransfer(OemUsbKeyboardFamily.Sunrex, 0x767a, 0x22, Color.Red, 100));
    }

    [TestMethod]
    public void AcerBreathingMatchesNativeFeatureOrderAndChecksums()
    {
        var steps = OemUsbLightingProtocol.BuildModeTransfer(OemUsbKeyboardFamily.Acer, 0x0117, 2, Color.FromArgb(0x12, 0x34, 0x56), 100, 5);
        Assert.HasCount(3, steps);
        CollectionAssert.AreEqual(new byte[] { 0, 0xb1, 0, 0, 0, 0, 0, 0, 0x4e }, steps[0].Report);
        CollectionAssert.AreEqual(new byte[] { 0, 0x14, 0, 0, 0x12, 0x34, 0x56, 0, 0x4f }, steps[1].Report);
        CollectionAssert.AreEqual(new byte[] { 0, 8, 2, 2, 5, 50, 1, 1, 0xba }, steps[2].Report);
        CollectionAssert.AreEqual(new[] { 15, 15, 0 }, steps.Select(s => s.DelayAfterMs).ToArray());
    }

    [TestMethod]
    public void DarfonUsesZeroCommandByteAndInvertedSpeedIncludingNativeWrap()
    {
        var steps = OemUsbLightingProtocol.BuildModeTransfer(OemUsbKeyboardFamily.Darfon, 0xba51, 4, Color.Red, 50, 11, 2);
        Assert.HasCount(4, steps);
        CollectionAssert.AreEqual(new byte[] { 0, 8, 2, 0, 0, 0, 0, 0, 0xf5 }, steps[1].Report);
        CollectionAssert.AreEqual(new byte[] { 0, 8, 0, 4, 0xff, 25, 1, 3, 0xd7 }, steps[3].Report);
    }

    [TestMethod]
    public void SunrexRandomEffectPlacesModeBeforeColorAndOffIsSingleReport()
    {
        var steps = OemUsbLightingProtocol.BuildModeTransfer(OemUsbKeyboardFamily.Sunrex, 0x666a, 2, Color.Red, 100, 5, randomColors: true);
        Assert.HasCount(4, steps);
        CollectionAssert.AreEqual(new byte[] { 0, 8, 2, 2, 5, 50, 0xe0, 0, 0xdc }, steps[2].Report);
        CollectionAssert.AreEqual(new byte[] { 0, 0x14, 0, 0, 0, 0, 0, 0, 0xeb }, steps[3].Report);
        var off = OemUsbLightingProtocol.BuildModeTransfer(OemUsbKeyboardFamily.Sunrex, 0x666a, 0, Color.Red, 100);
        Assert.HasCount(1, off);
        CollectionAssert.AreEqual(new byte[] { 0, 8, 1, 0, 0, 0, 0, 0, 0xf6 }, off[0].Report);
    }
}
