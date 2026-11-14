using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PreySense.Frontend.Views
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            Loaded += async (_, _) =>
            {
                if (DataContext is ViewModels.MainViewModel vm) await vm.RefreshStartupPreferenceAsync();
            };
            UpdateUsbChargingLabel();
            LanguageSelector.SelectedIndex = Services.LocalizationService.LanguageIndex;
            MaxHeight = SystemParameters.WorkArea.Height - 40;
                SettingsScroll.MaxHeight = Math.Max(240, MaxHeight - 56);
        }

        private async void BtnOpenTuningSettings_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ViewModels.MainViewModel vm || !vm.IsHardwareTuningEnabled) return;
            await vm.RefreshProchotLockStateAsync();
            if (!vm.IsHardwareTuningEnabled) return;
            new TuningSettingsWindow { Owner = this, DataContext = vm }.ShowDialog();
        }

        private void UsbCharging_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateUsbChargingLabel();
        }

        private void UpdateUsbChargingLabel()
        {
            UsbChargingSlider.ValueText = UsbChargingSlider.Value == 0
                ? Services.LocalizationService.Translate("Disabled") : $"{UsbChargingSlider.Value:0}%";
        }

        private async void UsbCharging_MouseReleased(object sender, MouseButtonEventArgs e) => await CommitUsbChargingAsync();
        private async void UsbCharging_MouseCaptureLost(object sender, MouseEventArgs e) => await CommitUsbChargingAsync();
        private async void UsbCharging_KeyReleased(object sender, KeyEventArgs e) => await CommitUsbChargingAsync();

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
