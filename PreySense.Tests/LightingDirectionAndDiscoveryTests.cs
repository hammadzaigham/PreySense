using PreySense.Rgb;
using PreySense.Core.Configuration;

namespace PreySense.Tests;

[TestClass]
public class LightingDirectionAndDiscoveryTests
{
    [TestMethod]
    public void ColorWaveIncludesUpDownOnlyWhenDirectionFieldExists()
    {
        var wave = new LightingModeInfo(7, "Color Wave", DirectionFlags: AcerHidLightingProtocol.DirectionFlags(7, true));
        CollectionAssert.AreEqual(new[] { "Left", "Right", "Up", "Down" }, wave.Directions.Select(d => d.Name).ToArray());
        Assert.AreEqual((byte)3, AcerHidLightingProtocol.NativeDirection(wave.ResolveDirection(2)));
        Assert.AreEqual((byte)4, AcerHidLightingProtocol.NativeDirection(wave.ResolveDirection(3)));
        Assert.AreEqual(0u, AcerHidLightingProtocol.DirectionFlags(7, false));
        Assert.AreEqual(2u, AcerHidLightingProtocol.DirectionFlags(8, true));
    }

    [TestMethod]
    public void PhysicalTargetsUseOnePreferredTransportAndKeepDistinctBarsAndHotkeys()
    {
        LightingDeviceInfo Device(string id, string name, string kind, bool keys = false) => new(id, name, kind,
            keys ? Enumerable.Range('A', 26).Select((c, i) => new LightingLedInfo(i, ((char)c).ToString())).ToArray()
                : [new(0, "LED 1")], [new(2, "Static")], "Test");
        var discovered = new[]
        {
            Device("acer-hid-second-21", "Keyboard", "Keyboard"),
            Device("acer-hid-first-21", "Keyboard", "Keyboard"),
            Device("acer-ec-keyboard", "Keyboard", "Keyboard"),
            Device("acer-service:0", "Keyboard", "Keyboard"),
            Device("usb:keyboard", "USB keyboard", "Keyboard", true),
            Device("acer-hid-second-65", "Mode Key", "Hotkey"),
            Device("acer-hid-first-65", "Mode Key", "Hotkey"),
            Device("acer-hid-first-64", "Turbo Key", "Hotkey"),
            Device("acer-hid-first-22", "Front Lightbar", "Lightbar"),
            Device("acer-ec-front", "Front lightbar", "Light bar"),
            Device("acer-ec-rear", "Rear lightbar", "Light bar"),
            Device("acer-hid-first-83", "Cover Logo", "Logo"),
            Device("acer-service:logo", "Logo", "Lighting device"),
        };
        var unique = LightingDeviceInfo.UniqueTargets(discovered);
        Assert.HasCount(5, unique);
        Assert.AreEqual("usb:keyboard", unique.Single(d => d.Kind == "Keyboard").Id);
        Assert.AreEqual("acer-hid-first-65", unique.Single(d => d.Name == "Mode Key").Id);
        Assert.AreEqual("acer-hid-first-22", unique.Single(d => d.Name == "Front Lightbar").Id);
        Assert.HasCount(1, unique.Where(d => d.Kind == "Hotkey").ToArray());
        Assert.AreEqual("Turbo Key", unique.Single(d => d.Kind == "Hotkey").DisplayName);
        Assert.IsTrue(unique.Any(d => d.Name == "Rear lightbar"));
        Assert.AreEqual("Dynamic Bar", unique.Single(d => d.Name == "Rear lightbar").DisplayName);
        Assert.AreEqual("acer-hid-first-83", unique.Single(d => d.Kind == "Logo").Id);
        CollectionAssert.AreEqual(unique.Select(d => d.Id).Order().ToArray(),
            LightingDeviceInfo.UniqueTargets(discovered.Reverse()).Select(d => d.Id).Order().ToArray());
    }

    [TestMethod]
    public void DirectionOptionsFollowAdvertisedCapabilityBitsAndUnsupportedEffectsHideThem()
    {
        var wave = new LightingModeInfo(3, "Wave", DirectionFlags: 0x06);
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, wave.Directions.Select(d => d.Value).ToArray());
        Assert.AreEqual(0, wave.ResolveDirection(5));
        Assert.AreEqual(3, wave.ResolveDirection(3));
        Assert.IsFalse(new LightingModeInfo(2, "Breathing").SupportsDirection);
        Assert.IsEmpty(new LightingModeInfo(-1, "Static").Directions);
        CollectionAssert.AreEqual(new[] { 4, 5 }, new LightingModeInfo(7, "Wave", DirectionFlags: 8)
            .Directions.Select(d => d.Value).ToArray());
    }

    [TestMethod]
    public void NativeHidDirectionsPreserveTheRecoveredOemMapping()
    {
        CollectionAssert.AreEqual(new byte[] { 2, 1, 3, 4, 5, 6 }, Enumerable.Range(0, 6)
            .Select(AcerHidLightingProtocol.NativeDirection).ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => AcerHidLightingProtocol.NativeDirection(6));
        Assert.Throws<ArgumentOutOfRangeException>(() => OemLightingController.Validate(new DeviceLightingConfig { Direction = 6 }));
    }
}
