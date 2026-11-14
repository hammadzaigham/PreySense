using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PreySense.Core.Driver;
using PreySense.Core.Hardware;
using PreySense.Core.Services;
using PreySense.Frontend.Views;
using PreySense.Mode;

namespace PreySense.Frontend.ViewModels
{
    public partial class MainViewModel
    {
        private readonly Services.LatestBackgroundWorker<bool> _energySaverWorker =
            new(PowerLimitController.SetEnergySaverOnBattery, (_, latest) => latest);
        private readonly object _modeSwitchLock = new();
        private readonly SemaphoreSlim _modeSwitchGate = new(1, 1);
        private CancellationTokenSource? _modeSwitchTrailingCts;
        private byte _pendingHardwareMode = 0xFF;
        private byte _lastAppliedHardwareMode = 0xFF;
        private bool _modeSwitchCooldownActive;
        internal Action<byte, string>? ModeToastPresenter { get; set; }

        internal void NotifyConfirmedPowerMode(byte mode)
        {
            ActivePowerMode = mode;
            UpdateModeName(mode);
            NotifyModeSelectionsChanged();
            ScheduleHotkeyLightingRestoreAfterModeSwitch();
            if (ModeToastPresenter is { } presenter) presenter(mode, GetModeTitle(mode));
            else ModeToastWindow.ShowConfirmedMode(mode, GetModeTitle(mode));
        }

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

            bool applyNow;
            lock (_modeSwitchLock)
            {
                _pendingHardwareMode = mode;
                applyNow = !_modeSwitchCooldownActive;
                if (applyNow)
                    _modeSwitchCooldownActive = true;
            }

            if (!applyNow)
                return;

            // Fixed mode-transition window from the first click; later clicks only update pending.
            ScheduleTrailingModeApply();

            if (!await TryEnterModeSwitchGateAsync()) return;
            try
            {
                byte toApply;
                lock (_modeSwitchLock)
                    toApply = _pendingHardwareMode;
                if (toApply == 0xFF) return;
                bool applied = await ApplyModeSwitchCoreAsync(toApply);
                lock (_modeSwitchLock)
                {
                    if (applied)
                        _lastAppliedHardwareMode = toApply;
                    if (_pendingHardwareMode == toApply)
                        _pendingHardwareMode = 0xFF;
                }
            }
            finally
            {
                ExitModeSwitchGate();
            }
        }

        /// <summary>
        /// First click applies immediately. Further clicks only update the pending mode;
        /// after the EC settle window the last pending mode is applied once (timer does not reset).
        /// </summary>
        private void ScheduleTrailingModeApply()
        {
            CancellationToken token;
            lock (_modeSwitchLock)
            {
                if (_modeSwitchTrailingCts != null)
                    return;
                _modeSwitchTrailingCts = new CancellationTokenSource();
                token = _modeSwitchTrailingCts.Token;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(AcerOperatingModeTiming.TransitionDelay, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (!await TryEnterModeSwitchGateAsync()) return;
                try
                {
                    if (token.IsCancellationRequested || _disposed) return;

                    CancellationTokenSource? spent;
                    lock (_modeSwitchLock)
                    {
                        spent = _modeSwitchTrailingCts;
                        _modeSwitchTrailingCts = null;
                    }
                    spent?.Dispose();

                    byte toApply;
                    lock (_modeSwitchLock)
                        toApply = _pendingHardwareMode;
                    if (toApply == 0xFF)
                    {
                        lock (_modeSwitchLock)
                            _modeSwitchCooldownActive = false;
                        return;
                    }

                    bool alreadyApplied;
                    lock (_modeSwitchLock)
                        alreadyApplied = toApply == _lastAppliedHardwareMode;
                    bool applied = alreadyApplied || await ApplyModeSwitchCoreAsync(toApply);

                    bool morePending;
                    lock (_modeSwitchLock)
                    {
                        if (applied)
                            _lastAppliedHardwareMode = toApply;
                        if (_pendingHardwareMode == toApply)
                        {
                            _pendingHardwareMode = 0xFF;
                            // A queued switch starts its own cooldown, even if no new click is waiting yet.
                            _modeSwitchCooldownActive = applied;
                            morePending = applied;
                        }
                        else
                        {
                            // Another mode was chosen while applying; keep cooldown and wait again.
                            _modeSwitchCooldownActive = true;
                            morePending = true;
                        }
                    }

                    if (morePending)
                        ScheduleTrailingModeApply();
                }
                finally
                {
                    ExitModeSwitchGate();
                }
            });
        }

        internal bool IsModeSelectionPending()
        {
            lock (_modeSwitchLock)
                return _pendingHardwareMode != 0xFF || _modeSwitchCooldownActive;
        }

