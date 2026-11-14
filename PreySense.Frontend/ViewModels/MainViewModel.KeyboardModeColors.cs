using System.Collections.ObjectModel;
using System.ComponentModel;
using PreySense.Core.Configuration;
using PreySense.Rgb;

namespace PreySense.Frontend.ViewModels;

public partial class MainViewModel
{
    private int _modeLightingBrightness = 100;
    public int ModeLightingBrightness
    {
        get => _modeLightingBrightness;
        set
        {
            int brightness = Math.Clamp(value, 0, 100);
            if (!SetProperty(ref _modeLightingBrightness, brightness)) return;
            if (_isLoadingLighting) return;
            ConfigurationService.Instance.Update(config => config.Lighting.ModeBrightness = brightness);
            if (!_disposed && _dispatcher != null) _ = ApplySharedModePaletteAsync();
        }
    }

    internal void StepSharedModeBrightness(int delta) =>
        ModeLightingBrightness = StepFnBrightness(ModeLightingBrightness, delta);

    private Dictionary<string, string> _keyboardPowerModeColors = new();
    public ObservableCollection<ModePowerModeColorEditor> KeyboardModeColorEditors { get; } = new();
    private ModePowerModeColorEditor? _selectedKeyboardModeColor;
    public ModePowerModeColorEditor? SelectedKeyboardModeColor
    {
        get => _selectedKeyboardModeColor;
        set
        {
            if (SetProperty(ref _selectedKeyboardModeColor, value))
                OnPropertyChanged(nameof(HasSelectedKeyboardModeColor));
        }
    }
    public bool HasSelectedKeyboardModeColor => SelectedKeyboardModeColor != null;

    private void LoadKeyboardModeColors(IReadOnlyDictionary<string, string>? colors)
    {
        _modeLightingBrightness = ConfigurationService.Instance.GetCurrent().Lighting.ModeBrightness;
        OnPropertyChanged(nameof(ModeLightingBrightness));
        _keyboardPowerModeColors = ModeKeyLighting.OnlyCustomOverrides(colors);
        foreach (var editor in KeyboardModeColorEditors)
            editor.PropertyChanged -= OnKeyboardModeColorEdited;
        KeyboardModeColorEditors.Clear();
        var settings = new DeviceLightingConfig { PowerModeColors = _keyboardPowerModeColors };
        foreach (var slot in ModeKeyLighting.Slots)
        {
            var editor = new ModePowerModeColorEditor(slot.PowerMode, slot.Name, slot.DefaultHex,
                ModeKeyLighting.ColorFor(settings, slot.PowerMode));
            editor.PropertyChanged += OnKeyboardModeColorEdited;
            KeyboardModeColorEditors.Add(editor);
        }
        SelectedKeyboardModeColor = KeyboardModeColorEditors.FirstOrDefault(e => e.PowerMode == ActivePowerMode)
            ?? KeyboardModeColorEditors.FirstOrDefault();
    }

    private void OnKeyboardModeColorEdited(object? sender, PropertyChangedEventArgs e)
    {
        if (_isLoadingLighting || e.PropertyName != nameof(ModePowerModeColorEditor.ColorHex)) return;
        _keyboardPowerModeColors = ModeKeyLighting.OnlyCustomOverrides(KeyboardModeColorEditors
            .ToDictionary(editor => ModeKeyLighting.Key(editor.PowerMode), editor => editor.ColorHex));
        ConfigurationService.Instance.Update(config =>
            config.Lighting.PowerModeColors = new(_keyboardPowerModeColors, StringComparer.OrdinalIgnoreCase));
        // Editing a different slot must not repaint the current mode or move selection.
        if (sender is not ModePowerModeColorEditor edited || edited.PowerMode != ActivePowerMode) return;
        UpdateKeyboardForMode(ActivePowerMode);
        if (_disposed || _dispatcher == null) return;
        _ = ApplySharedModePaletteAsync();
    }
    internal async Task ApplySharedModePaletteAsync()
    {
        if (_disposed) return;
        if (_lightingWorker != null && _dispatcher != null && _zoneHexColors != null)
        {
            LightingWork work = LightingWork.None;
            bool keyboardMode = string.Equals(_lightingEffectTag, "MODE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(_lightingEffectTag, "HW:Mode", StringComparison.OrdinalIgnoreCase);
            if (keyboardMode) work |= LightingWork.Keyboard;
            if (CoverLightingMode == 4 || (keyboardMode && IsCoverSyncWithKeyboard)) work |= LightingWork.Cover;
            if (work != LightingWork.None) await QueueLighting(work);
        }
        if (!_disposed) await Task.Run(() => ReapplyExtendedModeLighting(includeOtherHotkeyProfiles: false));
    }
}

