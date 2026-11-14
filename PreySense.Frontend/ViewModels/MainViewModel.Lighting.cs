using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PreySense;
using PreySense.Core.Configuration;
using PreySense.Frontend.Services;
using PreySense.Rgb;

namespace PreySense.Frontend.ViewModels
{
    public partial class MainViewModel
    {
        // RGB Lighting
        [ObservableProperty] private int _selectedRgbZone = 1;
        [ObservableProperty] private string _selectedHexColor = "#00AEC7";
        private readonly string[] _zoneHexColors = { "#00AEC7", "#00AEC7", "#00E5FF", "#00E5FF" };
        private bool _isSyncingZoneColor;
        private LightingLedEditor[]? _zoneColorChoices;
        public IReadOnlyList<LightingLedEditor> ZoneColorChoices => _zoneColorChoices ??=
            Enumerable.Range(0, 4).Select(i => new LightingLedEditor(new LightingLedInfo(i, $"Zone {i + 1}"), _zoneHexColors[i])).ToArray();
        public int SelectedZoneColorIndex
        {
            get => SelectedRgbZone - 1;
            set { if (value is >= 0 and < 4) SelectedRgbZone = value + 1; }
        }
        public string Zone1HexColor => _zoneHexColors[0];
        public string Zone2HexColor => _zoneHexColors[1];
        public string Zone3HexColor => _zoneHexColors[2];
        public string Zone4HexColor => _zoneHexColors[3];
        public string Zone1Foreground => ContrastForegroundHex(_zoneHexColors[0]);
        public string Zone2Foreground => ContrastForegroundHex(_zoneHexColors[1]);
        public string Zone3Foreground => ContrastForegroundHex(_zoneHexColors[2]);
        public string Zone4Foreground => ContrastForegroundHex(_zoneHexColors[3]);
        [ObservableProperty] private int _rgbBrightness = 100;
        [ObservableProperty] private int _rgbSpeed = 50;
        [ObservableProperty] private string _activeEffectName = "Mode";
        [ObservableProperty] private bool _isRgbSyncZonesEnabled = false;

        // Lighting Target Tabs (0 = Keyboard, 1 = Cover)
        [ObservableProperty] private int _selectedLightingTab = 0;
        public bool IsKeyboardTabSelected
        {
            get => SelectedLightingTab == 0;
            set
            {
                if (value) SelectedLightingTab = 0;
            }
        }
        public bool IsCoverTabSelected
        {
            get => SelectedLightingTab == 1;
            set
            {
                if (value) SelectedLightingTab = 1;
            }
        }

        // Cover / Top Backlight Controls
        [ObservableProperty] private bool _isCoverLightingEnabled = true;
        [ObservableProperty] private int _coverLightingMode = 4; // 1 = Static, 2 = Breathing, 3 = Neon, 4 = Mode
        [ObservableProperty] private string _coverHexColor = "#00AEC7";
        [ObservableProperty] private int _coverBrightness = 100;
        [ObservableProperty] private int _coverSpeed = 50;
        [ObservableProperty] private bool _isCoverSyncWithKeyboard = false;
        [ObservableProperty] private string _coverModeName = "Mode";

        // Keyboard Backlight Timeout
        [ObservableProperty] private bool _isKbdBacklightTimeoutEnabled = false;
        [NotifyPropertyChangedFor(nameof(KbdTimeoutDisplay))]
        [ObservableProperty] private int _kbdTimeoutSeconds = 30; // 0 = ON, 10..120
        public string KbdTimeoutDisplay => KbdTimeoutSeconds == 0 ? "OFF" : $"{KbdTimeoutSeconds}s";

        private bool _isSyncingRgbBrightness;
        private bool _isLoadingLighting;
        private bool _isApplyingLighting;
        private bool _pendingKeyboardLighting;
        private bool _pendingCoverLighting;
        private Color[]? _pendingZonePreset;
        private string _selectedZonePreset = "Mode";
        public string SelectedZonePreset
        {
            get => _selectedZonePreset;
            set
            {
                if (!string.Equals(_selectedZonePreset, value, StringComparison.Ordinal))
                {
                    _selectedZonePreset = value;
                    OnPropertyChanged(nameof(SelectedZonePreset));
                }
            }
        }
        private string _lightingEffectTag = "MODE";
        public string LightingEffectTag => _lightingEffectTag;
        private readonly UiDebouncer _lightingDebouncer = new(TimeSpan.FromMilliseconds(80));
        // HID/EC keyboard brightness is a 0–100 byte; step matches hardware resolution.
        private const int BrightnessStep = 1;
        private bool _coverSyncFrameHooked;

        private static int SnapBrightness(int value) =>
            Math.Clamp((int)Math.Round(value / (double)BrightnessStep) * BrightnessStep, 0, 100);

        public int KeyboardAnimationSpeedLevel
        {
            get => ToHidKeyboardSpeed(RgbSpeed);
            set => RgbSpeed = Math.Clamp(value, 1, 10) * 10;
        }
        public int LogoAnimationSpeedLevel
        {
            get => ToHidLogoSpeed(CoverSpeed);
            set => CoverSpeed = Math.Clamp(value, 1, 5) * 20;
        }

        private static byte ToHidKeyboardSpeed(int percent) =>
            (byte)Math.Clamp((int)Math.Round(Math.Max(percent, 1) / 10.0), 1, 10);

        private static byte ToHidLogoSpeed(int percent) =>
            (byte)Math.Clamp((int)Math.Round(Math.Max(percent, 1) / 20.0), 1, 5);

