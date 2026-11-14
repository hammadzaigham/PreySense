using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using PreySense.Core.Configuration;
using PreySense.Frontend.Services;
using PreySense.Rgb;

namespace PreySense.Frontend.ViewModels;

public sealed class LightingLedEditor : ObservableObject
{
    public int Index { get; }
    public string Name { get; }
    private string _colorHex;
    public string ColorHex
    {
        get => _colorHex;
        set { if (SetProperty(ref _colorHex, value)) OnPropertyChanged(nameof(ForegroundHex)); }
    }
    public string ForegroundHex => MainViewModel.ContrastForegroundHex(_colorHex);
    public LightingLedEditor(LightingLedInfo led, string colorHex) => (Index, Name, _colorHex) = (led.Index, led.Name, colorHex);
}

public sealed class ModePowerModeColorEditor : ObservableObject
{
    public byte PowerMode { get; }
    public string Name { get; }
    public string DefaultHex { get; }
    private string _colorHex;
    public string ColorHex
    {
        get => _colorHex;
        set
        {
            string normalized = ModeKeyLighting.NormalizeHex(value);
            if (!SetProperty(ref _colorHex, normalized)) return;
            OnPropertyChanged(nameof(ForegroundHex));
        }
    }
    public string ForegroundHex => MainViewModel.ContrastForegroundHex(_colorHex);
    public ModePowerModeColorEditor(byte powerMode, string name, string defaultHex, string colorHex)
    {
        PowerMode = powerMode;
        Name = name;
        DefaultHex = defaultHex;
        _colorHex = ModeKeyLighting.NormalizeHex(colorHex);
    }
}

public partial class MainViewModel
{
    public ObservableCollection<LightingDeviceInfo> LightingDevices { get; } = new();
    public ObservableCollection<LightingModeInfo> LightingDeviceModes { get; } = new();
    public ObservableCollection<LightingLedEditor> LightingLedEditors { get; } = new();
    public ObservableCollection<LightingLedEditor> SelectedLightingKeys { get; } = new();
    public ObservableCollection<ModePowerModeColorEditor> ModePowerModeColorEditors { get; } = new();
    private ModePowerModeColorEditor? _selectedModePowerModeColor;
    public ModePowerModeColorEditor? SelectedModePowerModeColor
    {
        get => _selectedModePowerModeColor;
        set
        {
            if (!SetProperty(ref _selectedModePowerModeColor, value)) return;
            OnPropertyChanged(nameof(HasSelectedModePowerModeColor));
        }
    }
    public bool HasSelectedModePowerModeColor => SelectedModePowerModeColor != null;
    private UiDebouncer? _deviceLightingDebouncer;
    private bool _loadingDeviceEditor, _propagatingKeyColor, _deviceLightingEditPending;
    private readonly bool _autoDeviceLightingEnabled = true;

    private void ScheduleDeviceLighting()
    {
        if (!_autoDeviceLightingEnabled || _loadingDeviceEditor || _disposed || SelectedLightingDevice == null) return;
        _deviceLightingEditPending = true;
        _deviceLightingDebouncer ??= new(TimeSpan.FromMilliseconds(80));
        _deviceLightingDebouncer.Debounce(() => { _deviceLightingEditPending = false; _ = ApplySelectedLightingDeviceAsync(); });
    }

    private void OnLightingLedColorEdited(object? sender, PropertyChangedEventArgs e)
    {
        if (_loadingDeviceEditor || _propagatingKeyColor || e.PropertyName != nameof(LightingLedEditor.ColorHex)) return;
        if (SyncAllBarLeds && ShowSyncAllBarLeds && sender is LightingLedEditor changed)
            SynchronizeBarLeds(changed.ColorHex);
        if (ReferenceEquals(sender, SelectedLightingKey) && SelectedLightingKeys?.Count > 1)
        {
            _propagatingKeyColor = true;
            try { foreach (var key in SelectedLightingKeys) key.ColorHex = SelectedLightingKey!.ColorHex; }
            finally { _propagatingKeyColor = false; }
        }
        ScheduleDeviceLighting();
    }

    private void ClearLightingLedEditors()
    {
        foreach (var key in LightingLedEditors) key.PropertyChanged -= OnLightingLedColorEdited;
        SelectedLightingKeys?.Clear();
        LightingLedEditors.Clear();
    }

    internal void SetSelectedLightingKeysColor(string color)
    {
        bool changed = false;
        _propagatingKeyColor = true;
        try
        {
            IEnumerable<LightingLedEditor> keys = SyncAllBarLeds && ShowSyncAllBarLeds ? LightingLedEditors
                : SelectedLightingKeys is { Count: > 0 } ? SelectedLightingKeys
                : SelectedLightingKey is { } single ? [single] : Array.Empty<LightingLedEditor>();
            foreach (var key in keys)
            {
                if (key.ColorHex == color) continue;
                key.ColorHex = color; changed = true;
            }
        }
        finally { _propagatingKeyColor = false; }
        if (changed) ScheduleDeviceLighting();
    }
    private LightingDeviceInfo? _selectedLightingDevice;
    public LightingDeviceInfo? SelectedLightingDevice
    {
        get => _selectedLightingDevice;
        set
        {
            if (ReferenceEquals(_selectedLightingDevice, value)) return;
            if (_deviceLightingEditPending && !_loadingDeviceEditor) _ = FlushExtendedLightingEditsAsync();
            if (!SetProperty(ref _selectedLightingDevice, value)) return;
            SyncLightingTabFromSelectedDevice();
            NotifyLightingEditorVisibility();
            LoadSelectedLightingDevice();
        }
    }

    private void SyncLightingTabFromSelectedDevice()
    {
        if (SelectedLightingDevice == null) return;
        if (SelectedLightingDevice.Kind.Equals("Keyboard", StringComparison.OrdinalIgnoreCase))
            SelectedLightingTab = 0;
        else if (IsLogoLightingKind(SelectedLightingDevice))
            SelectedLightingTab = 1;
    }

