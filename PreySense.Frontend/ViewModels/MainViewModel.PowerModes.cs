using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PreySense.Core.Acpi;
using PreySense.Core.Services;
using PreySense.Frontend.Views;
using PreySense.Mode;

namespace PreySense.Frontend.ViewModels
{
    public partial class MainViewModel
    {
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
        [ObservableProperty] private int _usbCPowerModeIndex = ProfileManager.SourceProfileIndex(0x01);
        [ObservableProperty] private int _batteryPowerModeIndex = ProfileManager.SourceProfileIndex(0x06);
        [ObservableProperty] private bool _isBatteryEnergySaverEnabled;

        private bool _suppressPowerProfile;

        // CPU Boost Mode (0=Disabled, 1=Enabled)
        [ObservableProperty] private int _cpuBoostMode = 1; // Enabled default
        [ObservableProperty] private string _cpuBoostModeName = "Enabled";

        [RelayCommand(AllowConcurrentExecutions = true)]
        public async Task SetModeAsync(object? modeParam)
        {
            if (IsFactoryCalibrationRunning || IsOpenCoreLoaded && !IsFactoryCalibrationComplete) return;
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
            StatusMessage = $"Switching to {GetModeTitle(mode)}...";

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

        internal byte CyclePerformanceMode(byte current)
        {
            if (!IsAcConnected)
            {
                return current == 0x06 ? (byte)0x01 : (byte)0x06;
            }

            return current switch
            {
                0x00 => 0x01,
                0x01 => 0x04,
                0x04 => 0x05,
                0x05 => 0x00,
                0x06 => 0x01,
                _ => 0x01
            };
        }

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
            if (IsFactoryCalibrationRunning || IsOpenCoreLoaded && !IsFactoryCalibrationComplete) return;
            if (!applyCustomLimits || !IsAcpiOverrideEnabled || !IsOpenCoreLoaded || !IsFactoryCalibrationComplete)
            {
                bool changed = await Task.Run(() => _hw.SetPowerMode(mode));
                if (!changed) { StatusMessage = "The requested Acer mode could not be activated."; return; }
                ActivePowerMode = mode;
                UpdateModeName(mode);
                NotifyModeSelectionsChanged();
                PersistSessionSettings();
                return;
            }
            ActivePowerMode = mode;
            UpdateModeName(mode);
            NotifyModeSelectionsChanged();
            StatusMessage = $"Switching to {GetModeTitle(mode)}...";

            int modePl1 = LimitPl1ToPlatform(mode switch
            {
                0x05 => CustomTurboPl1,
                0x04 => CustomPerfPl1,
                0x01 => CustomBalancedPl1,
                0x06 => CustomEcoPl1,
                _ => CustomQuietPl1
            });
            int modePl2 = LimitPl2(modePl1, mode switch
            {
                0x05 => CustomTurboPl2,
                0x04 => CustomPerfPl2,
                0x01 => CustomBalancedPl2,
                0x06 => CustomEcoPl2,
                _ => CustomQuietPl2
            });
            int modeGpu = mode switch
            {
                0x05 => CustomTurboGpuWatts,
                0x04 => CustomPerfGpuWatts,
                0x01 => CustomBalancedGpuWatts,
                0x06 => CustomEcoGpuWatts,
                _ => CustomQuietGpuWatts
            };
            int modeTrip = mode switch
            {
                0x05 => CustomTurboGpuTripPoint,
                0x04 => CustomPerfGpuTripPoint,
                0x01 => CustomBalancedGpuTripPoint,
                0x06 => CustomEcoGpuTripPoint,
                _ => CustomQuietGpuTripPoint
            };
            Pl1Watts = modePl1;
            Pl2Watts = modePl2;
            int modeTau = TauForMode(mode);
            TauSeconds = modeTau;
            GpuPowerTargetWatts = modeGpu;
            GpuThermalTripPointCelsius = modeTrip;
            ActiveGpuPowerCeiling = $"GPU Power Ceiling: {GpuPowerTargetWatts}W (ACPI Max: {GpuPowerTargetMax}W, Trip: {GpuThermalTripPointCelsius}°C)";

            IsBusy = true;
            bool cpuApplied = false;
            string cpuResult = string.Empty;
            try
            {
                await Task.Run(() =>
                {
                    if (_hw.CurrentPowerMode != mode)
                    {
                        if (!_hw.SetPowerMode(mode))
                            throw new InvalidOperationException("The requested Acer mode could not be activated.");
                    }
                    cpuApplied = PowerLimitController.SetCpuPowerLimits(modePl1, modePl2, true, modeTau);
                    cpuResult = PowerLimitController.LastCpuPowerLimitStatus;
                    if (!ApplyGpuModePower(mode, modeGpu))
                        throw new InvalidOperationException("GPU offsets could not be verified. Update PREYDEV to 0x0201 and reboot.");
                    if (!PreySense.Core.Hardware.PreySenseWmiDeviceClient.SetGpuThermalTripPoint((uint)modeTrip))
                        throw new InvalidOperationException("GPU thermal trip point could not be applied.");
                });
                ApplyCpuBoostForMode(mode);
                StatusMessage = cpuApplied ? $"{PowerModeName} Mode Activated (MSR + MMIO verified)" : $"{PowerModeName} Mode Activated; CPU limits partial: {cpuResult}";
                ModeToastWindow.ShowConfirmedMode(mode, GetModeTitle(mode));
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
                PersistSessionSettings();
            }
        }

