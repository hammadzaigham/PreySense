using System.Drawing;
using System.Text.Json;

namespace PreySense.Rgb;

public enum OemUsbKeyboardFamily { Acer, Darfon, Sunrex }
public sealed record OemUsbKey(int Index, string Name, int Slot);
public sealed record OemUsbLightingTransfer(
    byte[] BeginFeatureReport,
    IReadOnlyList<byte[]> BodyWriteReports,
    byte[] CommitFeatureReport,
    int BeforeBodyDelayMs,
    int BetweenWritesDelayMs,
    int BeforeCommitDelayMs);

/// <summary>Pure packet construction recovered from OEM DLLs. Does not open or write hardware.</summary>
public static partial class OemUsbLightingProtocol
{
    private static readonly byte[] GammaTable = BuildGammaTable();
    private static readonly Lazy<(Dictionary<string, OemUsbKey[]> Layouts, Dictionary<OemUsbKeyboardFamily, IReadOnlyList<OemUsbHardwareMode>> Modes)> Catalog = new(LoadCatalog);

    /// <summary>Applies a floored gamma 2.2 LED curve so low values (1..20) do not faintly glow, keeping 0 at 0 and 255 at 255.</summary>
    public static byte ApplyLedGamma(byte value) => GammaTable[value];

    private static byte[] BuildGammaTable()
    {
        var table = new byte[256];
        for (var i = 0; i < table.Length; i++)
            table[i] = (byte)Math.Floor(255 * Math.Pow(i / 255.0, 2.2) + 1e-9);
        return table;
    }

    public static bool IsKnownProduct(OemUsbKeyboardFamily family, ushort productId) =>
        Catalog.Value.Layouts.ContainsKey(LayoutId(family, productId));

    /// <summary>Keys whose named slots are actually transmitted by the OEM color path.</summary>
    public static IReadOnlyList<OemUsbKey> GetKeys(OemUsbKeyboardFamily family, ushort productId)
    {
        var length = PayloadLength(family);
        return Array.AsReadOnly(GetLayout(family, productId)
            .Where(k => k.Slot >= 0 && k.Slot * 4 + 3 < length && k.Name.Length > 0 && k.Name != "Unused")
            .ToArray());
    }

    /// <summary>Colors use original OEM key Index values. Unspecified indexes remain black.</summary>
    public static byte[] BuildStaticFrame(OemUsbKeyboardFamily family, ushort productId, IReadOnlyList<Color> colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        var layout = GetLayout(family, productId);
        if (colors.Count > layout.Max(k => k.Index) + 1)
            throw new ArgumentException("Color count exceeds the recovered keyboard layout.", nameof(colors));
        var frame = new byte[PayloadLength(family)];
        foreach (var key in layout)
        {
            if (key.Index >= colors.Count || key.Slot < 0 || key.Slot * 4 + 3 >= frame.Length || key.Name == "Unused")
                continue;
            var offset = key.Slot * 4;
            var color = colors[key.Index];
            frame[offset + 1] = ApplyLedGamma(color.R);
            frame[offset + 2] = ApplyLedGamma(color.G);
            frame[offset + 3] = ApplyLedGamma(color.B);
        }
        return frame;
    }

    /// <param name="commitValue">Brightness stored in the OEM Direct mode at offset 0x44, copied byte-for-byte.</param>
    public static OemUsbLightingTransfer BuildTransfer(OemUsbKeyboardFamily family, ushort productId,
        IReadOnlyList<Color> colors, byte commitValue = 30)
    {
        if (commitValue > 50) throw new ArgumentOutOfRangeException(nameof(commitValue));
        var frame = BuildStaticFrame(family, productId, colors);
        var writes = new List<byte[]>(8);
        for (var offset = 0; offset < frame.Length; offset += 64)
        {
            var count = Math.Min(64, frame.Length - offset);
            var report = new byte[count + 1]; // Report ID 0, then payload bytes.
            Array.Copy(frame, offset, report, 1, count);
            writes.Add(report);
        }
        // Chicony (Acer) synchronous WriteFile completes per packet on Windows, so 0 ms pacing avoids artificial frame lag.
        var (edgeDelay, writeDelay) = family switch
        {
            OemUsbKeyboardFamily.Acer => (0, 0),
            OemUsbKeyboardFamily.Sunrex => (15, 2),
            _ => (20, 2)
        };
        return new OemUsbLightingTransfer(
            [0, 0x12, 0, 0, 8, 0, 0, 0, 0xe5],
            writes.AsReadOnly(),
            [0, 8, 2, 0x33, 5, commitValue, 8, 1, unchecked((byte)~(0x4b + commitValue))],
            edgeDelay, writeDelay, edgeDelay);
    }