    private void NotifyLightingEditorVisibility()
    {
        OnPropertyChanged(nameof(ShowLightingDeviceTabs));
        OnPropertyChanged(nameof(ShowLegacyTargetTabs));
        OnPropertyChanged(nameof(IsLogoLightingDevice));
        OnPropertyChanged(nameof(IsLegacyKeyboardEditorVisible));
        OnPropertyChanged(nameof(IsLegacyCoverEditorVisible));
        OnPropertyChanged(nameof(IsDeviceLightingEditorVisible));
        OnPropertyChanged(nameof(ShowBacklightTimeout));
        OnPropertyChanged(nameof(DeviceLightingBrightnessStep));
        OnPropertyChanged(nameof(LightingDeviceTabColumns));
        OnPropertyChanged(nameof(ShowDeviceIndividualColors));
        OnPropertyChanged(nameof(ShowDeviceColorFields));
        OnPropertyChanged(nameof(ShowDeviceColorWheel));
        OnPropertyChanged(nameof(ShowModePowerModeColors));
        OnPropertyChanged(nameof(IsModeKeyFollowMode));
        OnPropertyChanged(nameof(IsModeKeyLightingDevice));
        OnPropertyChanged(nameof(ShowModeKeyIndependentControls));
        OnPropertyChanged(nameof(ShowSyncAllBarLeds));
    }

