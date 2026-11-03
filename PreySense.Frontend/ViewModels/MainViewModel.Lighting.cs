using System;
using System.Drawing;
using System.Diagnostics;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PreySense;
using PreySense.Frontend.Services;
using PreySense.Helpers;
using PreySense.Rgb;

namespace PreySense.Frontend.ViewModels
{
    public partial class MainViewModel
    {
        // RGB Lighting
        [ObservableProperty] private int _selectedRgbZone = 1;
        [ObservableProperty] private string _selectedHexColor = "#00E5FF";
        private readonly string[] _zoneHexColors = { "#00E5FF", "#00E5FF", "#00E5FF", "#00E5FF" };
        private bool _isSyncingZoneColor;
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
        [ObservableProperty] private string _activeEffectName = "Static";
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
        [ObservableProperty] private int _coverLightingMode = 1; // 1 = Static, 2 = Breathing, 3 = Neon
        [ObservableProperty] private string _coverHexColor = "#00E5FF";
        [ObservableProperty] private int _coverBrightness = 100;
        [ObservableProperty] private int _coverSpeed = 50;
        [ObservableProperty] private bool _isCoverSyncWithKeyboard = false;
        [ObservableProperty] private string _coverModeName = "Static";

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
        private string _selectedZonePreset = "Custom";
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
        private string _lightingEffectTag = "HW:0";
        public string LightingEffectTag => _lightingEffectTag;
        private readonly UiDebouncer _lightingDebouncer = new(TimeSpan.FromMilliseconds(80));
        private const int BrightnessStep = 25;
        private const int CoverSyncFrameIntervalMs = 250;
        private bool _coverSyncFrameHooked;
        private long _lastCoverSyncFrameTick;

        private static int SnapBrightness(int value) =>
            Math.Clamp((int)Math.Round(value / (double)BrightnessStep) * BrightnessStep, 0, 100);

        private static byte ToHidKeyboardSpeed(int percent) =>
            (byte)Math.Clamp((int)Math.Round(Math.Max(percent, 1) / 10.0), 1, 10);

        private static byte ToHidLogoSpeed(int percent) =>
            (byte)Math.Clamp((int)Math.Round(Math.Max(percent, 1) / 20.0), 1, 5);

        partial void OnSelectedLightingTabChanged(int value)
        {
            OnPropertyChanged(nameof(IsKeyboardTabSelected));
            OnPropertyChanged(nameof(IsCoverTabSelected));
        }

        partial void OnIsCoverLightingEnabledChanged(bool value)
        {
            ScheduleCoverLightingCommit();
        }

        partial void OnCoverLightingModeChanged(int value)
        {
            CoverModeName = value switch
            {
                2 => "Breathing",
                3 => "Neon Wave",
                _ => "Static"
            };
            ScheduleCoverLightingCommit();
        }

        partial void OnCoverBrightnessChanged(int value)
        {
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

            ApplyLiveBrightness(RgbBrightness);
            ScheduleKeyboardLightingCommit();
        }

        private void ApplyLiveBrightness(int percent)
        {
            if (_disposed || _isRestoringSession || _isLoadingLighting)
                return;

            byte brightness = (byte)Math.Clamp(percent, 0, 100);
            _hw.SetRgbBrightness(brightness);
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

            System.Threading.Interlocked.Add(ref _pendingBrightnessDelta, delta);
            _dispatcher.InvokeAsync(() =>
            {
                int steps = System.Threading.Interlocked.Exchange(ref _pendingBrightnessDelta, 0);
                if (_disposed || steps == 0)
                    return;

                int next = SnapBrightness(RgbBrightness + (steps * BrightnessStep));
                _isSyncingRgbBrightness = true;
                RgbBrightness = next;
                _isSyncingRgbBrightness = false;
                if (_hw is ProductionHardwareService prod)
                {
                    prod.Wmi.NoteBrightness((byte)next);
                    if (prod.LightingEngine.IsRunning)
                        prod.LightingEngine.SetBrightness((byte)next);
                }
                SaveRgbSettings();
                StatusMessage = $"Keyboard Brightness: {next}%";
                AppLogger.Log($"TurboKey: Fn brightness {next}% saved");
            });
        }

