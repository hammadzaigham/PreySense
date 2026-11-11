using System.Drawing;
using System.Text.Json;

namespace PreySense.Rgb;

public sealed record OemUsbHardwareMode(byte Id, string Name, uint Flags, int SpeedMin, int SpeedMax,
    int BrightnessMin, int BrightnessMax, int DefaultSpeed, int DefaultBrightness, int DefaultDirection, int ColorMode)
{
    public bool SupportsSpeed => (Flags & 1) != 0;
    public bool SupportsDirection => (Flags & 0x0e) != 0;
    public bool SupportsColor => (Flags & 0x40) != 0;
    public bool SupportsRandomColors => (Flags & 0x80) != 0;
}
public sealed record OemUsbFeatureStep(byte[] Report, int DelayAfterMs);

public static partial class OemUsbLightingProtocol
{
    private static readonly Lazy<Dictionary<OemUsbKeyboardFamily, IReadOnlyList<OemUsbHardwareMode>>> Modes = new(LoadModes);

    public static IReadOnlyList<OemUsbHardwareMode> GetSupportedModes(OemUsbKeyboardFamily family, ushort productId)
    {
        _ = GetLayout(family, productId);
        return Modes.Value[family];
    }

    /// <summary>OEM mode metadata caps native brightness at 50. UI percentages map linearly into that range.</summary>
    public static byte MapBrightnessPercent(byte percent) => (byte)(Math.Min(100, (int)percent) / 2);

    /// <summary>Builds only advertised ordinary keyboard modes. Speed and direction use the OEM raw values.</summary>
    public static IReadOnlyList<OemUsbFeatureStep> BuildModeTransfer(OemUsbKeyboardFamily family, ushort productId,
        byte modeId, Color color, byte brightnessPercent, byte? speed = null, byte direction = 0, bool randomColors = false)
    {
        var mode = GetSupportedModes(family, productId).SingleOrDefault(m => m.Id == modeId)
            ?? throw new NotSupportedException($"Mode {modeId:X2} is not a verified {family} keyboard effect.");
        var rawSpeed = speed ?? (byte)mode.DefaultSpeed;
        if (mode.SupportsSpeed && (rawSpeed < mode.SpeedMin || rawSpeed > mode.SpeedMax))
            throw new ArgumentOutOfRangeException(nameof(speed));
        if (!mode.SupportsSpeed) rawSpeed = (byte)mode.DefaultSpeed;
        if (direction > 7 || (!mode.SupportsDirection && direction != 0))
            throw new ArgumentOutOfRangeException(nameof(direction));
        if (randomColors && !mode.SupportsRandomColors)
            throw new ArgumentException("This mode has no recovered random-color capability.", nameof(randomColors));
        var brightness = MapBrightnessPercent(brightnessPercent);
        var rgb = mode.SupportsColor && !randomColors ? color : Color.Black;
        byte[] Command(params byte[] data)
        {
            if (data.Length != 7) throw new InvalidOperationException();
            var report = new byte[9];
            data.CopyTo(report, 1);
            report[8] = unchecked((byte)~data.Sum(b => b));
            return report;
        }
        byte[] Begin() => Command(0xb1, 0, 0, 0, 0, 0, 0);
        byte[] Reset() => Command(8, 2, 0, 0, 0, 0, 0);
        byte[] Rgb() => Command(0x14, 0, 0, rgb.R, rgb.G, rgb.B, 0);
        OemUsbFeatureStep Step(byte[] report, bool final = false) => new(report, final ? 0 : 15);
        if (modeId == 0)
        {
            if (family == OemUsbKeyboardFamily.Sunrex)
                return new[] { Step(Command(8, 1, 0, 0, 0, 0, 0), true) };
            return new[] { Step(Begin()), Step(Reset()), Step(Command(0x14, 0, 0, 0, 0, 0, 0)),
                Step(Command(8, family == OemUsbKeyboardFamily.Darfon ? (byte)0 : (byte)2, 1, 1, 0, 1, 0), true) };
        }
        var wireSpeed = unchecked((byte)(10 - rawSpeed));
        if (family == OemUsbKeyboardFamily.Sunrex)
        {
            var wireDirection = modeId == 3 ? (byte)(direction + 1) : (byte)0;
            var modeReport = Command(8, 2, modeId, wireSpeed, brightness, randomColors ? (byte)0xe0 : (byte)0, wireDirection);
            return new[] { Step(Begin()), Step(Reset()), Step(modeReport), Step(Rgb(), true) };
        }
        var nativeMode = Command(8, family == OemUsbKeyboardFamily.Darfon ? (byte)0 : (byte)2,
            modeId, wireSpeed, brightness, 1, (byte)(direction + 1));
        return family == OemUsbKeyboardFamily.Darfon
            ? new[] { Step(Begin()), Step(Reset()), Step(Rgb()), Step(nativeMode, true) }
            : new[] { Step(Begin()), Step(Rgb()), Step(nativeMode, true) };
    }

    private static Dictionary<OemUsbKeyboardFamily, IReadOnlyList<OemUsbHardwareMode>> LoadModes()
    {
        using var stream = typeof(OemUsbLightingProtocol).Assembly.GetManifestResourceStream(
            "PreySense.Core.Resources.Rgb.OemUsbKeyboardLayouts.json")!;
        using var document = JsonDocument.Parse(stream);
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
        return modes;
    }
}

public sealed partial class OemUsbLightingDevice
{
    public IReadOnlyList<OemUsbHardwareMode> GetSupportedModes() => OemUsbLightingProtocol.GetSupportedModes(Family, ProductId);

    public bool TryApplyMode(byte modeId, Color color, byte brightnessPercent, byte? speed = null,
        byte direction = 0, bool randomColors = false)
    {
        var steps = OemUsbLightingProtocol.BuildModeTransfer(Family, ProductId, modeId, color, brightnessPercent, speed, direction, randomColors);
        lock (_gate)
        {
            if (_disposed) { LastError = "Device is closed."; return false; }
            if (!_initialized)
            {
                if (!WriteOutput(OemUsbLightingProtocol.BuildInitializationReport())) return false;
                Thread.Sleep(2);
                _initialized = true;
            }
            foreach (var step in steps)
            {
                if (!WriteFeature(step.Report)) return false;
                if (step.DelayAfterMs != 0) Thread.Sleep(step.DelayAfterMs);
            }
            LastError = null;
            return true;
        }
    }
}