    private LightingModeInfo? _selectedLightingDeviceMode;
    public LightingModeInfo? SelectedLightingDeviceMode
    {
        get => _selectedLightingDeviceMode;
        set
        {
            if (!SetProperty(ref _selectedLightingDeviceMode, value)) return;
            _deviceLightingNativeSpeed = null;
            _updatingDeviceSpeedRange = true;
            try
            {
                OnPropertyChanged(nameof(DeviceLightingSpeedMinimum));
                OnPropertyChanged(nameof(DeviceLightingSpeedMaximum));
                OnPropertyChanged(nameof(DeviceLightingSpeedLevel));
            }
            finally { _updatingDeviceSpeedRange = false; }
            if (value?.Value == -1)
            {
                DeviceLightingIndividualColors = true;
                if (SelectedLightingKey == null) SelectedLightingKey = LightingLedEditors.FirstOrDefault();
            }
            OnPropertyChanged(nameof(IsDirectLightingDeviceMode));
            OnPropertyChanged(nameof(ShowIndividualLightingEditors));
            OnPropertyChanged(nameof(ShowDeviceIndividualColors));
            OnPropertyChanged(nameof(ShowKeyboardLayout));
            OnPropertyChanged(nameof(ShowIndividualLedGrid));
            OnPropertyChanged(nameof(ShowSyncAllBarLeds));
            OnPropertyChanged(nameof(ShowDeviceColorFields));
            OnPropertyChanged(nameof(ShowDeviceColorWheel));
            OnPropertyChanged(nameof(IsModeKeyFollowMode));
            OnPropertyChanged(nameof(ShowModePowerModeColors));
            OnPropertyChanged(nameof(ShowModeKeyIndependentControls));
            OnPropertyChanged(nameof(HasDeviceLightingSpeed));
            OnPropertyChanged(nameof(HasDeviceLightingRandomColors));
            OnPropertyChanged(nameof(DeviceLightingDirections));
            OnPropertyChanged(nameof(HasDeviceLightingDirection));
            _deviceLightingDirection = value?.ResolveDirection(null) ?? 0;
            OnPropertyChanged(nameof(DeviceLightingDirection));
            if (value?.Value == ModeKeyLighting.FollowOperatingMode && !_loadingDeviceEditor && !IsModeKeySyncWithKeyboard)
                SyncModePowerModeColorEditorsFromProfile();
            // Mirrored Breathing/Neon (4/5) must not clear sync — only a real user pick does.
            if (!_loadingDeviceEditor && IsModeKeySyncWithKeyboard
                && value?.Value != SyncedModeKeyModeFromKeyboardEffect(_lightingEffectTag))
            {
                IsModeKeySyncWithKeyboard = false;
                return;
            }
            ScheduleDeviceLighting();
        }
    }
    public bool HasDeviceLightingSpeed =>
        !IsModeKeySyncWithKeyboard && SelectedLightingDeviceMode?.SupportsSpeed == true;
    public bool HasDeviceLightingRandomColors =>
        !IsModeKeySyncWithKeyboard && SelectedLightingDeviceMode?.SupportsRandomColors == true;
    public IReadOnlyList<LightingDirectionInfo> DeviceLightingDirections => SelectedLightingDeviceMode?.Directions ?? [];
    public bool HasDeviceLightingDirection => !IsModeKeySyncWithKeyboard && SelectedLightingDeviceMode?.SupportsDirection == true;
    private int _deviceLightingDirection;
    public int DeviceLightingDirection
    {
        get => _deviceLightingDirection;
        set
        {
            int selected = SelectedLightingDeviceMode?.ResolveDirection(value) ?? 0;
            if (SetProperty(ref _deviceLightingDirection, selected)) ScheduleDeviceLighting();
        }
    }
    private bool _deviceLightingRandomColors;
    public bool DeviceLightingRandomColors { get => _deviceLightingRandomColors; set { if (SetProperty(ref _deviceLightingRandomColors, value)) ScheduleDeviceLighting(); } }
    public bool IsDirectLightingDeviceMode => SelectedLightingDeviceMode?.Value == -1;
    // Direct/custom modes edit LEDs directly; hardware Static already covers a single shared color.
    public bool ShowIndividualLightingEditors => IsDirectLightingDeviceMode;
    public bool ShowDeviceIndividualColors =>
        IsDirectLightingDeviceMode && (SelectedLightingDevice?.Leds.Count ?? 0) > 1;
    public bool IsPerKeyKeyboard => SelectedLightingDevice?.HasNamedKeys == true;
    public bool ShowLegacyLightingControls => !LightingDevices.Any(d => d.Kind == "Keyboard"
        && d.Leds.Count(l => l.Name.Length == 1 && char.IsLetter(l.Name[0])) >= 20);
    public bool ShowLightingDeviceTabs => HasLightingDevices;
    public int LightingDeviceTabColumns => Math.Max(1, LightingDevices.Count);
    public bool ShowLegacyTargetTabs => !HasLightingDevices && HasCoverLighting;
    public bool IsLogoLightingDevice => SelectedLightingDevice != null && IsLogoLightingKind(SelectedLightingDevice);
    public bool IsLegacyKeyboardEditorVisible =>
        (!HasLightingDevices && IsKeyboardTabSelected)
        || (ShowLegacyLightingControls && SelectedLightingDevice is { Kind: "Keyboard", Leds.Count: 4 });
    public bool IsLegacyCoverEditorVisible =>
        (!HasLightingDevices && IsCoverTabSelected)
        || (ShowLegacyLightingControls && IsLogoLightingDevice && HasCoverLighting);
    public bool IsDeviceLightingEditorVisible =>
        HasLightingDevices && SelectedLightingDevice != null
        && !IsLegacyKeyboardEditorVisible && !IsLegacyCoverEditorVisible;
    // One shared EC keyboard idle timeout (WMI 0x88402). Cover Logo has no separate timeout.
    public bool ShowBacklightTimeout =>
        IsLegacyKeyboardEditorVisible
        || (IsDeviceLightingEditorVisible
            && SelectedLightingDevice?.Kind.Equals("Keyboard", StringComparison.OrdinalIgnoreCase) == true);
    /// <summary>
    /// HID/EC targets accept a 0–100 brightness byte (1% steps). USB OEM maps UI 0–100% into
    /// hardware 0–50 (2% UI steps). Acer Lighting Service scales UI 0–100 into each mode range.
    /// </summary>
    public int DeviceLightingBrightnessStep
    {
        get
        {
            var id = SelectedLightingDevice?.Id ?? "";
            if (id.StartsWith("usb:", StringComparison.OrdinalIgnoreCase)) return 2;
            return 1;
        }
    }
    // Static and Breathing take a base color. Neon is a hardware color cycle — no color UI.
    // Mode uses the five performance-mode color slots instead.
    public bool IsModeKeyLightingDevice =>
        SelectedLightingDevice?.Kind.Equals("Hotkey", StringComparison.OrdinalIgnoreCase) == true;
    private bool _isModeKeySyncWithKeyboard;
    public bool IsModeKeySyncWithKeyboard
    {
        get => _isModeKeySyncWithKeyboard;
        set
        {
            if (!SetProperty(ref _isModeKeySyncWithKeyboard, value)) return;
            OnPropertyChanged(nameof(IsModeKeyFollowMode));
            OnPropertyChanged(nameof(ShowModePowerModeColors));
            OnPropertyChanged(nameof(ShowModeKeyIndependentControls));
            OnPropertyChanged(nameof(ShowDeviceColorFields));
            OnPropertyChanged(nameof(ShowDeviceColorWheel));
            OnPropertyChanged(nameof(HasDeviceLightingSpeed));
            OnPropertyChanged(nameof(HasDeviceLightingRandomColors));
            OnPropertyChanged(nameof(HasDeviceLightingDirection));
            if (_loadingDeviceEditor) return;
            if (value)
                MirrorModeKeyEditorFromKeyboard();
            ScheduleDeviceLighting();
            UpdateCoverSyncFrameHook();
        }
    }
    public bool IsModeKeyFollowMode =>
        !IsModeKeySyncWithKeyboard
        && SelectedLightingDeviceMode?.Value == ModeKeyLighting.FollowOperatingMode;
    public bool ShowModePowerModeColors => IsModeKeyFollowMode;
    public bool ShowModeKeyIndependentControls => IsModeKeyLightingDevice && !IsModeKeySyncWithKeyboard;
    public bool ShowDeviceColorFields =>
        !IsModeKeySyncWithKeyboard
        && !ShowKeyboardLayout
        && SelectedLightingDeviceMode != null
        && !IsModeKeyFollowMode
        && !SelectedLightingDeviceMode.Name.Equals("Neon", StringComparison.OrdinalIgnoreCase);
    public bool ShowDeviceColorWheel =>
        ShowDeviceColorFields
        && !ShowIndividualLedGrid
        && !IsModeKeyFollowMode
        && (SelectedLightingDeviceMode!.Name.Equals("Static", StringComparison.OrdinalIgnoreCase)
            || SelectedLightingDeviceMode.Name.Equals("Breathing", StringComparison.OrdinalIgnoreCase));
    public bool ShowKeyboardLayout => IsPerKeyKeyboard && IsDirectLightingDeviceMode;
    public bool ShowIndividualLedGrid => ShowIndividualLightingEditors && !IsPerKeyKeyboard;

