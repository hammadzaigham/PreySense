using System.Drawing;
using PreySense.Core.Configuration;
using PreySense.Frontend.Services;
using PreySense.Rgb;

namespace PreySense.Frontend.ViewModels;

public partial class MainViewModel
{
    [Flags]
    internal enum LightingWork { None = 0, Keyboard = 1, Cover = 2, Save = 4, Brightness = 8, Frame = 16 }
    internal sealed record LightingRequest(KeyboardLightingConfig Keyboard, CoverLightingConfig Cover, LightingWork Work,
        int? SavedKeyboardBrightness = null);

    internal static LightingRequest MergeLightingRequests(LightingRequest pending, LightingRequest latest)
    {
        // Animation samples are transient: they must never replace or inherit a settings save.
        if (latest.Work == LightingWork.Frame && pending.Work != LightingWork.Frame) return pending;
        if (pending.Work == LightingWork.Frame && latest.Work != LightingWork.Frame) return latest;
        return latest with { Work = pending.Work | latest.Work };
    }
    private readonly LatestBackgroundWorker<LightingRequest> _lightingWorker;

    // Only the dispatcher captures mutable editor state; the worker owns these copies.
    private LightingRequest CaptureLighting(LightingWork work)
    {
        _isApplyingLighting = true;
        try
        {
            if (IsCoverSyncWithKeyboard && (work & (LightingWork.Keyboard | LightingWork.Cover | LightingWork.Brightness)) != 0)
                MirrorCoverFromKeyboard();
            UpdateCoverSyncFrameHook();

            string selected = SelectedHexColor;
            string z1 = _zoneHexColors[0], z2 = _zoneHexColors[1], z3 = _zoneHexColors[2], z4 = _zoneHexColors[3];
            int brightness = RgbBrightness;
            bool dyn = _lightingEffectTag.StartsWith("DYN:", StringComparison.Ordinal);
            bool idleOff = dyn && _hw is ProductionHardwareService prod && prod.LightingEngine.IsIdleDimmed;

            // Software-frame sync: stamp the live averaged frame so Mode Key matches Logo,
            // and force keyboard brightness 0 while software idle has the backlight off.
            // Cover Enabled/Brightness stay at the UI values so idle never persists Enabled=false.
            if (work.HasFlag(LightingWork.Frame) && dyn)
            {
                string live = AverageZoneHexColor();
                selected = z1 = z2 = z3 = z4 = live;
            }
            if (idleOff)
                brightness = 0;

            return new(new()
            {
                Brightness = brightness, Speed = RgbSpeed, BacklightTimeoutSeconds = KbdTimeoutSeconds,
                SelectedColorHex = selected, SelectedZone = SelectedRgbZone, SyncZones = IsRgbSyncZonesEnabled,
                Zone1ColorHex = z1, Zone2ColorHex = z2, Zone3ColorHex = z3, Zone4ColorHex = z4,
                EffectTag = _lightingEffectTag, PresetTheme = _selectedZonePreset,
                PowerModeColors = new(),
                Direction = HasKeyboardLightingDirection ? KeyboardLightingDirection : _keyboardLightingDirection
            }, new()
            {
                Enabled = CoverBrightness > 0, Mode = CoverLightingMode, ColorHex = CoverHexColor,
                Brightness = CoverBrightness, Speed = CoverSpeed, SyncWithKeyboard = IsCoverSyncWithKeyboard
            }, work, SavedKeyboardBrightness: RgbBrightness);
        }
        finally { _isApplyingLighting = false; }
    }

    private Task QueueLighting(LightingWork work)
    {
        if (_disposed) return Task.CompletedTask;
        if (!_dispatcher.CheckAccess())
            return _dispatcher.InvokeAsync(() => QueueLighting(work)).Task.Unwrap();
        return _lightingWorker.Submit(CaptureLighting(work));
    }

