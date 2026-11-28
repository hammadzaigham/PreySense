using System.Xml.Linq;

namespace PreySense.Tests;

[TestClass]
public class TuningNavigationTests
{
    private static string Root()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "PreySense.slnx"))) root = root.Parent;
        return root?.FullName ?? throw new InvalidOperationException("Solution not found.");
    }

    [TestMethod]
    public void TuningNavigationIsAlwaysVisibleAndSettingsStayInSettings()
    {
        var settings = XDocument.Load(Path.Combine(Root(), "PreySense.Frontend", "Views", "SettingsWindow.xaml"));
        var tuning = XDocument.Load(Path.Combine(Root(), "PreySense.Frontend", "Views", "ModeSettingsWindow.xaml"));
        var main = XDocument.Load(Path.Combine(Root(), "PreySense.Frontend", "MainWindow.xaml"));
        Assert.IsFalse(settings.Descendants().Any(e => (string?)e.Attribute("Title") == "Enable Tuning"));
        Assert.IsFalse(tuning.Descendants().Any(e => (string?)e.Attribute("Title") == "Enable Tuning"));
        Assert.IsFalse(tuning.Descendants().Any(e => (string?)e.Attribute("Title") == "Tuning Settings"));
        var tuningSettings = settings.Descendants().Single(e => (string?)e.Attribute("Title") == "Tuning Settings");
        Assert.IsNull(tuningSettings.Attribute("Visibility"));
        var section = tuningSettings.Parent!.Parent!;
        Assert.AreEqual("Separator", section.ElementsBeforeSelf().Last().Name.LocalName);
        Assert.IsFalse(section.Descendants().Any(e => (string?)e.Attribute("Text") == "Tuning"));
        Assert.AreEqual("Separator", section.ElementsAfterSelf().First().Name.LocalName);
        var gamesButton = main.Descendants().Single(e => e.Attributes().Any(a => a.Name.LocalName == "AutomationProperties.AutomationId" && a.Value == "BtnNavGames"));
        Assert.AreEqual("{Binding IsGameModeEnabled, Converter={StaticResource BoolToVis}}", (string?)gamesButton.Attribute("Visibility"));
        Assert.AreEqual("1", (string?)gamesButton.Parent!.Attribute("Rows"));
        Assert.AreEqual("{Binding FooterBottomRowColumns}", (string?)gamesButton.Parent!.Attribute("Columns"));

        var modeButton = main.Descendants().Single(e => e.Attributes().Any(a => a.Name.LocalName == "AutomationProperties.AutomationId" && a.Value == "BtnNavMode"));
        Assert.IsNull(modeButton.Attribute("Visibility"));
        Assert.AreEqual("1", (string?)modeButton.Parent!.Attribute("Rows"));
        var quit = main.Descendants().Single(e => e.Attributes().Any(a => a.Name.LocalName == "AutomationProperties.AutomationId" && a.Value == "BtnNavQuit"));
        Assert.IsNull(quit.Attribute("Visibility"));
    }
}