        internal void ApplyPowerProfileForSource(PowerSourceKind source)
        {
            if (_suppressPowerProfile || _isRestoringSession || _disposed || IsFactoryCalibrationRunning
                || IsOpenCoreLoaded && !IsFactoryCalibrationComplete)
                return;

            byte mode = ProfileManager.SourceProfileMode(ProfileIndexFor(source));
            if (_appliedGameExe != null)
            {
                _modeBeforeGame = mode;
                return;
            }

            if (PeekPendingOrActiveMode() == mode)
                return;

            CancelPendingModeSwitch();
            _ = ApplyModeSwitchAsync(mode);
        }

        private int ProfileIndexFor(PowerSourceKind source) => source switch
        {
            PowerSourceKind.UsbC => UsbCPowerModeIndex,
            PowerSourceKind.Battery => BatteryPowerModeIndex,
            _ => AcPowerModeIndex
        };

        private void RememberSourceProfile(byte mode)
        {
            if (_suppressPowerProfile || _appliedGameExe != null)
                return;

            int index = ProfileManager.SourceProfileIndex(mode);
            _suppressPowerProfile = true;
            switch (_currentPowerSource)
            {
                case PowerSourceKind.UsbC:
                    if (UsbCPowerModeIndex != index)
                        UsbCPowerModeIndex = index;
                    break;
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
                ApplyPowerProfileForSource(PowerSourceKind.Ac);
        }

        partial void OnUsbCPowerModeIndexChanged(int value)
        {
            if (_suppressPowerProfile || value < 0)
                return;

            PersistSessionSettings();
            if (_currentPowerSource == PowerSourceKind.UsbC)
                ApplyPowerProfileForSource(PowerSourceKind.UsbC);
        }

        partial void OnBatteryPowerModeIndexChanged(int value)
        {
            if (_suppressPowerProfile || value < 0)
                return;

            PersistSessionSettings();
            if (_currentPowerSource == PowerSourceKind.Battery)
                ApplyPowerProfileForSource(PowerSourceKind.Battery);
        }

        partial void OnIsBatteryEnergySaverEnabledChanged(bool value)
        {
            if (_suppressPowerProfile)
                return;

            PersistSessionSettings();
            PowerLimitController.SetEnergySaverOnBattery(value);
        }

        partial void OnIsExtendedCpuBoostEnabledChanged(bool value)
        {
            PersistSessionSettings();
            OnPropertyChanged(nameof(CpuBoostEditorVisibility));
            OnPropertyChanged(nameof(ExtendedCpuBoostEditorVisibility));
        }

        public System.Windows.Visibility CpuBoostEditorVisibility =>
            IsExtendedCpuBoostEnabled ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

        public System.Windows.Visibility ExtendedCpuBoostEditorVisibility =>
            IsExtendedCpuBoostEnabled ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

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
            ActiveModeWattageInfo = mode switch
            {
                0x00 => "PL1: 45W | PL2: 140W (Quiet AC Profile)",
                0x01 => "PL1: 65W | PL2: 140W (Balanced AC Profile)",
                0x04 => "PL1: 75W | PL2: 140W (Performance AC Profile)",
                0x05 => "PL1: 85W | PL2: 140W (Turbo AC Profile)",
                _ => "PL1/PL2 Managed by EC Firmware"
            };
        }

        [RelayCommand]
        public void SetCpuBoost(string boostModeStr)
        {
            if (!int.TryParse(boostModeStr, out int boostVal))
                return;

            CpuBoostMode = Math.Clamp(boostVal, 0, 6);
        }

        internal static string GetCpuBoostModeName(int boostMode)
        {
            return boostMode switch
            {
                0 => "Disabled",
                2 => "Aggressive",
                3 => "Efficient Enabled",
                4 => "Efficient Aggressive",
                5 => "Aggressive at Guaranteed",
                6 => "Efficient Aggressive at Guaranteed",
                _ => "Enabled"
            };
        }

        partial void OnCpuBoostModeChanged(int value)
        {
            int boost = Math.Clamp(value, 0, 6);
            CpuBoostModeName = GetCpuBoostModeName(boost);
            _ = Task.Run(() => PowerLimitController.SetCpuBoost(boost));
            StatusMessage = $"CPU Boost set to: {CpuBoostModeName}";
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"SOFTWARE\PreySense\AdvancedSettings");
                key?.SetValue("CpuBoost", boost);
            }
            catch { }
        }
    }
}