    internal void ApplyLightingRequest(LightingRequest request)
    {
        if (_disposed) return;
        var keyboard = request.Keyboard;
        var cover = request.Cover;
        var color = ColorTranslator.FromHtml(keyboard.SelectedColorHex);
        bool keyboardMode = keyboard.EffectTag.Equals("MODE", StringComparison.OrdinalIgnoreCase)
            || keyboard.EffectTag.Equals("HW:Mode", StringComparison.OrdinalIgnoreCase);
        var sharedLighting = ConfigurationService.Instance.GetCurrent().Lighting;
        byte brightness = (byte)Math.Clamp(keyboardMode ? sharedLighting.ModeBrightness : keyboard.Brightness, 0, 100);
        bool fullKeyboard = request.Work.HasFlag(LightingWork.Keyboard);
        if (fullKeyboard)
        {
            bool isMode = keyboard.EffectTag.Equals("MODE", StringComparison.OrdinalIgnoreCase)
                || keyboard.EffectTag.Equals("HW:Mode", StringComparison.OrdinalIgnoreCase);
            bool dynamic = !isMode && keyboard.EffectTag.StartsWith("DYN:", StringComparison.Ordinal);
            if (_hw is ProductionHardwareService prod)
            {
                var engine = prod.LightingEngine;
                if (!dynamic) engine.StopAndWait();
                else if (Enum.TryParse<CustomEffectMode>(keyboard.EffectTag[4..], out var effect))
                {
                    engine.SetIdleTimeoutSeconds(keyboard.BacklightTimeoutSeconds);
                    engine.ActiveBrushColor = color;
                    if (engine.IsRunning && engine.CurrentMode == effect)
                    {
                        engine.SetBrightness(brightness);
                        engine.SetSpeed((byte)Math.Clamp(keyboard.Speed, 0, 100));
                    }
                    else
                    {
                        engine.StopAndWait();
                        engine.StartEffect(effect, brightness, keyboard.Speed);
                    }
                }
            }
            if (isMode)
            {
                // Resolve on every apply; stale static colors cannot replace Mode.
                var (z1, z2, z3, z4) = ModeKeyLighting.GetZoneColors(ActivePowerMode,
                    new DeviceLightingConfig { PowerModeColors = ConfigurationService.Instance.GetCurrent().Lighting.PowerModeColors });
                _hw.SetRgbAllZones(
                    ColorTranslator.FromHtml(z1), ColorTranslator.FromHtml(z2),
                    ColorTranslator.FromHtml(z3), ColorTranslator.FromHtml(z4), brightness);
            }
            else if (!dynamic)
            {
                if (keyboard.EffectTag.StartsWith("HW:", StringComparison.Ordinal) &&
                    int.TryParse(keyboard.EffectTag.AsSpan(3), out int mode) && mode != 0)
                    _hw.SetRgbHardwareEffect(mode, color.R, color.G, color.B, brightness, ToHidKeyboardSpeed(keyboard.Speed),
                        mode is 3 or 4 && keyboard.Direction is int direction ? AcerHidLightingProtocol.NativeDirection(direction) : (byte)1);
                else
                {
                    _hw.SetRgbAllZones(
                        ColorTranslator.FromHtml(keyboard.Zone1ColorHex), ColorTranslator.FromHtml(keyboard.Zone2ColorHex),
                        ColorTranslator.FromHtml(keyboard.Zone3ColorHex), ColorTranslator.FromHtml(keyboard.Zone4ColorHex), brightness);
                }
            }
            _hw.SetKbdBacklightTimeoutSeconds(keyboard.BacklightTimeoutSeconds);
        }
        else if (request.Work.HasFlag(LightingWork.Brightness) && _hw is ProductionHardwareService prod)
        {
            // Fn brightness already changed firmware; only update the cached/software-effect state.
            prod.Wmi.NoteBrightness(brightness);
            if (prod.LightingEngine.IsRunning) prod.LightingEngine.SetBrightness(brightness);
        }
        bool dynFrame = request.Work.HasFlag(LightingWork.Frame)
            && keyboard.EffectTag.StartsWith("DYN:", StringComparison.Ordinal);
        bool coverWrite = request.Work.HasFlag(LightingWork.Cover)
            || ((fullKeyboard || request.Work.HasFlag(LightingWork.Brightness)) && cover.SyncWithKeyboard);
        bool coverFrameWrite = dynFrame && cover.SyncWithKeyboard;
        if ((coverWrite || coverFrameWrite) && _hw.IsCoverLightingAvailable)
        {
            bool syncKeyboardMode = cover.SyncWithKeyboard &&
                (keyboard.EffectTag.Equals("MODE", StringComparison.OrdinalIgnoreCase)
                || keyboard.EffectTag.Equals("HW:Mode", StringComparison.OrdinalIgnoreCase));
            bool modeCover = syncKeyboardMode || cover.Mode == 4;
            bool coverEnabled = modeCover ? sharedLighting.ModeBrightness > 0 : cover.Enabled;
            int mode = !coverEnabled ? 0 : cover.SyncWithKeyboard
                ? SyncedCoverModeFromKeyboardEffect(keyboard.EffectTag)
                : cover.Mode == 4 ? 1 : cover.Mode;
            var coverColor = syncKeyboardMode
                ? ColorTranslator.FromHtml(ModeKeyLighting.ColorFor(new DeviceLightingConfig { PowerModeColors = ConfigurationService.Instance.GetCurrent().Lighting.PowerModeColors }, ActivePowerMode))
                : cover.Mode == 4
                ? ColorTranslator.FromHtml(ModeKeyLighting.ColorFor(new DeviceLightingConfig { PowerModeColors = ConfigurationService.Instance.GetCurrent().Lighting.PowerModeColors }, ActivePowerMode))
                : ColorTranslator.FromHtml(cover.ColorHex);
            _hw.SetCoverLogoLighting(mode, coverColor,
                (byte)Math.Clamp(syncKeyboardMode || cover.Mode == 4 ? sharedLighting.ModeBrightness
                    : cover.SyncWithKeyboard ? brightness : cover.Brightness, 0, 100),
                ToHidLogoSpeed(cover.SyncWithKeyboard ? keyboard.Speed : cover.Speed), coverEnabled);
        }
        // Mode Key frame sync must run even when Cover sync is off (idle-off included).
        if ((fullKeyboard || request.Work.HasFlag(LightingWork.Brightness) || dynFrame)
            && AnyModeKeySyncWithKeyboard())
            ApplySyncedModeKeyDevices(keyboard);
        if (request.Work.HasFlag(LightingWork.Save))
        {
            var brightnessTargets = request.Work.HasFlag(LightingWork.Brightness)
                && _hw is IExtendedLightingHardware lighting
                    ? lighting.DiscoverLightingDevices().Where(d => d.Kind == "Keyboard").Select(d => d.Id).ToArray()
                    : Array.Empty<string>();
            ConfigurationService.Instance.Update(config =>
            {
                // Idle dimming affects hardware output, never the user's saved brightness.
                if (request.SavedKeyboardBrightness is int savedBrightness)
                    keyboard.Brightness = savedBrightness;
                config.Lighting.Keyboard = keyboard;
                config.Lighting.Cover = cover;
                foreach (string id in brightnessTargets)
                    if (config.Lighting.Devices.TryGetValue(id, out var saved)) saved.Brightness = keyboard.Brightness;
            });
        }
    }
}
