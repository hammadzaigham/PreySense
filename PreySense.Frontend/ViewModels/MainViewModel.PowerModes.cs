using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PreySense.Core.Driver;
using PreySense.Core.Services;
using PreySense.Frontend.Views;
using PreySense.Mode;

namespace PreySense.Frontend.ViewModels
{
    public partial class MainViewModel
    {
        private readonly Services.LatestBackgroundWorker<bool> _energySaverWorker =
            new(PowerLimitController.SetEnergySaverOnBattery, (_, latest) => latest);
        private const int ModeSwitchDebounceMilliseconds = 1000;
        private readonly object _modeSwitchDebounceLock = new();
        private readonly SemaphoreSlim _modeSwitchGate = new(1, 1);
        private CancellationTokenSource? _modeSwitchDebounceCts;
        private byte _pendingHardwareMode = 0xFF;

        // Power Profiles (0=Quiet, 1=Balanced, 4=Performance, 5=Turbo, 6=Eco)
        [ObservableProperty] private byte _activePowerMode = 0x01; // Balanced
        [ObservableProperty] private string _powerModeName = "Balanced";
        [ObservableProperty] private string _activeModeWattageInfo = "PL1: 55W | PL2: 115W (Factory EC)";

        public bool IsEcoActive => ActivePowerMode == 6;
        public bool IsQuietActive => ActivePowerMode == 0;
        public bool IsBalancedActive => ActivePowerMode == 1;
        public bool IsPerformActive => ActivePowerMode == 4;
        public bool IsTurboActive => ActivePowerMode == 5;
        public bool IsEcoOrQuietActive => IsAcConnected ? IsQuietActive : (IsEcoActive || IsQuietActive);
        public string EcoOrQuietModeName => Services.LocalizationService.Translate(IsAcConnected ? "Quiet" : "Eco");
        public string EcoOrQuietModeParam => IsAcConnected ? "0" : "6";

        public IReadOnlyList<string> SourceProfileNames => ProfileManager.SourceProfileNames;

        [ObservableProperty] private int _acPowerModeIndex = ProfileManager.SourceProfileIndex(0x01);
        [ObservableProperty] private int _batteryPowerModeIndex = ProfileManager.SourceProfileIndex(0x06);
        [ObservableProperty] private bool _isBatteryEnergySaverEnabled;

        private bool _suppressPowerProfile;

        // CPU Boost Mode — Windows PERFBOOSTMODE 0–6 (default Enabled)
        [ObservableProperty] private int _cpuBoostMode = 1;
        [ObservableProperty] private string _cpuBoostModeName = "Enabled";

