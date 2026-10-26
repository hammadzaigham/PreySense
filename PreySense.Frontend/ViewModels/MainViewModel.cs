using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using PreySense.Core.Acpi;
using PreySense.Core.Services;
using PreySense;
using PreySense.Frontend.Services;
using PreySense.Gpu;
using PreySense.Helpers;
using PreySense.Mode;

namespace PreySense.Frontend.ViewModels
{
    public partial class MainViewModel : ObservableObject, IDisposable
    {
        private const double VisibleTelemetryIntervalMs = 1000;
        private readonly IHardwareService _hw;
        private readonly System.Timers.Timer _telemetryTimer;
        private readonly Dispatcher _dispatcher;
        private readonly TurboKeyWatcher _turboKeyWatcher;
        private readonly HidModeKeyWatcher _hidModeKeyWatcher;
        private DateTime _lastPhysicalModeKeyUtc;
        private int _pendingBrightnessDelta;
        private int _telemetryPollInProgress;
        private bool _telemetryPollingActive;
        private bool _hasPushedTelemetry;
        private HardwareTelemetry _lastPolledTelemetry;
        private bool _disposed;
        private int? _gpuModeDeferredForBattery;

        public IHardwareService HardwareService => _hw;

        public DtsAudioViewModel Dts { get; }

        // Telemetry Properties
        [ObservableProperty] private int _cpuTemp = 45;
        [ObservableProperty] private int _gpuTemp = 40;
        [ObservableProperty] private int _cpuRpm = 2200;
        [ObservableProperty] private int _gpuRpm = 2000;
        [ObservableProperty] private float _batteryPercent = 85;
        [ObservableProperty] private bool _isAcConnected = true;
        [ObservableProperty] private string _batteryStatusText = "85% (Plugged in)";
        [ObservableProperty] private float _cpuPackagePowerWatts = 0f;

        // Status Feedback
        [ObservableProperty] private string _statusMessage = "Ready";
        [ObservableProperty] private bool _isBusy = false;

        private bool? _lastAcState = null;
        private PowerSourceKind? _lastPowerSource;
        private PowerSourceKind _currentPowerSource = PowerSourceKind.Ac;

        private void OnPowerSourceChanged(object? sender, EventArgs e)
        {
            if (_disposed)
                return;

            _dispatcher.BeginInvoke(() =>
            {
                if (_disposed)
                    return;

                PowerSourceKind source = _hw.GetPowerSource();
                bool onAc = source != PowerSourceKind.Battery;
                _currentPowerSource = source;
                if (_lastAcState.HasValue && _lastAcState.Value != onAc)
                {
                    IsAcConnected = onAc;
                    NotifyModeSelectionsChanged();
                    ApplyAutoRefreshForPower(onAc);

                    ApplyGpuBatteryAuto(onAc);
                }

                _lastAcState = onAc;
                _lastPowerSource = source;
                ApplyPowerProfileForSource(source);
            });
        }

