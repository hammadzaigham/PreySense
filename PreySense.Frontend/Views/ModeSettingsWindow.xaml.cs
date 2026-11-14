using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using PreySense.Core.Configuration;
using PreySense.Core.Driver;
using PreySense.Frontend.Controls;
using PreySense.Frontend.ViewModels;
using PreySense.Helpers;

namespace PreySense.Frontend.Views
{
    public partial class ModeSettingsWindow : Window
    {
        private int _selectedModeIndex = 2; // 0=Eco, 1=Quiet, 2=Balanced, 3=Perf, 4=Turbo
        private bool _isUpdatingUi;
        private readonly Dictionary<int, ModeTuningConfig> _drafts = new();
        private ModeTuningConfig Draft => _drafts[_selectedModeIndex];
        private readonly System.Windows.Threading.DispatcherTimer _verificationTimer = new()
        {
            Interval = TimeSpan.FromSeconds(3)
        };

        public ModeSettingsWindow()
        {
            InitializeComponent();

            SliderTau.PreviewMouseLeftButtonUp += (_, _) => CommitSlider(SliderTau);
            _verificationTimer.Tick += (_, _) =>
            {
                _verificationTimer.Stop();
                SetVerificationText(string.Empty);
            };
            MaxHeight = SystemParameters.WorkArea.Height - 40;
            Loaded += ModeSettingsWindow_Loaded;
            foreach (string name in MainViewModel.CpuBoostModeNames)
                DropdownCpuBoost.Items.Add(name);
            DropdownCpuBoost.SelectionChanged += CpuBoostDropdown_Changed;
            Closed += (_, _) =>
            {
                _verificationTimer.Stop();
                if (DataContext is MainViewModel vm)
                    vm.PropertyChanged -= OnTuningVmPropertyChanged;
                DropdownCpuBoost.SelectionChanged -= CpuBoostDropdown_Changed;
            };
        }

        private void SetVerificationText(string text, Brush? foreground = null)
        {
            bool hasText = !string.IsNullOrWhiteSpace(text);
            TuningVerificationText.Text = hasText ? text : string.Empty;
            if (foreground is not null)
                TuningVerificationText.Foreground = foreground;
            TuningVerificationText.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
            TuningVerificationText.Margin = hasText ? new Thickness(14, 8, 14, 4) : new Thickness(14, 0, 14, 0);
        }

