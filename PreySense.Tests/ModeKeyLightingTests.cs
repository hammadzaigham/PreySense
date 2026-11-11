using PreySense.Core.Configuration;
using PreySense.Core.Hardware;
using PreySense.Rgb;

namespace PreySense.Tests;

[TestClass]
public class ModeKeyLightingTests
{
    [TestMethod]
    public void DefaultsMatchStockPredatorSenseModeKeyColors()
    {
        var settings = new DeviceLightingConfig { Mode = ModeKeyLighting.FollowOperatingMode };
        Assert.AreEqual("#33FF77", ModeKeyLighting.ColorFor(settings, 0x06));
        Assert.AreEqual("#00FF00", ModeKeyLighting.ColorFor(settings, 0x00));
        Assert.AreEqual("#3B82F6", ModeKeyLighting.ColorFor(settings, 0x01));
        Assert.AreEqual("#FF00EA", ModeKeyLighting.ColorFor(settings, 0x04));
        Assert.AreEqual("#BF00FF", ModeKeyLighting.ColorFor(settings, 0x05));
    }

    [TestMethod]
    public void NeverSetOrDefaultColorsAreNotPersisted()
    {
        var edited = new Dictionary<string, string>
        {
            ["06"] = "#33FF77",
            ["00"] = "#00FF00",
            ["01"] = "#3B82F6",
            ["04"] = "#AABBCC",
            ["05"] = "#BF00FF",
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
            PowerModeColors = new() { ["05"] = "#BF00FF", ["04"] = "#112233" }
        };
        Assert.AreEqual("#BF00FF", ModeKeyLighting.ColorFor(settings, 0x05));
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
