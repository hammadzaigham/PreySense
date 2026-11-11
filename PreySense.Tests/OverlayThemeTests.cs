using System.Text.Json;
using PreySense.Core.Configuration;

namespace PreySense.Tests;

[TestClass]
public class OverlayThemeTests
{
    [TestMethod]
    [DataRow(OverlayTheme.Dark, 100, "#F5F5F7")]
    [DataRow(OverlayTheme.White, 100, "#17191C")]
    public void Theme_ChangesAppearanceWithoutChangingMetricsOrLayout(OverlayTheme theme, int opacity, string text)
    {
        var overlay = new OverlayConfig { Enabled = true, ShowFps = false, ScalePercent = 125,
            PositionHorizontal = "Right", CornerRadiusPixels = 8 };
        var slots = overlay.Slots;
        overlay.ApplyTheme(theme);
        Assert.AreEqual(theme, overlay.Theme);
        Assert.AreEqual(opacity, overlay.BackgroundAlphaPercent);
        Assert.AreEqual(text, overlay.CpuTextColorHex);
        Assert.AreEqual(text, overlay.GpuTextColorHex);
        Assert.AreEqual(theme == OverlayTheme.Dark ? "#000000" : "#F5F5F7", overlay.BackgroundColorHex);
        Assert.AreEqual(100, overlay.TextAlphaPercent);
        Assert.AreSame(slots, overlay.Slots);
        Assert.AreEqual(125, overlay.ScalePercent);
        Assert.AreEqual("Right", overlay.PositionHorizontal);
        Assert.AreEqual(8, overlay.CornerRadiusPixels);
        Assert.IsTrue(overlay.Enabled);
        Assert.IsFalse(overlay.ShowFps);
    }

    [TestMethod]
    public void SavedTheme_PreservesSubsequentOpacityAndJsonColorEdits()
    {
        var overlay = new OverlayConfig();
        overlay.ApplyTheme(OverlayTheme.White);
        overlay.BackgroundAlphaPercent = 80;
        overlay.CpuTextColorHex = "#123456";
        string json = JsonSerializer.Serialize(overlay);
        StringAssert.Contains(json, "\"Theme\":\"White\"");
        var loaded = JsonSerializer.Deserialize<OverlayConfig>(json)!;
        Assert.AreEqual(OverlayTheme.White, loaded.Theme);
        Assert.AreEqual(80, loaded.BackgroundAlphaPercent);
        Assert.AreEqual("#123456", loaded.CpuTextColorHex);
    }

    [TestMethod]
    public void ExistingAppearance_IsNotOverwrittenWhenThemeFieldIsAbsent()
    {
        var overlay = JsonSerializer.Deserialize<OverlayConfig>("""{"BackgroundColorHex":"#123456","BackgroundOpacityPercent":70} """)!;
        Assert.AreEqual("#123456", overlay.BackgroundColorHex);
        Assert.AreEqual(70, overlay.BackgroundAlphaPercent);
    }
}
