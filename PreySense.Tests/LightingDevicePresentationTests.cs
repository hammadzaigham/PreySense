using PreySense.Rgb;

namespace PreySense.Tests;

[TestClass]
public class LightingDevicePresentationTests
{
    [TestMethod]
    public void DuplicateBarKeepsTheTransportWithAdvertisedAnimations()
    {
        var limited = new LightingDeviceInfo("acer-hid-front", "Front Lightbar", "Lightbar",
            [new(0, "LED 1")], [new(-2, "Mode"), new(2, "Static")], "");
        var animated = limited with { Id = "acer-service:front", Kind = "Lighting device",
            Modes = [new(-2, "Mode"), new(20, "Static"), new(30, "Wave", true), new(40, "Breathing", true)] };
        foreach (var devices in new[] { new[] { limited, animated }, new[] { animated, limited } })
        {
            var selected = LightingDeviceInfo.UniqueTargets(devices).Single();
            Assert.AreEqual(animated.Id, selected.Id);
            Assert.AreEqual("Dynamic Bar (Front)", selected.DisplayName);
            Assert.IsTrue(selected.GetEditorModes().Any(mode => mode.Value == 30 && mode.Name == "Wave"));
        }
    }

    [TestMethod]
    public void PerKeyKeyboard_UsesStaticEditorWithoutDuplicatingHardwareStatic()
    {
        var keys = Enumerable.Range('A', 26).Select((letter, i) => new LightingLedInfo(i, ((char)letter).ToString())).ToArray();
        var device = new LightingDeviceInfo("usb:sample", "Vendor keyboard", "Keyboard", keys,
            [new(-1, "Custom colors"), new(0, "Static"), new(3, "Wave", true)], "Sample");
        Assert.AreEqual("Per-Key Keyboard", device.DisplayName);
        var modes = device.GetEditorModes();
        Assert.HasCount(3, modes);
        Assert.IsTrue(ModeKeyLighting.IsFollowMode(modes[2].Value));
        Assert.AreEqual("Static", modes[0].Name);
        Assert.AreEqual(-1, modes[0].Value, "The existing individual-color transport ID must be retained.");
        Assert.AreEqual(3, modes[1].Value);
        Assert.AreEqual("Custom colors", device.Modes[0].Name, "Presentation must not rewrite hardware metadata.");
    }

    [TestMethod]
    public void LightBar_StaticUsesIndependentLedColorsAndZoneKeyboardKeepsItsModes()
    {
        LightingLedInfo[] leds = [new(0, "LED 1"), new(1, "LED 2")];
        var bar = new LightingDeviceInfo("bar", "Dynamic bar", "Light bar", leds,
            [new(-1, "Custom colors"), new(2, "Static"), new(4, "Breathing", true)], "Sample");
        Assert.AreEqual("Dynamic Bar", bar.DisplayName);
        Assert.AreEqual("Dynamic Bar (Front)", (bar with { Name = "Front lightbar" }).DisplayName);
        Assert.AreEqual("Static", bar.GetEditorModes()[0].Name);
        Assert.HasCount(3, bar.GetEditorModes());
        Assert.IsTrue(ModeKeyLighting.IsFollowMode(bar.GetEditorModes()[2].Value));
        var zones = bar with { Kind = "Keyboard", Name = "Acer keyboard" };
        Assert.AreEqual("Zone Keyboard", zones.DisplayName);
        CollectionAssert.AreEqual(zones.Modes.ToArray(), zones.GetEditorModes().Where(m => !ModeKeyLighting.IsFollowMode(m.Value)).ToArray());
    }
}
