using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using PreySense.Gpu;
using PreySense.Dialogs;
using PreySense.Mode;
using PreySense.UI;

namespace PreySense.Fan
{
    public partial class Fans
    {
        private void WireEvents()
        {
            buttonCPU.Click += (_, _) => ToggleNav(0);
            buttonGPU.Click += (_, _) => ToggleNav(1);

            comboPerfMode.SelectedIndexChanged += (_, _) =>
            {
                if (_isUpdatingUi) return;
                int idx = Math.Clamp(comboPerfMode.SelectedIndex, 0, PerfModes.Length - 1);
                byte mode = PerfModes[idx];
                if (mode == _editingMode) return;
                SaveEditingModeCurves();
                _activeMode = mode;
                LoadProfileForMode(mode);
                _mainForm.ApplyPowerMode(mode);
            };

            comboWindowsPowerMode.SelectedIndexChanged += (_, _) =>
            {
                if (_currentProfile != null && !_isUpdatingUi)
                {
                    _currentProfile.WindowsPowerMode = comboWindowsPowerMode.SelectedIndex;
                    ProfileManager.SaveProfile(_currentProfile);
                    _wmi.ApplyOverlayScheme(comboWindowsPowerMode.SelectedIndex);
                }
            };

            trackPl1.Scroll += (_, _) =>
            {
                if (_isUpdatingUi) return;
                EnforcePowerLimitOrder(pl1IsDriver: true);
            };
            trackPl2.Scroll += (_, _) =>
            {
                if (_isUpdatingUi) return;
                EnforcePowerLimitOrder(pl1IsDriver: false);
            };

            trackGpuCoreOffset.Scroll += (_, _) =>
            {
                if (_isUpdatingUi) return;
                _isUpdatingUi = true;
                numGpuCoreOffset.Value = trackGpuCoreOffset.Value;
                labelGpuCoreValue.Text = FormatGpuOffset(trackGpuCoreOffset.Value);
                _isUpdatingUi = false;
            };
            trackGpuMemoryOffset.Scroll += (_, _) =>
            {
                if (_isUpdatingUi) return;
                _isUpdatingUi = true;
                numGpuMemoryOffset.Value = trackGpuMemoryOffset.Value;
                labelGpuMemoryValue.Text = FormatGpuOffset(trackGpuMemoryOffset.Value);
                _isUpdatingUi = false;
            };

            checkApplyFanCurves.CheckedChanged += (_, _) =>
            {
                if (_isUpdatingUi) return;
                if (_currentProfile != null)
                {
                    _currentProfile.ApplyFanCurve = checkApplyFanCurves.Checked;
                    ProfileManager.SaveProfile(_currentProfile);
                }
                if (_editingMode == _activeMode)
                    _mainForm.ApplyFanCurvesForMode(_activeMode);
            };

            numFanRampUp.ValueChanged += (_, _) =>
            {
                if (_isUpdatingUi) return;
                if (_currentProfile != null)
                {
                    _currentProfile.FanRampUp = (int)numFanRampUp.Value;
                    ProfileManager.SaveProfile(_currentProfile);
                }
                if (_editingMode == _activeMode)
                    _mainForm.ApplyFanCurvesForMode(_activeMode);
            };
        }

        private void ShowCurveForMode(bool gpuMode)
        {
            if (_curveFrame == null || _cpuCurveCard == null || _gpuCurveCard == null) return;
            _curveFrame.SuspendLayout();
            _curveFrame.Controls.Clear();
            var card = gpuMode ? _gpuCurveCard : _cpuCurveCard;
            card.Dock = DockStyle.Fill;
            _curveFrame.Controls.Add(card);
            _curveFrame.ResumeLayout();
        }

        private void ToggleNav(int tabIndex)
        {
            buttonCPU.Activated = tabIndex == 0;
            buttonGPU.Activated = tabIndex == 1;
            panelCpuLimitsSection.Visible = tabIndex == 0;
            panelGpuOffsetsSection.Visible = tabIndex == 1;
            ShowCurveForMode(tabIndex == 1);

            if (_buttonApplySettings != null)
            {
                _buttonApplySettings.Text = tabIndex == 0 ? "Apply Limits" : "Apply Overclock";
                _buttonApplySettings.Enabled = tabIndex == 0 || _mainForm.GpuMode != 0;
            }
        }

        private void CommitGpuSettings()
        {
            checkApplyGpuLimits.Checked = true;
            if (_editingMode == _activeMode && _mainForm.GpuMode != 0)
            {
                int coreOffset = trackGpuCoreOffset.Value;
                int memoryOffset = trackGpuMemoryOffset.Value;
                Task.Run(() => NvidiaWrapper.ApplyGpuSettings(coreOffset, memoryOffset));
            }
            else if (_editingMode == _activeMode && _mainForm.GpuMode == 0)
            {
                AppLogger.Log("Fans: skipped GPU overclock apply because GPU mode is iGPU only.");
            }
        }