    private static bool IsLogoLightingKind(LightingDeviceInfo device) =>
        device.Kind.Equals("Logo", StringComparison.OrdinalIgnoreCase)
        || device.Name.Contains("logo", StringComparison.OrdinalIgnoreCase);
    private LightingLedEditor? _selectedLightingKey;
    public bool HasSelectedLightingKey => SelectedLightingKey != null;
    public LightingLedEditor? SelectedLightingKey
    {
        get => _selectedLightingKey;
        set
        {
            if (!SetProperty(ref _selectedLightingKey, value)) return;
            OnPropertyChanged(nameof(HasSelectedLightingKey));
            if (value is null) return;
            if (!DeviceLightingIndividualColors)
            {
                foreach (var key in LightingLedEditors) key.ColorHex = DeviceLightingColorHex;
                DeviceLightingIndividualColors = true;
            }
        }
    }
    private string _deviceLightingColorHex = "#00E5FF";
    public string DeviceLightingColorHex { get => _deviceLightingColorHex; set { if (SetProperty(ref _deviceLightingColorHex, value)) ScheduleDeviceLighting(); } }
    private int _deviceLightingBrightness = 100, _deviceLightingSpeed = 50;
    public int DeviceLightingBrightness
    {
        get => _deviceLightingBrightness;
        set
        {
            int step = Math.Max(1, DeviceLightingBrightnessStep);
            int snapped = Math.Clamp((int)Math.Round(value / (double)step) * step, 0, 100);
            if (SetProperty(ref _deviceLightingBrightness, snapped)) ScheduleDeviceLighting();
        }
    }
    private int? _deviceLightingNativeSpeed;
    private bool _updatingDeviceSpeedRange;
    public int DeviceLightingSpeedMinimum => SelectedLightingDeviceMode?.SpeedMinimum ?? 1;
    public int DeviceLightingSpeedMaximum => Math.Max(DeviceLightingSpeedMinimum, SelectedLightingDeviceMode?.SpeedMaximum ?? 1);
    public int DeviceLightingSpeedLevel
    {
        get => Math.Clamp(_deviceLightingNativeSpeed ?? SelectedLightingDeviceMode?.NativeSpeed(DeviceLightingSpeed) ?? 1,
            DeviceLightingSpeedMinimum, DeviceLightingSpeedMaximum);
        set
        {
            if (_loadingDeviceEditor || _updatingDeviceSpeedRange || !HasDeviceLightingSpeed) return;
            int level = Math.Clamp(value, DeviceLightingSpeedMinimum, DeviceLightingSpeedMaximum);
            if (_deviceLightingNativeSpeed == level) return;
            _deviceLightingNativeSpeed = level;
            SetProperty(ref _deviceLightingSpeed, SelectedLightingDeviceMode?.SpeedPercent(level) ?? 0, nameof(DeviceLightingSpeed));
            OnPropertyChanged();
            ScheduleDeviceLighting();
        }
    }
    public int DeviceLightingSpeed
    {
        get => _deviceLightingSpeed;
        set
        {
            if (!SetProperty(ref _deviceLightingSpeed, Math.Clamp(value, 0, 100))) return;
            _deviceLightingNativeSpeed = null;
            OnPropertyChanged(nameof(DeviceLightingSpeedLevel));
            ScheduleDeviceLighting();
        }
    }
    private bool _deviceLightingIndividualColors, _isRefreshingLightingDevices;
    public bool DeviceLightingIndividualColors
    {
        get => _deviceLightingIndividualColors;
        set
        {
            if (!SetProperty(ref _deviceLightingIndividualColors, value)) return;
            OnPropertyChanged(nameof(ShowIndividualLightingEditors));
            OnPropertyChanged(nameof(ShowIndividualLedGrid));
            OnPropertyChanged(nameof(ShowDeviceIndividualColors));
            ScheduleDeviceLighting();
        }
    }
    public bool IsRefreshingLightingDevices { get => _isRefreshingLightingDevices; private set => SetProperty(ref _isRefreshingLightingDevices, value); }
    private string _lightingDeviceStatus = "Detecting lighting devices…";
    public string LightingDeviceStatus { get => _lightingDeviceStatus; private set => SetProperty(ref _lightingDeviceStatus, value); }
    public bool HasLightingDevices => LightingDevices.Count != 0;
    private bool _hasCoverLighting;
    public bool HasCoverLighting { get => _hasCoverLighting; private set => SetProperty(ref _hasCoverLighting, value); }
    private LatestBackgroundWorker<Dictionary<string, ExtendedLightingRequest>>? _extendedLightingWorker;
    private Task? _deviceLightingSubmissions;
    private sealed record ExtendedLightingRequest(string DeviceId, string SettingsJson, bool DisableCoverSync);

    private bool _lightingDeviceDetectionPending;
    public async Task DetectLightingDevicesAsync()
    {
        if (!_dispatcher.CheckAccess()) { await _dispatcher.InvokeAsync(DetectLightingDevicesAsync).Task.Unwrap(); return; }
        if (_disposed) return;
        if (IsRefreshingLightingDevices) { _lightingDeviceDetectionPending = true; return; }
        if (_hw is not IExtendedLightingHardware hardware)
        {
            LightingDeviceStatus = "Additional lighting devices are unavailable with this hardware provider.";
            return;
        }
        IsRefreshingLightingDevices = true;
        LightingDeviceStatus = "Discovering lighting devices…";
        try
        {
            var detected = await Task.Run(() => (Devices: hardware.DiscoverLightingDevices(), Cover: _hw.IsCoverLightingAvailable));
            var devices = LightingDeviceInfo.UniqueTargets(detected.Devices);
            if (_disposed) return;
            HasCoverLighting = detected.Cover;
            if (!HasCoverLighting && IsCoverTabSelected) SelectedLightingTab = 0;
            string? previous = SelectedLightingDevice?.Id;
            var draft = SelectedLightingDevice == null ? null : CaptureDeviceLightingSettings();
            int? selectedKey = SelectedLightingKey?.Index;
            LightingDevices.Clear();
            foreach (var device in devices) LightingDevices.Add(device);
            SelectedLightingDevice = LightingDevices.FirstOrDefault(device => device.Id == previous) ?? LightingDevices.FirstOrDefault();
            if (SelectedLightingDevice?.Id == previous && draft != null)
            {
                LoadDeviceLightingSettings(draft);
                if (draft.IndividualColors) SelectedLightingKey = LightingLedEditors.FirstOrDefault(k => k.Index == selectedKey);
            }
            OnPropertyChanged(nameof(HasLightingDevices));
            OnPropertyChanged(nameof(ShowLegacyLightingControls));
            OnPropertyChanged(nameof(LightingDeviceTabColumns));
            NotifyKeyboardLightingDirection();
            NotifyLightingEditorVisibility();
            LightingDeviceStatus = devices.Count == 0
                ? "Using built-in keyboard lighting."
                : (SelectedLightingDevice?.Details ?? $"{devices.Count} lighting device(s) detected.");
        }
        catch (Exception ex) { LightingDeviceStatus = $"Lighting discovery failed: {ex.Message}"; }
        finally
        {
            IsRefreshingLightingDevices = false;
            if (_lightingDeviceDetectionPending) { _lightingDeviceDetectionPending = false; _ = DetectLightingDevicesAsync(); }
        }
    }