        public MainViewModel(IHardwareService hw)
        {
            _hw = hw;
            _isLightTheme = ThemeManager.LoadAndApply();
            AttachSessionLifecycleHooks();

            _dispatcher = Dispatcher.CurrentDispatcher;
            Dts = new DtsAudioViewModel(_dispatcher);
            _hw.PowerModeChanged += OnPowerModeChanged;
            _hw.PowerSourceChanged += OnPowerSourceChanged;
            _turboKeyWatcher = new TurboKeyWatcher();
            _turboKeyWatcher.TurboKeyPressed += OnTurboKeyPressed;
            _turboKeyWatcher.Start();
            _hidModeKeyWatcher = new HidModeKeyWatcher(HandleModeKeyPressed, StepKeyboardBrightness);
            _turboKeyWatcher.BrightnessKeyPressed += OnBrightnessKeyPressed;
            StartGameLaunchWatcher();

            // 1-second telemetry only while a PreySense window is visible.
            _telemetryTimer = new System.Timers.Timer(VisibleTelemetryIntervalMs) { AutoReset = true };
            _telemetryTimer.Elapsed += OnTelemetryTimerElapsed;
            SetTelemetryPollingActive(true);

            try
            {
                IsRunOnStartupEnabled = StartupTaskService.IsStartupEnabled();
            }
            catch { }

            try
            {
                using var advKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\PreySense\AdvancedSettings");
                IsLcdOverdriveEnabled = advKey?.GetValue("LcdOverdrive") is int od ? od == 1 : false;
                if (advKey?.GetValue("BlueLightShield") is int bl)
                {
                    BlueLightShieldMode = Math.Clamp(bl, 0, 2);
                }
                if (advKey?.GetValue("AcpiOverrideEnabled") is int ov)
                {
                    IsAcpiOverrideEnabled = ov == 1;
                }
                object? gpuBattery = advKey?.GetValue("GpuBatteryAuto");
                if (gpuBattery is null)
                {
                    using var rootKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\PreySense");
                    gpuBattery = rootKey?.GetValue("GpuBatteryAuto");
                }
                if (gpuBattery is int gpuBatteryValue)
                    IsGpuBatteryAutoEnabled = gpuBatteryValue == 1;
                if (advKey?.GetValue("CpuBoost") is int boost)
                {
                    CpuBoostMode = Math.Clamp(boost, 0, 6);
                    CpuBoostModeName = GetCpuBoostModeName(CpuBoostMode);
                }

                bool? savedServicesDebloated = AcerServiceDebloater.GetRegistryState();
                if (savedServicesDebloated.HasValue)
                {
                    IsAcerServicesDebloated = savedServicesDebloated.Value;
                    DebloaterStatusText = IsAcerServicesDebloated
                        ? "Acer OEM Services: Stopped & Disabled (~400MB Saved)"
                        : "Acer OEM Services: Active";
                }
                else
                {
                    DebloaterStatusText = "Acer OEM Services: Active";
                    _ = Task.Run(() =>
                    {
                        bool disabled = AcerServiceDebloater.AreAllServicesDisabled();
                        _dispatcher.InvokeAsync(() =>
                        {
                            if (_disposed)
                                return;
                            IsAcerServicesDebloated = disabled;
                            DebloaterStatusText = disabled
                                ? "Acer OEM Services: Stopped & Disabled (~400MB Saved)"
                                : "Acer OEM Services: Active";
                        });
                    });
                }
            }
            catch
            {
                IsLcdOverdriveEnabled = false;
                IsAcpiOverrideEnabled = false;
            }

            ActiveGpuMuxMode = LoadGpuModePreference();
            GpuMuxModeName = GetGpuModeName(ActiveGpuMuxMode);
            DisplayPanelInfo = $"{CurrentRefreshRate}Hz" + (IsLcdOverdriveEnabled ? " • Overdrive" : "");

            LoadCoverLightingSettings();
            LoadRgbSettings();
            LoadCpuLimitsFromRegistry();
            LoadCustomFanSpeeds();
            LoadSessionPreferences();
            UpdateModeName(ActivePowerMode);

            NotifyModeSelectionsChanged();
            NotifyGpuSelectionsChanged();
            NotifyFanSelectionsChanged();
            NotifyRefreshRateChanged();
            PersistSessionSettings();

            int startupPl1 = ActivePowerMode switch
            {
                0x05 => CustomTurboPl1,
                0x04 => CustomPerfPl1,
                0x01 => CustomBalancedPl1,
                0x06 => CustomEcoPl1,
                _ => CustomQuietPl1
            };
            int startupPl2 = ActivePowerMode switch
            {
                0x05 => CustomTurboPl2,
                0x04 => CustomPerfPl2,
                0x01 => CustomBalancedPl2,
                0x06 => CustomEcoPl2,
                _ => CustomQuietPl2
            };
            Pl1Watts = startupPl1;
            Pl2Watts = startupPl2;
            TauSeconds = TauForMode(ActivePowerMode);

            _ = InitializeSessionAsync();
            if (_hw is MockHardwareService)
                InitializeMockHardwareDiscovery();
            else
                _ = InitializeHardwareDiscoveryAsync();
        }

        private void InitializeMockHardwareDiscovery()
        {
            try
            {
                if (_hw.IsDdsAvailable && _hw.IsDdsSupported() && _hw.GetDdsStatus() is { IsSupported: true } status)
                {
                    ActiveGpuMuxMode = status.IsAutomatic ? 1 : status.ActiveMux == DdsActiveMux.dGPU ? 2 : 0;
                    GpuMuxModeName = GetGpuModeName(ActiveGpuMuxMode);
                    NotifyGpuSelectionsChanged();
                }
            }
            catch { }
            try { IsBiosDiscreteGpuActive = _hw.IsBiosDiscreteGpuActive; } catch { }
            InitializeRefreshRates();
        }