        public void SetRgbBrightnessLevel(object? param)
        {
            int b = param switch
            {
                int i => i,
                double d => (int)Math.Round(d),
                string s when int.TryParse(s, out int parsed) => parsed,
                _ => 100
            };
            RgbBrightness = SnapBrightness(b);
            if (_hw is ProductionHardwareService prod)
                prod.LightingEngine.SetBrightness((byte)RgbBrightness);
            StatusMessage = $"Keyboard Brightness: {RgbBrightness}%";
        }

        private void SetStatusMessageSafe(string message)
        {
            if (_disposed) return;
            if (_dispatcher.CheckAccess())
                StatusMessage = message;
            else
                _dispatcher.BeginInvoke(() => { if (!_disposed) StatusMessage = message; });
        }

        [RelayCommand]
        public void ApplyHardwareEffect(string effectParam)
        {
            if (int.TryParse(effectParam, out int mode))
            {
                if (_hw is ProductionHardwareService prod)
                {
                    prod.LightingEngine.Stop();
                }

                string[] hwNames = { "Static", "Breathing", "Neon", "Wave", "Ripple", "Zoom", "Snake", "Disco" };
                ActiveEffectName = mode >= 0 && mode < hwNames.Length ? hwNames[mode] : $"Hardware ({mode})";
                var c = ColorTranslator.FromHtml(SelectedHexColor);
                _hw.SetRgbHardwareEffect(mode, c.R, c.G, c.B, (byte)RgbBrightness, ToHidKeyboardSpeed(RgbSpeed), 1);
                SetStatusMessageSafe($"Hardware Lighting Effect: {ActiveEffectName}");
            }
        }

        [RelayCommand]
        public void ApplyCustomDynamicEffect(string effectModeStr)
        {
            if (!Enum.TryParse<CustomEffectMode>(effectModeStr, out var effect) || effect == CustomEffectMode.None)
                return;
            if (effect is CustomEffectMode.CpuHeatmap or CustomEffectMode.BatteryFuelGauge)
                return;

            var c = ColorTranslator.FromHtml(SelectedHexColor);
            byte scaledBrightness = (byte)Math.Clamp(RgbBrightness, 0, 100);
            int speed = Math.Clamp(RgbSpeed, 0, 100);

            ActiveEffectName = effect switch
            {
                CustomEffectMode.RainbowCycle => "Rainbow Cycle",
                CustomEffectMode.PoliceStrobe => "Police Strobe",
                CustomEffectMode.FireEmber => "Fire Ember",
                CustomEffectMode.MatrixCascade => "Matrix Cascade",
                CustomEffectMode.CylonScanner => "Cylon Scanner",
                CustomEffectMode.AuroraDrift => "Aurora Drift",
                CustomEffectMode.OceanTide => "Ocean Tide",
                CustomEffectMode.Heartbeat => "Heartbeat",
                CustomEffectMode.LightningStorm => "Lightning Storm",
                CustomEffectMode.Starlight => "Starlight",
                CustomEffectMode.IceCrystal => "Ice Crystal",
                _ => effect.ToString()
            };

            if (_hw is ProductionHardwareService prod)
            {
                prod.LightingEngine.SetIdleTimeoutSeconds(KbdTimeoutSeconds);
                prod.LightingEngine.ActiveBrushColor = c;
                if (prod.LightingEngine.IsRunning && prod.LightingEngine.CurrentMode == effect)
                {
                    prod.LightingEngine.SetBrightness(scaledBrightness);
                    prod.LightingEngine.SetSpeed((byte)speed);
                }
                else
                {
                    prod.LightingEngine.StartEffect(effect, scaledBrightness, speed);
                }
            }

            SetStatusMessageSafe($"Dynamic Animation: {ActiveEffectName}");
        }

