using System.Text.Json;
using PreySense.Core.Configuration;
using PreySense.Frontend.Services;

namespace PreySense.Tests;

[TestClass]
public class ThemeConfigurationTests
{
    [TestMethod]
    public void Glass_RoundTripsByNameAndOverridesOldLightPreference()
    {
        var json = JsonSerializer.Serialize(new GeneralConfig { Theme = ApplicationTheme.Glass, LightTheme = true });
        StringAssert.Contains(json, "\"Theme\":\"Glass\"");
        Assert.AreEqual(ApplicationTheme.Glass, ThemeManager.ResolveTheme(JsonSerializer.Deserialize<GeneralConfig>(json)!));
    }

    [TestMethod]
    public void ExistingPreferences_KeepTheirAppearanceWhenThemeIsAbsent()
    {
        Assert.AreEqual(ApplicationTheme.Dark, ThemeManager.ResolveTheme(new()));
        Assert.AreEqual(ApplicationTheme.Light, ThemeManager.ResolveTheme(new() { LightTheme = true }));
        Assert.AreEqual(ApplicationTheme.Glass, ThemeManager.ResolveTheme(new() { LightTheme = true, Theme = ApplicationTheme.Glass }));
    }
}
