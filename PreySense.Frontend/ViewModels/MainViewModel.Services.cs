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

        // Platform Features
        [ObservableProperty] private bool _isRunOnStartupEnabled = false;
        [ObservableProperty] private bool _isBootSoundEnabled = false;

        private int _startupPreferenceRevision;

        private async Task InitializeStartupPreferenceAsync()
        {
            try
            {
                int revision = _startupPreferenceRevision;
                bool enabled = await Task.Run(StartupTaskService.IsStartupEnabled);
                if (!_disposed && revision == _startupPreferenceRevision) IsRunOnStartupEnabled = enabled;
            }
            catch (Exception ex) { AppLogger.Error($"Startup preference read failed: {ex}"); }
        }

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
        public async Task ToggleRunOnStartupAsync()
        {
            if (_disposed) return;
            _startupPreferenceRevision++;
            try
            {
                bool enable = IsRunOnStartupEnabled;
                var result = await Task.Run(() =>
                {
                    bool success = StartupTaskService.SetStartupEnabled(enable);
                    return (success, enabled: success ? enable : StartupTaskService.IsStartupEnabled());
                });
                if (_disposed) return;
                bool success = result.success;
                if (success)
                {
                    DiagnosticMessage = enable ? "Run on Startup: Enabled (Elevated Task)" : "Run on Startup: Disabled";
                }
                else
                {
                    IsRunOnStartupEnabled = result.enabled;
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
            if (_disposed) return;
            bool enabled = !IsBootSoundEnabled;
            try
            {
                await Task.Run(() => _hw.SetBootSound(enabled));
                if (_disposed) return;
                IsBootSoundEnabled = enabled;
                DiagnosticMessage = enabled ? "Predator Boot Sound & Animation: Enabled" : "Predator Boot Sound: Muted";
            }
            catch (Exception ex) { DiagnosticMessage = $"Boot sound update failed: {ex.Message}"; }
        }

        [RelayCommand]
        public async Task ToggleBatteryLimitAsync()
        {
            if (_disposed) return;
            bool enabled = !IsBatteryLimitEnabled;
            try
            {
                await Task.Run(() => _hw.SetBatteryChargeLimit(enabled ? 1 : 0));
                if (_disposed) return;
                IsBatteryLimitEnabled = enabled;
                BatteryLimitPercent = enabled ? 80 : 100;
                DiagnosticMessage = enabled ? "Battery Care Limit Active (80%)" : "Battery Charge Limit Disabled (100%)";
            }
            catch (Exception ex) { DiagnosticMessage = $"Battery limit update failed: {ex.Message}"; }
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

            if (_disposed) return;
            bool enable = percent <= 80;
            try
            {
                await Task.Run(() => _hw.SetBatteryChargeLimit(enable ? 1 : 0));
                if (_disposed) return;
                BatteryLimitPercent = percent;
                IsBatteryLimitEnabled = enable;
                DiagnosticMessage = enable ? $"Battery Care Limit: {percent}% (Preserving Health)" : "Battery Charge Limit: 100% (Full Capacity)";
            }
            catch (Exception ex) { DiagnosticMessage = $"Battery limit update failed: {ex.Message}"; }
        }

    }
}
