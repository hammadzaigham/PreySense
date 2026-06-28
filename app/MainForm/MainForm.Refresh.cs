using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PreySense.Helpers;
using PreySense.Rgb;

namespace PreySense
{
    public partial class MainForm
    {
        public List<int> GetSupportedRefreshRates()
        {
            if (_supportedRefreshRates != null)
                return new List<int>(_supportedRefreshRates);

            var rates = new List<int>();
            DEVMODE dm = new(); dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            int modeNum = 0;
            while (EnumDisplaySettings(null, modeNum, ref dm))
            {
                if (dm.dmDisplayFrequency > 0 && !rates.Contains(dm.dmDisplayFrequency))
                {
                    rates.Add(dm.dmDisplayFrequency);
                }
                modeNum++;
            }
            _supportedRefreshRates = rates.OrderBy(r => r).ToList();
            return new List<int>(_supportedRefreshRates);
        }

        private int GetCurrentRefreshRate()
        {
            DEVMODE dm = new(); dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            return EnumDisplaySettings(null, -1, ref dm) ? dm.dmDisplayFrequency : 60;
        }

        private int GetMaxRefreshRate()
        {
            DEVMODE dm = new(); dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            int maxHz = 60, modeNum = 0;
            while (EnumDisplaySettings(null, modeNum, ref dm))
            {
                if (dm.dmDisplayFrequency > maxHz) maxHz = dm.dmDisplayFrequency;
                modeNum++;
            }
            return maxHz;
        }

        private void SetRefreshRate(int hz)
        {
            DEVMODE dm = new(); dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            if (EnumDisplaySettings(null, -1, ref dm))
            {
                dm.dmDisplayFrequency = hz;
                dm.dmFields = 0x400000;
                ChangeDisplaySettings(ref dm, 0x01);
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int ChangeDisplaySettings(ref DEVMODE devMode, int flags);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Ansi)]
        private struct DEVMODE
        {
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType;
            public int dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }

        private void InvalidateRefreshRateCache()
        {
            _supportedRefreshRates = null;
        }

        private void ApplyGammaForRefreshRate(int hz, bool force = false)
        {
            if (hz <= 0)
                return;

            if (!force && _lastAppliedGammaRefreshRate == hz)
                return;

            try
            {
                AppLogger.Log($"Applying color profile for refresh rate {hz}Hz");
                var profile = DisplayManager.LoadProfile(hz);
                DisplayManager.ApplyProfile(profile);
                _lastAppliedGammaRefreshRate = hz;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Error applying color profile for refresh rate: {ex.Message}");
            }
        }

        public void ConfigureRefreshRateButtons()
        {
            var uniqueRates = GetSupportedRefreshRates();
            AppLogger.Log($"Detected supported refresh rates: {string.Join(", ", uniqueRates)}");

            if (uniqueRates.Count == 0)
            {
                button60Hz.Visible = false;
                button120Hz.Visible = false;
                buttonMaxRefreshRate.Visible = false;
                return;
            }

            if (uniqueRates.Count == 1)
            {
                button60Hz.Visible = true;
                button60Hz.Text = $"{uniqueRates[0]}Hz";
                button60Hz.Tag = uniqueRates[0];
                button60Hz.BorderColor = Color.Green;
                button120Hz.Visible = false;
                buttonMaxRefreshRate.Visible = false;
            }
            else if (uniqueRates.Count == 2)
            {
                button60Hz.Visible = true;
                button60Hz.Text = $"{uniqueRates[0]}Hz";
                button60Hz.Tag = uniqueRates[0];
                button60Hz.BorderColor = Color.Green;

                button120Hz.Visible = true;
                button120Hz.Text = $"{uniqueRates[1]}Hz" + (uniqueRates[1] > 60 ? " + OD" : "");
                button120Hz.Tag = uniqueRates[1];
                button120Hz.BorderColor = Color.Red;

                buttonMaxRefreshRate.Visible = false;
            }
            else
            {
                int lowest = uniqueRates[0];
                int middle = uniqueRates[uniqueRates.Count / 2];
                int highest = uniqueRates[uniqueRates.Count - 1];

                button60Hz.Visible = true;
                button60Hz.Text = $"{lowest}Hz";
                button60Hz.Tag = lowest;
                button60Hz.BorderColor = Color.Green;

                button120Hz.Visible = true;
                button120Hz.Text = $"{middle}Hz";
                button120Hz.Tag = middle;
                button120Hz.BorderColor = Color.DodgerBlue;

                buttonMaxRefreshRate.Visible = true;
                buttonMaxRefreshRate.Text = $"{highest}Hz" + (highest > 60 ? " + OD" : "");
                buttonMaxRefreshRate.Tag = highest;
                buttonMaxRefreshRate.BorderColor = Color.Red;
            }
        }

