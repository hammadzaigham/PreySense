using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PreySense.Frontend.Views
{
    public partial class SettingsWindow : Window
    {
        private bool _openingTuningSettings;

        public SettingsWindow()
        {
            InitializeComponent();
            Loaded += async (_, _) =>
            {
                if (DataContext is ViewModels.MainViewModel vm)
                    await vm.RefreshStartupPreferenceAsync();
            };
            IsVisibleChanged += async (_, _) =>
            {
                if (!IsVisible) return;
                UpdateUsbChargingLabel();
                LanguageSelector.SelectedIndex = Services.LocalizationService.LanguageIndex;
                if (DataContext is ViewModels.MainViewModel vm)
                    await vm.RefreshStartupPreferenceAsync();
            };
            UpdateUsbChargingLabel();
            LanguageSelector.SelectedIndex = Services.LocalizationService.LanguageIndex;
            MaxHeight = SystemParameters.WorkArea.Height - 40;
            SettingsScroll.MaxHeight = Math.Max(240, MaxHeight - 56);
        }

        private async void BtnOpenTuningSettings_Click(object sender, RoutedEventArgs e)
        {
            if (_openingTuningSettings || DataContext is not ViewModels.MainViewModel vm || !vm.IsHardwareTuningEnabled) return;
            _openingTuningSettings = true;
            try
            {
                await vm.RefreshProchotLockStateAsync();
                if (!IsLoaded || !IsVisible || !vm.IsHardwareTuningEnabled) return;
                bool previousSuppression = App.SuppressAutoHide;
                App.SuppressAutoHide = true;
                try
                {
                    new TuningSettingsWindow { Owner = this, DataContext = vm }.ShowDialog();
                }
                finally { App.SuppressAutoHide = previousSuppression; }
            }
            catch (System.Exception ex) { PreySense.AppLogger.Error($"Could not open tuning settings: {ex}"); }
            finally { _openingTuningSettings = false; }
        }

        private void BtnOpenLightingModeSettings_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ViewModels.MainViewModel vm) return;
            bool previousSuppression = App.SuppressAutoHide;
            App.SuppressAutoHide = true;
            try
            {
                new LightingSettingsWindow { Owner = this, DataContext = vm }.ShowDialog();
            }
            finally { App.SuppressAutoHide = previousSuppression; }
        }

        private void BtnOpenFanSettings_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ViewModels.MainViewModel vm) return;
            bool previousSuppression = App.SuppressAutoHide;
            App.SuppressAutoHide = true;
            try
            {
                new FanSettingsWindow { Owner = this, DataContext = vm }.ShowDialog();
            }
            finally { App.SuppressAutoHide = previousSuppression; }
        }

        private void UsbCharging_DraftValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateUsbChargingLabel(e.NewValue);
        }

        private async void UsbCharging_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateUsbChargingLabel();
            if (IsLoaded)
                await CommitUsbChargingSafelyAsync();
        }

        private void UpdateUsbChargingLabel() => UpdateUsbChargingLabel(UsbChargingSlider.Value);

        private void UpdateUsbChargingLabel(double value)
        {
            UsbChargingSlider.ValueText = value == 0
                ? Services.LocalizationService.Translate("Disabled") : $"{value:0}%";
        }

        private async System.Threading.Tasks.Task CommitUsbChargingSafelyAsync()
        {
            try
            {
                await CommitUsbChargingAsync();
            }
            catch (System.Exception ex)
            {
                PreySense.AppLogger.Error($"Could not apply USB charging cutoff: {ex}");
            }
        }

        private async System.Threading.Tasks.Task CommitUsbChargingAsync()
        {
            if (DataContext is ViewModels.MainViewModel vm)
                await vm.SetUsbChargingAsync((int)UsbChargingSlider.Value);
        }

        private void LanguageSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LanguageSelector.SelectedItem is ComboBoxItem item && item.Tag is string language)
            {
                Services.LocalizationService.SetLanguage(language);
                UpdateUsbChargingLabel();
            }
        }

    }
}