        public static bool TryGetZonePresetColors(string? presetName, out Color[] colors, out string displayName)
        {
            Color z1, z2, z3, z4;
            switch (presetName?.ToLowerInvariant())
            {
                case "predator":
                    z1 = ColorTranslator.FromHtml("#00E5FF");
                    z2 = ColorTranslator.FromHtml("#00E5FF");
                    z3 = ColorTranslator.FromHtml("#00E5FF");
                    z4 = ColorTranslator.FromHtml("#FFFFFF");
                    displayName = "Predator Cyan Preset";
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
                case "royal":
                    z1 = ColorTranslator.FromHtml("#3C096C");
                    z2 = ColorTranslator.FromHtml("#5A189A");
                    z3 = ColorTranslator.FromHtml("#FFD60A");
                    z4 = ColorTranslator.FromHtml("#FFFFFF");
                    displayName = "Royal Preset";
                    break;
                case "arctic":
                    z1 = ColorTranslator.FromHtml("#ADE8F4");
                    z2 = ColorTranslator.FromHtml("#FFFFFF");
                    z3 = ColorTranslator.FromHtml("#48CAE4");
                    z4 = ColorTranslator.FromHtml("#0077B6");
                    displayName = "Arctic Preset";
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
                case "lava":
                    z1 = ColorTranslator.FromHtml("#8B0000");
                    z2 = ColorTranslator.FromHtml("#FF0000");
                    z3 = ColorTranslator.FromHtml("#FF4500");
                    z4 = ColorTranslator.FromHtml("#FF6B00");
                    displayName = "Lava Preset";
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
            OnPropertyChanged(nameof(LightingEffectTag));
            ActiveEffectName = GetEffectDisplayName(_lightingEffectTag, _selectedZonePreset);
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
            if (keyboard)
                ApplyKeyboardLightingNow();
            if (cover)
                ApplyCoverLighting(saveSettings: true);
        }

        public void CommitKeyboardLighting() => ApplyKeyboardLightingNow();

        public void CommitCoverLighting() => ApplyCoverLighting(saveSettings: true);

        private int _startupLightingRan;

        public void ApplyStartupLighting()
        {
            if (_disposed)
                return;

            if (Interlocked.CompareExchange(ref _startupLightingRan, 1, 0) != 0)
                return;

            _ = Task.Run(async () =>
            {
                AppLogger.Log("ApplyStartupLighting: Starting startup lighting application sequence.");

                // Poll for the RGB hardware client to become available (up to 15 seconds)
                const int maxAttempts = 30; // 30 * 500ms = 15 seconds max
                const int pollIntervalMs = 500;
                bool clientConnected = false;

                for (int attempt = 1; attempt <= maxAttempts && !_disposed; attempt++)
                {
                    try
                    {
                        if (_hw.IsRgbAvailable)
                        {
                            clientConnected = true;
                            AppLogger.Log($"ApplyStartupLighting: RGB client connected on attempt {attempt} (elapsed ~{(attempt - 1) * pollIntervalMs}ms).");
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Log($"ApplyStartupLighting: Check connection error on attempt {attempt}: {ex.Message}");
                    }

                    await Task.Delay(pollIntervalMs).ConfigureAwait(false);
                }

                if (_disposed)
                    return;

                if (!clientConnected)
                {
                    AppLogger.Log("ApplyStartupLighting: RGB client not detected within timeout window; attempting blind apply.");
                }

                // Pass 1: Immediate application upon connection
                try
                {
                    ApplyKeyboardLightingNow();
                    if (!IsCoverSyncWithKeyboard)
                        ApplyCoverLighting(saveSettings: false);
                    AppLogger.Log("ApplyStartupLighting: Pass 1 applied.");
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"ApplyStartupLighting Pass 1 error: {ex.Message}");
                }

                // Pass 2: Delayed reinforcement after 2.5 seconds to override any OEM firmware POST reset
                try
                {
                    await Task.Delay(2500).ConfigureAwait(false);
                    if (!_disposed)
                    {
                        ApplyKeyboardLightingNow();
                        if (!IsCoverSyncWithKeyboard)
                            ApplyCoverLighting(saveSettings: false);
                        AppLogger.Log("ApplyStartupLighting: Pass 2 (reinforcement) applied.");
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"ApplyStartupLighting Pass 2 error: {ex.Message}");
                }
            });
        }

        private void ApplyKeyboardLightingNow()
        {
            bool isDynamic = _lightingEffectTag.StartsWith("DYN:", StringComparison.Ordinal);
            if (_hw is ProductionHardwareService prod && !isDynamic)
                prod.LightingEngine.Stop();

            if (isDynamic)
                ApplyCustomDynamicEffect(_lightingEffectTag[4..]);
            else if (_lightingEffectTag.StartsWith("HW:", StringComparison.Ordinal) &&
                     _lightingEffectTag != "HW:0")
                ApplyHardwareEffect(_lightingEffectTag[3..]);
            else
                ApplyKeyboardLightingSettings();

            UpdateCoverSyncFrameHook();
            _hw.SetKbdBacklightTimeoutSeconds(KbdTimeoutSeconds);
            SaveRgbSettings();
            if (IsCoverSyncWithKeyboard)
                ApplyCoverLighting(saveSettings: true);
        }

        private int CoverModeFromKeyboardEffect()
        {
            if (_lightingEffectTag.Equals("HW:1", StringComparison.Ordinal))
                return 2;
            if (_lightingEffectTag.Equals("HW:2", StringComparison.Ordinal))
                return 3;
            return 1;
        }

        private bool UsesAveragedCoverColor()
        {
            if (_lightingEffectTag.StartsWith("DYN:", StringComparison.Ordinal))
                return true;
            return _lightingEffectTag.StartsWith("HW:", StringComparison.Ordinal)
                && !_lightingEffectTag.Equals("HW:0", StringComparison.Ordinal)
                && !_lightingEffectTag.Equals("HW:1", StringComparison.Ordinal)
                && !_lightingEffectTag.Equals("HW:2", StringComparison.Ordinal);
        }

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
                return "#00E5FF";
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

            bool shouldHook = IsCoverSyncWithKeyboard
                && _lightingEffectTag.StartsWith("DYN:", StringComparison.Ordinal);
            if (shouldHook == _coverSyncFrameHooked)
                return;

            if (shouldHook)
                prod.LightingEngine.FrameUpdated += OnLightingEngineFrameUpdated;
            else
                prod.LightingEngine.FrameUpdated -= OnLightingEngineFrameUpdated;
            _coverSyncFrameHooked = shouldHook;
        }