        partial void OnSelectedLightingTabChanged(int value)
        {
            OnPropertyChanged(nameof(IsKeyboardTabSelected));
            OnPropertyChanged(nameof(IsCoverTabSelected));
            NotifyLightingEditorVisibility();
            if (!HasLightingDevices) return;
            // Keep the detected-device tabs aligned with the legacy Keyboard/Logo selection.
            if (value == 0)
            {
                var keyboard = LightingDevices.FirstOrDefault(d => d.Kind.Equals("Keyboard", StringComparison.OrdinalIgnoreCase));
                if (keyboard != null && !ReferenceEquals(SelectedLightingDevice, keyboard))
                    SelectedLightingDevice = keyboard;
            }
            else if (value == 1)
            {
                var logo = LightingDevices.FirstOrDefault(IsLogoLightingKind);
                if (logo != null && !ReferenceEquals(SelectedLightingDevice, logo))
                    SelectedLightingDevice = logo;
            }
        }

        partial void OnCoverLightingModeChanged(int value)
        {
            CoverModeName = value switch
            {
                4 => "Mode",
                2 => "Breathing",
                3 => "Neon Wave",
                _ => "Static"
            };
            ScheduleCoverLightingCommit();
        }

        partial void OnCoverBrightnessChanged(int value)
        {
            int snapped = SnapBrightness(value);
            if (snapped != value)
            {
                CoverBrightness = snapped;
                return;
            }

            IsCoverLightingEnabled = snapped > 0;
            ScheduleCoverLightingCommit();
        }

        partial void OnCoverSpeedChanged(int value)
        {
            int snapped = SnapBrightness(value);
            if (snapped != value)
            {
                CoverSpeed = snapped;
                return;
            }

            OnPropertyChanged(nameof(LogoAnimationSpeedLevel));
            ScheduleCoverLightingCommit();
        }

        partial void OnCoverHexColorChanged(string value)
        {
            ScheduleCoverLightingCommit();
        }

        partial void OnIsCoverSyncWithKeyboardChanged(bool value)
        {
            if (value)
            {
                MirrorCoverFromKeyboard();
            }
            UpdateCoverSyncFrameHook();
            ScheduleCoverLightingCommit();
        }

        partial void OnIsRgbSyncZonesEnabledChanged(bool value)
        {
            if (value && !string.IsNullOrEmpty(SelectedHexColor))
            {
                RememberZoneColor(SelectedHexColor, allZones: true);
                SetZoneColor(SelectedHexColor);
            }
            ScheduleKeyboardLightingCommit();
        }

        partial void OnKbdTimeoutSecondsChanged(int value)
        {
            IsKbdBacklightTimeoutEnabled = value > 0;
            ScheduleKeyboardLightingCommit();
        }

        partial void OnRgbSpeedChanged(int value)
        {
            int snapped = SnapBrightness(value);
            if (snapped != value)
            {
                RgbSpeed = snapped;
                return;
            }

            OnPropertyChanged(nameof(KeyboardAnimationSpeedLevel));
            ScheduleKeyboardLightingCommit();
        }

        partial void OnSelectedHexColorChanged(string value)
        {
            if (_isSyncingZoneColor || _isLoadingLighting)
                return;

            RememberZoneColor(value, allZones: IsRgbSyncZonesEnabled);
            ScheduleKeyboardLightingCommit();
        }

        partial void OnSelectedRgbZoneChanged(int value)
        {
            OnPropertyChanged(nameof(SelectedZoneColorIndex));
            if (_isSyncingZoneColor || _isLoadingLighting)
                return;

            _isSyncingZoneColor = true;
            SelectedHexColor = _zoneHexColors[Math.Clamp(value - 1, 0, 3)];
            _isSyncingZoneColor = false;
        }

        partial void OnRgbBrightnessChanged(int value)
        {
            if (_isSyncingRgbBrightness)
                return;

            int snapped = SnapBrightness(value);
            if (snapped != value)
            {
                _isSyncingRgbBrightness = true;
                RgbBrightness = snapped;
                _isSyncingRgbBrightness = false;
            }

            ScheduleKeyboardLightingCommit();
        }

        [RelayCommand]
        public void SetZoneColor(string hexColor)
        {
            _pendingZonePreset = null;
            SelectedZonePreset = "Custom";
            ActiveEffectName = GetEffectDisplayName(_lightingEffectTag, "Custom");
            SelectedHexColor = hexColor;
            if (IsCoverSyncWithKeyboard)
            {
                CoverHexColor = hexColor;
            }
            ScheduleKeyboardLightingCommit();
        }

        public void DiscardPendingZonePreset()
        {
            _pendingZonePreset = null;
            SelectedZonePreset = "Custom";
            ActiveEffectName = GetEffectDisplayName(_lightingEffectTag, "Custom");
        }

