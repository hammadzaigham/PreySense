using System.Xml.Linq;

namespace PreySense.Tests;

[TestClass]
public class TuningLayoutTests
{
    [TestMethod]
    public void GpuPowerToggleRequiresGpuProfileSupport()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "PreySense.slnx"))) root = root.Parent;
        Assert.IsNotNull(root);
        var document = XDocument.Load(Path.Combine(root.FullName, "PreySense.Frontend", "Views", "TuningSettingsWindow.xaml"));
        var toggle = document.Descendants().Single(e => (string?)e.Attribute("Title") == "Enable GPU Power Limit Control");
        Assert.AreEqual("{Binding IsGpuProfileTuningAvailable}", (string?)toggle.Attribute("IsEnabled"));
    }

    [TestMethod]
    public void TuningReservesFooterSpaceAndUsesAutomaticScrollingAndDisabledTauText()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "PreySense.slnx"))) root = root.Parent;
        Assert.IsNotNull(root);
        var document = XDocument.Load(Path.Combine(root.FullName, "PreySense.Frontend", "Views", "ModeSettingsWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var scroll = document.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "TuningScroll");
        Assert.AreEqual("Auto", (string?)scroll.Attribute("VerticalScrollBarVisibility"));
        Assert.IsNull(scroll.Attribute("MaxHeight"));
        Assert.AreEqual("*", (string?)scroll.Parent!.Elements().First(e => e.Name.LocalName == "Grid.RowDefinitions").Elements().ElementAt(1).Attribute("Height"));
        var tau = document.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "LabelTau");
        Assert.AreEqual("{DynamicResource SettingValueStyle}", (string?)tau.Attribute("Style"));
        Assert.IsNull(tau.Attribute("Foreground"), "A local foreground would override the shared disabled color.");
    }
}