        private async void ModeSettingsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.PropertyChanged -= OnTuningVmPropertyChanged;
                vm.PropertyChanged += OnTuningVmPropertyChanged;
                await System.Threading.Tasks.Task.WhenAll(vm.RefreshProchotLockStateAsync(), vm.RefreshGpuClockTuningAsync());
            }
            SyncFromViewModel();
        }

        private void OnTuningVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.IsHardwareTuningEnabled) && IsLoaded)
                LoadModeData();
            else if (e.PropertyName is nameof(MainViewModel.EnableCpuPowerLimitControl)
                or nameof(MainViewModel.EnableGpuPowerLimitControl)
                or nameof(MainViewModel.EnableGpuOverclockingControl)
                or nameof(MainViewModel.EnableCpuBoostTuningControl))
            {
                UpdateTuningSectionsVisibility();
                UpdateActionButtonsEnabled();
            }
            else if (e.PropertyName == nameof(MainViewModel.IsProchotOffsetEnabled) && !_isUpdatingUi)
            {
                UpdateActionButtonsEnabled();
            }
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

        private void ComboModeSelect_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (ComboModeSelect == null || _isUpdatingUi || !IsLoaded || ComboModeSelect.SelectedIndex < 0) return;
            _selectedModeIndex = ComboModeSelect.SelectedIndex;
            LoadModeData();
        }

        private static string FormatTau(int seconds)
        {
            string text = CpuPowerLimitConfiguration.FormatTauSelection(seconds);
            return text is "Off" ? Services.LocalizationService.Translate(text) : text;
        }

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
            if (!_drafts.ContainsKey(_selectedModeIndex)) _drafts[_selectedModeIndex] = vm.TuningEditorMode(_selectedModeIndex);
            SetVerificationText(string.Empty);

            if (ComboModeSelect != null && ComboModeSelect.SelectedIndex != _selectedModeIndex)
            {
                ComboModeSelect.SelectedIndex = _selectedModeIndex;
            }

            int pl1 = Draft.CpuPl1Watts ?? 0;
            int pl2 = Draft.CpuPl2Watts ?? 0;
            int limitedPl1 = vm.LimitPl1ToPlatform(pl1);
            WriteClampedLimitsToEditor(limitedPl1, vm.LimitPl2(limitedPl1, pl2));

            if (SliderTau != null)
            {
                int tau = CpuPowerLimitConfiguration.SnapTauSelection(Draft.TauSeconds ?? 0);
                SliderTau.Maximum = CpuPowerLimitConfiguration.TauSliderMaximum;
                SliderTau.Value = CpuPowerLimitConfiguration.TauStepIndex(tau);
                if (LabelTau != null) LabelTau.Text = FormatTau(tau);
            }

            if (DropdownCpuBoost != null)
                DropdownCpuBoost.SelectedIndex = Math.Clamp(Draft.EffectiveCpuBoostMode ?? vm.CpuBoostForModeIndex(_selectedModeIndex), 0, 6);

            UpdateTuningSectionsVisibility();

            SwitchApplyGpuLimits.IsChecked = Draft.ApplyGpuLimits;
            SliderGpuDelta.Value = Draft.GpuDeltaWatts ?? 0;
            SliderGpuDynamicBoost.Value = Draft.GpuDynamicBoostWatts ?? 0;
            SliderPlatformPowerOffset.Value = Draft.PlatformPowerOffsetWatts ?? 0;
            UpdateGpuOffsetLabels(vm);

            if (!Draft.CpuPl1Watts.HasValue) RowPl1.Value = double.NaN;
            if (!Draft.CpuPl2Watts.HasValue) RowPl2.Value = double.NaN;
            if (!Draft.TauSeconds.HasValue && LabelTau is not null) LabelTau.Text = string.Empty;
            if (SwitchApplyCpuLimits != null)
                SwitchApplyCpuLimits.IsChecked = Draft.ApplyCpuLimits;

            LoadGpuClockRows(vm);
            UpdateApplyLimitPanels();
            UpdateActionButtonsEnabled();
            _isUpdatingUi = false;
        }

        private void ApplyLimitsToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingUi || !_drafts.ContainsKey(_selectedModeIndex)) return;
            Draft.ApplyCpuLimits = SwitchApplyCpuLimits?.IsChecked == true;
            Draft.ApplyGpuLimits = SwitchApplyGpuLimits?.IsChecked == true;
            UpdateApplyLimitPanels();
            UpdateActionButtonsEnabled();
        }

        private void LoadGpuClockRows(MainViewModel vm)
        {
            var state = vm.GpuClockTuningState;
            SwitchGpuOverclocking.IsChecked = Draft.ApplyGpuClockOffsets;
            bool available = HasSteppedClockRanges(state);
            SwitchGpuOverclocking.IsEnabled = available || Draft.ApplyGpuClockOffsets;
            if (available)
            {
                var core = state!.Core.ForStep(PreySense.Gpu.GpuClockTuningState.CoreStepMhz);
                var memory = state.Memory.ForStep(PreySense.Gpu.GpuClockTuningState.MemoryStepMhz);
                RowGpuCoreClock.Minimum = Math.Max(0, core.MinimumMhz);
                RowGpuCoreClock.Maximum = Math.Min(PreySense.Gpu.GpuClockTuningState.MaximumCoreOffsetMhz, core.MaximumMhz);
                RowGpuMemoryClock.Minimum = memory.MinimumMhz;
                RowGpuMemoryClock.Maximum = memory.MaximumMhz;
                RowGpuCoreClock.Value = state.Core.Snap(Draft.GpuCoreClockOffsetMhz ?? 0, PreySense.Gpu.GpuClockTuningState.CoreStepMhz);
                RowGpuMemoryClock.Value = state.Memory.Snap(Draft.GpuMemoryClockOffsetMhz ?? 0, PreySense.Gpu.GpuClockTuningState.MemoryStepMhz);
            }
            RowGpuCoreClock.ValueText = available ? $"{RowGpuCoreClock.Value:+0;-0;0} MHz" : "—";
            RowGpuMemoryClock.ValueText = available ? $"{RowGpuMemoryClock.Value:+0;-0;0} MHz" : "—";
            GpuOverclockingPanel.IsEnabled = available && Draft.ApplyGpuClockOffsets;
        }

        private void GpuOverclockingToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingUi || !_drafts.ContainsKey(_selectedModeIndex)) return;
            Draft.ApplyGpuClockOffsets = SwitchGpuOverclocking.IsChecked == true;
            GpuOverclockingPanel.IsEnabled = Draft.ApplyGpuClockOffsets &&
                DataContext is MainViewModel vm && HasSteppedClockRanges(vm.GpuClockTuningState);
            if (DataContext is MainViewModel clockVm)
                SwitchGpuOverclocking.IsEnabled = HasSteppedClockRanges(clockVm.GpuClockTuningState) || Draft.ApplyGpuClockOffsets;
            UpdateActionButtonsEnabled();
        }

        private static bool HasSteppedClockRanges(PreySense.Gpu.GpuClockTuningState? state) => state is not null
            && state.Core.ForStep(PreySense.Gpu.GpuClockTuningState.CoreStepMhz) is var core && core.MinimumMhz <= core.MaximumMhz
            && state.Memory.ForStep(PreySense.Gpu.GpuClockTuningState.MemoryStepMhz) is var memory && memory.MinimumMhz <= memory.MaximumMhz;

        private void GpuClockOffset_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingUi || !_drafts.ContainsKey(_selectedModeIndex)) return;
            StoreGpuClockSliders();
        }

        private void StoreGpuClockSliders()
        {
            if (DataContext is not MainViewModel vm || !HasSteppedClockRanges(vm.GpuClockTuningState)) return;
            var state = vm.GpuClockTuningState!;
            Draft.GpuCoreClockOffsetMhz = state.Core.Snap((int)Math.Round(RowGpuCoreClock.Value), PreySense.Gpu.GpuClockTuningState.CoreStepMhz);
            Draft.GpuMemoryClockOffsetMhz = state.Memory.Snap((int)Math.Round(RowGpuMemoryClock.Value), PreySense.Gpu.GpuClockTuningState.MemoryStepMhz);
            RowGpuCoreClock.ValueText = $"{Draft.GpuCoreClockOffsetMhz:+0;-0;0} MHz";
            RowGpuMemoryClock.ValueText = $"{Draft.GpuMemoryClockOffsetMhz:+0;-0;0} MHz";
        }

        private void UpdateTuningSectionsVisibility()
        {
            if (DataContext is not MainViewModel vm) return;

            if (BorderCpuLimits != null)
                BorderCpuLimits.Visibility = vm.EnableCpuPowerLimitControl ? Visibility.Visible : Visibility.Collapsed;

            if (BorderGpuOverclocking != null)
                BorderGpuOverclocking.Visibility = vm.EnableGpuOverclockingControl ? Visibility.Visible : Visibility.Collapsed;

            if (BorderGpuPowerLimit != null)
            {
                // GPU limits are available for every operating mode with verified ERM4 support.
                bool showGpuLimit = vm.EnableGpuPowerLimitControl;
                BorderGpuPowerLimit.Visibility = showGpuLimit ? Visibility.Visible : Visibility.Collapsed;
            }

            UpdateApplyLimitPanels();
        }

        private void UpdateApplyLimitPanels()
        {
            if (CpuLimitsPanel != null)
                CpuLimitsPanel.IsEnabled = SwitchApplyCpuLimits?.IsChecked == true;
            if (GpuLimitsPanel != null)
                GpuLimitsPanel.IsEnabled = BorderGpuPowerLimit?.Visibility == Visibility.Visible
                    && SwitchApplyGpuLimits?.IsChecked == true
                    && DataContext is MainViewModel vm && vm.IsGpuProfileTuningAvailable;
        }

        private void UpdateActionButtonsEnabled()
        {
            if (DataContext is not MainViewModel vm) return;
            bool cpuActive = vm.EnableCpuPowerLimitControl && SwitchApplyCpuLimits?.IsChecked == true;
            bool gpuPowerActive = vm.EnableGpuPowerLimitControl && vm.IsGpuProfileTuningAvailable && Draft.ApplyGpuLimits;
            bool gpuOcActive = vm.EnableGpuOverclockingControl && SwitchGpuOverclocking?.IsChecked == true;
            bool prochotOnly = vm.IsProchotOffsetEnabled;
            bool canAct = cpuActive || gpuPowerActive || gpuOcActive || prochotOnly || vm.EnableCpuBoostTuningControl;
            if (BtnApply != null) BtnApply.IsEnabled = canAct;
            if (BtnSave != null) BtnSave.IsEnabled = true;
            if (BtnReset != null) BtnReset.IsEnabled = canAct;
        }

        private void CommitSlider(System.Windows.Controls.Slider slider)
        {
            if (_isUpdatingUi || !_drafts.ContainsKey(_selectedModeIndex)) return;
            if (ReferenceEquals(slider, SliderTau))
            {
                Draft.TauSeconds = ReadTauSeconds(); LabelTau.Text = FormatTau(Draft.TauSeconds.Value);
            }
        }

        private GpuPowerOffsets ReadGpuOffsets() => new(
            (int)Math.Round(SliderGpuDelta.Value),
            (int)Math.Round(SliderGpuDynamicBoost.Value),
            (int)Math.Round(SliderPlatformPowerOffset.Value));

        private void UpdateGpuOffsetLabels(MainViewModel vm)
        {
            var offsets = ReadGpuOffsets();
            LabelGpuDelta.Text = $"{offsets.DeltaWatts} W";
            LabelGpuDynamicBoost.Text = $"{offsets.DynamicBoostWatts} W";
            LabelPlatformPowerOffset.Text = $"{offsets.PlatformOffsetWatts} W";
            RowGpuPower.Value = offsets.NominalReferenceWatts(vm.GpuBasePowerWatts) ?? double.NaN;
        }

        private void GpuOffsets_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingUi || !_drafts.ContainsKey(_selectedModeIndex) || DataContext is not MainViewModel vm) return;
            var offsets = ReadGpuOffsets();
            Draft.GpuDeltaWatts = offsets.DeltaWatts;
            Draft.GpuDynamicBoostWatts = offsets.DynamicBoostWatts;
            Draft.PlatformPowerOffsetWatts = offsets.PlatformOffsetWatts;
            Draft.GpuRuntimeLimitWatts = null;
            vm.StoreGpuOffsets(_selectedModeIndex, offsets);
            UpdateGpuOffsetLabels(vm);
            UpdateActionButtonsEnabled();
        }

        private void RowGpuPower_LostFocus(object sender, RoutedEventArgs e) { }

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
            if (_isUpdatingUi || DataContext is not MainViewModel vm || !vm.IsHardwareTuningEnabled) return;
            Draft.CpuPl1Watts = double.IsFinite(RowPl1.Value) ? (int)RowPl1.Value : null;
            Draft.CpuPl2Watts = double.IsFinite(RowPl2.Value) ? (int)RowPl2.Value : null;
            if (!Draft.CpuPl1Watts.HasValue || !Draft.CpuPl2Watts.HasValue) return;
            var (pl1, pl2) = ReadClampedLimits(preferPl2: false);
            WriteClampedLimitsToEditor(pl1, pl2);
            StoreModeLimits(pl1, pl2);
            Draft.CpuPl1Watts = pl1; Draft.CpuPl2Watts = pl2;
        }

        private void RowPl2_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingUi || DataContext is not MainViewModel vm || !vm.IsHardwareTuningEnabled) return;
            Draft.CpuPl1Watts = double.IsFinite(RowPl1.Value) ? (int)RowPl1.Value : null;
            Draft.CpuPl2Watts = double.IsFinite(RowPl2.Value) ? (int)RowPl2.Value : null;
            if (!Draft.CpuPl1Watts.HasValue || !Draft.CpuPl2Watts.HasValue) return;
            var (pl1, pl2) = ReadClampedLimits(preferPl2: true);
            WriteClampedLimitsToEditor(pl1, pl2);
            StoreModeLimits(pl1, pl2);
            Draft.CpuPl1Watts = pl1; Draft.CpuPl2Watts = pl2;
        }

        private void SliderTau_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (SliderTau == null || LabelTau == null) return;
            int val = ReadTauSeconds();
            LabelTau.Text = FormatTau(val);

            if (!_isUpdatingUi && DataContext is MainViewModel vm && vm.IsHardwareTuningEnabled)
            {
                Draft.TauSeconds = val;
                vm.SetTauForModeIndex(_selectedModeIndex, val);
            }
        }

        private async void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;
            if (!vm.IsHardwareTuningEnabled || vm.IsBusy)
            {
                SetVerificationText(
                    vm.IsBusy
                        ? Services.LocalizationService.Translate("Tuning is busy. Try again in a moment.")
                        : Services.LocalizationService.Translate("Tuning is unavailable until factory defaults are ready."),
                    TryFindResource("WarningTextBrush") as Brush ?? Brushes.OrangeRed);
                return;
            }

            string modeName = Services.LocalizationService.Translate(GetSelectedModeName());
            var confirmation = new CustomModalDialog
            {
                Owner = this,
                Title = "PreySense \u2022 Reset Mode",
                HeaderSubtitle = " \u2022 Reset Mode",
                Message = Services.LocalizationService.Format("Reset {0} to factory power settings and apply them now?", modeName),
                ConfirmText = "Reset & Apply",
                CancelText = "Cancel",
                ShowCancelButton = true
            };
            if (confirmation.ShowDialog() != true)
                return;

            bool resetGpuClocks = vm.EnableGpuOverclockingControl && SwitchGpuOverclocking.IsChecked == true;
            _isUpdatingUi = true;
            vm.ResetModeToCapturedFactoryDefaults(_selectedModeIndex);
            _drafts[_selectedModeIndex] = vm.FactoryTuningMode(_selectedModeIndex) ?? new();
            Draft.ApplyCpuLimits = false;
            Draft.ApplyCpuBoost = false;
            Draft.ApplyGpuLimits = false;
            Draft.GpuRuntimeLimitWatts = null;
            vm.IsProchotOffsetEnabled = false;
            _isUpdatingUi = false;
            LoadModeData();

            // Write factory CPU/GPU/PROCHOT once, then persist with every apply switch off.
            SetVerificationText(string.Empty);
            var (pl1, pl2, tau) = StoreEditorSettings(vm, updateActiveValues: true);
            byte mode = ModeIndexToByte(_selectedModeIndex);
            bool applyProchot = !vm.IsProchotOffsetLocked && vm.ProchotOffsetCelsius is not null;
            BtnApply.IsEnabled = BtnSave.IsEnabled = BtnReset.IsEnabled = false;
            ComboModeSelect.IsEnabled = TuningScroll.IsEnabled = false;
            try
            {
                bool verified = await vm.ApplyTuningProfileAsync(mode, pl1, pl2, tau,
                    null,
                    persist: true, vm.ProchotOffsetCelsius, applyProchot,
                    applyCpuLimits: true, persistApplySwitches: false, restoreMsrToStaticFloor: true,
                    clockOffsets: resetGpuClocks ? new PreySense.Gpu.GpuClockOffsets(0, 0) : null,
                    applyGpuClockOffsets: resetGpuClocks,
                    gpuOffsets: vm.IsGpuProfileTuningAvailable && Draft.HasGpuLimitValues ? ReadGpuOffsets() : null);
                Draft.ApplyCpuLimits = false;
                Draft.GpuRuntimeLimitWatts = null;
                vm.IsProchotOffsetEnabled = false;
                LoadModeData();
                string message = verified
                    ? Services.LocalizationService.Translate("Applied and read back successfully.")
                    : (string.IsNullOrWhiteSpace(vm.DiagnosticMessage)
                        ? Services.LocalizationService.Translate("Could not apply.")
                        : vm.DiagnosticMessage);
                SetVerificationText(message, verified
                    ? TryFindResource("SuccessTextBrush") as Brush ?? Brushes.LightGreen
                    : TryFindResource("WarningTextBrush") as Brush ?? Brushes.OrangeRed);
                _verificationTimer.Stop();
                if (IsLoaded) _verificationTimer.Start();
            }
            finally
            {
                UpdateActionButtonsEnabled();
                ComboModeSelect.IsEnabled = TuningScroll.IsEnabled = true;
            }
        }

        private async void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            await ApplySelectedModeAsync();
        }

        private async System.Threading.Tasks.Task ApplySelectedModeAsync(bool persist = false)
        {
            if (DataContext is not MainViewModel vm) return;
            if (!vm.IsHardwareTuningEnabled || vm.IsBusy)
            {
                SetVerificationText(
                    vm.IsBusy
                        ? Services.LocalizationService.Translate("Tuning is busy. Try again in a moment.")
                        : Services.LocalizationService.Translate("Tuning is unavailable until factory defaults are ready."),
                    TryFindResource("WarningTextBrush") as Brush ?? Brushes.OrangeRed);
                return;
            }
            SetVerificationText(string.Empty);
            RowPl1.CommitText(notify: false); RowPl2.CommitText(notify: false);
            StoreGpuClockSliders();
            bool applyGpuOc = vm.EnableGpuOverclockingControl && SwitchGpuOverclocking?.IsChecked == true;
            Draft.ApplyGpuClockOffsets = applyGpuOc;
            bool applyProchot = vm.IsProchotOffsetEnabled && !vm.IsProchotOffsetLocked;
            bool applyCpuLimits = vm.EnableCpuPowerLimitControl && SwitchApplyCpuLimits?.IsChecked == true;
            bool applyCpuBoost = vm.EnableCpuBoostTuningControl;
            Draft.ApplyCpuBoost = applyCpuBoost;
            Draft.ApplyCpuLimits = applyCpuLimits;
            Draft.CpuPl1Watts = double.IsFinite(RowPl1.Value) ? (int)RowPl1.Value : null;
            Draft.CpuPl2Watts = double.IsFinite(RowPl2.Value) ? (int)RowPl2.Value : null;
            Draft.TauSeconds = ReadTauSeconds();

            var gpuOffsets = vm.EnableGpuPowerLimitControl && SwitchApplyGpuLimits.IsChecked == true
                ? ReadGpuOffsets() : null;
            Draft.ApplyGpuLimits = gpuOffsets is not null;
            int? gpuRuntime = null;
            if (applyProchot && vm.ProchotOffsetCelsius is null)
            {
                SetVerificationText(Services.LocalizationService.Translate("Enter a PROCHOT offset, or turn its switch off."),
                    TryFindResource("WarningTextBrush") as Brush ?? Brushes.OrangeRed);
                return;
            }
            if (!persist && !applyCpuLimits && gpuOffsets is null && !applyProchot && !applyGpuOc && !applyCpuBoost)
            {
                SetVerificationText(Services.LocalizationService.Translate("Turn on CPU limits, GPU limits, GPU overclocking, or PROCHOT first."),
                    TryFindResource("WarningTextBrush") as Brush ?? Brushes.OrangeRed);
                return;
            }
            if (applyCpuLimits && !Draft.HasCpuLimitValues)
            {
                SetVerificationText(Services.LocalizationService.Translate("Set CPU limits and time limit before applying."),
                    TryFindResource("WarningTextBrush") as Brush ?? Brushes.OrangeRed);
                return;
            }
            var (pl1, pl2, tau) = StoreEditorSettings(vm, updateActiveValues: true);
            byte mode = ModeIndexToByte(_selectedModeIndex);
            BtnApply.IsEnabled = BtnSave.IsEnabled = BtnReset.IsEnabled = false;
            ComboModeSelect.IsEnabled = TuningScroll.IsEnabled = false;
            try
            {
                bool verified = await vm.ApplyTuningProfileAsync(mode, pl1, pl2, tau,
                    gpuRuntime, persist, vm.ProchotOffsetCelsius, applyProchot,
                    applyCpuLimits,
                    clockOffsets: (applyGpuOc && Draft.HasGpuClockValues) ? new PreySense.Gpu.GpuClockOffsets(
                        Draft.GpuCoreClockOffsetMhz!.Value, Draft.GpuMemoryClockOffsetMhz!.Value) : null,
                    applyGpuClockOffsets: applyGpuOc, applyCpuBoost: applyCpuBoost, gpuOffsets: gpuOffsets);
                string message = verified
                    ? Services.LocalizationService.Translate(!applyCpuLimits && gpuOffsets is null && !applyProchot && !applyGpuOc && !applyCpuBoost
                        ? "Saved. Disabled tuning sections were skipped." : "Applied and read back successfully.")
                    : (string.IsNullOrWhiteSpace(vm.DiagnosticMessage)
                        ? Services.LocalizationService.Translate("Could not apply.")
                        : vm.DiagnosticMessage);
                SetVerificationText(message, verified
                    ? TryFindResource("SuccessTextBrush") as Brush ?? Brushes.LightGreen
                    : TryFindResource("WarningTextBrush") as Brush ?? Brushes.OrangeRed);

                _verificationTimer.Stop();
                if (IsLoaded) _verificationTimer.Start();
            }
            finally
            {
                UpdateActionButtonsEnabled();
                ComboModeSelect.IsEnabled = TuningScroll.IsEnabled = true;
            }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            await ApplySelectedModeAsync(persist: true);
        }

        private (int Pl1, int Pl2, int Tau) StoreEditorSettings(MainViewModel vm, bool updateActiveValues)
        {
            var (pl1, pl2) = ReadClampedLimits(preferPl2: false);
            int tau = ReadTauSeconds();
            WriteClampedLimitsToEditor(pl1, pl2);
            StoreModeLimits(pl1, pl2);
            vm.SetTauForModeIndex(_selectedModeIndex, tau);
            StoreCpuBoost(vm);
            Draft.CpuBoostMode = vm.CpuBoostForModeIndex(_selectedModeIndex);
            Draft.CpuBoost = Draft.CpuBoostMode > 0;
            vm.StoreGpuOffsets(_selectedModeIndex, ReadGpuOffsets());
            if (updateActiveValues)
            {
                vm.Pl1Watts = pl1;
                vm.Pl2Watts = pl2;
            }
            return (pl1, pl2, tau);
        }

        private void StoreCpuBoost(MainViewModel vm)
        {
            int boost = Math.Clamp(DropdownCpuBoost?.SelectedIndex ?? 1, 0, 6);
            vm.SetCpuBoostForModeIndex(_selectedModeIndex, boost);
            Draft.CpuBoostMode = boost;
            Draft.CpuBoost = boost > 0;
        }

        private void CpuBoostDropdown_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_isUpdatingUi || DataContext is not MainViewModel vm)
                return;
            int boost = Math.Clamp(DropdownCpuBoost.SelectedIndex, 0, 6);
            vm.SetCpuBoostForModeIndex(_selectedModeIndex, boost);
            Draft.CpuBoostMode = boost;
            Draft.CpuBoost = boost > 0;
        }

        private static byte ModeIndexToByte(int modeIndex) => MainViewModel.ModeIndexToByte(modeIndex);
    }
}
