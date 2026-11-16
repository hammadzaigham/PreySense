using System.IO.Compression;
using System.Security.Cryptography;
using PreySense.Mode;

namespace PreySense.Tests;

[TestClass]
public class CompressedLightingResourceTests
{
    [TestMethod]
    public void EmbeddedLayoutsMatchEditableSourceExactly()
    {
        using var compressed = typeof(FanCurveProfile).Assembly.GetManifestResourceStream(
            "PreySense.Core.Resources.Rgb.OemUsbKeyboardLayouts.json.gz");
        Assert.IsNotNull(compressed);
        using var decoded = new GZipStream(compressed, CompressionMode.Decompress);
        using var bytes = new MemoryStream();
        decoded.CopyTo(bytes);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "PreySense.slnx"))) root = root.Parent;
        Assert.IsNotNull(root);
        byte[] source = File.ReadAllBytes(Path.Combine(root.FullName, "PreySense.Core", "Resources", "Rgb", "OemUsbKeyboardLayouts.json"));
        CollectionAssert.AreEqual(SHA256.HashData(source), SHA256.HashData(bytes.ToArray()),
            "Regenerate OemUsbKeyboardLayouts.json.gz after changing the editable JSON.");
    }
}