        private static Color GetRefreshRateOutlineColor(int hz, int lowest, int middle, int highest)
        {
            if (hz == lowest) return Color.Green;
            if (hz == highest) return Color.Red;
            if (hz == middle) return Color.DodgerBlue;
            return AccentColor;
        }

        private void SyncRefreshRateButtons(int currentHz, bool autoEnabled)
        {
            _currentRefreshRate = currentHz;
            buttonAutoRefreshRate.Activated = autoEnabled;

            if (autoEnabled)
            {
                button60Hz.Activated = false;
                button120Hz.Activated = false;
                buttonMaxRefreshRate.Activated = false;
                return;
            }

            button60Hz.Activated = button60Hz.Visible && button60Hz.Tag is int r1 && r1 == currentHz;
            button120Hz.Activated = button120Hz.Visible && button120Hz.Tag is int r2 && r2 == currentHz;
            buttonMaxRefreshRate.Activated = buttonMaxRefreshRate.Visible && buttonMaxRefreshRate.Tag is int r3 && r3 == currentHz;
        }

        private void ApplyAutoRefreshRate(bool pluggedIn)
        {
            if (!buttonAutoRefreshRate.Activated) return;

            var uniqueRates = GetSupportedRefreshRates();
            if (uniqueRates == null || uniqueRates.Count < 2) return;

            int targetRate = pluggedIn ? uniqueRates[uniqueRates.Count - 1] : uniqueRates[0];
            int currentRate = _currentRefreshRate;

            if (currentRate != targetRate)
            {
                AppLogger.Log($"Auto refresh rate: switching to {targetRate}Hz (Plugged in: {pluggedIn})");
                ApplyDisplayMode(targetRate);
            }
            else if (!pluggedIn)
            {
                UpdateScreenCardTitle();
            }
        }

        private void ToggleAutoRefreshRate()
        {
            buttonAutoRefreshRate.Activated = !buttonAutoRefreshRate.Activated;
            SaveState("ScreenAuto", buttonAutoRefreshRate.Activated ? 1 : 0);
            if (buttonAutoRefreshRate.Activated)
            {
                SyncRefreshRateButtons(_currentRefreshRate, true);
                ApplyAutoRefreshRate(SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online);
            }
            else
            {
                int current = _currentRefreshRate;
                SaveState("RefreshRate", current);
                SaveState("LcdOverdrive", (current == _maxHz && _maxHz > 60) ? 1 : 0);
                SyncRefreshRateButtons(current, false);
                UpdateScreenCardTitle();
                ApplyGammaForRefreshRate(current);
            }
        }

        private void UpdateScreenCardTitle()
        {
            int currentHz = _currentRefreshRate;
            bool odActive = (currentHz == _maxHz) && (_maxHz > 60);
            labelScreen.Text = $"Laptop Screen: {currentHz}Hz" + (odActive ? " + Overdrive" : "");
            SaveState("RefreshRate", currentHz);
            SaveState("LcdOverdrive", odActive ? 1 : 0);
        }

        private void ApplyDisplayMode(int hz)
        {
            bool enableOverdrive = (hz == _maxHz) && (_maxHz > 60);
            int currentHz = GetCurrentRefreshRate();
            bool currentOverdrive = (currentHz == _maxHz) && (_maxHz > 60);

            if (currentHz != hz || currentOverdrive != enableOverdrive)
            {
                _wmi.SetLcdOverdrive(enableOverdrive);
                SaveState("LcdOverdrive", enableOverdrive ? 1 : 0);
            }

            if (currentHz != hz)
                SetRefreshRate(hz);
            SyncRefreshRateButtons(hz, buttonAutoRefreshRate.Activated);

            SaveState("RefreshRate", hz);
            UpdateScreenCardTitle();
        }

        private void RestoreDisplayModeOnLaunch(int hz)
        {
            int currentHz = GetCurrentRefreshRate();
            if (currentHz != hz)
            {
                SetRefreshRate(hz);
                currentHz = hz;
            }

            SyncRefreshRateButtons(currentHz, false);
            SaveState("RefreshRate", currentHz);
            UpdateScreenCardTitle();
            ApplyGammaForRefreshRate(currentHz);
        }

    }
}