        private void ApplyCpuPowerLimits()
        {
            checkApplyCpuLimits.Checked = true;
            if (_editingMode == _activeMode)
            {
                int pl1 = trackPl1.Value;
                int pl2 = trackPl2.Value;
                Task.Run(() => PowerLimitController.SetCpuPowerLimits(pl1, pl2, pl2Enable: true));
            }
        }

        private void SaveVisibleSettings()
        {
            SaveEditingModeCurves();
            SaveCpuPowerSettings();
            SaveGpuSettings();
            if (_currentProfile != null)
            {
                ProfileManager.SaveProfile(_currentProfile);
            }
            AppLogger.Log($"Fans: saved settings for mode 0x{_editingMode:X2}.");
        }

        private void EnforcePowerLimitOrder(bool pl1IsDriver)
        {
            if (_enforcingPlOrder) return;
            _enforcingPlOrder = true;
            try
            {
                if (trackPl2.Value < trackPl1.Value)
                {
                    if (pl1IsDriver)
                    {
                        int target = Math.Min(trackPl1.Value, trackPl2.Maximum);
                        if (trackPl2.Value != target) trackPl2.Value = target;
                        if (trackPl1.Value > trackPl2.Value)
                            trackPl1.Value = Math.Max(trackPl2.Value, trackPl1.Minimum);
                    }
                    else
                    {
                        trackPl2.Value = Math.Min(trackPl1.Value, trackPl2.Maximum);
                    }
                }

                bool prev = _isUpdatingUi;
                _isUpdatingUi = true;
                if (numPl1 != null) numPl1.Value = Math.Clamp(trackPl1.Value, numPl1.Minimum, numPl1.Maximum);
                if (numPl2 != null) numPl2.Value = Math.Clamp(trackPl2.Value, numPl2.Minimum, numPl2.Maximum);
                _isUpdatingUi = prev;
            }
            finally
            {
                _enforcingPlOrder = false;
            }
        }

        private void LoadProfileForMode(byte mode)
        {
            _editingMode = mode;
            _currentProfile = ProfileManager.LoadProfile(mode);
            var factoryDefaults = ProfileManager.LoadFactoryDefaultProfile(mode);
            ApplyCpuLimitBounds(factoryDefaults);
            _isUpdatingUi = true;
            if (comboPerfMode != null && comboPerfMode.Items.Count > 0)
                comboPerfMode.SelectedIndex = IndexFromPerfMode(mode);
            trackPl1.Value = Math.Clamp(_currentProfile.CpuPl1, trackPl1.Minimum, trackPl1.Maximum);
            trackPl2.Value = Math.Clamp(_currentProfile.CpuPl2, trackPl2.Minimum, trackPl2.Maximum);
            numPl1.Value = trackPl1.Value;
            numPl2.Value = trackPl2.Value;
            comboWindowsPowerMode.SelectedIndex = Math.Clamp(_currentProfile.WindowsPowerMode, 0, 2);
            checkApplyCpuLimits.Checked = _currentProfile.ApplyCpuLimits;
            trackGpuCoreOffset.Value = Math.Clamp(_currentProfile.GpuCoreOffset, trackGpuCoreOffset.Minimum, trackGpuCoreOffset.Maximum);
            trackGpuMemoryOffset.Value = Math.Clamp(_currentProfile.GpuMemoryOffset, trackGpuMemoryOffset.Minimum, trackGpuMemoryOffset.Maximum);
            numGpuCoreOffset.Value = trackGpuCoreOffset.Value;
            numGpuMemoryOffset.Value = trackGpuMemoryOffset.Value;
            labelGpuCoreValue.Text = FormatGpuOffset(trackGpuCoreOffset.Value);
            labelGpuMemoryValue.Text = FormatGpuOffset(trackGpuMemoryOffset.Value);
            checkApplyGpuLimits.Checked = _currentProfile.ApplyGpuLimits;
            _cpuCurve = FanCurveStorage.LoadCpuCurve(mode);
            _gpuCurve = FanCurveStorage.LoadGpuCurve(mode);
            _curveCpu.Points = _cpuCurve;
            _curveGpu.Points = _gpuCurve;
            checkApplyFanCurves.Checked = _currentProfile.ApplyFanCurve;
            numFanRampUp.Value = Math.Clamp(_currentProfile.FanRampUp, numFanRampUp.Minimum, numFanRampUp.Maximum);
            EnforcePowerLimitOrder(pl1IsDriver: false);
            _isUpdatingUi = false;
            UpdateGpuOverclockAvailability();
        }