        private void OnLightingEngineFrameUpdated(Color[] frame)
        {
            if (!IsCoverSyncWithKeyboard || _disposed || frame is not { Length: >= 4 })
                return;
            if (!UsesAveragedCoverColor())
                return;
            long now = Stopwatch.GetTimestamp();
            if (_lastCoverSyncFrameTick != 0 && Stopwatch.GetElapsedTime(_lastCoverSyncFrameTick, now).TotalMilliseconds < CoverSyncFrameIntervalMs)
                return;
            _lastCoverSyncFrameTick = now;

            string hex = AverageZoneHexColor(
                $"#{frame[0].R:X2}{frame[0].G:X2}{frame[0].B:X2}",
                $"#{frame[1].R:X2}{frame[1].G:X2}{frame[1].B:X2}",
                $"#{frame[2].R:X2}{frame[2].G:X2}{frame[2].B:X2}",
                $"#{frame[3].R:X2}{frame[3].G:X2}{frame[3].B:X2}");
            var color = ColorTranslator.FromHtml(hex);
            _dispatcher.BeginInvoke(() =>
            {
                if (_disposed || !IsCoverSyncWithKeyboard)
                    return;
                CoverHexColor = hex;
                _hw.SetCoverLogoLighting(1, color, (byte)Math.Clamp(RgbBrightness, 0, 100), ToHidLogoSpeed(RgbSpeed), IsCoverLightingEnabled);
            });
        }

