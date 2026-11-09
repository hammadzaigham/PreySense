using System.Drawing;
using PreySense.Core.Configuration;
using PreySense.Frontend.Services;
using PreySense.Rgb;

namespace PreySense.Frontend.ViewModels;

public partial class MainViewModel
{
    [Flags]
    internal enum LightingWork { None = 0, Keyboard = 1, Cover = 2, Save = 4, Brightness = 8, Frame = 16 }
    internal sealed record LightingRequest(KeyboardLightingConfig Keyboard, CoverLightingConfig Cover, LightingWork Work);
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
            return new(new()
            {
                Brightness = RgbBrightness, Speed = RgbSpeed, BacklightTimeoutSeconds = KbdTimeoutSeconds,
                SelectedColorHex = SelectedHexColor, SelectedZone = SelectedRgbZone, SyncZones = IsRgbSyncZonesEnabled,
                Zone1ColorHex = _zoneHexColors[0], Zone2ColorHex = _zoneHexColors[1],
                Zone3ColorHex = _zoneHexColors[2], Zone4ColorHex = _zoneHexColors[3],
                EffectTag = _lightingEffectTag, PresetTheme = _selectedZonePreset
            }, new()
            {
                Enabled = IsCoverLightingEnabled, Mode = CoverLightingMode, ColorHex = CoverHexColor,
                Brightness = CoverBrightness, Speed = CoverSpeed, SyncWithKeyboard = IsCoverSyncWithKeyboard
            }, work);
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
        byte brightness = (byte)Math.Clamp(keyboard.Brightness, 0, 100);
        bool fullKeyboard = request.Work.HasFlag(LightingWork.Keyboard);
        if (fullKeyboard)
        {
            bool dynamic = keyboard.EffectTag.StartsWith("DYN:", StringComparison.Ordinal);
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
            if (!dynamic)
            {
                if (keyboard.EffectTag.StartsWith("HW:", StringComparison.Ordinal) &&
                    int.TryParse(keyboard.EffectTag.AsSpan(3), out int mode) && mode != 0)
                    _hw.SetRgbHardwareEffect(mode, color.R, color.G, color.B, brightness, ToHidKeyboardSpeed(keyboard.Speed), 1);
                else
                {
                    _hw.SetRgbAllZones(
                        ColorTranslator.FromHtml(keyboard.Zone1ColorHex), ColorTranslator.FromHtml(keyboard.Zone2ColorHex),
                        ColorTranslator.FromHtml(keyboard.Zone3ColorHex), ColorTranslator.FromHtml(keyboard.Zone4ColorHex));
                    _hw.SetRgbBrightness(brightness);
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
        bool coverWrite = request.Work.HasFlag(LightingWork.Cover) || ((fullKeyboard || request.Work.HasFlag(LightingWork.Brightness)) && cover.SyncWithKeyboard);
        bool frameWrite = request.Work.HasFlag(LightingWork.Frame) && cover.SyncWithKeyboard && keyboard.EffectTag.StartsWith("DYN:");
        if (coverWrite || frameWrite)
        {
            int mode = !cover.Enabled ? 0 : frameWrite ? 1 : cover.Mode;
            _hw.SetCoverLogoLighting(mode, ColorTranslator.FromHtml(cover.ColorHex),
                (byte)Math.Clamp(cover.SyncWithKeyboard ? keyboard.Brightness : cover.Brightness, 0, 100),
                ToHidLogoSpeed(cover.SyncWithKeyboard ? keyboard.Speed : cover.Speed), cover.Enabled);
        }
        if (request.Work.HasFlag(LightingWork.Save))
            ConfigurationService.Instance.Update(config =>
            {
                config.Lighting.Keyboard = keyboard;
                config.Lighting.Cover = cover;
            });
    }
}