        private async Task InitializeHardwareDiscoveryAsync()
        {
            var result = await Task.Run(() =>
            {
                int? liveDdsMode = null;
                bool? biosDiscrete = null;
                System.Collections.Generic.List<int>? rates = null;
                int refreshRate = 0;
                try
                {
                    if (_hw.IsDdsAvailable && _hw.IsDdsSupported())
                    {
                        var status = _hw.GetDdsStatus();
                        if (status is { IsSupported: true })
                            liveDdsMode = status.IsAutomatic ? 1 : status.ActiveMux == DdsActiveMux.dGPU ? 2 : 0;
                    }
                }
                catch { }
                try { biosDiscrete = _hw.IsBiosDiscreteGpuActive; } catch { }
                try
                {
                    rates = _hw.GetSupportedRefreshRates();
                    refreshRate = _hw.GetDisplayRefreshRate();
                }
                catch { }
                return (liveDdsMode, biosDiscrete, rates, refreshRate);
            });

            if (_disposed) return;
            await _dispatcher.InvokeAsync(() =>
            {
                if (_disposed) return;
                if (result.liveDdsMode.HasValue)
                {
                    ActiveGpuMuxMode = result.liveDdsMode.Value;
                    GpuMuxModeName = GetGpuModeName(ActiveGpuMuxMode);
                    SaveGpuModePreference(ActiveGpuMuxMode);
                    NotifyGpuSelectionsChanged();
                }
                if (result.biosDiscrete.HasValue)
                    IsBiosDiscreteGpuActive = result.biosDiscrete.Value;
                ApplyRefreshRateOptions(result.rates, result.refreshRate);
            });
        }

        private void PollTelemetry()
        {
            if (_disposed || !_telemetryPollingActive)
                return;

            if (_dispatcher.CheckAccess())
            {
                Task.Run(PollTelemetry);
                return;
            }

            if (Interlocked.Exchange(ref _telemetryPollInProgress, 1) != 0)
                return;

            try
            {
                var telemetry = _hw.GetCurrentTelemetry();
                if (_hasPushedTelemetry && telemetry.Equals(_lastPolledTelemetry))
                    return;

                _lastPolledTelemetry = telemetry;
                _hasPushedTelemetry = true;
                _dispatcher.InvokeAsync(() =>
                {
                    if (!_disposed)
                        UpdateTelemetry(telemetry);
                });
            }
            finally
            {
                Volatile.Write(ref _telemetryPollInProgress, 0);
            }
        }

        private void UpdateTelemetry(HardwareTelemetry t)
        {
            CpuTemp = t.CpuTemp;
            GpuTemp = t.GpuTemp;
            CpuRpm = t.CpuRpm;
            GpuRpm = t.GpuRpm;
            CpuPackagePowerWatts = t.CpuPkgPowerWatts;
            BatteryPercent = t.BatteryPercent;
            IsAcConnected = t.IsAcConnected;
            _currentPowerSource = t.PowerSource;
            string sourceLabel = t.PowerSource switch
            {
                PowerSourceKind.UsbC => "USB-C",
                PowerSourceKind.Battery => "Battery",
                _ => "AC"
            };
            var batteryStatus = $"{Math.Round(t.BatteryPercent)}% ({sourceLabel})";
            if (!string.Equals(BatteryStatusText, batteryStatus, StringComparison.Ordinal))
                BatteryStatusText = batteryStatus;

            if (_lastPowerSource.HasValue && _lastPowerSource.Value != t.PowerSource)
            {
                if (_lastAcState != t.IsAcConnected)
                {
                    NotifyModeSelectionsChanged();
                    ApplyAutoRefreshForPower(t.IsAcConnected);

                    ApplyGpuBatteryAuto(t.IsAcConnected);
                }

                ApplyPowerProfileForSource(t.PowerSource);
            }
            else if (!_lastPowerSource.HasValue)
            {
                ApplyGpuBatteryAuto(t.IsAcConnected);
            }
            _lastAcState = t.IsAcConnected;
            _lastPowerSource = t.PowerSource;
        }

