using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PreySense.Core.Services;
using PreySense.Frontend.Services;

namespace PreySense.Frontend.ViewModels
{
    public partial class MainViewModel
    {
        // Acer OEM Services Debloater
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsAcerServicesEnabled))]
        private bool _isAcerServicesDebloated = false;
        [ObservableProperty] private string _debloaterStatusText = "Acer OEM Services: Active";
        public bool IsAcerServicesEnabled => !IsAcerServicesDebloated;

        // Battery Care Limit
        [ObservableProperty] private bool _isBatteryLimitEnabled = true;
        [ObservableProperty] private int _batteryLimitPercent = 80;
        [ObservableProperty] private bool _isGpuBatteryAutoEnabled = false;
        [ObservableProperty] private bool _isExtendedCpuBoostEnabled = false;

        public ObservableCollection<string> CpuBoostModes { get; } = new()
        {
            "Disabled",
            "Enabled",
            "Aggressive",
            "Efficient Enabled",
            "Efficient Aggressive",
            "Aggressive at Guaranteed",
            "Efficient Aggressive at Guaranteed"
        };

        // ACPI Platform Features
        [ObservableProperty] private bool _isRunOnStartupEnabled = false;
        [ObservableProperty] private bool _isBootSoundEnabled = false;

        [NotifyPropertyChangedFor(nameof(UsbChargingDisplay))]
        [NotifyPropertyChangedFor(nameof(IsUsbChargingEnabled))]
        [ObservableProperty] private int _usbChargingPercent = 0; // 0 (Off), 10, 20, 30
        public bool IsUsbChargingEnabled => UsbChargingPercent > 0;
        public string UsbChargingDisplay => UsbChargingPercent == 0
            ? LocalizationService.Translate("Disabled")
            : LocalizationService.Format("{0}% Battery Limit", UsbChargingPercent);

        private readonly UiDebouncer _usbChargingDebouncer = new(TimeSpan.FromMilliseconds(200));

        partial void OnUsbChargingPercentChanged(int value)
        {
            _usbChargingDebouncer.Debounce(() =>
            {
                int target = UsbChargingPercent;
                _ = Task.Run(() => _hw.SetUsbChargingLevel(target));
                StatusMessage = target > 0
                    ? $"USB Power-Off Charging: Cutoff at {target}% Battery"
                    : "USB Power-Off Charging: Disabled";
            });
        }

        [RelayCommand]
        public async Task ToggleAcerServicesDebloaterAsync()
        {
            IsBusy = true;
            StatusMessage = IsAcerServicesDebloated ? "Restoring Acer Services..." : "Debloating Acer OEM Services...";
            try
            {
                if (IsAcerServicesDebloated)
                {
                    await Task.Run(() => PreySense.Core.Services.AcerServiceDebloater.EnableAndStartAll());
                    IsAcerServicesDebloated = false;
                    DebloaterStatusText = "Acer OEM Services: Active";
                    StatusMessage = "Acer Services Restored";
                }
                else
                {
                    await Task.Run(() => PreySense.Core.Services.AcerServiceDebloater.StopAndDisableAll());
                    IsAcerServicesDebloated = true;
                    DebloaterStatusText = "Acer OEM Services: Stopped & Disabled (~400MB Saved)";
                    StatusMessage = "Acer Services Debloated";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public void ToggleRunOnStartup()
        {
            try
            {
                bool success = StartupTaskService.SetStartupEnabled(IsRunOnStartupEnabled);
                if (success)
                {
                    StatusMessage = IsRunOnStartupEnabled ? "Run on Startup: Enabled (Elevated Task)" : "Run on Startup: Disabled";
                }
                else
                {
                    IsRunOnStartupEnabled = StartupTaskService.IsStartupEnabled();
                    StatusMessage = "Startup config failed: check permissions";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Startup config failed: {ex.Message}";
            }
        }

        [RelayCommand]
        public async Task ToggleBootSoundAsync()
        {
            IsBootSoundEnabled = !IsBootSoundEnabled;
            await Task.Run(() => _hw.SetBootSound(IsBootSoundEnabled));
            StatusMessage = IsBootSoundEnabled ? "Predator Boot Sound & Animation: Enabled" : "Predator Boot Sound: Muted";
        }

        [RelayCommand]
        public async Task ToggleUsbChargingAsync()
        {
            int target = UsbChargingPercent > 0 ? 0 : 10;
            UsbChargingPercent = target;
            await Task.Run(() => _hw.SetUsbChargingLevel(target));
            StatusMessage = target > 0 
                ? $"USB Power-Off Charging Enabled: Cutoff at {target}% Battery" 
                : "USB Power-Off Charging: Disabled";
        }

        [RelayCommand]
        public async Task SetUsbChargingPercentAsync(object? param)
        {
            int val = param switch
            {
                int i => i,
                double d => (int)Math.Round(d),
                string s when int.TryParse(s, out int parsed) => parsed,
                _ => 0
            };

            int snapped = val switch
            {
                >= 25 => 30,
                >= 15 => 20,
                >= 5 => 10,
                _ => 0
            };

            UsbChargingPercent = snapped;
            await Task.Run(() => _hw.SetUsbChargingLevel(snapped));
            StatusMessage = snapped > 0 
                ? $"USB Power-Off Charging: Cutoff at {snapped}% Battery" 
                : "USB Power-Off Charging: Disabled";
        }

        [RelayCommand]
        public async Task ToggleBatteryLimitAsync()
        {
            IsBatteryLimitEnabled = !IsBatteryLimitEnabled;
            int limitMode = IsBatteryLimitEnabled ? 1 : 0;
            BatteryLimitPercent = IsBatteryLimitEnabled ? 80 : 100;
            await Task.Run(() => _hw.SetBatteryChargeLimit(limitMode));
            StatusMessage = IsBatteryLimitEnabled ? "Battery Care Limit Active (80%)" : "Battery Charge Limit Disabled (100%)";
        }

        [RelayCommand]
        public async Task SetBatteryLimitPercentAsync(object? param)
        {
            int percent = param switch
            {
                int i => i,
                double d => (int)Math.Round(d),
                string s when int.TryParse(s, out int parsed) => parsed,
                _ => 80
            };

            BatteryLimitPercent = percent;
            bool enable = percent <= 80;
            IsBatteryLimitEnabled = enable;
            await Task.Run(() => _hw.SetBatteryChargeLimit(enable ? 1 : 0));
            StatusMessage = enable ? $"Battery Care Limit: {percent}% (Preserving Health)" : "Battery Charge Limit: 100% (Full Capacity)";
        }

    }
}
