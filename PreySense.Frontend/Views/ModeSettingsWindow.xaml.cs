using System;
using System.ComponentModel;
using System.Windows;
using PreySense.Core.Acpi;
using PreySense.Frontend.Controls;
using PreySense.Frontend.ViewModels;
using PreySense.Helpers;

namespace PreySense.Frontend.Views
{
    public partial class ModeSettingsWindow : Window
    {
        private int _selectedModeIndex = 2; // 0=Eco, 1=Quiet, 2=Balanced, 3=Perf, 4=Turbo
        private bool _isUpdatingUi;

        public ModeSettingsWindow()
        {
            InitializeComponent();
            MaxHeight = SystemParameters.WorkArea.Height - 40;
            TuningScroll.MaxHeight = Math.Max(1, MaxHeight - 220);
            Loaded += ModeSettingsWindow_Loaded;
            DependencyPropertyDescriptor
                .FromProperty(SettingSwitchRow.IsCheckedProperty, typeof(SettingSwitchRow))
                .AddValueChanged(SwitchCpuBoost, CpuBoostSwitch_Changed);
        }

        private void ModeSettingsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            SyncFromViewModel();
        }

        internal void SyncFromViewModel()
        {
            if (DataContext is MainViewModel vm)
            {
                _selectedModeIndex = vm.ActivePowerMode switch
                {
                    0x06 => 0,
                    0x00 => 1,
                    0x04 => 3,
                    0x05 => 4,
                    _ => 2
                };
            }

            LoadModeData();
        }

