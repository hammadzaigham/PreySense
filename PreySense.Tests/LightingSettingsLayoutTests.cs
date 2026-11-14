using System.Xml.Linq;

namespace PreySense.Tests;

[TestClass]
public class LightingSettingsLayoutTests
{
    [TestMethod]
    public void ModeBrightnessLivesInSettingsAndDeviceSlidersHideForMode()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "PreySense.slnx"))) root = root.Parent;
        Assert.IsNotNull(root);
        var views = Path.Combine(root.FullName, "PreySense.Frontend", "Views");
        var settings = XDocument.Load(Path.Combine(views, "LightingSettingsWindow.xaml"));
        Assert.IsTrue(settings.Descendants().Any(e => e.Name.LocalName == "Slider"
            && (string?)e.Attribute("Value") == "{Binding ModeLightingBrightness, Mode=TwoWay, Delay=150}"));
        var lighting = XDocument.Load(Path.Combine(views, "KeyboardWindow.xaml"));
        foreach (var binding in new[] { "RgbBrightness", "CoverBrightness", "DeviceLightingBrightness" })
        {
            var slider = lighting.Descendants().Single(e => e.Name.LocalName == "Slider"
                && ((string?)e.Attribute("Value"))?.StartsWith("{Binding " + binding) == true);
            var border = slider.Ancestors().First(e => e.Name.LocalName == "Border");
            Assert.IsTrue(border.Descendants().Any(e => e.Name.LocalName == "DataTrigger"
                && ((string?)e.Attribute("Binding") is "{Binding LightingEffectTag}" or "{Binding CoverLightingMode}" or "{Binding IsModeKeyFollowMode}")
                && e.Descendants().Any(setter => (string?)setter.Attribute("Property") == "Visibility" && (string?)setter.Attribute("Value") == "Collapsed")));
        }
    }

    [TestMethod]
    public void DeviceLightingModesHaveConsistentLabelsAndPrecedeBrightness()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "PreySense.slnx"))) root = root.Parent;
        Assert.IsNotNull(root);
        var document = XDocument.Load(Path.Combine(root.FullName, "PreySense.Frontend", "Views", "KeyboardWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        foreach (string name in new[] { "ComboLightingMode", "ComboCoverMode", "ComboDynamicSelect", "ComboSoftwareSelect" })
        {
            var combo = document.Descendants().Single(e => (string?)e.Attribute(x + "Name") == name);
            Assert.IsTrue(combo.Parent!.Elements().Any(e => (string?)e.Attribute("Text") == (name is "ComboDynamicSelect" or "ComboSoftwareSelect" ? "Effect" : "Lighting Mode")));
        }
        foreach (string sectionName in new[] { "SectionKeyboard", "SectionCover" })
        {
            var section = document.Descendants().Single(e => (string?)e.Attribute(x + "Name") == sectionName);
            var labels = section.Descendants().Select(e => (string?)e.Attribute("Text")).Where(t => t is "Lighting Mode" or "Brightness").ToArray();
            Assert.AreEqual("Lighting Mode", labels[0]);
            Assert.AreEqual("Brightness", labels[1]);
        }
    }

    [TestMethod]
    public void ModeColorEditorsLiveOnlyInSharedLightingSettings()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "PreySense.slnx"))) root = root.Parent;
        Assert.IsNotNull(root);
        var folder = Path.Combine(root.FullName, "PreySense.Frontend", "Views");
        var lighting = XDocument.Load(Path.Combine(folder, "KeyboardWindow.xaml"));
        var settings = XDocument.Load(Path.Combine(folder, "LightingSettingsWindow.xaml"));
        var appSettings = XDocument.Load(Path.Combine(folder, "SettingsWindow.xaml"));
        Assert.IsTrue(appSettings.Descendants().Any(e => (string?)e.Attribute("Title") == "Lighting Settings"
            && (string?)e.Attribute("Click") == "BtnOpenLightingModeSettings_Click"));
        foreach (var attribute in lighting.Descendants().Attributes())
        {
            Assert.DoesNotContain("KeyboardModeColorEditors", attribute.Value);
            Assert.DoesNotContain("ModePowerModeColorEditors", attribute.Value);
            Assert.DoesNotContain("SelectedKeyboardModeColor.ColorHex", attribute.Value);
            Assert.DoesNotContain("SelectedModePowerModeColor.ColorHex", attribute.Value);
        }
        Assert.IsTrue(settings.Descendants().Any(e => (string?)e.Attribute("ItemsSource") == "{Binding KeyboardModeColorEditors}"));
        Assert.IsFalse(settings.Descendants().Any(e => (string?)e.Attribute("Content") == "Close"));
        Assert.HasCount(1, settings.Descendants().Where(e => e.Name.LocalName == "ColorWheelControl").ToArray());
    }
}