        [RelayCommand]
        public void SelectLightingTab(string tab)
        {
            SelectedLightingTab = (tab == "1" || tab.Equals("Cover", StringComparison.OrdinalIgnoreCase)) ? 1 : 0;
            OnPropertyChanged(nameof(IsKeyboardTabSelected));
            OnPropertyChanged(nameof(IsCoverTabSelected));
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
        public void SetCoverBrightnessLevel(object? param)
        {
            int b = param switch
            {
                int i => i,
                double d => (int)Math.Round(d),
                string s when int.TryParse(s, out int parsed) => parsed,
                _ => 100
            };
            CoverBrightness = SnapBrightness(b);
        }

        [RelayCommand]
        public void SetCoverSpeedLevel(object? param)
        {
            int speedVal = param switch
            {
                int i => i,
                double d => (int)Math.Round(d),
                string str when int.TryParse(str, out int parsed) => parsed,
                _ => 50
            };
            CoverSpeed = SnapBrightness(speedVal);
        }

        [RelayCommand]
        public void ToggleCoverPower()
        {
            IsCoverLightingEnabled = !IsCoverLightingEnabled;
        }

        [RelayCommand]
        public void ApplyCoverPreset(string preset)
        {
            string hex = preset switch
            {
                "Predator" => "#00E5FF",
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
                _ => "#00E5FF"
            };
            SetCoverColor(hex);
        }

        public void ApplyLightingSettings()
        {
            if (IsCoverTabSelected)
            {
                ApplyCoverLighting(saveSettings: false);
                return;
            }

            ApplyKeyboardLightingSettings();
        }

        public void ApplyKeyboardLightingSettings()
        {
            var keyboardColor = ColorTranslator.FromHtml(SelectedHexColor);
            if (_pendingZonePreset is { Length: >= 4 })
            {
                _hw.SetRgbAllZones(_pendingZonePreset[0], _pendingZonePreset[1], _pendingZonePreset[2], _pendingZonePreset[3]);
            }
            else if (IsRgbSyncZonesEnabled)
            {
                _hw.SetRgbAllZones(keyboardColor, keyboardColor, keyboardColor, keyboardColor);
            }
            else
            {
                _hw.SetRgbAllZones(
                    ColorTranslator.FromHtml(_zoneHexColors[0]),
                    ColorTranslator.FromHtml(_zoneHexColors[1]),
                    ColorTranslator.FromHtml(_zoneHexColors[2]),
                    ColorTranslator.FromHtml(_zoneHexColors[3]));
            }

            _hw.SetRgbBrightness((byte)Math.Clamp(RgbBrightness, 0, 100));
            _hw.SetKbdBacklightTimeoutSeconds(KbdTimeoutSeconds);
            SaveRgbSettings();
            SetStatusMessageSafe("Lighting settings applied.");
        }

        public void SaveLightingSettings()
        {
            SaveRgbSettings();
            SaveCoverLightingSettings();
            SetStatusMessageSafe("Lighting settings saved.");
        }

        public void ApplyCoverLighting(bool saveSettings = true)
        {
            _isApplyingLighting = true;
            try
            {
                bool sync = IsCoverSyncWithKeyboard;
                if (sync)
                    MirrorCoverFromKeyboard();

                string hex = CoverHexColor;
                var color = ColorTranslator.FromHtml(hex);
                byte b = (byte)Math.Clamp(sync ? RgbBrightness : CoverBrightness, 0, 100);
                if (sync)
                    CoverBrightness = b;
                byte s = ToHidLogoSpeed(sync ? RgbSpeed : CoverSpeed);
                int mode = !IsCoverLightingEnabled ? 0 : (sync ? CoverModeFromKeyboardEffect() : CoverLightingMode);
                _hw.SetCoverLogoLighting(mode, color, b, s, IsCoverLightingEnabled);

                string modeDesc = (sync ? CoverModeFromKeyboardEffect() : CoverLightingMode) switch
                {
                    2 => "Breathing",
                    3 => "Neon Wave",
                    _ => "Static"
                };
                SetStatusMessageSafe(IsCoverLightingEnabled
                    ? $"Cover Backlight: {modeDesc} ({hex}, {CoverBrightness}%)"
                    : "Cover Backlight: Disabled (Off)");

                if (saveSettings)
                {
                    SaveCoverLightingSettings();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"ApplyCoverLighting error: {ex.Message}");
            }
            finally
            {
                _isApplyingLighting = false;
            }
        }

        public void LoadCoverLightingSettings()
        {
            _isLoadingLighting = true;
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\PreySense\Lighting\Cover");
                if (key != null)
                {
                    IsCoverLightingEnabled = (key.GetValue("Enabled") as int? ?? 1) == 1;
                    CoverLightingMode = key.GetValue("Mode") as int? ?? 1;
                    CoverHexColor = key.GetValue("Color") as string ?? "#00E5FF";
                    CoverBrightness = SnapBrightness(MigrateSavedBrightness(key.GetValue("Brightness") as int? ?? 100));
                    CoverSpeed = SnapBrightness(MigrateSavedSpeed(key.GetValue("Speed") as int? ?? 50));
                    IsCoverSyncWithKeyboard = (key.GetValue("SyncWithKeyboard") as int? ?? 0) == 1;
                    CoverModeName = CoverLightingMode switch
                    {
                        2 => "Breathing",
                        3 => "Neon Wave",
                        _ => "Static"
                    };
                }
            }
            catch { }
            finally
            {
                _isLoadingLighting = false;
            }
        }