        private async void ComboModeSelect_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (ComboModeSelect == null || _isUpdatingUi || !IsLoaded || ComboModeSelect.SelectedIndex < 0) return;
            _selectedModeIndex = ComboModeSelect.SelectedIndex;
            LoadModeData();
            if (DataContext is MainViewModel vm)
                await vm.SetModeAsync(ModeIndexToByte(_selectedModeIndex));
        }

        private static string FormatTau(int seconds) => CpuPowerLimitConfiguration.FormatTauSelection(seconds);

        private int ReadTauSeconds() =>
            CpuPowerLimitConfiguration.TauSecondsForIndex((int)Math.Round(SliderTau?.Value ?? 0));

        private string GetSelectedModeName() => _selectedModeIndex switch
        {
            0 => "Eco",
            1 => "Quiet",
            2 => "Balanced",
            3 => "Performance",
            4 => "Turbo",
            _ => "Custom"
        };

        private void LoadModeData()
        {
            if (DataContext is not MainViewModel vm) return;
            _isUpdatingUi = true;

            if (ComboModeSelect != null && ComboModeSelect.SelectedIndex != _selectedModeIndex)
            {
                ComboModeSelect.SelectedIndex = _selectedModeIndex;
            }

            int pl1 = _selectedModeIndex switch
            {
                0 => vm.CustomEcoPl1,
                1 => vm.CustomQuietPl1,
                2 => vm.CustomBalancedPl1,
                3 => vm.CustomPerfPl1,
                _ => vm.CustomTurboPl1
            };

            int pl2 = _selectedModeIndex switch
            {
                0 => vm.CustomEcoPl2,
                1 => vm.CustomQuietPl2,
                2 => vm.CustomBalancedPl2,
                3 => vm.CustomPerfPl2,
                _ => vm.CustomTurboPl2
            };

            int limitedPl1 = vm.LimitPl1ToPlatform(pl1);
            WriteClampedLimitsToEditor(limitedPl1, vm.LimitPl2(limitedPl1, pl2));

            if (SliderTau != null)
            {
                int tau = CpuPowerLimitConfiguration.SnapTauSelection(vm.TauForModeIndex(_selectedModeIndex));
                SliderTau.Maximum = CpuPowerLimitConfiguration.TauSliderMaximum;
                SliderTau.Value = CpuPowerLimitConfiguration.TauStepIndex(tau);
                if (LabelTau != null) LabelTau.Text = FormatTau(tau);
            }

            if (SwitchCpuBoost != null)
            {
                SwitchCpuBoost.Visibility = vm.CpuBoostEditorVisibility;
                SwitchCpuBoost.IsChecked = vm.CpuBoostForModeIndex(_selectedModeIndex);
            }
            if (DropdownCpuBoost != null)
            {
                DropdownCpuBoost.Visibility = vm.ExtendedCpuBoostEditorVisibility;
                DropdownCpuBoost.SelectedIndex = Math.Clamp(vm.CpuBoostMode, 0, vm.CpuBoostModes.Count - 1);
            }

            int gpuWatts = _selectedModeIndex switch
            {
                0 => vm.CustomEcoGpuWatts,
                1 => vm.CustomQuietGpuWatts,
                2 => vm.CustomBalancedGpuWatts,
                3 => vm.CustomPerfGpuWatts,
                _ => vm.CustomTurboGpuWatts
            };
            if (RowGpuPower != null)
            {
                var offsets = vm.GpuOffsetsForMode(_selectedModeIndex, gpuWatts);
                RowGpuDelta.Value = offsets.DeltaWatts;
                RowGpuDynamicBoost.Value = offsets.DynamicBoostWatts;
                gpuWatts = offsets.NominalReferenceWatts(vm.GpuBasePowerWatts);
                RowGpuPower.Value = gpuWatts;
            }
            vm.GpuPowerTargetWatts = gpuWatts;

            int gpuTrip = _selectedModeIndex switch
            {
                0 => vm.CustomEcoGpuTripPoint,
                1 => vm.CustomQuietGpuTripPoint,
                2 => vm.CustomBalancedGpuTripPoint,
                3 => vm.CustomPerfGpuTripPoint,
                _ => vm.CustomTurboGpuTripPoint
            };
            if (SliderGpuTripPoint != null)
            {
                SliderGpuTripPoint.Value = gpuTrip;
            }
            vm.GpuThermalTripPointCelsius = gpuTrip;

            _isUpdatingUi = false;
        }

        private void SliderGpuTripPoint_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingUi || DataContext is not MainViewModel vm || !vm.IsAcpiOverrideEnabled) return;
            int val = (int)Math.Round(e.NewValue);
            switch (_selectedModeIndex)
            {
                case 0: vm.CustomEcoGpuTripPoint = val; break;
                case 1: vm.CustomQuietGpuTripPoint = val; break;
                case 2: vm.CustomBalancedGpuTripPoint = val; break;
                case 3: vm.CustomPerfGpuTripPoint = val; break;
                case 4: vm.CustomTurboGpuTripPoint = val; break;
            }
            vm.GpuThermalTripPointCelsius = val;
        }

        private System.Windows.Threading.DispatcherTimer? _debounceTauTimer;

        private (int pl1, int pl2) ReadClampedLimits(bool preferPl2)
        {
            if (DataContext is not MainViewModel vm)
                return (10, 10);

            int pl1 = vm.LimitPl1ToPlatform((int)Math.Round(RowPl1?.Value ?? 10));
            int pl2 = (int)Math.Round(RowPl2?.Value ?? pl1);
            if (preferPl2 && pl2 < pl1)
                pl1 = vm.LimitPl1ToPlatform(pl2);
            pl2 = vm.LimitPl2(pl1, pl2);
            return (pl1, pl2);
        }

        private void WriteClampedLimitsToEditor(int pl1, int pl2)
        {
            bool wasUpdating = _isUpdatingUi;
            _isUpdatingUi = true;
            if (RowPl1 != null) RowPl1.Value = pl1;
            if (RowPl2 != null) RowPl2.Value = pl2;
            _isUpdatingUi = wasUpdating;
        }

        private void StoreModeLimits(int pl1, int pl2)
        {
            if (DataContext is not MainViewModel vm) return;
            switch (_selectedModeIndex)
            {
                case 0: vm.CustomEcoPl1 = pl1; vm.CustomEcoPl2 = pl2; break;
                case 1: vm.CustomQuietPl1 = pl1; vm.CustomQuietPl2 = pl2; break;
                case 2: vm.CustomBalancedPl1 = pl1; vm.CustomBalancedPl2 = pl2; break;
                case 3: vm.CustomPerfPl1 = pl1; vm.CustomPerfPl2 = pl2; break;
                case 4: vm.CustomTurboPl1 = pl1; vm.CustomTurboPl2 = pl2; break;
            }
        }

        private void RowPl1_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingUi || DataContext is not MainViewModel vm || !vm.IsAcpiOverrideEnabled) return;
            var (pl1, pl2) = ReadClampedLimits(preferPl2: false);
            WriteClampedLimitsToEditor(pl1, pl2);
            StoreModeLimits(pl1, pl2);
        }

        private void RowPl2_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingUi || DataContext is not MainViewModel vm || !vm.IsAcpiOverrideEnabled) return;
            var (pl1, pl2) = ReadClampedLimits(preferPl2: true);
            WriteClampedLimitsToEditor(pl1, pl2);
            StoreModeLimits(pl1, pl2);
        }

        private int ReadGpuWatts()
        {
            int baseWatts = (DataContext as MainViewModel)?.GpuBasePowerWatts ?? 85;
            return ReadGpuOffsets().NominalReferenceWatts(baseWatts);
        }

        private GpuPowerOffsets ReadGpuOffsets() => new(
            Math.Clamp((int)Math.Round(RowGpuDelta.Value), 0, 255),
            Math.Clamp((int)Math.Round(RowGpuDynamicBoost.Value), 0, 255));

        private void GpuOffsets_Committed(object? sender, EventArgs e)
        {
            if (_isUpdatingUi || DataContext is not MainViewModel vm || !vm.IsAcpiOverrideEnabled) return;
            var offsets = ReadGpuOffsets();
            RowGpuDelta.Value = offsets.DeltaWatts;
            RowGpuDynamicBoost.Value = offsets.DynamicBoostWatts;
            RowGpuPower.Value = offsets.NominalReferenceWatts(vm.GpuBasePowerWatts);
            StoreGpuWatts((int)RowGpuPower.Value);
        }

        private void StoreGpuWatts(int watts)
        {
            if (DataContext is not MainViewModel vm) return;
            vm.StoreGpuOffsets(_selectedModeIndex, ReadGpuOffsets());
            switch (_selectedModeIndex)
            {
                case 0: vm.CustomEcoGpuWatts = watts; break;
                case 1: vm.CustomQuietGpuWatts = watts; break;
                case 2: vm.CustomBalancedGpuWatts = watts; break;
                case 3: vm.CustomPerfGpuWatts = watts; break;
                case 4: vm.CustomTurboGpuWatts = watts; break;
            }
            vm.GpuPowerTargetWatts = watts;
        }

        private void RowGpuPower_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingUi || DataContext is not MainViewModel vm || !vm.IsAcpiOverrideEnabled) return;
            int gpuW = ReadGpuWatts();
            if (RowGpuPower != null) RowGpuPower.Value = gpuW;
            StoreGpuWatts(gpuW);
        }

        private void SliderTau_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (SliderTau == null || LabelTau == null) return;
            int val = ReadTauSeconds();
            LabelTau.Text = FormatTau(val);

            if (!_isUpdatingUi && DataContext is MainViewModel vm && vm.IsAcpiOverrideEnabled)
            {
                if (_debounceTauTimer == null)
                {
                    _debounceTauTimer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(80)
                    };
                    _debounceTauTimer.Tick += (s, ev) =>
                    {
                        _debounceTauTimer.Stop();
                        if (SliderTau == null) return;
                        vm.SetTauForModeIndex(_selectedModeIndex, ReadTauSeconds());
                    };
                }
                _debounceTauTimer.Stop();
                _debounceTauTimer.Start();
            }
        }

        private async void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm || !vm.IsAcpiOverrideEnabled) return;

            string modeName = Services.LocalizationService.Translate(GetSelectedModeName());
            var confirmation = new CustomModalDialog
            {
                Owner = this,
                Title = "PreySense • Reset Mode",
                HeaderSubtitle = " • Reset Mode",
                Message = Services.LocalizationService.Format("Reset {0} to factory power settings and apply them now?", modeName),
                ConfirmText = "Reset & Apply",
                CancelText = "Cancel",
                ShowCancelButton = true
            };
            if (confirmation.ShowDialog() != true)
                return;

            _isUpdatingUi = true;

            vm.ResetModeToCapturedFactoryDefaults(_selectedModeIndex);

            _isUpdatingUi = false;
            LoadModeData();
            await ApplySelectedModeAsync();
            string applyStatus = vm.StatusMessage;
            vm.SaveCpuLimitsToRegistry();
            vm.StatusMessage = applyStatus;
        }

        private async void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            await ApplySelectedModeAsync();
        }

        private async System.Threading.Tasks.Task ApplySelectedModeAsync()
        {
            if (DataContext is not MainViewModel vm || !vm.IsAcpiOverrideEnabled) return;

            var (pl1, pl2) = ReadClampedLimits(preferPl2: false);
            int tau = ReadTauSeconds();

            WriteClampedLimitsToEditor(pl1, pl2);
            StoreModeLimits(pl1, pl2);
            vm.SetTauForModeIndex(_selectedModeIndex, tau);
            vm.SetCpuBoostForModeIndex(_selectedModeIndex, SwitchCpuBoost?.IsChecked == true);
            vm.Pl1Watts = pl1;
            vm.Pl2Watts = pl2;

            int gpuW = ReadGpuWatts();
            if (RowGpuPower != null) RowGpuPower.Value = gpuW;
            StoreGpuWatts(gpuW);

            if (SliderGpuTripPoint != null)
            {
                int gpuTrip = (int)Math.Round(SliderGpuTripPoint.Value);
                switch (_selectedModeIndex)
                {
                    case 0: vm.CustomEcoGpuTripPoint = gpuTrip; break;
                    case 1: vm.CustomQuietGpuTripPoint = gpuTrip; break;
                    case 2: vm.CustomBalancedGpuTripPoint = gpuTrip; break;
                    case 3: vm.CustomPerfGpuTripPoint = gpuTrip; break;
                    case 4: vm.CustomTurboGpuTripPoint = gpuTrip; break;
                }
                vm.GpuThermalTripPointCelsius = gpuTrip;
            }

            // Apply CPU limits to hardware
            // Select the edited mode before writing its mode-table entries.
            byte selectedMode = ModeIndexToByte(_selectedModeIndex);
            if (vm.ActivePowerMode != selectedMode || !vm.IsGpuHardwareModeActive(selectedMode))
                await vm.ApplyModeSwitchAsync(selectedMode);
            if (!await System.Threading.Tasks.Task.Run(() => vm.IsGpuHardwareModeActive(selectedMode)))
            {
                vm.StatusMessage = "The selected Acer mode could not be confirmed; limits were not applied.";
                return;
            }
            vm.ApplyCpuBoostForMode(selectedMode);
            await vm.ApplyPowerLimitsCommand.ExecuteAsync(null);
            string cpuApplyStatus = vm.StatusMessage;

            // Apply GPU settings to hardware
            await vm.ApplyGpuClocksCommand.ExecuteAsync(null);
            vm.StatusMessage = cpuApplyStatus + " | " + vm.StatusMessage;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm || !vm.IsAcpiOverrideEnabled) return;

            var (pl1, pl2) = ReadClampedLimits(preferPl2: false);
            int tau = ReadTauSeconds();

            WriteClampedLimitsToEditor(pl1, pl2);
            StoreModeLimits(pl1, pl2);
            vm.SetTauForModeIndex(_selectedModeIndex, tau);
            vm.SetCpuBoostForModeIndex(_selectedModeIndex, SwitchCpuBoost?.IsChecked == true);

            int gpuW = ReadGpuWatts();
            if (RowGpuPower != null) RowGpuPower.Value = gpuW;
            StoreGpuWatts(gpuW);

            if (SliderGpuTripPoint != null)
            {
                int gpuTrip = (int)Math.Round(SliderGpuTripPoint.Value);
                switch (_selectedModeIndex)
                {
                    case 0: vm.CustomEcoGpuTripPoint = gpuTrip; break;
                    case 1: vm.CustomQuietGpuTripPoint = gpuTrip; break;
                    case 2: vm.CustomBalancedGpuTripPoint = gpuTrip; break;
                    case 3: vm.CustomPerfGpuTripPoint = gpuTrip; break;
                    case 4: vm.CustomTurboGpuTripPoint = gpuTrip; break;
                }
            }

            // Persist to registry
            vm.SaveCpuLimitsToRegistry();

            string modeName = GetSelectedModeName();
            string boost = vm.IsExtendedCpuBoostEnabled
                ? vm.CpuBoostModeName
                : (SwitchCpuBoost?.IsChecked == true ? "on" : "off");
            vm.StatusMessage = $"Saved {modeName} limits (PL1 {pl1}W, PL2 {pl2}W, Tau {FormatTau(tau)}, CPU Boost {boost}) to registry.";
        }

        private void CpuBoostSwitch_Changed(object? sender, EventArgs e)
        {
            if (_isUpdatingUi || DataContext is not MainViewModel vm || SwitchCpuBoost?.IsChecked is not bool enabled)
                return;
            vm.SetCpuBoostForModeIndex(_selectedModeIndex, enabled);
        }

        private static byte ModeIndexToByte(int modeIndex) => modeIndex switch
        {
            0 => 0x06,
            1 => 0x00,
            3 => 0x04,
            4 => 0x05,
            _ => 0x01
        };
    }
}
