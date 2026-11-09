using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using PreySense.Core.Configuration;
using PreySense.Overlay;

namespace PreySense.Tests;

[TestClass]
public class OverlayAppearanceTests
{
    [TestMethod]
    public void JsonAppearance_RoundTripsAndUiStyleUpdatesPreserveIt()
    {
        string path = Path.Combine(Path.GetTempPath(), "PreySenseAppearance-" + Guid.NewGuid() + ".json");
        try
        {
            var store = new ConfigurationService(path);
            store.Update(config =>
            {
                config.Overlay.BackgroundColorHex = "#102030";
                config.Overlay.CpuTextColorHex = "#aAbBcC";
                config.Overlay.GpuGraphColorHex = "#FF8800";
                config.Overlay.CornerRadiusPixels = 10;
                config.Overlay.GraphLineWidthPixels = 2.5f;
                config.Overlay.ShowBackgroundShading = true;
            });
            store.Update(config => config.Overlay.ScalePercent = 125);
            var saved = store.Reload().Overlay;
            Assert.AreEqual("#102030", saved.BackgroundColorHex);
            Assert.AreEqual("#aAbBcC", saved.CpuTextColorHex);
            Assert.AreEqual("#FF8800", saved.GpuGraphColorHex);
            Assert.AreEqual(10, saved.CornerRadiusPixels);
            Assert.AreEqual(2.5f, saved.GraphLineWidthPixels);
            Assert.IsTrue(saved.ShowBackgroundShading);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [TestMethod]
    [DataRow("#FFF")]
    [DataRow("#00000000")]
    [DataRow("#GG0000")]
    [DataRow("red")]
    [DataRow(null)]
    public void InvalidColors_AreRejected(string? color)
    {
        var config = new PreySenseConfig();
        config.Overlay.CpuGraphColorHex = color!;
        Assert.Throws<ArgumentException>(() => ConfigurationService.Validate(config));
    }

    [TestMethod]
    [DataRow(-1, 1.5f)]
    [DataRow(17, 1.5f)]
    [DataRow(6, 0.49f)]
    [DataRow(6, 4.01f)]
    public void OutOfRangeGeometry_IsRejected(int radius, float width)
    {
        var config = new PreySenseConfig();
        config.Overlay.CornerRadiusPixels = radius;
        config.Overlay.GraphLineWidthPixels = width;
        Assert.Throws<ArgumentOutOfRangeException>(() => ConfigurationService.Validate(config));
    }

    [TestMethod]
    public void RoundedOverlayPaint_LeavesAllFourCornerPixelsTransparent()
    {
        using var overlay = new PaintableOverlay();
        using var bitmap = new Bitmap(350, 100, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        overlay.Paint(graphics);
        Assert.AreEqual(0, bitmap.GetPixel(0, 0).A);
        Assert.AreEqual(0, bitmap.GetPixel(overlay.Width - 1, 0).A);
        Assert.AreEqual(0, bitmap.GetPixel(0, overlay.Height - 1).A);
        Assert.AreEqual(0, bitmap.GetPixel(overlay.Width - 1, overlay.Height - 1).A);
        Assert.IsGreaterThan(0, (int)bitmap.GetPixel(overlay.Width / 2, overlay.Height / 2).A);
    }

    private sealed class PaintableOverlay : HardwareOverlay
    {
        public void Paint(Graphics graphics) => PerformPaint(new PaintEventArgs(graphics, Bound));
    }
}
