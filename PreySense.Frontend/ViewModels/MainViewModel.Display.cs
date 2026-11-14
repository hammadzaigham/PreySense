using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PreySense.Gpu;

namespace PreySense.Frontend.ViewModels
{
    public partial class MainViewModel
    {
        // Display & Vision
        [ObservableProperty] private int _currentRefreshRate;
        [ObservableProperty] private bool _isAutoRefreshRateEnabled = false;
        [ObservableProperty] private bool _is60HzSupported = false;
        [ObservableProperty] private bool _is120HzSupported = false;
        [ObservableProperty] private bool _is240HzSupported;
        [ObservableProperty] private bool _isAutoRefreshRateVisible = true;

        public ObservableCollection<string> AvailableRefreshRates { get; } = new();
        [ObservableProperty] private string? _selectedRefreshRateOption;
        private bool _isUpdatingRefreshRateSelection = false;

        private readonly object _displaySettingsDebounceLock = new();
        private CancellationTokenSource? _displaySettingsDebounceCts;


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

        private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            if (_disposed) return;
            TriggerDisplayStateRefresh();
        }

        public void TriggerDisplayStateRefresh()
        {
            if (_disposed) return;
            var lockObj = _displaySettingsDebounceLock ?? this;
            lock (lockObj)
            {
                _displaySettingsDebounceCts?.Cancel();
                _displaySettingsDebounceCts?.Dispose();
                _displaySettingsDebounceCts = new CancellationTokenSource();
                var token = _displaySettingsDebounceCts.Token;

                Task.Delay(300, token).ContinueWith(async t =>
                {
                    if (t.IsCanceled || _disposed) return;
                    await RefreshDisplayAndDdsStateAsync();
                }, TaskScheduler.Default);
            }
        }

        private void DisposeDisplaySettingsWatcher()
        {
            var lockObj = _displaySettingsDebounceLock ?? this;
            lock (lockObj)
            {
                _displaySettingsDebounceCts?.Cancel();
                _displaySettingsDebounceCts?.Dispose();
                _displaySettingsDebounceCts = null;
            }
        }

        public async Task RefreshDisplayAndDdsStateAsync()
        {
            if (_disposed || Volatile.Read(ref _gpuSwitchInProgress) != 0) return;

            int revision = Volatile.Read(ref _gpuDisplayRevision);

            var result = await Task.Run(() =>
            {
                bool biosDiscrete = false;
                try { biosDiscrete = _hw.IsBiosDiscreteGpuActive; } catch { }

                DdsStatus? dds = null;
                if (!biosDiscrete && _hw.IsDdsAvailable)
                {
                    try { dds = _hw.GetDdsStatus(); }
                    catch (Exception ex) { AppLogger.Error($"GPU display status read failed: {ex.Message}"); }
                }

                System.Collections.Generic.List<int>? supportedRates = null;
                int currentRate = 0;
                try
                {
                    supportedRates = _hw.GetSupportedRefreshRates();
                    currentRate = _hw.GetDisplayRefreshRate();
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"Display refresh rate read failed: {ex.Message}");
                }

                return (biosDiscrete, dds, supportedRates, currentRate);
            });

            if (_disposed || Volatile.Read(ref _gpuSwitchInProgress) != 0 ||
                revision != Volatile.Read(ref _gpuDisplayRevision))
                return;

            if (_dispatcher == null || _dispatcher.CheckAccess())
            {
                ApplyDisplayAndDdsState(result, revision);
            }
            else
            {
                _ = _dispatcher.InvokeAsync(() => ApplyDisplayAndDdsState(result, revision));
            }
        }

        private void ApplyDisplayAndDdsState(
            (bool biosDiscrete, DdsStatus? dds, System.Collections.Generic.List<int>? supportedRates, int currentRate) result,
            int revision)
        {
            if (_disposed || Volatile.Read(ref _gpuSwitchInProgress) != 0 ||
                revision != Volatile.Read(ref _gpuDisplayRevision))
                return;

            IsBiosDiscreteGpuActive = result.biosDiscrete;
            if (IsBiosDiscreteGpuActive)
                GpuMuxModeName = "dGPU Only";

            if (result.dds != null)
            {
                ApplyDdsStatus(result.dds);
            }
            NotifyGpuSelectionsChanged();

            if (result.supportedRates != null && result.supportedRates.Count > 0)
            {
                ApplyRefreshRateOptions(result.supportedRates, result.currentRate);
            }
            else
            {
                ApplyRefreshRateOptions(result.supportedRates, result.currentRate);
            }
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
                NotifyRefreshRateChanged();
            }
            finally { _isUpdatingRefreshRateSelection = false; }
        }

        private void ApplyRefreshRateOptions(System.Collections.Generic.List<int>? supportedRates, int hwRate)
        {
            try
            {
                if (supportedRates == null || supportedRates.Count == 0)
                    supportedRates = new System.Collections.Generic.List<int>();

                _isUpdatingRefreshRateSelection = true;
                CurrentRefreshRate = Math.Max(0, hwRate);

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

                SelectedRefreshRateOption = CurrentRefreshRate > 0 ? $"{CurrentRefreshRate} Hz" : "N/A";

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
            if (int.TryParse(numStr, out int hz) && hz > 0)
            {
                _ = SetRefreshRateAsync(hz);
            }
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
                var result = await Task.Run(() =>
                {
                    if (_hw.GetDisplayRefreshRate() == hz) return (applied: true, actual: hz);
                    bool applied = _hw.SetDisplayRefreshRate(hz);
                    int actual = _hw.GetDisplayRefreshRate();
                    return (applied, actual);
                });
                if (_disposed) return;
                UpdateLiveRefreshRate(result.actual > 0 ? result.actual : CurrentRefreshRate);
                DiagnosticMessage = !result.applied
                    ? $"Display rejected {hz}Hz. Still at {CurrentRefreshRate}Hz."
                    : result.actual != hz
                        ? $"{hz}Hz was not confirmed. Still at {CurrentRefreshRate}Hz."
                        : $"Display: {hz}Hz";
            }
            catch (Exception ex)
            {
                if (!_disposed) UpdateLiveRefreshRate(CurrentRefreshRate);
                DiagnosticMessage = $"Could not change refresh rate: {ex.Message}";
            }
        }

        [RelayCommand]
        public void ToggleAutoRefreshRate()
        {
            IsAutoRefreshRateEnabled = !IsAutoRefreshRateEnabled;
            PersistSessionSettings();
            DiagnosticMessage = IsAutoRefreshRateEnabled
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


    }
}
