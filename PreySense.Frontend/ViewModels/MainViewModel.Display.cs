using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PreySense.Frontend.ViewModels
{
    public partial class MainViewModel
    {
        // Display & Vision
        [ObservableProperty] private int _currentRefreshRate = 240;
        [ObservableProperty] private string _displayPanelInfo = "240Hz";
        [ObservableProperty] private bool _isAutoRefreshRateEnabled = false;
        [ObservableProperty] private bool _isBlueLightShieldEnabled = false;
        [ObservableProperty] private int _blueLightShieldMode = 0;
        [ObservableProperty] private bool _is60HzSupported = false;
        [ObservableProperty] private bool _is120HzSupported = false;
        [ObservableProperty] private bool _is240HzSupported = true;
        [ObservableProperty] private bool _isAutoRefreshRateVisible = true;
        [ObservableProperty] private bool _isLcdOverdriveEnabled = false;

        public ObservableCollection<string> AvailableRefreshRates { get; } = new();
        [ObservableProperty] private string? _selectedRefreshRateOption;
        private bool _isUpdatingRefreshRateSelection = false;

        public ObservableCollection<string> BlueLightShieldModes { get; } = new()
        {
            "Off",
            "Low (18% Reduction)",
            "High (36% Reduction)"
        };

        public bool Is240HzActive => CurrentRefreshRate == 240;
        public bool Is120HzActive => CurrentRefreshRate == 120;
        public bool Is60HzActive => CurrentRefreshRate == 60;

        private void NotifyRefreshRateChanged()
        {
            OnPropertyChanged(nameof(Is240HzActive));
            OnPropertyChanged(nameof(Is120HzActive));
            OnPropertyChanged(nameof(Is60HzActive));
        }

        private void InitializeRefreshRates()
        {
            System.Collections.Generic.List<int>? supportedRates = null;
            int hwRate = 0;
            try
            {
                supportedRates = _hw.GetSupportedRefreshRates();
                hwRate = _hw.GetDisplayRefreshRate();
            }
            catch { }

            ApplyRefreshRateOptions(supportedRates, hwRate);
        }

        private void UpdateLiveRefreshRate(int hz)
        {
            _isUpdatingRefreshRateSelection = true;
            try
            {
                CurrentRefreshRate = hz;
                string option = $"{hz} Hz";
                if (!AvailableRefreshRates.Contains(option)) AvailableRefreshRates.Add(option);
                SelectedRefreshRateOption = option;
                DisplayPanelInfo = $"{hz}Hz" + (IsLcdOverdriveEnabled ? " • Overdrive" : "");
                NotifyRefreshRateChanged();
            }
            finally { _isUpdatingRefreshRateSelection = false; }
        }

        private void ApplyRefreshRateOptions(System.Collections.Generic.List<int>? supportedRates, int hwRate)
        {
            try
            {
                if (supportedRates == null || supportedRates.Count == 0)
                    supportedRates = new System.Collections.Generic.List<int> { 60, 120, 240 };

                _isUpdatingRefreshRateSelection = true;
                if (hwRate > 0)
                    CurrentRefreshRate = hwRate;

                var orderedRates = supportedRates.Distinct().ToList();
                if (CurrentRefreshRate > 0 && !orderedRates.Contains(CurrentRefreshRate))
                    orderedRates.Add(CurrentRefreshRate);
                string[] nextRates = orderedRates.OrderByDescending(r => r).Select(r => $"{r} Hz").ToArray();
                if (!AvailableRefreshRates.SequenceEqual(nextRates))
                {
                    AvailableRefreshRates.Clear();
                    foreach (string rate in nextRates)
                        AvailableRefreshRates.Add(rate);
                }

                Is60HzSupported = supportedRates.Contains(60);
                Is120HzSupported = supportedRates.Contains(120);
                Is240HzSupported = supportedRates.Contains(240);

                SelectedRefreshRateOption = $"{CurrentRefreshRate} Hz";

                DisplayPanelInfo = $"{CurrentRefreshRate}Hz" + (IsLcdOverdriveEnabled ? " • Overdrive" : "");
                NotifyRefreshRateChanged();
            }
            finally
            {
                _isUpdatingRefreshRateSelection = false;
            }
        }

        partial void OnSelectedRefreshRateOptionChanged(string? value)
        {
            if (_isUpdatingRefreshRateSelection || string.IsNullOrWhiteSpace(value)) return;
            string numStr = value.Replace("Hz", "").Trim();
            if (int.TryParse(numStr, out int hz) && hz > 0 && hz != CurrentRefreshRate)
            {
                _ = SetRefreshRateAsync(hz);
            }
        }

        partial void OnBlueLightShieldModeChanged(int value)
        {
            int clamped = Math.Clamp(value, 0, 2);
            if (clamped != value)
            {
                BlueLightShieldMode = clamped;
                return;
            }

            IsBlueLightShieldEnabled = clamped > 0;
            _hw.SetBlueLightShield(clamped);
            StatusMessage = clamped switch
            {
                1 => "Acer BlueLight Shield: Low (18% Reduction)",
                2 => "Acer BlueLight Shield: High (36% Reduction)",
                _ => "Acer BlueLight Shield: Off"
            };

            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"SOFTWARE\PreySense\AdvancedSettings");
                key?.SetValue("BlueLightShield", clamped);
            }
            catch { }
        }

        [RelayCommand]
        public async Task SetRefreshRateAsync(object? hzParam)
        {
            int hz = hzParam switch
            {
                int i => i,
                string s when int.TryParse(s.Replace("Hz", "").Trim(), out int parsed) => parsed,
                _ => 0
            };
            if (hz <= 0) return;
            try
            {
                using var hardwareLease = await PreySense.Core.Services.HardwareOperationGate.EnterDisplaySwitchAsync();
                if (hz == CurrentRefreshRate) { UpdateLiveRefreshRate(CurrentRefreshRate); return; }
                var result = await Task.Run(() =>
                {
                    bool applied = _hw.SetDisplayRefreshRate(hz);
                    int actual = _hw.GetDisplayRefreshRate();
                    return (applied, actual);
                });
                if (_disposed) return;
                UpdateLiveRefreshRate(result.actual > 0 ? result.actual : CurrentRefreshRate);
                StatusMessage = !result.applied
                    ? $"Display rejected {hz}Hz; current rate is {CurrentRefreshRate}Hz."
                    : result.actual != hz
                        ? $"Requested {hz}Hz was not confirmed; current rate is {CurrentRefreshRate}Hz."
                        : $"Display: {hz}Hz";
            }
            catch (Exception ex)
            {
                if (!_disposed) UpdateLiveRefreshRate(CurrentRefreshRate);
                StatusMessage = $"Refresh rate change failed: {ex.Message}";
            }
        }

        [RelayCommand]
        public void ToggleAutoRefreshRate()
        {
            IsAutoRefreshRateEnabled = !IsAutoRefreshRateEnabled;
            PersistSessionSettings();
            StatusMessage = IsAutoRefreshRateEnabled
                ? "Auto Refresh Rate: 60 Hz on battery, highest rate when plugged in"
                : "Auto Refresh Rate: Disabled";
        }

        internal void ApplyAutoRefreshForPower(bool onExternalPower)
        {
            if (!IsAutoRefreshRateEnabled)
                return;

            int targetHz = onExternalPower ? HighestRefreshRate() : 60;
            _ = SetRefreshRateAsync(targetHz);
        }

        private int HighestRefreshRate()
        {
            return AvailableRefreshRates
                .Select(r => int.TryParse(r.Replace("Hz", "", StringComparison.OrdinalIgnoreCase).Trim(), out int val) ? val : 0)
                .DefaultIfEmpty(240)
                .Max();
        }

        [RelayCommand]
        public async Task ToggleBlueLightShieldAsync()
        {
            BlueLightShieldMode = BlueLightShieldMode == 0 ? 1 : 0;
            await Task.CompletedTask;
        }

        [RelayCommand]
        public async Task ToggleLcdOverdriveAsync()
        {
            bool requestedState = !IsLcdOverdriveEnabled;
            bool applied = await Task.Run(() => _hw.SetLcdOverdrive(requestedState));
            if (!applied)
            {
                StatusMessage = "LCD overdrive command was rejected by the firmware.";
                return;
            }

            IsLcdOverdriveEnabled = requestedState;
            DisplayPanelInfo = $"{CurrentRefreshRate}Hz" + (requestedState ? " • Overdrive" : "");
            StatusMessage = requestedState ? "LCD 3ms Response Overdrive: Enabled" : "LCD Response Overdrive: Off";
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"SOFTWARE\PreySense\AdvancedSettings");
                key?.SetValue("LcdOverdrive", requestedState ? 1 : 0, Microsoft.Win32.RegistryValueKind.DWord);
            }
            catch { }
        }
    }
}
