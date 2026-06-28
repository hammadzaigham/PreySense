using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;
using Microsoft.Win32;
using PreySense.Helpers;
using PreySense.UI;

namespace PreySense.Rgb
{
    [SupportedOSPlatform("windows")]
    public partial class RgbForm : RForm
    {
        private readonly MainForm _mainForm;
        private readonly WmiController _wmi;
        private readonly ColorDialog _colorPicker = new() { FullOpen = true };

        private static readonly string[] RgbModeNames = RgbProfile.UiModeNames;

        private static readonly string[] CustomPresetNames =
        {
            "None / Custom", "Ice Cold", "Volcano", "Cyberpunk", "Sunset Glow", "Deep Space", "Nordic Aurora", "Magma Flow"
        };

        private static readonly Color[][] CustomPresetColors =
        {
            new[] { Color.Empty, Color.Empty, Color.Empty, Color.Empty },
            new[] { Color.Cyan, Color.DeepSkyBlue, Color.Cyan, Color.DeepSkyBlue },
            new[] { Color.Red, Color.OrangeRed, Color.DarkRed, Color.Orange },
            new[] { Color.FromArgb(255, 0, 128), Color.Cyan, Color.FromArgb(255, 0, 128), Color.Cyan },
            new[] { Color.DarkOrange, Color.DeepPink, Color.Purple, Color.DarkViolet },
            new[] { Color.MidnightBlue, Color.MediumSlateBlue, Color.DarkOrchid, Color.RoyalBlue },
            new[] { Color.Teal, Color.SpringGreen, Color.MediumAquamarine, Color.LimeGreen },
            new[] { Color.DarkRed, Color.OrangeRed, Color.Gold, Color.Yellow }
        };

        private bool _ledTimeoutEnabled = false;
        private bool _loadingState;
        private bool _hardwareApplyEnabled;

        public RgbForm(MainForm mainForm, WmiController wmi)
        {
            _mainForm = mainForm;
            _wmi = wmi;
            _dpiScale = DeviceDpi / 96f;
            _loadingState = true;

            InitializeComponent();
            InitTheme(false);
            LoadState();
            _loadingState = false;
        }

        private void ArmHardwareApply() => _hardwareApplyEnabled = true;

        private bool CanApplyHardware() => !_loadingState && _hardwareApplyEnabled;

        private void UpdateModeLayout(int mode)
        {
            UpdateDirectionVisibility(mode);
            bool isStatic = mode == 0;
            _speedSettingRow.Parent!.Visible = !isStatic;
            _brightnessSettingRow.Parent!.Visible = true;
            _zoneRow.Visible = isStatic;
            _presetRow.Visible = isStatic;
        }

        private static int GetRegistryInt(RegistryKey? key, string name, int fallback) =>
            key != null ? (int)key.GetValue(name, fallback) : fallback;

        private void ApplyMode()
        {
            if (!CanApplyHardware()) return;
            int idx = _effectDropdown.SelectedIndex;
            int mode = idx >= 0 && idx < RgbProfile.ModeCount ? idx : 0;
            byte brightness = GetRgbBrightness();
            byte speed = (byte)_speedSettingRow.Value;
            byte direction = GetSelectedDirection();
            UpdateModeLayout(mode);
            _wmi.SetRgbMode(mode, _wmi.LastR, _wmi.LastG, _wmi.LastB, brightness, speed, direction);
            SaveRgbState(mode);
        }

        private byte GetRgbBrightness() => (byte)_brightnessSettingRow.Value;

        private byte GetSelectedDirection() =>
            _directionDropdown.SelectedIndex == 1 ? (byte)1 : (byte)2;

        private void UpdateDirectionVisibility(int mode)
        {
            _directionRow.Visible = mode == RgbProfile.WaveModeIndex;
        }

        private void ApplyDirection()
        {
            if (!CanApplyHardware()) return;
            if (_effectDropdown.SelectedIndex != RgbProfile.WaveModeIndex) return;
            byte direction = GetSelectedDirection();
            _wmi.SetDirection(direction);
            SaveRgbState();
        }

        private void ToggleLedTimeout()
        {
            ArmHardwareApply();
            SetLedTimeout(!_ledTimeoutEnabled);
        }

        private void SetLedTimeout(bool enabled)
        {
            _ledTimeoutEnabled = enabled;
            _ledTimeoutButton.Activated = enabled;
            _ledTimeoutButton.BackColor = enabled ? colorStandard : buttonSecond;
            _ledTimeoutButton.ForeColor = enabled ? SystemColors.ControlLightLight : foreMain;
            if (!CanApplyHardware()) return;
            _wmi.SetLedTimeout(enabled);
            _mainForm.SaveState("LedTimeout", enabled ? 1 : 0);
        }

        private void PickZone(int zone)
        {
            ArmHardwareApply();
            _colorPicker.Color = _wmi.ZoneColors[zone];
            if (_colorPicker.ShowDialog() != DialogResult.OK) return;


            Color color = _colorPicker.Color;
            _wmi.ZoneColors[zone] = color;
            _presetDropdown.SelectedIndex = 0;
            _wmi.SetZoneColors(_wmi.ZoneColors, GetRgbBrightness());
            SaveRgbState(0);
            UpdateZoneColors();
        }