        [RelayCommand]
        public void StepKeyboardBrightness(int delta)
        {
            if (delta == 0)
                return;

            _dispatcher.InvokeAsync(() =>
            {
                if (_disposed)
                    return;

                var lighting = ConfigurationService.Instance.GetCurrent().Lighting;
                var keyboardDevice = LightingDevices?.FirstOrDefault(d => d.Kind.Equals("Keyboard", StringComparison.OrdinalIgnoreCase));
                bool keyboardMode = keyboardDevice?.HasNamedKeys == true
                    ? !lighting.Devices.TryGetValue(keyboardDevice.Id, out var keyboardProfile) || ModeKeyLighting.IsFollowMode(keyboardProfile.Mode)
                    : string.Equals(_lightingEffectTag, "MODE", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(_lightingEffectTag, "HW:Mode", StringComparison.OrdinalIgnoreCase);
                if (keyboardMode)
                {
                    StepSharedModeBrightness(delta);
                    DiagnosticMessage = $"Mode Lighting Brightness: {ModeLightingBrightness}%";
                    return;
                }

                int current = KeyboardBrightnessForFn(lighting, keyboardDevice);
                int next = StepFnBrightness(current, delta);
                _isSyncingRgbBrightness = true;
                RgbBrightness = next;
                _isSyncingRgbBrightness = false;
                if (SelectedLightingDevice?.Kind == "Keyboard")
                {
                    // Reflect the firmware rung exactly, even on a USB editor whose
                    // ordinary slider uses two-percent increments.
                    _deviceLightingBrightness = next;
                    OnPropertyChanged(nameof(DeviceLightingBrightness));
                }
                _fnKeyboardBrightnessSave = QueueLighting(LightingWork.Brightness | LightingWork.Save);
                DiagnosticMessage = $"Keyboard Brightness: {next}%";
                AppLogger.Log($"TurboKey: Fn brightness {next}% saved");
            });
        }

        private Task? _fnKeyboardBrightnessSave;

        internal int KeyboardBrightnessForFn(LightingConfig lighting, LightingDeviceInfo? keyboardDevice)
        {
            if (SelectedLightingDevice?.Kind == "Keyboard" && IsDeviceLightingEditorVisible)
                return DeviceLightingBrightness;
            // The per-key profile remains authoritative while another device's editor is open.
            if (keyboardDevice?.HasNamedKeys == true && lighting.Devices.TryGetValue(keyboardDevice.Id, out var profile))
                return _fnKeyboardBrightnessSave is { IsCompleted: false } ? RgbBrightness : profile.Brightness;
            return RgbBrightness;
        }

        private readonly FnBrightnessEventFilter _fnBrightnessEvents = new();
        public void HandleFnBrightness(int delta, string source)
        {
            if (_fnBrightnessEvents.Accept(source, delta, Environment.TickCount64))
                StepKeyboardBrightness(delta);
        }

        internal static int StepFnBrightness(int brightness, int steps)
        {
            int current = Math.Clamp(brightness, 0, 100);
            if (steps == 0) return current;
            // Fn uses the firmware's 0/25/50/75/100 ladder. The first press
            // reaches the next rung in that direction, even from a slider value.
            long rung = steps > 0 ? current / 25L + steps
                : (current + 24L) / 25L + steps;
            return (int)Math.Clamp(rung * 25, 0, 100);
        }

        [RelayCommand]
        public void ApplyHardwareEffect(string effectParam)
        {
            if (int.TryParse(effectParam, out int mode) && mode is >= 0 and <= 7)
                SetLightingEffect($"HW:{mode}");
        }

        [RelayCommand]
        public void ApplyCustomDynamicEffect(string effectModeStr)
        {
            if (!Enum.TryParse<CustomEffectMode>(effectModeStr, out var effect) ||
                effect is CustomEffectMode.None or CustomEffectMode.CpuHeatmap or CustomEffectMode.BatteryFuelGauge
                    or CustomEffectMode.OceanTide or CustomEffectMode.IceCrystal) return;
            SetLightingEffect($"DYN:{effect}");
        }

        public static bool TryGetZonePresetColors(string? presetName, out Color[] colors, out string displayName)
        {
            Color z1, z2, z3, z4;
            switch (presetName?.ToLowerInvariant())
            {
                case "mode":
                    var (mz1, mz2, mz3, mz4) = ModeKeyLighting.GetZoneColors(0x01); // Balanced default
                    z1 = ColorTranslator.FromHtml(mz1);
                    z2 = ColorTranslator.FromHtml(mz2);
                    z3 = ColorTranslator.FromHtml(mz3);
                    z4 = ColorTranslator.FromHtml(mz4);
                    displayName = "Mode Preset";
                    break;
                case "predator":
                    z1 = z2 = z3 = z4 = ColorTranslator.FromHtml("#00AEC7");
                    displayName = "Predator Preset";
                    break;
                case "cyberpunk":
                    z1 = ColorTranslator.FromHtml("#FFDF00");
                    z2 = ColorTranslator.FromHtml("#FF007F");
                    z3 = ColorTranslator.FromHtml("#00E5FF");
                    z4 = ColorTranslator.FromHtml("#9D4EDD");
                    displayName = "Cyberpunk Preset";
                    break;
                case "fireice":
                    z1 = ColorTranslator.FromHtml("#00E5FF");
                    z2 = ColorTranslator.FromHtml("#0077B6");
                    z3 = ColorTranslator.FromHtml("#FFB703");
                    z4 = ColorTranslator.FromHtml("#E71D36");
                    displayName = "Fire & Ice Preset";
                    break;
                case "matrix":
                    z1 = ColorTranslator.FromHtml("#00FF66");
                    z2 = ColorTranslator.FromHtml("#2EC4B6");
                    z3 = ColorTranslator.FromHtml("#00FF66");
                    z4 = ColorTranslator.FromHtml("#00FF66");
                    displayName = "Matrix Code Preset";
                    break;
                case "stealth":
                    z1 = Color.White; z2 = Color.White; z3 = Color.White; z4 = Color.White;
                    displayName = "Stealth White Preset";
                    break;
                case "sunset":
                    z1 = ColorTranslator.FromHtml("#FF6B00");
                    z2 = ColorTranslator.FromHtml("#FF3D5A");
                    z3 = ColorTranslator.FromHtml("#C9184A");
                    z4 = ColorTranslator.FromHtml("#7209B7");
                    displayName = "Sunset Preset";
                    break;
                case "ocean":
                    z1 = ColorTranslator.FromHtml("#003566");
                    z2 = ColorTranslator.FromHtml("#0077B6");
                    z3 = ColorTranslator.FromHtml("#00B4D8");
                    z4 = ColorTranslator.FromHtml("#CAF0F8");
                    displayName = "Ocean Preset";
                    break;
                case "ember":
                    z1 = ColorTranslator.FromHtml("#6A040F");
                    z2 = ColorTranslator.FromHtml("#DC2F02");
                    z3 = ColorTranslator.FromHtml("#F48C06");
                    z4 = ColorTranslator.FromHtml("#FFBA08");
                    displayName = "Ember Preset";
                    break;
                case "vaporwave":
                    z1 = ColorTranslator.FromHtml("#FF71CE");
                    z2 = ColorTranslator.FromHtml("#B967FF");
                    z3 = ColorTranslator.FromHtml("#01CDFE");
                    z4 = ColorTranslator.FromHtml("#05FFA1");
                    displayName = "Vaporwave Preset";
                    break;
                case "sakura":
                    z1 = ColorTranslator.FromHtml("#FF8FAB");
                    z2 = ColorTranslator.FromHtml("#FFB3C6");
                    z3 = ColorTranslator.FromHtml("#FFFFFF");
                    z4 = ColorTranslator.FromHtml("#FB6F92");
                    displayName = "Sakura Preset";
                    break;
                case "rainbow":
                    z1 = ColorTranslator.FromHtml("#FF0000");
                    z2 = ColorTranslator.FromHtml("#FFD000");
                    z3 = ColorTranslator.FromHtml("#00E676");
                    z4 = ColorTranslator.FromHtml("#2979FF");
                    displayName = "Rainbow Preset";
                    break;
                case "toxic":
                    z1 = ColorTranslator.FromHtml("#C8FF00");
                    z2 = ColorTranslator.FromHtml("#7CFC00");
                    z3 = ColorTranslator.FromHtml("#39FF14");
                    z4 = ColorTranslator.FromHtml("#0B6E4F");
                    displayName = "Toxic Preset";
                    break;
                case "midnight":
                    z1 = ColorTranslator.FromHtml("#10002B");
                    z2 = ColorTranslator.FromHtml("#3A0CA3");
                    z3 = ColorTranslator.FromHtml("#4361EE");
                    z4 = ColorTranslator.FromHtml("#4CC9F0");
                    displayName = "Midnight Preset";
                    break;
                case "dracula":
                    z1 = ColorTranslator.FromHtml("#BD93F9");
                    z2 = ColorTranslator.FromHtml("#FF79C6");
                    z3 = ColorTranslator.FromHtml("#8BE9FD");
                    z4 = ColorTranslator.FromHtml("#50FA7B");
                    displayName = "Dracula Preset";
                    break;
                case "neonhaze":
                    z1 = ColorTranslator.FromHtml("#FF00FF");
                    z2 = ColorTranslator.FromHtml("#7700FF");
                    z3 = ColorTranslator.FromHtml("#00FFFF");
                    z4 = ColorTranslator.FromHtml("#FF00AA");
                    displayName = "Neon Haze Preset";
                    break;
                case "deepspace":
                    z1 = ColorTranslator.FromHtml("#000033");
                    z2 = ColorTranslator.FromHtml("#001166");
                    z3 = ColorTranslator.FromHtml("#4400AA");
                    z4 = ColorTranslator.FromHtml("#AAAAFF");
                    displayName = "Deep Space Preset";
                    break;
                case "inferno":
                    z1 = ColorTranslator.FromHtml("#FF0000");
                    z2 = ColorTranslator.FromHtml("#FF4400");
                    z3 = ColorTranslator.FromHtml("#FF8800");
                    z4 = ColorTranslator.FromHtml("#FFFF00");
                    displayName = "Inferno Preset";
                    break;
                case "cyberstorm":
                    z1 = ColorTranslator.FromHtml("#00FF88");
                    z2 = ColorTranslator.FromHtml("#00CCFF");
                    z3 = ColorTranslator.FromHtml("#FF00FF");
                    z4 = ColorTranslator.FromHtml("#FFFF00");
                    displayName = "Cyber Storm Preset";
                    break;
                default:
                    colors = Array.Empty<Color>();
                    displayName = string.Empty;
                    return false;
            }

            colors = new[] { z1, z2, z3, z4 };
            return true;
        }

        [RelayCommand]
        public void ApplyZonePreset(string presetName)
        {
            if (!TryGetZonePresetColors(presetName, out var colors, out var displayName))
                return;

            _pendingZonePreset = colors;
            SelectedZonePreset = presetName;
            IsRgbSyncZonesEnabled = false;
            _zoneHexColors[0] = ToRgbHex(colors[0]);
            _zoneHexColors[1] = ToRgbHex(colors[1]);
            _zoneHexColors[2] = ToRgbHex(colors[2]);
            _zoneHexColors[3] = ToRgbHex(colors[3]);
            NotifyZoneColorProperties();
            _isSyncingZoneColor = true;
            SelectedHexColor = _zoneHexColors[Math.Clamp(SelectedRgbZone - 1, 0, 3)];
            _isSyncingZoneColor = false;
            ActiveEffectName = displayName;
            ScheduleKeyboardLightingCommit();
        }

        public void SetLightingEffect(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return;
            _lightingEffectTag = tag;
            NotifyKeyboardLightingDirection();
            UpdateKeyboardForMode(ActivePowerMode);
            OnPropertyChanged(nameof(LightingEffectTag));
            ActiveEffectName = GetEffectDisplayName(_lightingEffectTag, _selectedZonePreset);
            if (IsModeKeySyncWithKeyboard)
                MirrorModeKeyEditorFromKeyboard();
            ScheduleKeyboardLightingCommit();
        }

        private void ScheduleKeyboardLightingCommit()
        {
            if (_disposed || _isRestoringSession || _isLoadingLighting || _isApplyingLighting)
                return;
            _pendingKeyboardLighting = true;
            _lightingDebouncer.Debounce(FlushLightingCommits);
        }

        private void ScheduleCoverLightingCommit()
        {
            if (_disposed || _isRestoringSession || _isLoadingLighting || _isApplyingLighting)
                return;
            _pendingCoverLighting = true;
            _lightingDebouncer.Debounce(FlushLightingCommits);
        }

        private void FlushLightingCommits()
        {
            if (_disposed || _isRestoringSession || _isLoadingLighting || _isApplyingLighting)
                return;

            bool keyboard = _pendingKeyboardLighting;
            bool cover = _pendingCoverLighting;
            _pendingKeyboardLighting = false;
            _pendingCoverLighting = false;
            if (!keyboard && !cover)
                return;

            _ = QueueLighting((keyboard ? LightingWork.Keyboard : LightingWork.None)
                | (cover ? LightingWork.Cover : LightingWork.None) | LightingWork.Save);
        }

        private int _startupLightingRan;

        public Task ApplyStartupLightingAsync()
        {
            if (_disposed)
                return Task.CompletedTask;

            if (Interlocked.CompareExchange(ref _startupLightingRan, 1, 0) != 0)
                return Task.CompletedTask;

            var keyboardStartup = Task.Run(async () =>
            {
                AppLogger.Log("ApplyStartupLighting: applying lighting from JSON once.");

                bool clientConnected = false;
                try
                {
                    if (_hw.IsRgbAvailable)
                    {
                        clientConnected = true;
                    }
                    else
                    {
                        // Prefer WM_DEVICECHANGE; pulse every 2s only until first open or timeout
                        // (covers late CreateFile without re-enumerating SetupDi at 2 Hz).
                        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(15));
                        while (!_disposed && !cts.IsCancellationRequested)
                        {
                            var arrival = PreySense.Core.Hardware.HidArrivalHub.WaitAsync(cts.Token);
                            var pulse = Task.Delay(2000, cts.Token);
                            try
                            {
                                await Task.WhenAny(arrival, pulse).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException)
                            {
                                break;
                            }

                            if (_disposed) return;
                            if (_hw.IsRgbAvailable)
                            {
                                clientConnected = true;
                                break;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"ApplyStartupLighting: RGB wait failed: {ex.Message}");
                }

                if (_disposed)
                    return;

                if (!clientConnected)
                {
                    AppLogger.Log("ApplyStartupLighting: RGB unavailable; skipping apply.");
                    return;
                }

                try
                {
                    await _dispatcher.InvokeAsync(() => QueueLighting(LightingWork.Keyboard | LightingWork.Cover)).Task.Unwrap();
                    AppLogger.Log("ApplyStartupLighting: applied once from JSON.");
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"ApplyStartupLighting error: {ex.Message}");
                }
            });
            return RestoreExtendedLightingAfterAsync(keyboardStartup);
        }

        private void ApplySavedLighting()
        {
            if (_dispatcher.CheckAccess())
                _ = RestoreExtendedLightingAfterAsync(QueueLighting(LightingWork.Keyboard | LightingWork.Cover));
            else _dispatcher.BeginInvoke(() => ApplySavedLighting());
        }

        internal static bool IsDynamicNeonSyncEffect(string effectTag) =>
            effectTag.StartsWith("DYN:", StringComparison.Ordinal)
            || (effectTag.StartsWith("HW:", StringComparison.Ordinal)
                && int.TryParse(effectTag.AsSpan(3), out int effect) && effect >= 2);

        internal static int SyncedCoverModeFromKeyboardEffect(string effectTag) =>
            effectTag == "HW:1" ? 2 : IsDynamicNeonSyncEffect(effectTag) ? 3 : 1;

        private int CoverModeFromKeyboardEffect() => SyncedCoverModeFromKeyboardEffect(_lightingEffectTag);

        /// <summary>
        /// Sync With Keyboard averages all four zones for Static and software effects only.
        /// Breathing, Neon, and other hardware animations use the selected keyboard color.
        /// </summary>
        private bool UsesAveragedCoverColor() =>
            _lightingEffectTag.Equals("HW:0", StringComparison.Ordinal)
            || _lightingEffectTag.StartsWith("DYN:", StringComparison.Ordinal);

        internal static string AverageZoneHexColor(params string[] zoneHex)
        {
            int r = 0, g = 0, b = 0, count = 0;
            foreach (string hex in zoneHex)
            {
                if (string.IsNullOrWhiteSpace(hex))
                    continue;
                try
                {
                    var c = ColorTranslator.FromHtml(hex);
                    r += c.R;
                    g += c.G;
                    b += c.B;
                    count++;
                }
                catch { }
            }

            if (count == 0)
                return "#00AEC7";
            return $"#{r / count:X2}{g / count:X2}{b / count:X2}";
        }

        private string AverageZoneHexColor()
        {
            if (_hw is ProductionHardwareService prod)
            {
                var frame = new Color[4];
                if (prod.LightingEngine.IsRunning && prod.LightingEngine.TryCopyLastFrame(frame))
                {
                    return AverageZoneHexColor(
                        $"#{frame[0].R:X2}{frame[0].G:X2}{frame[0].B:X2}",
                        $"#{frame[1].R:X2}{frame[1].G:X2}{frame[1].B:X2}",
                        $"#{frame[2].R:X2}{frame[2].G:X2}{frame[2].B:X2}",
                        $"#{frame[3].R:X2}{frame[3].G:X2}{frame[3].B:X2}");
                }
            }

            if (_pendingZonePreset is { Length: >= 4 })
            {
                return AverageZoneHexColor(
                    ToRgbHex(_pendingZonePreset[0]),
                    ToRgbHex(_pendingZonePreset[1]),
                    ToRgbHex(_pendingZonePreset[2]),
                    ToRgbHex(_pendingZonePreset[3]));
            }

            return AverageZoneHexColor(_zoneHexColors[0], _zoneHexColors[1], _zoneHexColors[2], _zoneHexColors[3]);
        }

        private void MirrorCoverFromKeyboard()
        {
            if (!IsCoverSyncWithKeyboard)
                return;
            CoverHexColor = UsesAveragedCoverColor() ? AverageZoneHexColor() : SelectedHexColor;
            CoverBrightness = RgbBrightness;
            CoverSpeed = RgbSpeed;
            CoverLightingMode = CoverModeFromKeyboardEffect();
        }

        private void UpdateCoverSyncFrameHook()
        {
            if (_hw is not ProductionHardwareService prod)
                return;

            bool shouldHook = (IsCoverSyncWithKeyboard || AnyModeKeySyncWithKeyboard())
                && _lightingEffectTag.StartsWith("DYN:", StringComparison.Ordinal);
            if (shouldHook == _coverSyncFrameHooked)
                return;

            if (shouldHook)
                prod.LightingEngine.IdleStateChanged += OnLightingEngineIdleStateChanged;
            else
                prod.LightingEngine.IdleStateChanged -= OnLightingEngineIdleStateChanged;
            _coverSyncFrameHooked = shouldHook;
        }

        private void OnLightingEngineIdleStateChanged(Color[] frame, bool idleOff)
        {
            if (_disposed || frame is not { Length: >= 4 })
                return;
            bool coverSync = IsCoverSyncWithKeyboard;
            bool modeKeySync = AnyModeKeySyncWithKeyboard();
            if (!coverSync && !modeKeySync)
                return;
            // Neon runs on the device. The engine sends initial/idle transitions only,
            // so normal animation frames perform no accessory discovery or configuration cloning.

            string hex = AverageZoneHexColor(
                $"#{frame[0].R:X2}{frame[0].G:X2}{frame[0].B:X2}",
                $"#{frame[1].R:X2}{frame[1].G:X2}{frame[1].B:X2}",
                $"#{frame[2].R:X2}{frame[2].G:X2}{frame[2].B:X2}",
                $"#{frame[3].R:X2}{frame[3].G:X2}{frame[3].B:X2}");
            string effectTag = _lightingEffectTag;
            _dispatcher.BeginInvoke(() =>
            {
                if (_disposed || _lightingEffectTag != effectTag
                    || !effectTag.StartsWith("DYN:", StringComparison.Ordinal))
                    return;
                if (IsCoverSyncWithKeyboard)
                {
                    _isApplyingLighting = true;
                    try { CoverHexColor = hex; }
                    finally { _isApplyingLighting = false; }
                }
                if (IsModeKeySyncWithKeyboard)
                {
                    _loadingDeviceEditor = true;
                    try
                    {
                        DeviceLightingColorHex = hex;
                        DeviceLightingBrightness = idleOff ? 0 : RgbBrightness;
                    }
                    finally { _loadingDeviceEditor = false; }
                }
                _ = QueueLighting(LightingWork.Frame);
            });
        }

        [RelayCommand]
        public void SetCoverColor(string hexColor)
        {
            CoverHexColor = hexColor;
        }

        [RelayCommand]
        public void SetCoverMode(string modeStr)
        {
            if (int.TryParse(modeStr, out int m))
            {
                CoverLightingMode = Math.Clamp(m, 1, 3);
            }
        }

        [RelayCommand]
        public void ApplyCoverPreset(string preset)
        {
            string hex = preset switch
            {
                "Predator" => "#00AEC7",
                "Fire" => "#FF0033",
                "Amber" => "#F59E0B",
                "Neon" => "#FF007F",
                "Matrix" => "#00FF66",
                "White" => "#FFFFFF",
                "Purple" => "#8B00FF",
                "Sunset" => "#FF6B00",
                "Ocean" => "#0077B6",
                "Sakura" => "#FF8FAB",
                "Toxic" => "#39FF14",
                "Lava" => "#FF4500",
                "Midnight" => "#3A0CA3",
                "Ice" => "#48CAE4",
                "Vapor" => "#FF71CE",
                _ => "#00AEC7"
            };
            SetCoverColor(hex);
        }

        public void SaveLightingSettings() => _ = QueueLighting(LightingWork.Save);

        public void LoadCoverLightingSettings()
        {
            _isLoadingLighting = true;
            try
            {
                var cover = PreySense.Core.Configuration.ConfigurationService.Instance.GetCurrent().Lighting.Cover;
                CoverLightingMode = cover.Mode;
                CoverHexColor = NormalizeHex(cover.ColorHex);
                // Brightness 0 replaces the old on/off switch.
                CoverBrightness = cover.Enabled ? SnapBrightness(cover.Brightness) : 0;
                IsCoverLightingEnabled = CoverBrightness > 0;
                CoverSpeed = SnapBrightness(cover.Speed);
                IsCoverSyncWithKeyboard = cover.SyncWithKeyboard;
                CoverModeName = CoverLightingMode switch
                {
                    2 => "Breathing",
                    3 => "Neon Wave",
                    _ => "Static"
                };
            }
            catch (Exception ex)
            {
                AppLogger.Log($"LoadCoverLightingSettings: {ex.Message}");
            }
            finally
            {
                _isLoadingLighting = false;
            }
        }

        public void LoadRgbSettings()
        {
            _isLoadingLighting = true;
            try
            {
                var kbd = PreySense.Core.Configuration.ConfigurationService.Instance.GetCurrent().Lighting.Keyboard;
                LoadKeyboardModeColors(PreySense.Core.Configuration.ConfigurationService.Instance.GetCurrent().Lighting.PowerModeColors);
                _keyboardLightingDirection = kbd.Direction;
                OnPropertyChanged(nameof(KeyboardLightingDirection));
                _isSyncingRgbBrightness = true;
                RgbBrightness = SnapBrightness(kbd.Brightness);
                _isSyncingRgbBrightness = false;
                RgbSpeed = SnapBrightness(kbd.Speed);
                KbdTimeoutSeconds = Math.Clamp(kbd.BacklightTimeoutSeconds, 0, 120);
                SelectedRgbZone = Math.Clamp(kbd.SelectedZone, 1, 4);
                IsRgbSyncZonesEnabled = kbd.SyncZones;
                _zoneHexColors[0] = NormalizeHex(kbd.Zone1ColorHex);
                _zoneHexColors[1] = NormalizeHex(kbd.Zone2ColorHex);
                _zoneHexColors[2] = NormalizeHex(kbd.Zone3ColorHex);
                _zoneHexColors[3] = NormalizeHex(kbd.Zone4ColorHex);
                NotifyZoneColorProperties();
                SelectedHexColor = _zoneHexColors[Math.Clamp(SelectedRgbZone - 1, 0, 3)];
                _lightingEffectTag = string.IsNullOrWhiteSpace(kbd.EffectTag) ? "MODE" : kbd.EffectTag;
                string preset = string.IsNullOrWhiteSpace(kbd.PresetTheme) ? "Mode" : kbd.PresetTheme;
                if (string.Equals(preset, "None", StringComparison.OrdinalIgnoreCase))
                    preset = "Custom";
                SelectedZonePreset = IsValidZonePreset(preset) ? preset : "Mode";
                _pendingZonePreset = null;
                ActiveEffectName = GetEffectDisplayName(_lightingEffectTag, _selectedZonePreset);
                OnPropertyChanged(nameof(LightingEffectTag));
                NotifyKeyboardLightingDirection();
            }
            catch (Exception ex)
            {
                AppLogger.Log($"LoadRgbSettings: {ex.Message}");
            }
            finally
            {
                _isLoadingLighting = false;
            }
        }

        private void RememberZoneColor(string hex, bool allZones)
        {
            string normalized = NormalizeHex(hex);
            if (allZones)
            {
                for (int i = 0; i < 4; i++)
                    _zoneHexColors[i] = normalized;
                NotifyZoneColorProperties();
                return;
            }

            int idx = Math.Clamp(SelectedRgbZone - 1, 0, 3);
            _zoneHexColors[idx] = normalized;
            NotifyZoneColorProperties(idx);
        }

        private void NotifyZoneColorProperties(int? zoneIndex = null)
        {
            if (_zoneColorChoices is { } choices)
                for (int i = 0; i < choices.Length; i++)
                    if (zoneIndex is null || zoneIndex == i) choices[i].ColorHex = _zoneHexColors[i];
            if (zoneIndex is null)
            {
                OnPropertyChanged(nameof(Zone1HexColor));
                OnPropertyChanged(nameof(Zone2HexColor));
                OnPropertyChanged(nameof(Zone3HexColor));
                OnPropertyChanged(nameof(Zone4HexColor));
                OnPropertyChanged(nameof(Zone1Foreground));
                OnPropertyChanged(nameof(Zone2Foreground));
                OnPropertyChanged(nameof(Zone3Foreground));
                OnPropertyChanged(nameof(Zone4Foreground));
                return;
            }

            switch (zoneIndex.Value)
            {
                case 0:
                    OnPropertyChanged(nameof(Zone1HexColor));
                    OnPropertyChanged(nameof(Zone1Foreground));
                    break;
                case 1:
                    OnPropertyChanged(nameof(Zone2HexColor));
                    OnPropertyChanged(nameof(Zone2Foreground));
                    break;
                case 2:
                    OnPropertyChanged(nameof(Zone3HexColor));
                    OnPropertyChanged(nameof(Zone3Foreground));
                    break;
                default:
                    OnPropertyChanged(nameof(Zone4HexColor));
                    OnPropertyChanged(nameof(Zone4Foreground));
                    break;
            }
        }

        private static string ToRgbHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

        private static string NormalizeHex(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
                return "#00AEC7";

            string value = hex.Trim();
            if (value.Length == 6 && !value.StartsWith('#'))
                value = "#" + value;

            try
            {
                return ToRgbHex(ColorTranslator.FromHtml(value));
            }
            catch
            {
                return "#00AEC7";
            }
        }

        internal static string ContrastForegroundHex(string hex)
        {
            try
            {
                var c = ColorTranslator.FromHtml(hex);
                static double Linear(byte channel)
                {
                    double value = channel / 255.0;
                    return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
                }
                double luminance = 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
                return luminance > 0.179 ? "#111111" : "#F5F5F5";
            }
            catch
            {
                return "#F5F5F5";
            }
        }

        public void UpdateKeyboardForMode(byte mode)
        {
            if (_disposed || _zoneHexColors == null || _lightingEffectTag == null) return;
            if (_lightingEffectTag.Equals("MODE", StringComparison.OrdinalIgnoreCase) ||
                _lightingEffectTag.Equals("HW:Mode", StringComparison.OrdinalIgnoreCase))
            {
                var (z1, z2, z3, z4) = ModeKeyLighting.GetZoneColors(mode, new DeviceLightingConfig { PowerModeColors = _keyboardPowerModeColors });
                _zoneHexColors[0] = z1;
                _zoneHexColors[1] = z2;
                _zoneHexColors[2] = z3;
                _zoneHexColors[3] = z4;
                NotifyZoneColorProperties();
                _isSyncingZoneColor = true;
                SelectedHexColor = _zoneHexColors[Math.Clamp(SelectedRgbZone - 1, 0, 3)];
                _isSyncingZoneColor = false;
                ActiveEffectName = "Mode";
                if (IsCoverSyncWithKeyboard || CoverLightingMode == 4)
                {
                    CoverHexColor = IsCoverSyncWithKeyboard ? z1
                        : ModeKeyLighting.ColorFor(new DeviceLightingConfig { PowerModeColors = PreySense.Core.Configuration.ConfigurationService.Instance.GetCurrent().Lighting.PowerModeColors }, mode);
                }
                ScheduleKeyboardLightingCommit();
            }
            else if (CoverLightingMode == 4)
            {
                CoverHexColor = ModeKeyLighting.ColorFor(new DeviceLightingConfig { PowerModeColors = PreySense.Core.Configuration.ConfigurationService.Instance.GetCurrent().Lighting.PowerModeColors }, mode);
                ScheduleCoverLightingCommit();
            }
        }

        private static bool IsValidZonePreset(string preset) => preset is
            "Mode" or "Custom" or "None" or "Predator" or "Cyberpunk" or "FireIce" or "Matrix" or "Stealth" or
            "Sunset" or "Ocean" or "Ember" or "Vaporwave" or
            "Sakura" or "Rainbow" or "Toxic" or "Midnight" or "Dracula" or
            "NeonHaze" or "DeepSpace" or "Inferno" or "CyberStorm";

        private static string GetEffectDisplayName(string effectTag, string preset)
        {
            if (effectTag.Equals("MODE", StringComparison.OrdinalIgnoreCase) ||
                effectTag.Equals("HW:Mode", StringComparison.OrdinalIgnoreCase))
                return "Mode";

            if (effectTag.StartsWith("DYN:", StringComparison.Ordinal) &&
                Enum.TryParse<CustomEffectMode>(effectTag[4..], out var effect))
            {
                return effect switch
                {
                    CustomEffectMode.RainbowCycle => "Rainbow Cycle",
                    CustomEffectMode.PoliceStrobe => "Police Strobe",
                    CustomEffectMode.FireEmber => "Fire Ember",
                    CustomEffectMode.MatrixCascade => "Matrix Cascade",
                    CustomEffectMode.CylonScanner => "Cylon Scanner",
                    CustomEffectMode.AuroraDrift => "Aurora Drift",
                    CustomEffectMode.Heartbeat => "Heartbeat",
                    CustomEffectMode.LightningStorm => "Lightning Storm",
                    CustomEffectMode.Starlight => "Starlight",
                    CustomEffectMode.NeonPulse => "Neon Pulse",
                    CustomEffectMode.PlasmaOrb => "Plasma Orb",
                    CustomEffectMode.GlitchStorm => "Glitch Storm",
                    CustomEffectMode.BioHazard => "Bio Hazard",
                    CustomEffectMode.SolarFlare => "Solar Flare",
                    _ => effect.ToString()
                };
            }

            if (effectTag.StartsWith("HW:", StringComparison.Ordinal) &&
                int.TryParse(effectTag[3..], out int mode))
            {
                string[] hwNames = { "Static", "Breathing", "Neon", "Wave", "Ripple", "Zoom", "Snake", "Disco" };
                if (mode == 0 && !string.IsNullOrWhiteSpace(preset) &&
                    !preset.Equals("None", StringComparison.OrdinalIgnoreCase) &&
                    !preset.Equals("Custom", StringComparison.OrdinalIgnoreCase))
                {
                    return TryGetZonePresetColors(preset, out _, out var name)
                        ? name
                        : System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(preset.ToLowerInvariant()) + " Preset";
                }
                return mode >= 0 && mode < hwNames.Length ? hwNames[mode] : $"Hardware ({mode})";
            }

            return "Static";
        }

    }
}
