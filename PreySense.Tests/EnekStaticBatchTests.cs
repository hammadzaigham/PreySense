using System.Drawing;

namespace PreySense.Tests;

[TestClass]
public class EnekStaticBatchTests
{
    [TestMethod]
    public void EqualRgbColorsUseOneAllZonesReportWithRequestedBrightness()
    {
        var reports = new List<(byte Brightness, byte R, byte G, byte B, byte Mask)>();
        bool success = EnekRgbClient.WriteStaticZones(
            new[] { Color.Red, Color.FromArgb(255, 0, 0), Color.FromArgb(50, 255, 0, 0), Color.Red }, 25,
            (brightness, r, g, b, mask) => { reports.Add((brightness, r, g, b, mask)); return true; });
        Assert.IsTrue(success);
        CollectionAssert.AreEqual(new[] { ((byte)25, (byte)255, (byte)0, (byte)0, (byte)0x0F) }, reports.ToArray());
    }

    [TestMethod]
    public void MixedColorsKeepTheirIndividualMasksAndAllReportsAreAttemptedOnFailure()
    {
        var reports = new List<(byte Brightness, Color Color, byte Mask)>();
        Color[] colors = [Color.Red, Color.Blue, Color.Green, Color.White];
        bool success = EnekRgbClient.WriteStaticZones(colors, 75,
            (brightness, r, g, b, mask) =>
            {
                reports.Add((brightness, Color.FromArgb(r, g, b), mask));
                return mask != 2;
            });
        Assert.IsFalse(success);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 4, 8 }, reports.Select(report => report.Mask).ToArray());
        CollectionAssert.AreEqual(colors.Select(color => color.ToArgb()).ToArray(), reports.Select(report => report.Color.ToArgb()).ToArray());
        Assert.IsTrue(reports.All(report => report.Brightness == 75));
    }

    [TestMethod]
    public void UniformReportFailureIsReturnedWithoutRetrying()
    {
        int writes = 0;
        Assert.IsFalse(EnekRgbClient.WriteStaticZones(new[] { Color.Red, Color.Red, Color.Red, Color.Red }, 100,
            (_, _, _, _, _) => { writes++; return false; }));
        Assert.AreEqual(1, writes);
    }
}