        internal byte PeekPendingOrActiveMode()
        {
            lock (_modeSwitchLock)
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
            if (!await TryEnterModeSwitchGateAsync()) return;
            try
            {
                bool applied = await ApplyModeSwitchCoreAsync(mode, applyCustomLimits);
                lock (_modeSwitchLock)
                {
                    if (applied)
                        _lastAppliedHardwareMode = mode;
                    _pendingHardwareMode = 0xFF;
                    _modeSwitchCooldownActive = false;
                }
            }
            finally { ExitModeSwitchGate(); }
        }

        private void CancelPendingModeSwitch()
        {
            lock (_modeSwitchLock)
            {
                _modeSwitchTrailingCts?.Cancel();
                _modeSwitchTrailingCts?.Dispose();
                _modeSwitchTrailingCts = null;
                _pendingHardwareMode = 0xFF;
                _modeSwitchCooldownActive = false;
            }
        }

        /// <summary>
        /// Acquire the mode-switch gate, or return false if PreySense is shutting down /
        /// the semaphore was already disposed. Callers must pair a true result with
        /// <see cref="ExitModeSwitchGate"/>.
        /// </summary>
        private async Task<bool> TryEnterModeSwitchGateAsync()
        {
            if (_disposed) return false;
            try
            {
                await _modeSwitchGate.WaitAsync().ConfigureAwait(true);
            }
            catch (ObjectDisposedException)
            {
                return false;
            }

            if (_disposed)
            {
                ExitModeSwitchGate();
                return false;
            }

            return true;
        }

        private void ExitModeSwitchGate()
        {
            try { _modeSwitchGate.Release(); }
            catch (ObjectDisposedException) { }
            catch (SemaphoreFullException) { }
        }

        /// <summary>
        /// Cancel trailing work, wait out any in-flight holder, then dispose the gate so
        /// shutdown cannot race WaitAsync/Release on a torn-down semaphore.
        /// </summary>
        private void DrainAndDisposeModeSwitchGate()
        {
            CancelPendingModeSwitch();
            try
            {
                if (_modeSwitchGate.Wait(TimeSpan.FromSeconds(3)))
                    _modeSwitchGate.Release();
            }
            catch (ObjectDisposedException) { }
            catch (SemaphoreFullException) { }

            try { _modeSwitchGate.Dispose(); }
            catch (ObjectDisposedException) { }
        }

        /// <returns>False when the Acer mode was not applied (caller must not mark it as last-applied).</returns>
        private async Task<bool> ApplyModeSwitchCoreAsync(byte mode, bool applyCustomLimits = true)
        {
            if (!ProfileManager.IsSupportedMode(mode)) return false;
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
                    await RefreshModeAfterFailedSwitchAsync();
                    return false;
                }
                finally { IsBusy = false; }
            }
            else
            {
                bool changed;
                try { changed = await Task.Run(() => _hw.SetPowerMode(mode)); }
                catch (Exception ex)
                {
                    AppLogger.Error($"Acer mode switch failed: {ex}");
                    DiagnosticMessage = $"Could not switch Acer mode: {ex.Message}";
                    await RefreshModeAfterFailedSwitchAsync();
                    return false;
                }
                if (!changed)
                {
                    DiagnosticMessage = "Could not switch Acer mode.";
                    await RefreshModeAfterFailedSwitchAsync();
                    return false;
                }
            }
            NotifyConfirmedPowerMode(mode);
            if (!await ApplyCpuBoostForModeAsync(mode))
                DiagnosticMessage = "Mode selected, but CPU boost failed.";
            // OnPowerModeChanged skips RestoreFanMode while the mode-switch gate is held — load here.
            await Task.Run(RestoreFanMode).ConfigureAwait(true);
            PersistSessionSettings();
            return true;
        }

        private async Task RefreshModeAfterFailedSwitchAsync()
        {
            byte? actual = null;
            try { actual = await Task.Run(() => _hw.TryGetPowerMode(out byte current) ? (byte?)current : null); }
            catch (Exception ex) { AppLogger.Error($"Could not read Acer mode after failed switch: {ex}"); }
            ActivePowerMode = actual ?? 0xFF;
            UpdateModeName(ActivePowerMode);
            NotifyModeSelectionsChanged();
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
            ActiveModeWattageInfo = "—";
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
                    profile.ApplyCpuBoost = true;
                    profile.CpuBoostMode = boost;
                    profile.CpuBoost = boost > 0;
                });
            }
            catch { }
            if (!await TryEnterModeSwitchGateAsync()) return;
            try
            {
                if (!_disposed && ActivePowerMode == mode)
                    await ApplyCpuBoostForModeAsync(mode);
            }
            finally { ExitModeSwitchGate(); }
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