        public void SetTelemetryPollingActive(bool isVisible)
        {
            if (_disposed)
                return;

            _telemetryPollingActive = isVisible;
            if (isVisible)
            {
                _telemetryTimer.Interval = VisibleTelemetryIntervalMs;
                if (!_telemetryTimer.Enabled)
                    _telemetryTimer.Start();
            }
            else
            {
                _telemetryTimer.Stop();
            }
        }

        private void OnTelemetryTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e) => PollTelemetry();

        private void OnPowerModeChanged(object? sender, byte mode)
        {
            App.Current?.Dispatcher?.InvokeAsync(() =>
            {
                if (_disposed) return;
                if (IsFactoryCalibrationRunning) return;
                // PreySense owns the chassis Mode / Turbo key. Ignore OEM firmware
                // echoes while a local cycle is pending or just queued.
                if (IsModeSelectionPending()
                    || (DateTime.UtcNow - _lastPhysicalModeKeyUtc).TotalMilliseconds < 1500
                    || ActivePowerMode == mode)
                    return;
                ActivePowerMode = mode;
                UpdateModeName(mode);
                NotifyModeSelectionsChanged();
                PersistSessionSettings();
                Views.ModeToastWindow.ShowConfirmedMode(mode, GetModeTitle(mode));
            });
        }

        private void OnTurboKeyPressed(object? sender, HotKeyEventArgs e) => HandleModeKeyPressed();

        private void OnBrightnessKeyPressed(object? sender, int delta) => StepKeyboardBrightness(delta);

        public void HandleModeKeyPressed()
        {
            _dispatcher.InvokeAsync(async () =>
            {
                if (_disposed) return;
                if ((DateTime.UtcNow - _lastPhysicalModeKeyUtc).TotalMilliseconds < 80)
                    return;
                _lastPhysicalModeKeyUtc = DateTime.UtcNow;
                byte current = PeekPendingOrActiveMode();
                byte next = CyclePerformanceMode(current);
                AppLogger.Log($"TurboKey: queued mode 0x{next:X2} (was 0x{current:X2}); hardware apply debounced.");
                await SetModeAsync(next);
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _startupWindowReady.TrySetResult();
            _telemetryTimer.Stop();
            _telemetryTimer.Elapsed -= OnTelemetryTimerElapsed;
            _telemetryTimer.Dispose();
            PreySense.Mode.PawnCpuPowerControl.Dispose();
            PersistSessionSettings();
            DetachSessionLifecycleHooks();
            _hw.PowerModeChanged -= OnPowerModeChanged;
            _hw.PowerSourceChanged -= OnPowerSourceChanged;
            _turboKeyWatcher.TurboKeyPressed -= OnTurboKeyPressed;
            _turboKeyWatcher.BrightnessKeyPressed -= OnBrightnessKeyPressed;
            _turboKeyWatcher.Dispose();
            _hidModeKeyWatcher.Dispose();
            _gameLaunchWatcher?.Dispose();
            _gameLaunchWatcher = null;
            lock (_modeSwitchDebounceLock)
            {
                _modeSwitchDebounceCts?.Cancel();
                _modeSwitchDebounceCts?.Dispose();
                _modeSwitchDebounceCts = null;
            }
            _modeSwitchGate.Dispose();
            Dts.DisposeManager();
            _usbChargingDebouncer.Dispose();
            _customFanDebouncer.Dispose();
            _lightingDebouncer.Dispose();
            if (_coverSyncFrameHooked && _hw is ProductionHardwareService hooked)
            {
                hooked.LightingEngine.FrameUpdated -= OnLightingEngineFrameUpdated;
                _coverSyncFrameHooked = false;
            }
            if (_hw is IDisposable disposableHardware)
                disposableHardware.Dispose();
            GC.SuppressFinalize(this);
        }

        private string GetModeTitle(byte mode) => LocalizationService.Translate(mode switch
        {
            0x00 => "Quiet",
            0x01 => "Balanced",
            0x04 => "Performance",
            0x05 => "Turbo",
            0x06 => "Eco",
            _ => "Custom"
        });
    }
}