    /// <summary>The OEM factory sends this output report once before color updates, then waits 2ms.</summary>
    public static byte[] BuildInitializationReport()
    {
        var report = new byte[65];
        report[1] = 0x41;
        report[2] = 1;
        return report;
    }

    private static int PayloadLength(OemUsbKeyboardFamily family) => family switch
    {
        OemUsbKeyboardFamily.Acer or OemUsbKeyboardFamily.Darfon => 512,
        OemUsbKeyboardFamily.Sunrex => 504,
        _ => throw new ArgumentOutOfRangeException(nameof(family))
    };

    private static OemUsbKey[] GetLayout(OemUsbKeyboardFamily family, ushort productId) =>
        Catalog.Value.Layouts.TryGetValue(LayoutId(family, productId), out var keys) ? keys :
            throw new NotSupportedException($"No verified {family} layout for product {productId:X4}.");

    private static string LayoutId(OemUsbKeyboardFamily family, ushort productId) => $"{family}:{productId:x4}";

    private static (Dictionary<string, OemUsbKey[]> Layouts, Dictionary<OemUsbKeyboardFamily, IReadOnlyList<OemUsbHardwareMode>> Modes) LoadCatalog()
    {
        using var stream = typeof(OemUsbLightingProtocol).Assembly.GetManifestResourceStream(
            "PreySense.Core.Resources.Rgb.OemUsbKeyboardLayouts.json.gz")
            ?? throw new InvalidOperationException("Embedded OEM USB keyboard layouts are missing.");
        using var decoded = new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Decompress);
        using var document = JsonDocument.Parse(decoded);
        var layouts = new Dictionary<string, OemUsbKey[]>(StringComparer.Ordinal);
        static OemUsbKey[] ReadKeys(JsonElement array) => array.EnumerateArray().Select(k => new OemUsbKey(
            k.GetProperty("index").GetInt32(), k.GetProperty("name").GetString()!, k.GetProperty("slot").GetInt32())).ToArray();
        var acerKeys = ReadKeys(document.RootElement.GetProperty("Acer").GetProperty("keys"));
        layouts.Add("Acer:0117", acerKeys);
        layouts.Add("Acer:011a", acerKeys);
        layouts.Add("Acer:0119", acerKeys);
        layouts.Add("Darfon:ba51", ReadKeys(document.RootElement.GetProperty("Darfon").GetProperty("keys")));
        foreach (var product in document.RootElement.GetProperty("Sunrex").EnumerateObject())
            layouts.Add($"Sunrex:{product.Name}", ReadKeys(product.Value));

        var modes = new Dictionary<OemUsbKeyboardFamily, IReadOnlyList<OemUsbHardwareMode>>();
        foreach (var family in document.RootElement.GetProperty("Modes").EnumerateObject())
        {
            var list = family.Value.EnumerateArray().Select(m =>
            {
                int Number(string property) => m.GetProperty(property).GetInt32();
                return new OemUsbHardwareMode((byte)Number("id"), m.GetProperty("name").GetString()!, (uint)Number("flags"),
                    Number("speedMin"), Number("speedMax"), Number("brightnessMin"), Number("brightnessMax"),
                    Number("defaultSpeed"), Number("defaultBrightness"), Number("defaultDirection"), Number("colorMode"));
            }).ToArray();
            modes.Add(Enum.Parse<OemUsbKeyboardFamily>(family.Name), Array.AsReadOnly(list));
        }

        return (layouts, modes);
    }
}