        [RelayCommand(AllowConcurrentExecutions = true)]
        public async Task SetModeAsync(object? modeParam)
        {
            byte mode = modeParam switch
            {
                byte b => b,
                int i when i is >= 0 and <= 255 => (byte)i,
                string s when byte.TryParse(s, out byte parsed) => parsed,
                _ => 0xFF
            };
            if (!ProfileManager.IsSupportedMode(mode)) return;
            if (mode == PeekPendingOrActiveMode())
                return;

            RememberSourceProfile(mode);

            ActivePowerMode = mode;
            UpdateModeName(mode);
            NotifyModeSelectionsChanged();
            DiagnosticMessage = $"Switching to {GetModeTitle(mode)}...";

            CancellationToken debounceToken;
            lock (_modeSwitchDebounceLock)
            {
                _pendingHardwareMode = mode;
                _modeSwitchDebounceCts?.Cancel();
                _modeSwitchDebounceCts?.Dispose();
                _modeSwitchDebounceCts = new CancellationTokenSource();
                debounceToken = _modeSwitchDebounceCts.Token;
            }

            try
            {
                await Task.Delay(ModeSwitchDebounceMilliseconds, debounceToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await _modeSwitchGate.WaitAsync();
            try
            {
                if (debounceToken.IsCancellationRequested) return;
                byte toApply;
                lock (_modeSwitchDebounceLock)
                    toApply = _pendingHardwareMode;
                if (toApply == 0xFF)
                    return;
                await ApplyModeSwitchCoreAsync(toApply);
                lock (_modeSwitchDebounceLock)
                {
                    if (_pendingHardwareMode == toApply)
                    {
                        _pendingHardwareMode = 0xFF;
                        _modeSwitchDebounceCts?.Dispose();
                        _modeSwitchDebounceCts = null;
                    }
                }
            }
            finally
            {
                _modeSwitchGate.Release();
            }
        }

        internal bool IsModeSelectionPending()
        {
            lock (_modeSwitchDebounceLock)
                return _pendingHardwareMode != 0xFF;
        }

        internal byte PeekPendingOrActiveMode()
        {
            lock (_modeSwitchDebounceLock)
                return _pendingHardwareMode != 0xFF ? _pendingHardwareMode : ActivePowerMode;
        }

        /// <summary>
        /// Chassis / turbo key order on AC and battery:
        /// Eco → Quiet → Balanced → Performance → Turbo → Eco.
        /// </summary>
        internal byte CyclePerformanceMode(byte current) => current switch
        {
            0x06 => 0x00, // Eco → Quiet
            0x00 => 0x01, // Quiet → Balanced
            0x01 => 0x04, // Balanced → Performance
            0x04 => 0x05, // Performance → Turbo
            0x05 => 0x06, // Turbo → Eco
            _ => 0x01
        };

        internal async Task ApplyModeSwitchAsync(byte mode, bool applyCustomLimits = true)
        {
            CancelPendingModeSwitch();
            await _modeSwitchGate.WaitAsync();
            try { await ApplyModeSwitchCoreAsync(mode, applyCustomLimits); }
            finally { _modeSwitchGate.Release(); }
        }

        private void CancelPendingModeSwitch()
        {
            lock (_modeSwitchDebounceLock)
            {
                _modeSwitchDebounceCts?.Cancel();
                _modeSwitchDebounceCts?.Dispose();
                _modeSwitchDebounceCts = null;
                _pendingHardwareMode = 0xFF;
            }
        }

        private async Task ApplyModeSwitchCoreAsync(byte mode, bool applyCustomLimits = true)
        {
            if (!ProfileManager.IsSupportedMode(mode)) return;
            if (applyCustomLimits && IsHardwareTuningEnabled)
            {
                IsBusy = true;
                try
                {
                    var result = await Task.Run(() => ApplySavedDriverTuning(mode));
                    if (!result.Success) throw new InvalidOperationException(result.Status);
                    DiagnosticMessage = result.Status;
                }
                catch (Exception ex)
                {
                    DiagnosticMessage = $"Could not apply automatic tuning: {ex.Message}";
                    var actual = await Task.Run(() => _hw.TryGetPowerMode(out byte current) ? (byte?)current : null);
                    if (actual.HasValue) ActivePowerMode = actual.Value;
                    UpdateModeName(ActivePowerMode);
                    NotifyModeSelectionsChanged();
                    return;
                }
                finally { IsBusy = false; }
            }
            else
            {
                bool changed = await Task.Run(() => _hw.SetPowerMode(mode));
                if (!changed) { DiagnosticMessage = "Could not switch Acer mode."; return; }
            }
            ActivePowerMode = mode;
            if (!await ApplyCpuBoostForModeAsync(mode))
                DiagnosticMessage = "Mode selected, but CPU boost failed.";
            UpdateModeName(mode);
            NotifyModeSelectionsChanged();
            PersistSessionSettings();
        }

        internal void ApplyPowerProfileForSource(PowerSourceKind source)
        {
            if (source == PowerSourceKind.Unknown || _suppressPowerProfile || _isRestoringSession || _disposed)
                return;

            byte mode = ProfileManager.SourceProfileMode(ProfileIndexFor(source));
            if (PeekPendingOrActiveMode() == mode)
                return;

            CancelPendingModeSwitch();
            _ = ApplyModeSwitchAsync(mode);
        }

        private int ProfileIndexFor(PowerSourceKind source) => source switch
        {
            PowerSourceKind.Battery => ProfileManager.BatterySourceProfileIndexOrDefault(BatteryPowerModeIndex),
            _ => AcPowerModeIndex
        };

        private void RememberSourceProfile(byte mode)
        {
            if (_suppressPowerProfile)
                return;

            int index = ProfileManager.SourceProfileIndex(mode);
            _suppressPowerProfile = true;
            switch (_currentPowerSource)
            {
                case PowerSourceKind.Battery:
                    if (BatteryPowerModeIndex != index)
                        BatteryPowerModeIndex = index;
                    break;
                default:
                    if (AcPowerModeIndex != index)
                        AcPowerModeIndex = index;
                    break;
            }
            _suppressPowerProfile = false;
        }

        partial void OnAcPowerModeIndexChanged(int value)
        {
            if (_suppressPowerProfile || value < 0)
                return;

            PersistSessionSettings();
            if (_currentPowerSource == PowerSourceKind.Ac)
                ApplyPowerProfileForSource(_currentPowerSource);
        }


        partial void OnBatteryPowerModeIndexChanged(int value)
        {
            if (_suppressPowerProfile)
                return;

            if (value < 0 || value >= ProfileManager.SourceProfileNames.Length)
            {
                _suppressPowerProfile = true;
                BatteryPowerModeIndex = ProfileManager.SourceProfileIndex(ProfileManager.DefaultBatterySourceMode);
                _suppressPowerProfile = false;
                PersistSessionSettings();
                if (_currentPowerSource == PowerSourceKind.Battery)
                    ApplyPowerProfileForSource(PowerSourceKind.Battery);
                return;
            }

            PersistSessionSettings();
            if (_currentPowerSource == PowerSourceKind.Battery)
                ApplyPowerProfileForSource(PowerSourceKind.Battery);
        }

        partial void OnIsBatteryEnergySaverEnabledChanged(bool value)
        {
            if (_suppressPowerProfile)
                return;

            PersistSessionSettings();
            _ = _energySaverWorker.Submit(value);
        }

        private void NotifyModeSelectionsChanged()
        {
            OnPropertyChanged(nameof(IsEcoActive));
            OnPropertyChanged(nameof(IsQuietActive));
            OnPropertyChanged(nameof(IsBalancedActive));
            OnPropertyChanged(nameof(IsPerformActive));
            OnPropertyChanged(nameof(IsTurboActive));
            OnPropertyChanged(nameof(IsEcoOrQuietActive));
            OnPropertyChanged(nameof(EcoOrQuietModeName));
            OnPropertyChanged(nameof(EcoOrQuietModeParam));
        }

        private void UpdateModeName(byte mode)
        {
            PowerModeName = GetModeTitle(mode);
            ActiveModeWattageInfo = "N/A";
        }

        [RelayCommand]
        public async Task SetCpuBoostAsync(string boostModeStr)
        {
            if (!int.TryParse(boostModeStr, out int boostVal))
                return;
            byte mode = ActivePowerMode;
            int index = mode switch { 0x06 => 0, 0x00 => 1, 0x04 => 3, 0x05 => 4, _ => 2 };
            int boost = Math.Clamp(boostVal, 0, 6);
            SetCpuBoostForModeIndex(index, boost);
            try
            {
                TuningStore.Update(cfg =>
                {
                    var profile = mode switch
                    {
                        0 => cfg.Tuning.Quiet,
                        1 => cfg.Tuning.Balanced,
                        4 => cfg.Tuning.Performance,
                        5 => cfg.Tuning.Turbo,
                        6 => cfg.Tuning.Eco,
                        _ => cfg.Tuning.Balanced
                    };
                    profile.CpuBoostMode = boost;
                    profile.CpuBoost = boost > 0;
                });
            }
            catch { }
            await _modeSwitchGate.WaitAsync();
            try
            {
                if (!_disposed && ActivePowerMode == mode)
                    await ApplyCpuBoostForModeAsync(mode);
            }
            finally { _modeSwitchGate.Release(); }
        }

        internal static string GetCpuBoostModeName(int boostMode) =>
            CpuBoostModeNames[Math.Clamp(boostMode, 0, CpuBoostModeNames.Length - 1)];

        partial void OnCpuBoostModeChanged(int value)
        {
            int boost = Math.Clamp(value, 0, 6);
            CpuBoostModeName = GetCpuBoostModeName(boost);

        }
    }
}
