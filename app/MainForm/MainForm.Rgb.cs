using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;
using PreySense.Helpers;
using PreySense.Rgb;

namespace PreySense
{
    public partial class MainForm
    {

        private void ApplyRgbModeFromDropdown(int idx)
        {
            if (!_isLoaded || _isApplyingSavedRgbState) return;
            int mode = MapDropdownIndexToMode(idx);
            _wmi.SetRgbMode(mode, _wmi.LastR, _wmi.LastG, _wmi.LastB, GetBrightnessSliderValue(), _wmi.Speed, _wmi.Direction);
            SaveState("RGB_Mode", mode);
            SaveState("RGB_ModeVersion", 2);
            buttonRgbProfiles.Enabled = (mode == 0);
        }

        private void PickBacklightColor()
        {
            _colorPicker.Color = pictureBacklightSwatch.BackColor;
            if (_colorPicker.ShowDialog() == DialogResult.OK)
            {
                Color c = _colorPicker.Color;
                pictureBacklightSwatch.BackColor = c;
                Color[] colors = new Color[4] { c, c, c, c };
                _wmi.SetZoneColors(colors, GetBrightnessSliderValue());
                SaveState("RGB_Zone0_R", c.R);
                SaveState("RGB_Zone0_G", c.G);
                SaveState("RGB_Zone0_B", c.B);
            }
        }

        public byte GetBrightnessSliderValue()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\PreySense");
                int level = (int)(key?.GetValue("RGB_Brightness", 5) ?? 5);
                return (byte)RgbProfile.NormalizeStepLevel(level);
            }
            catch { return 5; }
        }

        private void OpenRgbProfilesForm()
        {
            using var f = new RgbForm(this, _wmi);
            f.ShowDialog(this);
            ApplyGammaForRefreshRate(_currentRefreshRate, force: true);
            this.ActiveControl = null;
        }

        private void OpenRgbForm()
        {
            using var f = new PreySense.Rgb.RgbForm(this, _wmi);
            f.ShowDialog(this);
        }
    }
}