    private void LoadSelectedLightingDevice()
    {
        bool wasLoading = _loadingDeviceEditor;
        _loadingDeviceEditor = true;
        try
        {
            LightingDeviceModes.Clear();
            ClearLightingLedEditors();
            SelectedLightingKey = null;
            OnPropertyChanged(nameof(IsPerKeyKeyboard));
            OnPropertyChanged(nameof(ShowKeyboardLayout));
            OnPropertyChanged(nameof(ShowIndividualLedGrid));
            var device = SelectedLightingDevice;
            if (device is null) { SelectedLightingDeviceMode = null; return; }
            var saved = ConfigurationService.Instance.GetCurrent().Lighting.Devices;
            var settings = saved.TryGetValue(device.Id, out var profile) ? profile : new DeviceLightingConfig();
            if (!saved.ContainsKey(device.Id) && device.Kind == "Keyboard" && device.Leds.Count == 4 && _zoneHexColors is { Length: 4 })
            {
                settings.Brightness = RgbBrightness;
                settings.Speed = RgbSpeed;
                settings.ColorHex = _zoneHexColors[0];
                settings.IndividualColors = !IsRgbSyncZonesEnabled;
                settings.LedColors = device.Leds.Select((led, index) => (led.Index, Color: _zoneHexColors[index]))
                    .ToDictionary(item => item.Index, item => item.Color);
            }
            // Missing profiles use the same Mode default for every physical lighting target.
            foreach (var mode in device.GetEditorModes()) LightingDeviceModes.Add(mode);
            LoadDeviceLightingSettings(settings);
            LightingDeviceStatus = device.Details;
            NotifyLightingEditorVisibility();
        }
        finally { _loadingDeviceEditor = wasLoading; }
    }

    private void LoadDeviceLightingSettings(DeviceLightingConfig settings)
    {
        bool wasLoading = _loadingDeviceEditor;
        _loadingDeviceEditor = true;
        try
        {
            var device = SelectedLightingDevice;
            if (device == null) return;
            ClearLightingLedEditors();
            SelectedLightingDeviceMode = LightingDeviceModes.FirstOrDefault(mode => mode.Value == settings.Mode)
                ?? (device.Modes.Any(m => ModeKeyLighting.IsFollowMode(m.Value))
                    ? LightingDeviceModes.FirstOrDefault(m => ModeKeyLighting.IsFollowMode(m.Value)) : null)
                ?? LightingDeviceModes.FirstOrDefault();
            DeviceLightingBrightness = ModeKeyLighting.IsFollowMode(settings.Mode)
                ? ConfigurationService.Instance.GetCurrent().Lighting.ModeBrightness : settings.Brightness;
            DeviceLightingSpeed = settings.Speed;
            DeviceLightingDirection = SelectedLightingDeviceMode?.ResolveDirection(settings.Direction) ?? 0;
            _deviceLightingNativeSpeed = settings.AnimationSpeedLevel;
            OnPropertyChanged(nameof(DeviceLightingSpeedLevel));
            DeviceLightingColorHex = ModeKeyLighting.IsFollowMode(settings.Mode)
                ? ModeKeyLighting.SharedColorFor(ConfigurationService.Instance.GetCurrent().Lighting.PowerModeColors, ActivePowerMode)
                : settings.ColorHex;
            DeviceLightingIndividualColors = SelectedLightingDeviceMode?.Value == -1 || settings.IndividualColors;
            DeviceLightingRandomColors = settings.RandomColors;
            SyncAllBarLeds = device.IsLightBar && settings.SyncLeds;
            IsModeKeySyncWithKeyboard = device.Kind.Equals("Hotkey", StringComparison.OrdinalIgnoreCase) && settings.SyncWithKeyboard;
            foreach (var led in device.Leds)
            {
                var editor = new LightingLedEditor(led, settings.LedColors.TryGetValue(led.Index, out var color) ? color : settings.ColorHex);
                editor.PropertyChanged += OnLightingLedColorEdited;
                LightingLedEditors.Add(editor);
            }
            if (IsDirectLightingDeviceMode && !IsPerKeyKeyboard)
                SelectedLightingKey = LightingLedEditors.FirstOrDefault();
            if ((device.Kind.Equals("Hotkey", StringComparison.OrdinalIgnoreCase) || ModeKeyLighting.IsFollowMode(settings.Mode)) && !settings.SyncWithKeyboard)
                LoadModePowerModeColorEditors(settings);
            else
                ClearModePowerModeColorEditors();
            if (settings.SyncWithKeyboard)
                MirrorModeKeyEditorFromKeyboard();
        }
        finally { _loadingDeviceEditor = wasLoading; }
    }