        public void SaveCoverLightingSettings()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"SOFTWARE\PreySense\Lighting\Cover");
                if (key != null)
                {
                    key.SetValue("Enabled", IsCoverLightingEnabled ? 1 : 0);
                    key.SetValue("Mode", CoverLightingMode);
                    key.SetValue("Color", CoverHexColor);
                    key.SetValue("Brightness", CoverBrightness);
                    key.SetValue("Speed", CoverSpeed);
                    key.SetValue("SyncWithKeyboard", IsCoverSyncWithKeyboard ? 1 : 0);
                }
            }
            catch { }
        }

        public void LoadRgbSettings()
        {
            _isLoadingLighting = true;
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\PreySense\Lighting\Keyboard");
                if (key != null)
                {
                    _isSyncingRgbBrightness = true;
                    RgbBrightness = SnapBrightness(MigrateSavedBrightness(ReadRegistryInt(key.GetValue("Brightness"), 100)));
                    _isSyncingRgbBrightness = false;
                    RgbSpeed = SnapBrightness(MigrateSavedSpeed(ReadRegistryInt(key.GetValue("Speed"), 50)));
                    KbdTimeoutSeconds = Math.Clamp(ReadRegistryInt(key.GetValue("BacklightTimeoutSeconds"), 30), 0, 120);
                    SelectedHexColor = key.GetValue("SelectedColor") as string ?? "#00E5FF";
                    SelectedRgbZone = Math.Clamp(ReadRegistryInt(key.GetValue("SelectedZone"), 1), 1, 4);
                    IsRgbSyncZonesEnabled = ReadRegistryInt(key.GetValue("SyncZones"), 0) == 1;
                    string fallbackHex = NormalizeHex(SelectedHexColor);
                    _zoneHexColors[0] = NormalizeHex(key.GetValue("Zone1Color") as string ?? fallbackHex);
                    _zoneHexColors[1] = NormalizeHex(key.GetValue("Zone2Color") as string ?? fallbackHex);
                    _zoneHexColors[2] = NormalizeHex(key.GetValue("Zone3Color") as string ?? fallbackHex);
                    _zoneHexColors[3] = NormalizeHex(key.GetValue("Zone4Color") as string ?? fallbackHex);
                    NotifyZoneColorProperties();
                    SelectedHexColor = _zoneHexColors[Math.Clamp(SelectedRgbZone - 1, 0, 3)];
                    string effectTag = key.GetValue("EffectTag") as string ?? "HW:0";
                    _lightingEffectTag = IsValidLightingEffectTag(effectTag) ? effectTag : "HW:0";
                    string preset = key.GetValue("PresetTheme") as string ?? "Custom";
                    if (string.Equals(preset, "None", StringComparison.OrdinalIgnoreCase))
                        preset = "Custom";

                    if (TryGetZonePresetColors(preset, out var presetColors, out var presetDisplayName))
                    {
                        SelectedZonePreset = preset;
                        _pendingZonePreset = presetColors;
                        IsRgbSyncZonesEnabled = false;
                        _zoneHexColors[0] = ToRgbHex(presetColors[0]);
                        _zoneHexColors[1] = ToRgbHex(presetColors[1]);
                        _zoneHexColors[2] = ToRgbHex(presetColors[2]);
                        _zoneHexColors[3] = ToRgbHex(presetColors[3]);
                        NotifyZoneColorProperties();
                        SelectedHexColor = _zoneHexColors[Math.Clamp(SelectedRgbZone - 1, 0, 3)];
                        ActiveEffectName = presetDisplayName;
                    }
                    else
                    {
                        SelectedZonePreset = "Custom";
                        _pendingZonePreset = null;
                        ActiveEffectName = GetEffectDisplayName(_lightingEffectTag, _selectedZonePreset);
                    }
                    OnPropertyChanged(nameof(LightingEffectTag));
                }
            }
            catch { }
            finally
            {
                _isLoadingLighting = false;
            }
        }

        public void SaveRgbSettings()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"SOFTWARE\PreySense\Lighting\Keyboard");
                if (key != null)
                {
                    key.SetValue("Brightness", RgbBrightness, Microsoft.Win32.RegistryValueKind.DWord);
                    key.SetValue("Speed", RgbSpeed, Microsoft.Win32.RegistryValueKind.DWord);
                    key.SetValue("BacklightTimeoutSeconds", KbdTimeoutSeconds, Microsoft.Win32.RegistryValueKind.DWord);
                    key.SetValue("SelectedColor", SelectedHexColor);
                    key.SetValue("Zone1Color", _zoneHexColors[0]);
                    key.SetValue("Zone2Color", _zoneHexColors[1]);
                    key.SetValue("Zone3Color", _zoneHexColors[2]);
                    key.SetValue("Zone4Color", _zoneHexColors[3]);
                    key.SetValue("SelectedZone", SelectedRgbZone, Microsoft.Win32.RegistryValueKind.DWord);
                    key.SetValue("SyncZones", IsRgbSyncZonesEnabled ? 1 : 0, Microsoft.Win32.RegistryValueKind.DWord);
                    key.SetValue("EffectTag", _lightingEffectTag);
                    key.SetValue("PresetTheme", _selectedZonePreset);
                }
            }
            catch { }
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
                return "#00E5FF";

            string value = hex.Trim();
            if (value.Length == 6 && !value.StartsWith('#'))
                value = "#" + value;

            try
            {
                return ToRgbHex(ColorTranslator.FromHtml(value));
            }
            catch
            {
                return "#00E5FF";
            }
        }

        private static string ContrastForegroundHex(string hex)
        {
            try
            {
                var c = ColorTranslator.FromHtml(hex);
                double luminance = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
                return luminance > 0.62 ? "#111111" : "#F5F5F5";
            }
            catch
            {
                return "#F5F5F5";
            }
        }

        private static int ReadRegistryInt(object? value, int fallback)
        {
            return value switch
            {
                int i => i,
                long l => (int)l,
                string s when int.TryParse(s, out int parsed) => parsed,
                _ => fallback
            };
        }

        private static int MigrateSavedBrightness(int saved)
        {
            if (saved <= 5)
                return Math.Clamp(saved * 25, 0, 100);
            return SnapBrightness(saved);
        }

        private static int MigrateSavedSpeed(int saved)
        {
            if (saved <= 9)
                return saved <= 0 ? 0 : SnapBrightness(Math.Max(25, (int)Math.Round(saved * 100.0 / 9.0)));
            return SnapBrightness(saved);
        }

        private static bool IsValidLightingEffectTag(string tag)
        {
            if (string.Equals(tag, "HW:0", StringComparison.Ordinal))
                return true;
            if (tag.StartsWith("HW:", StringComparison.Ordinal) &&
                int.TryParse(tag.AsSpan(3), out int mode) &&
                mode is >= 1 and <= 7)
                return true;
            return tag.StartsWith("DYN:", StringComparison.Ordinal) &&
                   Enum.TryParse<CustomEffectMode>(tag.AsSpan(4), out var effect) &&
                   effect is not (CustomEffectMode.None or CustomEffectMode.CpuHeatmap or CustomEffectMode.BatteryFuelGauge);
        }

        private static bool IsValidZonePreset(string preset) => preset is
            "Custom" or "None" or "Predator" or "Cyberpunk" or "FireIce" or "Matrix" or "Stealth" or
            "Sunset" or "Ocean" or "Ember" or "Vaporwave" or "Royal" or "Arctic" or
            "Sakura" or "Rainbow" or "Toxic" or "Lava" or "Midnight" or "Dracula";

        private static string GetEffectDisplayName(string effectTag, string preset)
        {
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
                    CustomEffectMode.OceanTide => "Ocean Tide",
                    CustomEffectMode.Heartbeat => "Heartbeat",
                    CustomEffectMode.LightningStorm => "Lightning Storm",
                    CustomEffectMode.Starlight => "Starlight",
                    CustomEffectMode.IceCrystal => "Ice Crystal",
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

        [RelayCommand]
        public async Task ToggleKbdTimeoutAsync()
        {
            IsKbdBacklightTimeoutEnabled = !IsKbdBacklightTimeoutEnabled;
            int seconds = IsKbdBacklightTimeoutEnabled ? (KbdTimeoutSeconds > 0 ? KbdTimeoutSeconds : 30) : 0;
            KbdTimeoutSeconds = seconds;
            OnPropertyChanged(nameof(KbdTimeoutDisplay));
            await Task.Run(() => _hw.SetKbdBacklightTimeoutSeconds(seconds));
            StatusMessage = seconds > 0 ? $"Keyboard Backlight Sleep: {seconds}s" : "Keyboard Backlight: Always On (No Sleep)";
        }

        [RelayCommand]
        public async Task SetKbdTimeoutSecondsAsync(object? param)
        {
            int sec = param switch
            {
                int i => i,
                double d => (int)Math.Round(d),
                string s when int.TryParse(s, out int parsed) => parsed,
                _ => 30
            };

            KbdTimeoutSeconds = sec;
            IsKbdBacklightTimeoutEnabled = sec > 0;
            OnPropertyChanged(nameof(KbdTimeoutDisplay));
            await Task.Run(() => _hw.SetKbdBacklightTimeoutSeconds(sec));
            StatusMessage = sec > 0 ? $"Keyboard Backlight Sleep: {sec}s" : "Keyboard Backlight: Always On (No Sleep)";
        }
    }
}
