using PreySense.Rgb;

namespace PreySense.Tests;

[TestClass]
public class LightingDevicePresentationTests
{
    [TestMethod]
    public void PerKeyKeyboard_UsesStaticEditorWithoutDuplicatingHardwareStatic()
    {
        var keys = Enumerable.Range('A', 26).Select((letter, i) => new LightingLedInfo(i, ((char)letter).ToString())).ToArray();
        var device = new LightingDeviceInfo("usb:sample", "Vendor keyboard", "Keyboard", keys,
            [new(-1, "Custom colors"), new(0, "Static"), new(3, "Wave", true)], "Sample");
        Assert.AreEqual("Per-Key Keyboard", device.DisplayName);
        var modes = device.GetEditorModes();
        Assert.HasCount(2, modes);
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
        Assert.HasCount(2, bar.GetEditorModes());
        var zones = bar with { Kind = "Keyboard", Name = "Acer keyboard" };
        Assert.AreEqual("Zone Keyboard", zones.DisplayName);
        Assert.AreSame(zones.Modes, zones.GetEditorModes());
    }
}