    public Task ApplySelectedLightingDeviceAsync()
    {
        if (!_dispatcher.CheckAccess()) return _dispatcher.InvokeAsync(ApplySelectedLightingDeviceAsync).Task.Unwrap();
        if (_disposed || SelectedLightingDevice is null || _hw is not IExtendedLightingHardware) return Task.CompletedTask;
        var settings = CaptureDeviceLightingSettings();
        try
        {
            OemLightingController.Validate(settings);
        }
        catch { LightingDeviceStatus = "Enter valid colors such as #00E5FF before applying."; return Task.CompletedTask; }
        bool disableCoverSync = SelectedLightingDevice.Id.EndsWith("-83", StringComparison.OrdinalIgnoreCase);
        if (disableCoverSync && IsCoverSyncWithKeyboard)
        {
            _isApplyingLighting = true;
            try { IsCoverSyncWithKeyboard = false; _pendingCoverLighting = false; }
            finally { _isApplyingLighting = false; }
        }
        _extendedLightingWorker ??= new(
            batch => { foreach (var item in batch.Values) ApplyExtendedLightingRequest(item); },
            (pending, latest) =>
            {
                var merged = new Dictionary<string, ExtendedLightingRequest>(pending);
                foreach (var item in latest) merged[item.Key] = item.Value;
                return merged;
            });
        LightingDeviceStatus = "Applying lighting…";
        var request = new ExtendedLightingRequest(SelectedLightingDevice.Id, JsonSerializer.Serialize(settings), disableCoverSync);
        Task submitted = SubmitExtendedAfterLegacyAsync(request, _lightingWorker?.WhenIdleAsync() ?? Task.CompletedTask);
        _deviceLightingSubmissions = _deviceLightingSubmissions is { IsCompleted: false } previous
            ? Task.WhenAll(previous, submitted) : submitted;
        return submitted;
    }

    private DeviceLightingConfig CaptureDeviceLightingSettings()
    {
        bool sync = IsModeKeyLightingDevice && IsModeKeySyncWithKeyboard;
        if (sync)
            MirrorModeKeyEditorFromKeyboard();
        var settings = new DeviceLightingConfig
        {
            Mode = sync
                ? SyncedModeKeyModeFromKeyboardEffect(_lightingEffectTag)
                : SelectedLightingDeviceMode?.Value ?? -1,
            Brightness = DeviceLightingBrightness,
            Speed = DeviceLightingSpeed,
            AnimationSpeedLevel = !sync && HasDeviceLightingSpeed ? DeviceLightingSpeedLevel : null,
            Direction = !sync && HasDeviceLightingDirection ? DeviceLightingDirection : null,
            ColorHex = DeviceLightingColorHex,
            IndividualColors = !sync && IsDirectLightingDeviceMode,
            RandomColors = !sync && HasDeviceLightingRandomColors && DeviceLightingRandomColors,
            SyncLeds = SelectedLightingDevice?.IsLightBar == true && SyncAllBarLeds,
            SyncWithKeyboard = sync,
            LedColors = !sync && IsDirectLightingDeviceMode
                ? LightingLedEditors.ToDictionary(led => led.Index, led => led.ColorHex) : new()
        };
        if (!sync && ModeKeyLighting.IsFollowMode(settings.Mode))
            settings.ColorHex = ModeKeyLighting.SharedColorFor(ConfigurationService.Instance.GetCurrent().Lighting.PowerModeColors, ActivePowerMode);
        return settings;
    }

    private void MirrorModeKeyEditorFromKeyboard()
    {
        _loadingDeviceEditor = true;
        try
        {
            DeviceLightingColorHex = UsesAveragedCoverColor() ? AverageZoneHexColor() : SelectedHexColor;
            DeviceLightingBrightness = RgbBrightness;
            DeviceLightingSpeed = RgbSpeed;
            int modeValue = SyncedModeKeyModeFromKeyboardEffect(_lightingEffectTag);
            var mode = LightingDeviceModes.FirstOrDefault(m => m.Value == modeValue);
            if (mode != null && !ReferenceEquals(SelectedLightingDeviceMode, mode))
                SelectedLightingDeviceMode = mode;
        }
        finally { _loadingDeviceEditor = false; }
    }

    /// <summary>Acer HID: Static=2, Breathing=4, Neon=5. Other dynamic effects sync as Neon.</summary>
    internal static int SyncedModeKeyModeFromKeyboardEffect(string effectTag) => effectTag switch
    {
        "HW:1" => 4,
        "HW:2" => 5,
        _ when IsDynamicNeonSyncEffect(effectTag) => 5,
        _ => 2,
    };

    private static string ModeKeyColorFromKeyboardConfig(KeyboardLightingConfig keyboard)
    {
        // Match Cover: average only for Static and software; Breathing/Neon/other HW use selected color.
        bool average = keyboard.EffectTag.Equals("HW:0", StringComparison.Ordinal)
            || keyboard.EffectTag.StartsWith("DYN:", StringComparison.Ordinal);
        if (average)
            return AverageZoneHexColor(
                keyboard.Zone1ColorHex, keyboard.Zone2ColorHex, keyboard.Zone3ColorHex, keyboard.Zone4ColorHex);
        return string.IsNullOrWhiteSpace(keyboard.SelectedColorHex) ? "#00E5FF" : keyboard.SelectedColorHex;
    }

    private static DeviceLightingConfig BuildSyncedModeKeySettings(
        KeyboardLightingConfig keyboard, DeviceLightingConfig? prior)
    {
        int mode = SyncedModeKeyModeFromKeyboardEffect(keyboard.EffectTag);
        return new DeviceLightingConfig
        {
            Mode = mode,
            Brightness = keyboard.Brightness,
            Speed = mode is 4 or 5 ? keyboard.Speed : 0,
            ColorHex = ModeKeyColorFromKeyboardConfig(keyboard),
            SyncWithKeyboard = true,
            PowerModeColors = prior?.PowerModeColors != null
                ? new Dictionary<string, string>(prior.PowerModeColors, StringComparer.OrdinalIgnoreCase)
                : new(),
        };
    }

