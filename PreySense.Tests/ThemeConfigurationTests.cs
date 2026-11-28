using System.Text.Json;
using PreySense.Core.Configuration;
using PreySense.Frontend.Services;

namespace PreySense.Tests;

[TestClass]
public class ThemeConfigurationTests
{
    [TestMethod]
    public void Glass_RoundTripsByName()
    {
        var json = JsonSerializer.Serialize(new GeneralConfig { Theme = ApplicationTheme.Glass });
        StringAssert.Contains(json, "\"Theme\":\"Glass\"");
        Assert.AreEqual(ApplicationTheme.Glass, ThemeManager.ResolveTheme(JsonSerializer.Deserialize<GeneralConfig>(json)!));
    }

    [TestMethod]
    public void ResolveTheme_DefaultsToDarkWhenThemeIsNull()
    {
        Assert.AreEqual(ApplicationTheme.Dark, ThemeManager.ResolveTheme(new()));
        Assert.AreEqual(ApplicationTheme.Light, ThemeManager.ResolveTheme(new() { Theme = ApplicationTheme.Light }));
        Assert.AreEqual(ApplicationTheme.Glass, ThemeManager.ResolveTheme(new() { Theme = ApplicationTheme.Glass }));
    }
}
