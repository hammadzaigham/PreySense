using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;
using PreySense.Dialogs;
using PreySense.Fan;
using PreySense.Gpu;
using PreySense.Helpers;
using PreySense.Mode;
using PreySense.Rgb;
using PreySense.Input;

namespace PreySense
{
    public partial class MainForm
    {
        private void LoadMemory()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\PreySense");
                if (key != null)
                {
                    LoadPersistedGpuState(key);
                    LoadPersistedRefreshState(key);
                    LoadPersistedRgbState(key);
                    LoadPersistedFanState(key);
                }
            }
            catch { }
        }

        private void LoadPersistedGpuState(RegistryKey key)
        {
            int gpuMode = GetRegistryInt(key, "GPUMode", 1);
            int gpuBattery = GetRegistryInt(key, "GpuBatteryAuto", 0);
            if (gpuMode == 3)
            {
                gpuMode = 1;
                gpuBattery = 1;
                SaveState("GPUMode", 1);
                SaveState("GpuBatteryAuto", 1);
            }

            _gpuBatteryAuto = (gpuBattery == 1);
            MarkGpuMode(gpuMode);
            HighlightFanMode(GetRegistryInt(key, "Fan", 0x01));
        }

        private void LoadPersistedRefreshState(RegistryKey key)
        {
            int screenAuto = GetRegistryInt(key, "ScreenAuto", 0);
            buttonAutoRefreshRate.Activated = (screenAuto == 1);

            int refresh = GetRegistryInt(key, "RefreshRate", 0);
            int currentHz = GetCurrentRefreshRate();
            _currentRefreshRate = currentHz;
            if (buttonAutoRefreshRate.Activated)
            {
                SyncRefreshRateButtons(currentHz, true);
            }
            else if (refresh > 0)
            {
                RestoreDisplayModeOnLaunch(refresh);
            }
            else
            {
                SyncRefreshRateButtons(currentHz, false);
            }
        }

        private void LoadPersistedRgbState(RegistryKey key)
        {
            _isApplyingSavedRgbState = true;
            try
            {
                int modeVersion = GetRegistryInt(key, "RGB_ModeVersion", 1);
                int rgbMode = RgbProfile.NormalizeSavedMode(GetRegistryInt(key, "RGB_Mode", 0), modeVersion);
                comboRgbLightingMode.SelectedIndex = MapModeToDropdownIndex(rgbMode);
                buttonRgbProfiles.Enabled = (rgbMode == 0);

                int rgbSpeed = RgbProfile.NormalizeStepLevel(GetRegistryInt(key, "RGB_Speed", 3));
                int rgbDirection = GetRegistryInt(key, "RGB_Direction", 1);
                byte direction = rgbDirection == 2 ? (byte)2 : (byte)1;

                int r = GetRegistryInt(key, "RGB_Zone0_R", 0);
                int g = GetRegistryInt(key, "RGB_Zone0_G", 150);
                int b = GetRegistryInt(key, "RGB_Zone0_B", 255);

                Color[] colors = new Color[4];
                for (int i = 0; i < 4; i++)
                {
                    int zR = GetRegistryInt(key, $"RGB_Zone{i}_R", r);
                    int zG = GetRegistryInt(key, $"RGB_Zone{i}_G", g);
                    int zB = GetRegistryInt(key, $"RGB_Zone{i}_B", b);
                    colors[i] = Color.FromArgb(zR, zG, zB);
                }

                pictureBacklightSwatch.BackColor = colors[0];

                byte brightness = GetBrightnessSliderValue();
                _wmi.SetCachedRgbState(rgbMode, (byte)r, (byte)g, (byte)b, brightness, (byte)rgbSpeed, direction, colors);
            }
            finally
            {
                _isApplyingSavedRgbState = false;
            }
        }

        private void LoadPersistedFanState(RegistryKey key)
        {
            labelBatteryStatusLimitTitle.Text = $"Battery Charge Limit: {sliderBatteryChargeLimit.Value}%";

            int calibrated = GetRegistryInt(key, "Fan_Calibrated", 0);
            if (calibrated == 1)
            {
                PreySense.Fan.FanCurveControl.MaxCpuRpmSeen = GetRegistryInt(key, "Fan_MaxCpuRpm", 6400);
                PreySense.Fan.FanCurveControl.MaxGpuRpmSeen = GetRegistryInt(key, "Fan_MaxGpuRpm", 6400);
            }

            byte activePowerMode = GetActivePowerMode();
            var profile = ProfileManager.LoadProfile(activePowerMode);
            _cpuCurve = FanCurveStorage.LoadCpuCurve(activePowerMode);
            _gpuCurve = FanCurveStorage.LoadGpuCurve(activePowerMode);

            int legacyFanEnabled = GetRegistryInt(key, "Fan_CurveEnabled", 0);
            if (legacyFanEnabled == 1 && !profile.ApplyFanCurve)
            {
                profile.ApplyFanCurve = true;
                ProfileManager.SaveProfile(profile);
            }

            _applyCustomFans = profile.ApplyFanCurve;
            _fanRampUp = _applyCustomFans ? profile.FanRampUp : 0;
            _maxFanEnabled = GetRegistryInt(key, "Fan_MaxSpeed", 0) == 1;
            buttonTurboFanModePower.Activated = _applyCustomFans;
        }

        private static int GetRegistryInt(RegistryKey? key, string name, int defaultValue)
        {
            return key != null ? (int)key.GetValue(name, defaultValue)! : defaultValue;
        }

        public void ApplyPowerMode(byte mode)
        {
            ApplyPowerMode(mode, persistHardware: true);
        }

        private void ApplyPowerMode(byte mode, bool persistHardware)
        {
            _lastModeChangeTime = DateTime.Now;
            byte currentMode = 0x01;
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\PreySense");
                if (k != null) currentMode = (byte)(int)k.GetValue("Power", 0x01);
            }
            catch { }

            if (mode == 0x05 && currentMode != 0x05)
            {
                _prevPowerMode = currentMode;
                SaveState("PrevPower", _prevPowerMode);
            }

            if (persistHardware)
                _wmi.SetPowerMode(mode);
            _lastKnownProfile = mode;

            HighlightPowerBtn(mode);
            SaveState("Power", mode);

            if (IsOnBatteryPower())
            {
                SaveState("PowerBattery", mode);
            }
            else
            {
                SaveState("PowerAC", mode);
            }

            string modeName = mode switch
            {
                0x00 => "Silent",
                0x04 => "Performance",
                0x05 => "Turbo",
                0x06 => "Eco",
                _ => "Balanced"
            };

            UpdatePerformanceModeLabel(modeName);
            fansForm?.SyncActiveMode(mode);

            _ = Task.Run(() =>
            {
                try
                {
                    PreySense.Mode.ProfileManager.ApplyProfile(modeName, _wmi);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to auto-apply profile settings for {modeName}: {ex.Message}");
                }
            });

            ApplyFanCurvesForMode(mode);
        }

        private void SyncPowerModeFromHardware(byte mode)
        {
            ApplyPowerMode(mode, persistHardware: false);
        }

        private void MarkPowerMode(byte mode)
        {
            _lastKnownProfile = mode;
            HighlightPowerBtn(mode);
            string modeName = mode switch
            {
                0x00 => "Silent",
                0x04 => "Performance",
                0x05 => "Turbo",
                0x06 => "Eco",
                _ => "Balanced"
            };
            UpdatePerformanceModeLabel(modeName);
            fansForm?.SyncActiveMode(mode);
        }

        private void HighlightPowerBtn(byte mode)
        {
            buttonEcoMode.Activated = (mode == 0x00 || mode == 0x06);
            buttonBalancedMode.Activated = (mode == 0x01);
            buttonPerformanceMode.Activated = (mode == 0x04);
            buttonTurboFanMode.Activated = (mode == 0x05);
        }

        private void UpdatePerformanceModeLabel(string modeName, bool batteryAutoEco = false)
        {
            labelPerformanceMode.Text = batteryAutoEco
                ? $"Performance Mode: {modeName} (Battery)"
                : $"Performance Mode: {modeName}";
        }

        private void ToggleTurboMode()
        {
            if (SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline) return;
            byte currentMode = _wmi.GetPowerProfile();
            if (currentMode == 0x05)
            {
                byte prev = 0x01;
                try
                {
                    using var k = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\PreySense");
                    if (k != null) prev = (byte)(int)k.GetValue("PrevPower", 0x01);
                }
                catch { }
                ApplyPowerMode(prev);
            }
            else
            {
                ApplyPowerMode(0x05);
            }
        }

        private void CheckPowerSourceTransition()
        {
            CheckPowerSourceTransition(null);
        }

        private void CheckPowerSourceTransition(bool? onBatteryOverride)
        {
            bool currentPluggedIn = onBatteryOverride.HasValue ? !onBatteryOverride.Value : !IsOnBatteryPower();
            if (_isPluggedIn != currentPluggedIn)
            {
                _isPluggedIn = currentPluggedIn;
                HandlePowerSourceChanged(currentPluggedIn);
            }
        }

        private void HandlePowerSourceChanged(bool pluggedIn)
        {
            AppLogger.Log($"Power source changed. Plugged in: {pluggedIn}");

            UpdatePowerUIForBatteryState(!pluggedIn);
            ApplyAutoRefreshRate(pluggedIn);

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\PreySense");
                byte powerMode;
                if (pluggedIn)
                {
                    powerMode = (byte)(int)(key?.GetValue("PowerAC", _lastKnownProfile == 0xFF ? 0x01 : _lastKnownProfile) ?? (_lastKnownProfile == 0xFF ? 0x01 : _lastKnownProfile));
                    ApplyPowerMode(powerMode);
                }
                else
                {
                    powerMode = 0x06;
                    SaveState("PowerBattery", powerMode);
                    ApplyPowerMode(powerMode);
                    UpdatePerformanceModeLabel("Eco", batteryAutoEco: true);
                    return;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Error applying power mode on power source change: {ex.Message}");
            }

            if (_gpuBatteryAuto && _gpuMode != 2)
            {
                int targetMode = pluggedIn ? 1 : 0;
                if (_gpuMode != targetMode)
                {
                    ApplyGpuMode(targetMode);
                }
            }
        }

        private void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.StatusChange)
            {
                _wmi.InvalidateAcStatusCache();
                if (this.InvokeRequired)
                {
                    this.BeginInvoke(new Action(CheckPowerSourceTransition));
                }
                else
                {
                    CheckPowerSourceTransition();
                }
            }
        }

        private void MarkGpuMode(int mode)
        {
            _gpuMode = mode;
            buttonEnduranceMode.Activated = (mode == 0);
            buttonGpuStandardMode.Activated = (mode == 1);
            buttonGpuUltimateMode.Activated = (mode == 2);

            string labelText = mode switch
            {
                0 => "GPU Mode: iGPU only",
                1 => "GPU Mode: iGPU + dGPU",
                2 => "GPU Mode: dGPU exclusive",
                _ => "GPU Mode"
            };
            labelGpuMode.Text = labelText;
            labelGpuHint.Text = "";
        }

        private void ApplyHardwareGpuTransition(int mode)
        {
            if (mode == 0)
            {
                var control = new NvidiaGpuControl();
                if (control.IsValid)
                {
                    control.KillGPUApps();
                }
                NvidiaWrapper.SetGpuMode(0);
                NvidiaGpuControl.StopNVService();
                DeviceHelper.SetDeviceState("VEN_10DE", false);
                _wmi.SetGpuMuxMode(WmiController.GpuMuxHybrid);
            }
            else if (mode == 1)
            {
                DeviceHelper.SetDeviceState("VEN_10DE", true);
                NvidiaGpuControl.RestartNVService();
                _wmi.SetGpuMuxMode(WmiController.GpuMuxHybrid);
                NvidiaWrapper.SetGpuMode(1);
            }
        }

        public void ApplyGpuMode(int mode)
        {
            int oldMode = _gpuMode;
            if (oldMode == mode) return;

            if (mode == 2)
            {
                var result = ConfirmDialog.Show(this,
                    "Switching to Ultimate Mode requires a restart.\nBefore restarting, save any open files and close all programs.",
                    "Restart Required");
                if (result != DialogResult.Yes)
                {
                    MarkGpuMode(oldMode);
                    return;
                }

                _gpuMode = mode;
                SaveState("GPUMode", mode);
                MarkGpuMode(mode);

                if (!_wmi.SetGpuMuxMode(WmiController.MapUiGpuModeToMuxMode(mode)))
                {
                    AppLogger.Log("ApplyGpuMode: AcerService GPU_MODE (discrete) failed; canceling Ultimate switch.");
                    _gpuMode = oldMode;
                    SaveState("GPUMode", oldMode);
                    MarkGpuMode(oldMode);
                    return;
                }

                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "shutdown",
                        Arguments = "/r /t 0",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"Failed to initiate system reboot: {ex.Message}");
                }
                return;
            }

            if (oldMode == 2)
            {
                var result = ConfirmDialog.Show(this,
                    "Switching out of Ultimate Mode requires a restart.\nBefore restarting, save any open files and close all programs.",
                    "Restart Required");
                if (result != DialogResult.Yes)
                {
                    MarkGpuMode(2);
                    return;
                }

                _gpuMode = mode;
                SaveState("GPUMode", mode);
                MarkGpuMode(mode);

                if (!_wmi.SetGpuMuxMode(WmiController.MapUiGpuModeToMuxMode(mode)))
                {
                    AppLogger.Log("ApplyGpuMode: AcerService GPU_MODE (hybrid) failed; staying in Ultimate.");
                    MarkGpuMode(2);
                    return;
                }

                ApplyHardwareGpuTransition(mode);

                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "shutdown",
                        Arguments = "/r /t 0",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"Failed to initiate system reboot: {ex.Message}");
                }
                return;
            }

            _gpuMode = mode;
            _lastModeChangeTime = DateTime.Now;
            MarkGpuMode(mode);
            SaveState("GPUMode", mode);

            Task.Run(() =>
            {
                try
                {
                    ApplyHardwareGpuTransition(mode);
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"ApplyGpuMode background execution failed: {ex.Message}");
                }
            });
        }

    }
}