        private void UpdateGpuOverclockAvailability()
        {
            const bool allowed = true;
            trackGpuCoreOffset.Enabled = allowed;
            trackGpuMemoryOffset.Enabled = allowed;
            numGpuCoreOffset.Enabled = allowed;
            numGpuMemoryOffset.Enabled = allowed;
            labelGpuOffsets.Enabled = true;
            labelGpuCoreTitle.Enabled = true;
            labelGpuMemoryTitle.Enabled = true;
            labelGpuCoreValue.Enabled = true;
            labelGpuMemoryValue.Enabled = true;
            labelGpuOffsets.ForeColor = foreMain;
            labelGpuCoreTitle.ForeColor = foreMain;
            labelGpuMemoryTitle.ForeColor = foreMain;
            labelGpuCoreValue.ForeColor = foreMain;
            labelGpuMemoryValue.ForeColor = foreMain;
        }

        private void ResetDefaults()
        {
            if (ConfirmDialog.Show(this, "Reset fan curves, power limits, and Windows power mode to factory defaults?", "Factory Defaults", "Apply") != DialogResult.Yes)
                return;

            byte mode = _currentProfile?.PowerMode ?? _wmi.GetPowerProfile();
            ProfileManager.ClearUserProfileValues(mode);
            _currentProfile = ProfileManager.LoadProfile(mode);
            ApplyCpuLimitBounds(_currentProfile);
            var def = FanCurveStorage.DefaultCurve();
            _cpuCurve = def;
            _gpuCurve = (PointF[])def.Clone();
            _curveCpu.Points = _cpuCurve;
            _curveGpu.Points = _gpuCurve;
            _isUpdatingUi = true;
            trackPl1.Value = Math.Clamp(_currentProfile.CpuPl1, trackPl1.Minimum, trackPl1.Maximum);
            trackPl2.Value = Math.Clamp(_currentProfile.CpuPl2, trackPl2.Minimum, trackPl2.Maximum);
            numPl1.Value = trackPl1.Value;
            numPl2.Value = trackPl2.Value;
            comboWindowsPowerMode.SelectedIndex = Math.Clamp(_currentProfile.WindowsPowerMode, 0, 2);
            checkApplyCpuLimits.Checked = _currentProfile.ApplyCpuLimits;
            checkApplyFanCurves.Checked = _currentProfile.ApplyFanCurve;
            numFanRampUp.Value = Math.Clamp(_currentProfile.FanRampUp, numFanRampUp.Minimum, numFanRampUp.Maximum);
            trackGpuCoreOffset.Value = Math.Clamp(_currentProfile.GpuCoreOffset, trackGpuCoreOffset.Minimum, trackGpuCoreOffset.Maximum);
            trackGpuMemoryOffset.Value = Math.Clamp(_currentProfile.GpuMemoryOffset, trackGpuMemoryOffset.Minimum, trackGpuMemoryOffset.Maximum);
            numGpuCoreOffset.Value = trackGpuCoreOffset.Value;
            numGpuMemoryOffset.Value = trackGpuMemoryOffset.Value;
            labelGpuCoreValue.Text = FormatGpuOffset(trackGpuCoreOffset.Value);
            labelGpuMemoryValue.Text = FormatGpuOffset(trackGpuMemoryOffset.Value);
            checkApplyGpuLimits.Checked = _currentProfile.ApplyGpuLimits;
            _isUpdatingUi = false;
            UpdateGpuOverclockAvailability();
            if (_editingMode == _activeMode)
            {
                _mainForm.SetActiveFanCurves(_cpuCurve, _gpuCurve);
                _mainForm.ApplyFanCurvesForMode(_activeMode);
                ApplyCpuPowerLimits();
                CommitGpuSettings();
            }
        }

        private void SaveEditingModeCurves()
        {
            if (_curveCpu?.Points != null)
            {
                _cpuCurve = _curveCpu.Points;
                FanCurveStorage.SaveCpuCurve(_editingMode, _cpuCurve);
            }
            if (_curveGpu?.Points != null)
            {
                _gpuCurve = _curveGpu.Points;
                FanCurveStorage.SaveGpuCurve(_editingMode, _gpuCurve);
            }
            if (_editingMode == _activeMode)
                _mainForm.SetActiveFanCurves(_cpuCurve, _gpuCurve);
        }

        private void OnFanCurveEdited()
        {
            if (_isUpdatingUi) return;
            SaveEditingModeCurves();
        }

