using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

        // Platform Features
        [ObservableProperty] private bool _isRunOnStartupEnabled = false;
        [ObservableProperty] private bool _isBootSoundEnabled = false;

        [RelayCommand]
        public async Task ToggleAcerServicesDebloaterAsync()
        {
            IsBusy = true;
            DiagnosticMessage = IsAcerServicesDebloated ? "Restoring Acer Services..." : "Debloating Acer OEM Services...";
            try
            {
                if (IsAcerServicesDebloated)
                {
                    await Task.Run(() => PreySense.Core.Services.AcerServiceDebloater.EnableAndStartAll());
                    IsAcerServicesDebloated = false;
                    DebloaterStatusText = "Acer OEM Services: Active";
                    DiagnosticMessage = "Acer Services Restored";
                }
                else
                {
                    await Task.Run(() => PreySense.Core.Services.AcerServiceDebloater.StopAndDisableAll());
                    IsAcerServicesDebloated = true;
                    DebloaterStatusText = "Acer OEM Services: Stopped & Disabled (~400MB Saved)";
                    DiagnosticMessage = "Acer Services Debloated";
                }
            }
            catch (Exception ex)
            {
                DiagnosticMessage = $"Error: {ex.Message}";
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
                    DiagnosticMessage = IsRunOnStartupEnabled ? "Run on Startup: Enabled (Elevated Task)" : "Run on Startup: Disabled";
                }
                else
                {
                    IsRunOnStartupEnabled = StartupTaskService.IsStartupEnabled();
                    DiagnosticMessage = "Startup config failed: check permissions";
                }
            }
            catch (Exception ex)
            {
                DiagnosticMessage = $"Startup config failed: {ex.Message}";
            }
        }

        [RelayCommand]
        public async Task ToggleBootSoundAsync()
        {
            IsBootSoundEnabled = !IsBootSoundEnabled;
            await Task.Run(() => _hw.SetBootSound(IsBootSoundEnabled));
            DiagnosticMessage = IsBootSoundEnabled ? "Predator Boot Sound & Animation: Enabled" : "Predator Boot Sound: Muted";
        }

        [RelayCommand]
        public async Task ToggleBatteryLimitAsync()
        {
            IsBatteryLimitEnabled = !IsBatteryLimitEnabled;
            int limitMode = IsBatteryLimitEnabled ? 1 : 0;
            BatteryLimitPercent = IsBatteryLimitEnabled ? 80 : 100;
            await Task.Run(() => _hw.SetBatteryChargeLimit(limitMode));
            DiagnosticMessage = IsBatteryLimitEnabled ? "Battery Care Limit Active (80%)" : "Battery Charge Limit Disabled (100%)";
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
            DiagnosticMessage = enable ? $"Battery Care Limit: {percent}% (Preserving Health)" : "Battery Charge Limit: 100% (Full Capacity)";
        }

    }
}