    private void ApplySyncedModeKeyDevices(KeyboardLightingConfig keyboard)
    {
        if (_disposed || _hw is not IExtendedLightingHardware hardware) return;
        lock (ExtendedLightingWriteGate)
        {
            var profiles = ConfigurationService.Instance.GetCurrent().Lighting.Devices;
            // Discover() is a cached snapshot after the one-time startup probe (no HID re-scan).
            foreach (var device in hardware.DiscoverLightingDevices())
            {
                if (!device.Kind.Equals("Hotkey", StringComparison.OrdinalIgnoreCase)) continue;
                profiles.TryGetValue(device.Id, out var saved);
                bool sync = saved?.SyncWithKeyboard == true
                    || (SelectedLightingDevice?.Id == device.Id && IsModeKeySyncWithKeyboard);
                if (!sync) continue;
                var settings = BuildSyncedModeKeySettings(keyboard, saved);
                try
                {
                    OemLightingController.Validate(settings);
                    hardware.ApplyLightingDevice(device.Id, ModeKeyLighting.ResolveForApply(settings, ActivePowerMode,
                        ConfigurationService.Instance.GetCurrent().Lighting.PowerModeColors,
                        ConfigurationService.Instance.GetCurrent().Lighting.ModeBrightness));
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"Mode Key sync with keyboard failed ({device.Name}): {ex.Message}");
                }
            }
        }
    }

    private bool AnyModeKeySyncWithKeyboard()
    {
        if (IsModeKeySyncWithKeyboard) return true;
        if (_hw is not IExtendedLightingHardware hardware) return false;
        var profiles = ConfigurationService.Instance.GetCurrent().Lighting.Devices;
        foreach (var device in hardware.DiscoverLightingDevices())
        {
            if (!device.Kind.Equals("Hotkey", StringComparison.OrdinalIgnoreCase)) continue;
            if (profiles.TryGetValue(device.Id, out var saved) && saved.SyncWithKeyboard)
                return true;
        }
        return false;
    }

    private void ClearModePowerModeColorEditors()
    {
        foreach (var editor in ModePowerModeColorEditors)
            editor.PropertyChanged -= OnModePowerModeColorEdited;
        ModePowerModeColorEditors.Clear();
        SelectedModePowerModeColor = null;
    }

    private void LoadModePowerModeColorEditors(DeviceLightingConfig settings)
    {
        ClearModePowerModeColorEditors();
        foreach (var slot in ModeKeyLighting.Slots)
        {
            var editor = new ModePowerModeColorEditor(
                slot.PowerMode, slot.Name, slot.DefaultHex, ModeKeyLighting.SharedColorFor(ConfigurationService.Instance.GetCurrent().Lighting.PowerModeColors, slot.PowerMode));
            editor.PropertyChanged += OnModePowerModeColorEdited;
            ModePowerModeColorEditors.Add(editor);
        }
        SelectedModePowerModeColor = ModePowerModeColorEditors.FirstOrDefault(e => e.PowerMode == ActivePowerMode)
            ?? ModePowerModeColorEditors.FirstOrDefault();
    }

    private void SyncModePowerModeColorEditorsFromProfile()
    {
        DeviceLightingConfig? saved = null;
        if (SelectedLightingDevice != null)
            ConfigurationService.Instance.GetCurrent().Lighting.Devices.TryGetValue(SelectedLightingDevice.Id, out saved);
        LoadModePowerModeColorEditors(saved ?? new DeviceLightingConfig { Mode = ModeKeyLighting.FollowOperatingMode });
    }

    private void OnModePowerModeColorEdited(object? sender, PropertyChangedEventArgs e)
    {
        if (_loadingDeviceEditor || e.PropertyName != nameof(ModePowerModeColorEditor.ColorHex)) return;
        ScheduleDeviceLighting();
    }

    public void SelectModePowerModeColor(ModePowerModeColorEditor? editor)
    {
        if (editor == null) return;
        SelectedModePowerModeColor = editor;
    }

    private async Task SubmitExtendedAfterLegacyAsync(ExtendedLightingRequest request, Task previousWrites)
    {
        await previousWrites.ConfigureAwait(false);
        if (!_disposed) await _extendedLightingWorker!.Submit(new() { [request.DeviceId] = request }).ConfigureAwait(false);
    }

    // Serialize saved-profile reads and hardware writes with editor write-and-save transactions.
    private static readonly object ExtendedLightingWriteGate = new();

    private void ApplyExtendedLightingRequest(ExtendedLightingRequest request)
    {
        if (_disposed || _hw is not IExtendedLightingHardware hardware) return;
        try
        {
            var settings = JsonSerializer.Deserialize<DeviceLightingConfig>(request.SettingsJson)!;
            lock (ExtendedLightingWriteGate)
            {
                hardware.ApplyLightingDevice(request.DeviceId, ModeKeyLighting.ResolveForApply(settings, ActivePowerMode,
                    ConfigurationService.Instance.GetCurrent().Lighting.PowerModeColors,
                        ConfigurationService.Instance.GetCurrent().Lighting.ModeBrightness));
                ConfigurationService.Instance.Update(config =>
                {
                    config.Lighting.Devices[request.DeviceId] = settings;
                    if (request.DisableCoverSync) config.Lighting.Cover.SyncWithKeyboard = false;
                });
            }
            _dispatcher.BeginInvoke(() => { if (!_disposed && SelectedLightingDevice?.Id == request.DeviceId) LightingDeviceStatus = "Lighting applied and saved."; });
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Extended lighting update failed: {ex}");
            _dispatcher.BeginInvoke(() => { if (!_disposed && SelectedLightingDevice?.Id == request.DeviceId) LightingDeviceStatus = $"Lighting update failed: {ex.Message}"; });
        }
    }

    internal Task FlushExtendedLightingEditsAsync()
    {
        _deviceLightingDebouncer?.Cancel();
        if (!_deviceLightingEditPending) return _deviceLightingSubmissions ?? _extendedLightingWorker?.WhenIdleAsync() ?? Task.CompletedTask;
        _deviceLightingEditPending = false;
        Task submitted = ApplySelectedLightingDeviceAsync();
        return _deviceLightingSubmissions ?? submitted;
    }

    internal Task StopExtendedLightingAsync(bool drainPending = true)
    {
        Task pending = drainPending ? FlushExtendedLightingEditsAsync() : Task.CompletedTask;
        _deviceLightingDebouncer?.Dispose();
        return DrainAsync();
        async Task DrainAsync()
        {
            await pending.ConfigureAwait(false);
            if (_extendedLightingWorker != null) await _extendedLightingWorker.StopAsync(drainPending).ConfigureAwait(false);
        }
    }

    private async Task RestoreExtendedLightingAfterAsync(Task keyboardStartup)
    {
        try
        {
            await keyboardStartup.ConfigureAwait(false);
            if (_disposed || _hw is not IExtendedLightingHardware hardware) return;
            await Task.Run(() =>
            {
                var devices = hardware.DiscoverLightingDevices();
                lock (ExtendedLightingWriteGate)
                {
                    var lighting = ConfigurationService.Instance.GetCurrent().Lighting;
                    var profiles = lighting.Devices;
                    bool useLegacyEditors = !devices.Any(device => device.HasNamedKeys);
                    foreach (var device in devices)
                    {
                        if (_disposed) return;
                        // The four-zone editor owns this keyboard through Lighting.Keyboard. An old
                        // device profile must not overwrite the settings just restored by that path.
                        if (useLegacyEditors && device is { Kind: "Keyboard", Leds.Count: 4 } && _hw.IsRgbAvailable) continue;
                        if (useLegacyEditors && IsLogoLightingKind(device) && _hw.IsCoverLightingAvailable) continue;
                        var settings = profiles.TryGetValue(device.Id, out var saved) ? saved : new DeviceLightingConfig();
                        try { hardware.ApplyLightingDevice(device.Id, ModeKeyLighting.ResolveForApply(settings, ActivePowerMode, lighting.PowerModeColors, lighting.ModeBrightness)); }
                        catch (Exception ex) { AppLogger.Log($"Saved device lighting unavailable ({device.Name}): {ex.Message}"); }
                    }
                }
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Could not restore saved device lighting: {ex}");
        }
    }

    /// <summary>Firmware can overwrite any LED during a mode transition, even when the desired color is unchanged.</summary>
    private int _modeLightingRestoreRevision;

    private void ScheduleHotkeyLightingRestoreAfterModeSwitch()
    {
        if (_disposed || _dispatcher == null) return;
        int revision = System.Threading.Interlocked.Increment(ref _modeLightingRestoreRevision);
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(ModeKeyLighting.EcModeTransitionDelay).ConfigureAwait(false);
                if (_disposed || revision != System.Threading.Volatile.Read(ref _modeLightingRestoreRevision)) return;
                await _dispatcher.InvokeAsync(() =>
                {
                    if (_disposed || revision != System.Threading.Volatile.Read(ref _modeLightingRestoreRevision))
                        return Task.CompletedTask;
                    if (IsModeKeyFollowMode && ModePowerModeColorEditors != null)
                        SelectedModePowerModeColor = ModePowerModeColorEditors.FirstOrDefault(e => e.PowerMode == ActivePowerMode)
                            ?? SelectedModePowerModeColor;
                    if (_zoneHexColors != null) UpdateKeyboardForMode(ActivePowerMode);
                    return ApplySharedModePaletteAsync();
                }).Task.Unwrap().ConfigureAwait(false);
                if (!_disposed && revision == System.Threading.Volatile.Read(ref _modeLightingRestoreRevision))
                    ReapplyExtendedModeLighting();
            }
            catch (Exception ex) { AppLogger.Log($"Lighting restore after mode switch failed: {ex.Message}"); }
        });
    }

    internal void ReapplyExtendedModeLighting(bool includeOtherHotkeyProfiles = true)
    {
        if (_disposed || _hw is not IExtendedLightingHardware hardware) return;
        lock (ExtendedLightingWriteGate)
        {
            var lighting = ConfigurationService.Instance.GetCurrent().Lighting;
            var devices = hardware.DiscoverLightingDevices();
            bool useLegacyEditors = !devices.Any(device => device.HasNamedKeys);
            foreach (var device in devices)
            {
                if (_disposed) return;
                // Keep the same single authoritative profile as startup. A stale extended
                // profile must not replace the four-zone or cover editor's current choice.
                if (useLegacyEditors && device is { Kind: "Keyboard", Leds.Count: 4 } && _hw.IsRgbAvailable) continue;
                if (useLegacyEditors && IsLogoLightingKind(device) && _hw.IsCoverLightingAvailable) continue;
                var settings = lighting.Devices.TryGetValue(device.Id, out var saved) ? saved : new DeviceLightingConfig();
                bool shouldRestore = ModeKeyLighting.IsFollowMode(settings.Mode)
                    || (includeOtherHotkeyProfiles && (device.Kind.Equals("Hotkey", StringComparison.OrdinalIgnoreCase) || settings.SyncWithKeyboard));
                if (!shouldRestore) continue;
                try
                {
                    var apply = settings.SyncWithKeyboard
                        ? BuildSyncedModeKeySettings(lighting.Keyboard, settings) : settings;
                    // Resolve for every write rather than caching color equality. This also gives
                    // every transport and fake the same explicit current-mode color.
                    hardware.ApplyLightingDevice(device.Id, ModeKeyLighting.ResolveForApply(apply, ActivePowerMode, lighting.PowerModeColors, lighting.ModeBrightness));
                }
                catch (Exception ex) { AppLogger.Log($"Lighting restore after mode switch failed ({device.Name}): {ex.Message}"); }
            }
        }
    }
}
