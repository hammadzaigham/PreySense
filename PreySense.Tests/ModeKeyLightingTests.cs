using PreySense.Core.Configuration;
using PreySense.Core.Hardware;
using PreySense.Rgb;

namespace PreySense.Tests;

[TestClass]
public class ModeKeyLightingTests
{
    [TestMethod]
    public void DefaultsMatchRecoveredPhn16s71FirmwarePalette()
    {
        var settings = new DeviceLightingConfig { Mode = ModeKeyLighting.FollowOperatingMode };
        Assert.AreEqual("#10FF10", ModeKeyLighting.ColorFor(settings, 0x06));
        Assert.AreEqual("#A0FF90", ModeKeyLighting.ColorFor(settings, 0x00));
        Assert.AreEqual("#00AEC7", ModeKeyLighting.ColorFor(settings, 0x01));
        Assert.AreEqual("#2E09D0", ModeKeyLighting.ColorFor(settings, 0x04));
        Assert.AreEqual("#C700FF", ModeKeyLighting.ColorFor(settings, 0x05));
    }

    [TestMethod]
    public void NeverSetOrDefaultColorsAreNotPersisted()
    {
        var edited = new Dictionary<string, string>
        {
            ["06"] = "#10FF10",
            ["00"] = "#A0FF90",
            ["01"] = "#00AEC7",
            ["04"] = "#AABBCC",
            ["05"] = "#C700FF",
        };
        var overrides = ModeKeyLighting.OnlyCustomOverrides(edited);
        CollectionAssert.AreEquivalent(new[] { "04" }, overrides.Keys.ToArray());
        Assert.AreEqual("#AABBCC", overrides["04"]);
    }

    [TestMethod]
    public void StoredDefaultEqualsIsTreatedAsUnset()
    {
        var settings = new DeviceLightingConfig
        {
            Mode = ModeKeyLighting.FollowOperatingMode,
            PowerModeColors = new() { ["05"] = "#C700FF", ["04"] = "#112233" }
        };
        Assert.AreEqual("#C700FF", ModeKeyLighting.ColorFor(settings, 0x05));
        Assert.AreEqual("#112233", ModeKeyLighting.ColorFor(settings, 0x04));
        var cleaned = ModeKeyLighting.OnlyCustomOverrides(settings.PowerModeColors);
        Assert.IsFalse(cleaned.ContainsKey("05"));
        Assert.AreEqual("#112233", cleaned["04"]);
    }

    [TestMethod]
    public void ResolveForApplyKeepsFollowModeAndResolvedColor()
    {
        var settings = new DeviceLightingConfig
        {
            Mode = ModeKeyLighting.FollowOperatingMode,
            Brightness = 40,
            PowerModeColors = new() { ["04"] = "#AABBCC" }
        };
        var resolved = ModeKeyLighting.ResolveForApply(settings, 0x04);
        Assert.AreEqual(ModeKeyLighting.FollowOperatingMode, resolved.Mode);
        Assert.AreEqual("#AABBCC", resolved.ColorHex);
        Assert.AreEqual(40, resolved.Brightness);
        Assert.HasCount(1, resolved.PowerModeColors);
    }

    [TestMethod]
    public void KeyboardModeColorsMatchModeKeyForEveryOperatingMode()
    {
        var settings = new DeviceLightingConfig();
        foreach (var slot in ModeKeyLighting.Slots)
        {
            var zones = ModeKeyLighting.GetZoneColors(slot.PowerMode);
            Assert.AreEqual((slot.DefaultHex, slot.DefaultHex, slot.DefaultHex, slot.DefaultHex), zones);
            var resolved = ModeKeyLighting.ResolveForApply(
                new DeviceLightingConfig { Mode = ModeKeyLighting.FollowOperatingMode }, slot.PowerMode);
            Assert.IsTrue(resolved.LedColors.Values.All(c => c == ModeKeyLighting.ColorFor(settings, slot.PowerMode)));
        }
    }

    [TestMethod]
    public void CustomPowerModeColorOverrideAppliesToAllKeyboardZones()
    {
        var settings = new DeviceLightingConfig
        {
            Mode = ModeKeyLighting.FollowOperatingMode,
            PowerModeColors = new() { ["01"] = "#112233" }
        };
        var zones = ModeKeyLighting.GetZoneColors(0x01, settings);
        Assert.AreEqual("#112233", zones.Z1);
        Assert.AreEqual("#112233", zones.Z2);
        Assert.AreEqual("#112233", zones.Z3);
        Assert.AreEqual("#112233", zones.Z4);
    }

    [TestMethod]
    public void ResolveForApplyPopulatesZoneLedColors()
    {
        var settings = new DeviceLightingConfig { Mode = ModeKeyLighting.FollowOperatingMode };
        var resolved = ModeKeyLighting.ResolveForApply(settings, 0x01);
        Assert.AreEqual(ModeKeyLighting.FollowOperatingMode, resolved.Mode);
        Assert.AreEqual("#00AEC7", resolved.LedColors[0]);
        Assert.AreEqual("#00AEC7", resolved.LedColors[1]);
        Assert.AreEqual("#00AEC7", resolved.LedColors[2]);
        Assert.AreEqual("#00AEC7", resolved.LedColors[3]);
    }

    [TestMethod]
    public void FiveSlotsAreEcoQuietBalancedPerformanceTurbo()
    {
        CollectionAssert.AreEqual(
            new[] { "Eco", "Quiet", "Balanced", "Performance", "Turbo" },
            ModeKeyLighting.Slots.Select(s => s.Name).ToArray());
    }

    [TestMethod]
    public void RgbRestoreDelayRemainsIndependentOfModeSwitchCooldown()
    {
        Assert.AreEqual(1550d, ModeKeyLighting.EcModeTransitionDelay.TotalMilliseconds);
        Assert.AreEqual(2000d, AcerOperatingModeTiming.TransitionDelay.TotalMilliseconds);
    }
}
