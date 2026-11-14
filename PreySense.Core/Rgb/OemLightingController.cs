using System.Drawing;
using System.Security.Cryptography;
using System.ServiceProcess;
using System.Text;
using PreySense.Core.Configuration;

namespace PreySense.Rgb;

/// <summary>Owns detected OEM lighting transports. Discovery reads capabilities, never colors.</summary>
public sealed class OemLightingController : IDisposable
{
    private sealed record Device(LightingDeviceInfo Info, Action<DeviceLightingConfig> Apply);
    private readonly object _gate = new();
    private readonly Dictionary<string, Device> _devices = new(StringComparer.Ordinal);
    private readonly List<IDisposable> _transports = new();
    private IReadOnlyList<AcerEcLightingDevice> _ecDevices = Array.Empty<AcerEcLightingDevice>();
    private bool _discovered, _disposed;

    public IReadOnlyList<LightingDeviceInfo> Discover(bool refresh = false)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            // Acer KYD/ENEK targets are hardwired for the laptop lifetime — probe once at
            // startup (or first use). refresh is ignored so animation/sync paths cannot re-scan.
            if (!_discovered)
                DiscoverCore();
            return LightingDeviceInfo.UniqueTargets(_devices.Values.Select(d => d.Info));
        }
    }

    private void InvalidateTransports_NoLock()
    {
        foreach (var transport in _transports) transport.Dispose();
        _transports.Clear();
        _devices.Clear();
        _ecDevices = Array.Empty<AcerEcLightingDevice>();
        _discovered = false;
    }

    private void DiscoverCore()
    {
        _discovered = true;
        try
        {
            foreach (var usb in OemUsbLightingDevice.Discover())
            {
                _transports.Add(usb);
                var leds = usb.Keys.Select(k => new LightingLedInfo(k.Index, k.Name)).ToArray();
                if (leds.Length == 0) continue;
                var modes = new List<LightingModeInfo> { new(ModeKeyLighting.FollowOperatingMode, "Mode"), new(-1, "Static / individual keys") };
                modes.AddRange(usb.GetSupportedModes().Select(m => new LightingModeInfo(m.Id, m.Name,
                    m.SupportsSpeed, m.SupportsColor, m.SupportsRandomColors, m.SpeedMin, m.SpeedMax,
                    m.Flags & 0x0e, m.DefaultDirection)));
                var info = new LightingDeviceInfo("usb:" + StableId(usb.Id), usb.Name, "Keyboard", leds,
                    modes, $"{leds.Length} individually addressable keys • hardware lighting effects");
                _devices.Add(info.Id, new(info, settings =>
                {
                    if (settings.Mode != -1 && settings.Mode != ModeKeyLighting.FollowOperatingMode)
                    {
                        var mode = usb.GetSupportedModes().Single(m => m.Id == settings.Mode);
                        byte speed = (byte)ResolveAnimationSpeed(settings, mode.SpeedMin, mode.SpeedMax);
                        if (!usb.TryApplyMode(mode.Id, ColorTranslator.FromHtml(settings.ColorHex), (byte)settings.Brightness, speed,
                            direction: (byte)info.Modes.Single(m => m.Value == settings.Mode).ResolveDirection(settings.Direction),
                            randomColors: settings.RandomColors))
                            throw new IOException(usb.LastError ?? "USB hardware effect failed.");
                        return;
                    }
                    var colors = Enumerable.Repeat(Color.Black, leds.Max(l => l.Index) + 1).ToArray();
                    foreach (var led in leds) colors[led.Index] = ColorFor(settings, led.Index);
                    if (!usb.ApplyColors(colors, checked((byte)settings.Brightness)))
                        throw new IOException(usb.LastError ?? "USB lighting update failed.");
                }));
            }
        }
        catch (Exception ex) { AppLogger.Log($"USB lighting discovery: {ex.Message}"); }

        try
        {
            foreach (var hid in AcerHidLightingDevice.Discover(out string diagnostic))
            {
                _transports.Add(hid);
                var leds = Enumerable.Range(0, hid.LedCount)
                    .Select(i => new LightingLedInfo(i, hid.Kind == "Keyboard" ? $"Zone {i + 1}" : $"LED {i + 1}")).ToArray();
                // Hardware already advertises Static. Keep a separate direct mode only when multiple LEDs need per-LED colors.
                var modes = new List<LightingModeInfo>();
                // Follow operating-mode colors (first, and the default when no profile exists).
                modes.Add(new(ModeKeyLighting.FollowOperatingMode, "Mode"));
                if (hid.LedCount > 1)
                    modes.Add(new(-1, "Custom colors"));
                // Mode/Turbo Key effects use the advertised speed range, just like keyboard
                // sync. Do not suppress their slider merely because they contain one LED.
                modes.AddRange(hid.Modes.Select(m => new LightingModeInfo(m.Value, m.Name,
                    AcerHidLightingProtocol.SupportsAnimationSpeed(hid.Target, hid.LedCount, hid.MaximumSpeed, m.Value),
                    SpeedMinimum: 1, SpeedMaximum: hid.MaximumSpeed,
                    DirectionFlags: hid.Target == 0x21 ? AcerHidLightingProtocol.DirectionFlags((byte)m.Value, hid.HasDirectionField) : 0)));
                var info = new LightingDeviceInfo(hid.Id, hid.Name, hid.Kind, leds, modes,
                    $"{hid.LedCount} independently controlled {(hid.Kind == "Keyboard" ? "zones" : "LEDs")} • detected hardware effects");
                _devices.Add(info.Id, new(info, settings =>
                {
                    if (settings.Mode == -1)
                        hid.ApplyColors(leds.Select(l => Pack(ColorFor(settings, l.Index))).ToArray(), (byte)settings.Brightness);
                    else if (settings.Mode == ModeKeyLighting.FollowOperatingMode)
                    {
                        if (hid.Kind == "Keyboard" && leds.Length == 4)
                            hid.ApplyColors(leds.Select(l => Pack(ColorFor(settings, l.Index))).ToArray(), (byte)settings.Brightness);
                        else
                        {
                            var color = ColorTranslator.FromHtml(settings.ColorHex);
                            hid.ApplyMode(2, 0, (byte)settings.Brightness, 0, color.R, color.G, color.B);
                        }
                    }
                    else
                    {
                        var color = ColorTranslator.FromHtml(settings.ColorHex);
                        hid.ApplyMode((byte)settings.Mode, hid.MaximumSpeed == 0 ? (byte)0 : (byte)ResolveAnimationSpeed(settings, 1, hid.MaximumSpeed),
                            (byte)settings.Brightness,
                            info.Modes.Single(m => m.Value == settings.Mode) is { SupportsDirection: true } directional
                                ? AcerHidLightingProtocol.NativeDirection(directional.ResolveDirection(settings.Direction)) : (byte)0,
                            color.R, color.G, color.B);
                    }
                }));
            }
        }
        catch (Exception ex) { AppLogger.Log($"Acer HID lighting discovery: {ex.Message}"); }

        try
        {
            _ecDevices = AcerEcLightingDevice.Discover(out string diagnostic);
            if (!string.IsNullOrEmpty(diagnostic)) AppLogger.Log($"EC lighting discovery: {diagnostic}");
            foreach (var ec in _ecDevices)
            {
                _transports.Add(ec);
                // The EC transport remains available for failed HID writes, but is not a second keyboard in the UI.
                if (ec.IsKeyboard && _devices.Values.Any(d => d.Info.Kind == "Keyboard")) continue;
                var leds = Enumerable.Range(0, ec.LedCount)
                    .Select(i => new LightingLedInfo(i, ec.IsKeyboard ? $"Zone {i + 1}" : $"LED {i + 1}")).ToArray();
                var modes = new List<LightingModeInfo>();
                modes.Add(new(ModeKeyLighting.FollowOperatingMode, "Mode"));
                if (ec.LedCount > 1)
                    modes.Add(new(-1, "Custom colors"));
                if (ec.IsKeyboard)
                {
                    string[] names = ["Static", "Breathing", "Neon", "Wave", "Ripple", "Zoom", "Snake", "Disco"];
                    modes.AddRange(names.Select((name, value) => new LightingModeInfo(value, name, value != 0,
                        DirectionFlags: value == 3 ? 0x0eu : value == 4 ? 2u : 0)));
                }
                var info = new LightingDeviceInfo(ec.Id, ec.Name, ec.IsKeyboard ? "Keyboard" : "Light bar", leds,
                    modes, ec.IsKeyboard
                        ? $"Keyboard with {ec.LedCount} independent color zones"
                        : "Colors apply to this bar. Hardware animations affect all light bars.");
                _devices.Add(info.Id, new(info, settings =>
                {
                    if (settings.Mode == -1)
                        ec.ApplyColors(leds.Select(l => Pack(ColorFor(settings, l.Index))).ToArray(), checked((byte)settings.Brightness));
                    else if (settings.Mode == ModeKeyLighting.FollowOperatingMode)
                    {
                        ec.ApplyColors(leds.Select(l => Pack(ColorFor(settings, l.Index))).ToArray(), checked((byte)settings.Brightness));
                    }
                    else
                    {
                        var color = ColorTranslator.FromHtml(settings.ColorHex);
                        ec.ApplyMode(checked((byte)settings.Mode), (byte)ResolveAnimationSpeed(settings, 1, 9),
                            checked((byte)settings.Brightness),
                            info.Modes.Single(m => m.Value == settings.Mode) is { SupportsDirection: true } directional
                                ? AcerHidLightingProtocol.NativeDirection(directional.ResolveDirection(settings.Direction)) : (byte)1,
                            color.R, color.G, color.B);
                    }
                }));
            }
        }
        catch (Exception ex) { AppLogger.Log($"EC lighting discovery: {ex.Message}"); }

        // An existing running service can expose additional surfaces. Never undo the user's services setting.
        try
        {
            using var service = new ServiceController("AcerLightingService");
            if (service.Status != ServiceControllerStatus.Running) return;
            // Per-request pipes; clearing _devices on invalidate drops apply closures for GC.
            var pipe = new AcerLightingPipeClient();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            int count = pipe.GetDeviceCountAsync(deadline.Token).GetAwaiter().GetResult();
            if (count > 64) throw new InvalidDataException("OEM lighting service returned too many devices.");
            for (int index = 0; index < count; index++)
            {
                var device = pipe.GetDeviceInfoAsync(index, deadline.Token).GetAwaiter().GetResult();
                var colors = pipe.GetDeviceLedsAsync(index, deadline.Token).GetAwaiter().GetResult();
                if (colors.Length == 0 || colors.Length > 1024) continue;
                var leds = Enumerable.Range(0, colors.Length).Select(i => new LightingLedInfo(i, $"LED {i + 1}")).ToArray();
                var modes = device.Modes.Select(m => new LightingModeInfo(m.Value, m.Name,
                    (m.Flags & 1) != 0, (m.Flags & 0x40) != 0, (m.Flags & 0x80) != 0, m.SpeedMinimum, m.SpeedMaximum,
                    m.Flags & 0x0e, m.Direction)).ToList();
                var direct = device.Modes.FirstOrDefault(m => m.Name.Contains("direct", StringComparison.OrdinalIgnoreCase));
                if (direct != null && colors.Length > 1)
                    modes.Insert(0, new(-1, "Custom colors"));
                if (modes.Count == 0) continue;
                modes.Insert(0, new(ModeKeyLighting.FollowOperatingMode, "Mode"));
                var info = new LightingDeviceInfo($"acer-service:{index}:{StableId(device.Name)}", device.Name,
                    device.DeviceType == 5 ? "Keyboard" : "Lighting device", leds, modes,
                    "Acer Lighting Service • LED numbers follow the controller's order; physical key names are unavailable");
                _devices.Add(info.Id, new(info, settings =>
                {
                    // Service indices can be reassigned after hotplug. Verify identity/count before any write.
                    var live = pipe.GetDeviceInfoAsync(device.Index).GetAwaiter().GetResult();
                    var liveColors = pipe.GetDeviceLedsAsync(device.Index).GetAwaiter().GetResult();
                    if (live.Name != device.Name || live.DeviceType != device.DeviceType || liveColors.Length != leds.Length)
                        throw new IOException("The lighting device changed. Close and reopen the Lighting window if it is not detected automatically.");
                    bool followMode = ModeKeyLighting.IsFollowMode(settings.Mode);
                    int modeValue = settings.Mode == -1 || followMode
                        ? direct?.Value ?? (followMode
                            ? live.Modes.FirstOrDefault(m => m.Name.Equals("Static", StringComparison.OrdinalIgnoreCase))?.Value
                                ?? throw new InvalidOperationException("A static lighting effect is unavailable.")
                            : throw new InvalidOperationException("Individual LEDs are not supported by this controller."))
                        : settings.Mode;
                    var mode = live.Modes.FirstOrDefault(m => m.Value == modeValue)
                        ?? throw new InvalidOperationException("The selected hardware effect is unavailable.");
                    int brightness = Scale(settings.Brightness, mode.BrightnessMinimum, mode.BrightnessMaximum);
                    int speed = ResolveAnimationSpeed(settings, mode.SpeedMinimum, mode.SpeedMaximum);
                    uint packedColor = Pack(ColorTranslator.FromHtml(settings.ColorHex)) | (settings.RandomColors ? 0x01000000u : 0);
                    pipe.SetDeviceModeAsync(device.Index, modeValue, packedColor,
                        speed, brightness, info.Modes.Single(m => m.Value == settings.Mode).ResolveDirection(settings.Direction)).GetAwaiter().GetResult();
                    if (settings.Mode == -1 || (followMode && direct != null))
                        pipe.SetDeviceLedsAsync(device.Index, leds.Select(l => Pack(ColorFor(settings, l.Index))).ToArray()).GetAwaiter().GetResult();
                }));
            }
        }
        catch (Exception ex) { AppLogger.Log($"Acer lighting service discovery: {ex.Message}"); }
    }

    public void Apply(string id, DeviceLightingConfig settings)
    {
        Validate(settings);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_devices.TryGetValue(id, out var device)) throw new InvalidOperationException("Lighting device is no longer available.");
            if (!device.Info.Modes.Any(m => m.Value == settings.Mode)) throw new InvalidOperationException("Lighting effect is not supported by this device.");
            if (settings.RandomColors && !device.Info.Modes.Any(m => m.Value == settings.Mode && m.SupportsRandomColors))
                throw new InvalidOperationException("Random colors are not supported by this effect.");
            var mode = device.Info.Modes.Single(m => m.Value == settings.Mode);
            if (settings.Direction is int direction && !mode.Directions.Any(d => d.Value == direction))
                throw new InvalidOperationException("Direction is not supported by this effect.");
            device.Apply(settings);
        }
    }

    public bool ApplyKeyboardFallback(Color[] colors, byte brightness)
    {
        Discover();
        lock (_gate)
        {
            var keyboard = _ecDevices.FirstOrDefault(d => d.IsKeyboard && d.LedCount == colors.Length);
            if (keyboard == null) return false;
            keyboard.ApplyColors(colors.Select(Pack).ToArray(), brightness);
            return true;
        }
    }

    public bool HasKeyboard => Discover().Any(d => d.Kind == "Keyboard");

    public bool ApplyKeyboardModeFallback(int mode, Color color, byte brightness, byte speed, byte direction)
    {
        if (mode is < 0 or > 7) return false;
        Discover();
        lock (_gate)
        {
            var keyboard = _ecDevices.FirstOrDefault(d => d.IsKeyboard);
            if (keyboard == null) return false;
            keyboard.ApplyMode((byte)mode, (byte)Math.Clamp((int)speed, 1, 9), brightness, direction, color.R, color.G, color.B);
            return true;
        }
    }

    private static int ResolveAnimationSpeed(DeviceLightingConfig settings, int minimum, int maximum)
    {
        if (settings.AnimationSpeedLevel is not int level) return LightingModeInfo.NativeSpeed(settings.Speed, minimum, maximum);
        if (level < minimum || level > maximum) throw new ArgumentOutOfRangeException(nameof(settings.AnimationSpeedLevel));
        return level;
    }

    private static int Scale(int percent, int minimum, int maximum) =>
        maximum > minimum ? minimum + (int)Math.Round((maximum - (double)minimum) * percent / 100) : minimum;
    private static string StableId(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
    private static uint Pack(Color c) => (uint)(c.R | c.G << 8 | c.B << 16);
    private static Color ColorFor(DeviceLightingConfig settings, int index)
    {
        if (settings.Mode == ModeKeyLighting.FollowOperatingMode && settings.LedColors.Count > 0)
        {
            if (settings.LedColors.TryGetValue(index, out string? modeLedColor))
                return ColorTranslator.FromHtml(modeLedColor);
            if (settings.LedColors.TryGetValue(index % 4, out string? quadColor))
                return ColorTranslator.FromHtml(quadColor);
        }
        return ColorTranslator.FromHtml(
            settings.IndividualColors && settings.LedColors.TryGetValue(index, out string? color) ? color : settings.ColorHex);
    }
    public static void Validate(DeviceLightingConfig settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.AnimationSpeedLevel is < 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(settings.AnimationSpeedLevel));
        if (settings.Direction is < 0 or > 5)
            throw new ArgumentOutOfRangeException(nameof(settings.Direction));
        if (settings.Brightness is < 0 or > 100 || settings.Speed is < 0 or > 100 || settings.LedColors is null || settings.LedColors.Count > 1024)
            throw new ArgumentOutOfRangeException(nameof(settings), "Invalid lighting level or color count.");
        static void CheckColor(string value)
        {
            if (value is null || value.Length != 7 || value[0] != '#'
                || !uint.TryParse(value.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out _))
                throw new ArgumentException("Lighting colors must be #RRGGBB.");
        }
        CheckColor(settings.ColorHex);
        foreach (var pair in settings.LedColors)
        {
            if (pair.Key < 0 || pair.Key > 4095) throw new ArgumentOutOfRangeException(nameof(settings));
            CheckColor(pair.Value);
        }
        settings.PowerModeColors ??= new();
        if (settings.PowerModeColors.Count > 32) throw new ArgumentOutOfRangeException(nameof(settings), "Too many power-mode colors.");
        foreach (var pair in settings.PowerModeColors)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > 8) throw new ArgumentOutOfRangeException(nameof(settings));
            CheckColor(pair.Value);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            InvalidateTransports_NoLock();
        }
    }
}