        private void SyncZones()
        {
            ArmHardwareApply();
            Color color = _wmi.ZoneColors[0];
            for (int i = 0; i < 4; i++) _wmi.ZoneColors[i] = color;
            _presetDropdown.SelectedIndex = 0;
            _wmi.SetZoneColors(_wmi.ZoneColors, GetRgbBrightness());
            SaveRgbState(0);
            UpdateZoneColors();
        }

        private void UpdateZoneColors()
        {
            for (int i = 0; i < 4; i++)
                _zoneColorButtons[i].BackColor = _wmi.ZoneColors[i];
        }

        private void ApplyPreset()
        {
            if (!CanApplyHardware()) return;
            int presetIdx = _presetDropdown.SelectedIndex;
            if (presetIdx <= 0) return;

            Color[] colors = CustomPresetColors[presetIdx];
            for (int i = 0; i < 4; i++)
            {
                _wmi.ZoneColors[i] = colors[i];
            }

            _mainForm.SaveState("RGB_Preset", presetIdx);
            _wmi.SetZoneColors(_wmi.ZoneColors, GetRgbBrightness());
            SaveRgbState(0);
            UpdateZoneColors();
        }

        private void SaveRgbState(int? modeOverride = null)
        {
            int mode = modeOverride ?? (_effectDropdown.SelectedIndex >= 0 ? _effectDropdown.SelectedIndex : _wmi.LastRgbMode);
            mode = Math.Clamp(mode, 0, RgbProfile.ModeCount - 1);

            _mainForm.SaveState("RGB_Mode", mode);
            _mainForm.SaveState("RGB_ModeVersion", 2);
            _mainForm.SaveState("RGB_Speed", _speedSettingRow.Value);
            _mainForm.SaveState("RGB_Brightness", _brightnessSettingRow.Value);
            _mainForm.SaveState("RGB_Direction", GetSelectedDirection());

            for (int i = 0; i < Math.Min(4, _wmi.ZoneColors.Length); i++)
            {
                Color color = _wmi.ZoneColors[i];
                _mainForm.SaveState($"RGB_Zone{i}_R", color.R);
                _mainForm.SaveState($"RGB_Zone{i}_G", color.G);
                _mainForm.SaveState($"RGB_Zone{i}_B", color.B);
            }
        }

        private void LoadState()
        {
            try
            {
                _loadingState = true;
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\PreySense");
                int savedModeVersion = GetRegistryInt(key, "RGB_ModeVersion", 1);
                int savedMode = key != null
                    ? RgbProfile.NormalizeSavedMode(GetRegistryInt(key, "RGB_Mode", _wmi.LastRgbMode), savedModeVersion)
                    : _wmi.LastRgbMode;
                int rgbMode = Math.Clamp(_wmi.LastRgbMode, 0, RgbProfile.ModeCount - 1);
                if (rgbMode == 0 && savedMode != 0)
                    rgbMode = savedMode;
                _effectDropdown.SelectedIndex = rgbMode;

                Color[] colors = new Color[4];
                for (int i = 0; i < colors.Length; i++)
                {
                    Color fallback = i < _wmi.ZoneColors.Length ? _wmi.ZoneColors[i] : Color.FromArgb(_wmi.LastR, _wmi.LastG, _wmi.LastB);
                    int zR = GetRegistryInt(key, $"RGB_Zone{i}_R", fallback.R);
                    int zG = GetRegistryInt(key, $"RGB_Zone{i}_G", fallback.G);
                    int zB = GetRegistryInt(key, $"RGB_Zone{i}_B", fallback.B);
                    colors[i] = Color.FromArgb(zR, zG, zB);
                }

                int speed = RgbProfile.NormalizeStepLevel(GetRegistryInt(key, "RGB_Speed", _wmi.Speed));
                int brightness = RgbProfile.NormalizeStepLevel(GetRegistryInt(key, "RGB_Brightness", _wmi.Brightness));
                int direction = GetRegistryInt(key, "RGB_Direction", _wmi.Direction);
                int preset = GetRegistryInt(key, "RGB_Preset", 0);

                _wmi.SetCachedRgbState(
                    rgbMode,
                    colors[0].R,
                    colors[0].G,
                    colors[0].B,
                    (byte)brightness,
                    (byte)speed,
                    direction == 1 ? (byte)1 : (byte)2,
                    colors);

                _speedSettingRow.Value = speed;
                _brightnessSettingRow.Value = brightness;
                _presetDropdown.SelectedIndex = Math.Clamp(preset, 0, CustomPresetNames.Length - 1);
                _directionDropdown.SelectedIndex = direction == 1 ? 1 : 0;

                bool ledTimeout = key?.GetValue("LedTimeout") is int savedTimeout
                    ? savedTimeout == 1
                    : _wmi.GetLedTimeout();
                SetLedTimeout(ledTimeout);

                UpdateZoneColors();
                UpdateModeLayout(rgbMode);
            }
            catch { }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            _fontSection.Dispose();
            _fontLabel.Dispose();
            _fontBody.Dispose();
            _fontBold.Dispose();
        }
    }
}