        private void SaveCpuPowerSettings()
        {
            ClampCpuLimitsToFactoryMax();
            if (_currentProfile != null)
            {
                _currentProfile.CpuPl1 = trackPl1.Value;
                _currentProfile.CpuPl2 = trackPl2.Value;
                _currentProfile.WindowsPowerMode = comboWindowsPowerMode.SelectedIndex;
                _currentProfile.ApplyCpuLimits = checkApplyCpuLimits.Checked;
                ProfileManager.SaveProfile(_currentProfile);
            }
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\PreySense");
                key.SetValue("PWR_SPL", trackPl1.Value);
                key.SetValue("PWR_SPPT", trackPl2.Value);
                key.SetValue("WindowsPowerMode", comboWindowsPowerMode.SelectedIndex);
                key.SetValue("Power_Apply", checkApplyCpuLimits.Checked ? 1 : 0);
            }
            catch { }
        }

        private void SaveGpuSettings()
        {
            if (_currentProfile != null)
            {
                _currentProfile.GpuCoreOffset = trackGpuCoreOffset.Value;
                _currentProfile.GpuMemoryOffset = trackGpuMemoryOffset.Value;
                _currentProfile.ApplyGpuLimits = checkApplyGpuLimits.Checked;
                ProfileManager.SaveProfile(_currentProfile);
            }
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\PreySense");
                key.SetValue("GPU_CoreOffset", trackGpuCoreOffset.Value);
                key.SetValue("GPU_MemoryOffset", trackGpuMemoryOffset.Value);
                key.SetValue("GPU_Apply", checkApplyGpuLimits.Checked ? 1 : 0);
            }
            catch { }
        }

        private void LoadStates()
        {
            try
            {
                byte curMode = _mainForm.CurrentPowerMode;
                _activeMode = curMode;
                _isUpdatingUi = true;
                comboPerfMode.Items.Clear();
                comboPerfMode.Items.AddRange(PerfModeNames);
                comboPerfMode.SelectedIndex = IndexFromPerfMode(curMode);
                _isUpdatingUi = false;
                LoadProfileForMode(curMode);
            }
            catch
            {
                _pl1MaxW = Math.Max(5, trackPl1.Maximum);
                _pl2MaxW = Math.Max(5, trackPl2.Maximum);
                trackPl1.Minimum = 5;
                trackPl2.Minimum = 5;
                trackPl1.Maximum = _pl1MaxW;
                trackPl2.Maximum = _pl2MaxW;
                trackPl1.Value = Math.Clamp(45, trackPl1.Minimum, trackPl1.Maximum);
                trackPl2.Value = Math.Clamp(65, trackPl2.Minimum, trackPl2.Maximum);
            }
        }

        public void SyncActiveMode(byte mode)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => SyncActiveMode(mode)));
                return;
            }

            if (Array.IndexOf(PerfModes, mode) < 0)
                mode = 0x01;

            if (_activeMode == mode && _editingMode == mode)
                return;

            _activeMode = mode;
            LoadProfileForMode(mode);
        }

        private void ApplyCpuLimitBounds(PerformanceProfile factoryDefaults)
        {
            _pl1MaxW = 200;
            _pl2MaxW = 200;
            trackPl1.Minimum = 5;
            trackPl2.Minimum = 5;
            trackPl1.Maximum = _pl1MaxW;
            trackPl2.Maximum = _pl2MaxW;
            if (numPl1 != null) { numPl1.Minimum = 5; numPl1.Maximum = _pl1MaxW; }
            if (numPl2 != null) { numPl2.Minimum = 5; numPl2.Maximum = _pl2MaxW; }
            trackPl1.Value = Math.Clamp(trackPl1.Value, trackPl1.Minimum, trackPl1.Maximum);
            trackPl2.Value = Math.Clamp(trackPl2.Value, trackPl2.Minimum, trackPl2.Maximum);
            if (numPl1 != null) numPl1.Value = trackPl1.Value;
            if (numPl2 != null) numPl2.Value = trackPl2.Value;
            StyleTrackTicks(trackPl1);
            StyleTrackTicks(trackPl2);
        }

        private void ClampCpuLimitsToFactoryMax()
        {
            trackPl1.Value = Math.Clamp(trackPl1.Value, trackPl1.Minimum, trackPl1.Maximum);
            trackPl2.Value = Math.Clamp(trackPl2.Value, trackPl2.Minimum, trackPl2.Maximum);
            if (numPl1 != null) numPl1.Value = Math.Clamp(numPl1.Value, numPl1.Minimum, numPl1.Maximum);
            if (numPl2 != null) numPl2.Value = Math.Clamp(numPl2.Value, numPl2.Minimum, numPl2.Maximum);
        }

        private static string FormatGpuOffset(int value) => value switch
        {
            > 0 => $"+{value:N0} MHz",
            < 0 => $"{value:N0} MHz",
            _ => "0 MHz"
        };

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            _telemetryTimer?.Stop();
            _telemetryTimer?.Dispose();
        }
    }
}
