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
            "None / Custom", "Ice Cold", "Volcano", "Cyberpunk", "Neon Matrix", "Forest", "Miami Vice"
        };

        private static readonly Color[][] CustomPresetColors =
        {
            new[] { Color.Empty, Color.Empty, Color.Empty, Color.Empty },
            new[] { Color.Cyan, Color.DeepSkyBlue, Color.Cyan, Color.DeepSkyBlue },
            new[] { Color.Red, Color.OrangeRed, Color.DarkRed, Color.Orange },
            new[] { Color.FromArgb(255, 0, 128), Color.Cyan, Color.FromArgb(255, 0, 128), Color.Cyan },
            new[] { Color.Lime, Color.Green, Color.Lime, Color.Green },
            new[] { Color.ForestGreen, Color.DarkGreen, Color.ForestGreen, Color.DarkGreen },
            new[] { Color.FromArgb(255, 105, 180), Color.FromArgb(0, 240, 255), Color.FromArgb(255, 105, 180), Color.FromArgb(0, 240, 255) }
        };

        private Color _keyColor = Color.FromArgb(0, 150, 255);
        private bool _ledTimeoutEnabled = false;
        private bool _loadingState;

        public RgbForm(MainForm mainForm, WmiController wmi)
        {
            _mainForm = mainForm;
            _wmi = wmi;
            _dpiScale = DeviceDpi / 96f;

            InitializeComponent();
            InitTheme(true);
            LoadState();
        }

        private void ApplyMode()
        {
            if (_loadingState) return;
            int idx = _effectDropdown.SelectedIndex;
            int mode = idx >= 0 && idx < RgbProfile.ModeCount ? idx : 0;
            byte brightness = GetRgbBrightness();
            byte speed = (byte)_speedSettingRow.Value;
            byte direction = GetSelectedDirection();
            UpdateDirectionVisibility(mode);
            bool isStatic = mode == 0;
            _speedSettingRow.Parent!.Visible = !isStatic;
            _brightnessSettingRow.Parent!.Visible = true;
            _zoneRow.Visible = isStatic;
            _presetRow.Visible = isStatic;
            _wmi.SetRgbMode(mode, _wmi.LastR, _wmi.LastG, _wmi.LastB, brightness, speed, direction);
            SaveRgbState(mode);
        }

        private byte GetRgbBrightness() => (byte)_brightnessSettingRow.Value;

        private byte GetSelectedDirection() =>
            _directionDropdown.SelectedIndex == 1 ? (byte)2 : (byte)1;

        private void UpdateDirectionVisibility(int mode)
        {
            _directionRow.Visible = mode == RgbProfile.WaveModeIndex;
        }

        private void ApplyDirection()
        {
            if (_loadingState) return;
            if (_effectDropdown.SelectedIndex != RgbProfile.WaveModeIndex) return;
            byte direction = GetSelectedDirection();
            _wmi.SetDirection(direction);
            SaveRgbState();
        }

        private void ToggleLedTimeout()
        {
            SetLedTimeout(!_ledTimeoutEnabled);
        }

        private void SetLedTimeout(bool enabled)
        {
            _ledTimeoutEnabled = enabled;
            _ledTimeoutButton.Activated = enabled;
            _ledTimeoutButton.BackColor = enabled ? colorStandard : buttonSecond;
            _ledTimeoutButton.ForeColor = enabled ? SystemColors.ControlLightLight : foreMain;
            if (_loadingState) return;
            _wmi.SetLedTimeout(enabled);
            _mainForm.SaveState("LedTimeout", enabled ? 1 : 0);
        }

        private void PickZone(int zone)
        {
            _colorPicker.Color = _wmi.ZoneColors[zone];
            if (_colorPicker.ShowDialog() != DialogResult.OK) return;

            Color color = _colorPicker.Color;
            _wmi.ZoneColors[zone] = color;
            _wmi.SetZoneColors(_wmi.ZoneColors, GetRgbBrightness());
            SaveRgbState(0);
            UpdateZoneColors();
        }

        private void SyncZones()
        {
            Color color = _wmi.ZoneColors[0];
            for (int i = 0; i < 4; i++) _wmi.ZoneColors[i] = color;
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
                if (key == null) return;

                int modeVersion = (int)key.GetValue("RGB_ModeVersion", 1);
                int rgbMode = RgbProfile.NormalizeSavedMode((int)key.GetValue("RGB_Mode", 0), modeVersion);
                _effectDropdown.SelectedIndex = Math.Clamp(rgbMode, 0, RgbProfile.ModeCount - 1);

                int r = (int)key.GetValue("RGB_Zone0_R", 0);
                int g = (int)key.GetValue("RGB_Zone0_G", 150);
                int b = (int)key.GetValue("RGB_Zone0_B", 255);
                _keyColor = Color.FromArgb(r, g, b);

                int speed = RgbProfile.NormalizeStepLevel((int)key.GetValue("RGB_Speed", 3));
                _speedSettingRow.Value = speed;

                int brightness = RgbProfile.NormalizeStepLevel((int)key.GetValue("RGB_Brightness", 5));
                _brightnessSettingRow.Value = brightness;

                int preset = (int)key.GetValue("RGB_Preset", 0);
                _presetDropdown.SelectedIndex = Math.Clamp(preset, 0, CustomPresetNames.Length - 1);

                int direction = (int)key.GetValue("RGB_Direction", 1);
                _directionDropdown.SelectedIndex = direction == 2 ? 1 : 0;

                bool ledTimeout = key.GetValue("LedTimeout") is int savedTimeout
                    ? savedTimeout == 1
                    : _wmi.GetLedTimeout();
                SetLedTimeout(ledTimeout);

                UpdateZoneColors();
                UpdateDirectionVisibility(rgbMode);
                bool isStatic = rgbMode == 0;
                _speedSettingRow.Parent!.Visible = !isStatic;
                _brightnessSettingRow.Parent!.Visible = true;
                _zoneRow.Visible = isStatic;
                _presetRow.Visible = isStatic;
            }
            catch { }
            finally
            {
                _loadingState = false;
            }
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
