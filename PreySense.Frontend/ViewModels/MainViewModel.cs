using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Win32;
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
        private const double TelemetryIntervalMs = 1000;
        private readonly IHardwareService _hw;
        private readonly Dispatcher _dispatcher;
        private readonly TurboKeyWatcher _turboKeyWatcher;
        private readonly HidModeKeyWatcher _hidModeKeyWatcher;
        private DateTime _lastPhysicalModeKeyUtc;
        private int _pendingBrightnessDelta;
        private int _powerSourceUpdateQueued;
        private int _telemetryPollInProgress;
        private volatile bool _telemetryWindowVisible;
        private bool ShouldPollTelemetry => _telemetryWindowVisible || OverlayBridge.IsShowing;
        private bool _disposed;
        private int? _gpuModeDeferredForBattery;

        public IHardwareService HardwareService => _hw;

        public DtsAudioViewModel Dts { get; }

        // Telemetry Properties
        [ObservableProperty, NotifyPropertyChangedFor(nameof(CpuTemperatureText))] private int _cpuTemp = -1;
        [ObservableProperty, NotifyPropertyChangedFor(nameof(GpuTemperatureText))] private int _gpuTemp = -1;
        [ObservableProperty, NotifyPropertyChangedFor(nameof(CpuFanText))] private int _cpuRpm = -1;
        [ObservableProperty, NotifyPropertyChangedFor(nameof(GpuFanText))] private int _gpuRpm = -1;
        [ObservableProperty] private float _batteryPercent = 85;
        [ObservableProperty] private bool _isAcConnected = true;
        [ObservableProperty] private string _batteryStatusText = "85% (Plugged in)";
        [ObservableProperty] private float _cpuPackagePowerWatts = 0f;
        [ObservableProperty, NotifyPropertyChangedFor(nameof(CpuUsageText))] private int? _cpuUsage;
        [ObservableProperty, NotifyPropertyChangedFor(nameof(CpuMaxCoreUsageText))] private int? _cpuMaxCoreUsage;
        [ObservableProperty, NotifyPropertyChangedFor(nameof(CpuPCoreUsageText))] private int? _cpuPCoreUsage;
        [ObservableProperty, NotifyPropertyChangedFor(nameof(CpuECoreUsageText))] private int? _cpuECoreUsage;
        [ObservableProperty, NotifyPropertyChangedFor(nameof(RamUsageText))] private int? _ramUsage;
        [ObservableProperty] private int? _ramUsedMb;

        public string CpuTemperatureText => CpuTemp > 0 ? $"{CpuTemp}°C" : "N/A";
        public string GpuTemperatureText => GpuTemp > 0 ? $"{GpuTemp}°C" : "N/A";
        public string CpuFanText => CpuRpm >= 0 ? $"{CpuRpm} RPM" : "N/A";
        public string GpuFanText => GpuRpm >= 0 ? $"{GpuRpm} RPM" : "N/A";
        public string CpuUsageText => CpuUsage.HasValue ? $"{CpuUsage.Value}%" : "N/A";
        public string CpuMaxCoreUsageText => CpuMaxCoreUsage.HasValue ? $"{CpuMaxCoreUsage.Value}%" : "N/A";
        public string CpuPCoreUsageText => CpuPCoreUsage.HasValue ? $"{CpuPCoreUsage.Value}%" : "N/A";
        public string CpuECoreUsageText => CpuECoreUsage.HasValue ? $"{CpuECoreUsage.Value}%" : "N/A";
        public string RamUsageText => RamUsage.HasValue ? $"{RamUsage.Value}%" : "N/A";

        // Status Feedback
        [ObservableProperty] private string _statusMessage = "Ready";
        [ObservableProperty] private bool _isBusy = false;

        private bool? _lastAcState = null;
        private PowerSourceKind? _lastPowerSource;
        private PowerSourceKind _currentPowerSource = PowerSourceKind.Ac;

        private void OnPowerSourceReceived(object? sender, PowerSourceKind source)
        {
            if (source is PowerSourceKind.Ac or PowerSourceKind.UsbC)
            {
                _hw.NotePluggedSource(source);
            }
            OnPowerSourceChanged(this, EventArgs.Empty);
        }

        private void OnPowerSourceChanged(object? sender, EventArgs e)
        {
            if (_disposed || Interlocked.Exchange(ref _powerSourceUpdateQueued, 1) != 0)
                return;

            _dispatcher.BeginInvoke(() =>
            {
                try
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
                }
                finally
                {
                    Volatile.Write(ref _powerSourceUpdateQueued, 0);
                }
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
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            _turboKeyWatcher = new TurboKeyWatcher();
            _turboKeyWatcher.TurboKeyPressed += OnTurboKeyPressed;
            _turboKeyWatcher.PowerSourceReceived += OnPowerSourceReceived;
            if (_hw is not MockHardwareService)
            {
                _turboKeyWatcher.Start();
            }
            _hidModeKeyWatcher = new HidModeKeyWatcher(HandleModeKeyPressed, StepKeyboardBrightness);
            _turboKeyWatcher.BrightnessKeyPressed += OnBrightnessKeyPressed;
            StartGameLaunchWatcher();

            PreySense.Core.Services.TelemetryCoordinator.SnapshotAvailable += OnCoordinatorSnapshotAvailable;
            UpdateFanDemand();

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
            LoadSessionPreferences();
            UpdateModeName(ActivePowerMode);

            NotifyModeSelectionsChanged();
            NotifyGpuSelectionsChanged();
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

            if (_hw is ProductionHardwareService)
            {
                ApplyStartupLighting();
            }

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
                    ApplyDdsStatus(status);
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
            int revision = Volatile.Read(ref _gpuDisplayRevision);
            var result = await Task.Run(() =>
            {
                int? liveDdsMode = null;
                DdsStatus? liveDdsStatus = null;
                bool? biosDiscrete = null;
                bool? igpuOnly = null;
                System.Collections.Generic.List<int>? rates = null;
                int refreshRate = 0;
                try
                {
                    if (_hw.IsDdsAvailable && _hw.IsDdsSupported())
                    {
                        var status = _hw.GetDdsStatus();
                        liveDdsStatus = status;
                        if (status is { IsSupported: true })
                            liveDdsMode = status.IsAutomatic ? 1 : status.ActiveMux == DdsActiveMux.dGPU ? 2 : 0;
                    }
                }
                catch { }
                try { biosDiscrete = _hw.IsBiosDiscreteGpuActive; } catch { }
                try { igpuOnly = _hw.IsDiscreteGpuDisabled; } catch { }
                try
                {
                    rates = _hw.GetSupportedRefreshRates();
                    refreshRate = _hw.GetDisplayRefreshRate();
                }
                catch { }
                return (liveDdsMode, liveDdsStatus, biosDiscrete, igpuOnly, rates, refreshRate);
            });

            if (_disposed) return;
            await _dispatcher.InvokeAsync(() =>
            {
                if (_disposed || revision != Volatile.Read(ref _gpuDisplayRevision)) return;
                if (result.liveDdsMode.HasValue)
                {
                    ActiveGpuMuxMode = result.liveDdsMode.Value;
                    GpuMuxModeName = GetGpuModeName(ActiveGpuMuxMode);
                    NotifyGpuSelectionsChanged();
                }
                if (result.biosDiscrete.HasValue)
                    IsBiosDiscreteGpuActive = result.biosDiscrete.Value;
                IsGpuIgpuOnlyActive = result.igpuOnly == true && !IsBiosDiscreteGpuActive;
                if (IsGpuIgpuOnlyActive)
                {
                    GpuMuxModeName = "iGPU Only";
                    NotifyGpuSelectionsChanged();
                }
                ApplyDdsStatus(result.liveDdsStatus);
                ApplyRefreshRateOptions(result.rates, result.refreshRate);
            });

        }

        private void PollTelemetry()
        {
            if (_disposed) return;
            if (!ShouldPollTelemetry && !RequiresFanTemperatures) return;

            if (_dispatcher.CheckAccess())
            {
                Task.Run(PollTelemetry);
                return;
            }

            if (Interlocked.Exchange(ref _telemetryPollInProgress, 1) != 0)
                return;

            try
            {
                using var lease = HardwareOperationGate.TryEnterTelemetry();
                if (lease == null) return;
                int displayRevision = Volatile.Read(ref _gpuDisplayRevision);
                bool fullTelemetry = ShouldPollTelemetry;
                var telemetry = fullTelemetry ? _hw.GetCurrentTelemetry() : _hw.GetFanTemperatures();
                ProcessFanCurves(telemetry);
                if (!fullTelemetry) return;
                _dispatcher.InvokeAsync(() =>
                {
                    if (!_disposed && ShouldPollTelemetry)
                    {
                        UpdateTelemetry(telemetry);
                    }
                });
            }
            catch (Exception ex)
            {
                ReleaseCustomFans();
                AppLogger.Log($"Fan/telemetry read failed: {ex.Message}");
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
            var cpu = CpuUsageMonitor.GetSnapshot();
            if (cpu.HasValue)
            {
                CpuUsage = cpu.Value.TotalUsage;
                CpuMaxCoreUsage = cpu.Value.MaxCoreUsage;
                CpuPCoreUsage = cpu.Value.PCoreUsage;
                CpuECoreUsage = cpu.Value.ECoreUsage;
            }
            else
            {
                CpuUsage = CpuUsageMonitor.GetCpuUsage();
                CpuMaxCoreUsage = CpuUsageMonitor.GetMaxCoreUsage();
                CpuPCoreUsage = CpuUsageMonitor.GetPCoreUsage();
                CpuECoreUsage = CpuUsageMonitor.GetECoreUsage();
            }
            var ram = SystemMemoryMonitor.GetMemoryInfo();
            RamUsage = ram?.UsedPercent;
            RamUsedMb = ram?.UsedMb;
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

        private void OnCoordinatorSnapshotAvailable(TelemetrySnapshot snapshot)
        {
            if (_disposed) return;

            var telemetry = snapshot.ToHardwareTelemetry();
            ProcessFanCurves(telemetry);

            if (!snapshot.IsFanOnly)
            {
                bool onAc = snapshot.IsAcConnected;
                PowerSourceKind source = snapshot.PowerSource;
                if (_lastPowerSource.HasValue && _lastPowerSource.Value != source)
                {
                    if (_lastAcState != onAc)
                    {
                        _dispatcher.InvokeAsync(() =>
                        {
                            if (_disposed) return;
                            NotifyModeSelectionsChanged();
                            ApplyAutoRefreshForPower(onAc);
                            ApplyGpuBatteryAuto(onAc);
                        });
                    }

                    _dispatcher.InvokeAsync(() =>
                    {
                        if (_disposed) return;
                        ApplyPowerProfileForSource(source);
                    });
                }
                else if (!_lastPowerSource.HasValue)
                {
                    _dispatcher.InvokeAsync(() =>
                    {
                        if (_disposed) return;
                        ApplyGpuBatteryAuto(onAc);
                    });
                }
                _lastAcState = onAc;
                _lastPowerSource = source;
            }

            if (!_telemetryWindowVisible) return;

            _dispatcher.InvokeAsync(() =>
            {
                if (_disposed || !_telemetryWindowVisible) return;
                UpdateTelemetryFromSnapshot(snapshot);
            });
        }

        private void UpdateTelemetryFromSnapshot(TelemetrySnapshot snapshot)
        {
            CpuTemp = snapshot.CpuTemp ?? 0;
            GpuTemp = snapshot.GpuTemp ?? 0;
            if (snapshot.CpuRpm.HasValue) CpuRpm = snapshot.CpuRpm.Value;
            if (snapshot.GpuRpm.HasValue) GpuRpm = snapshot.GpuRpm.Value;

            if (snapshot.IsFanOnly)
            {
                // Fan-only partial update: preserve existing CPU, RAM, battery, and power source telemetry
                return;
            }

            CpuPackagePowerWatts = snapshot.CpuPkgPowerWatts ?? 0f;
            CpuUsage = snapshot.CpuUsage;
            CpuMaxCoreUsage = snapshot.CpuMaxCoreUsage;
            CpuPCoreUsage = snapshot.CpuPCoreUsage;
            CpuECoreUsage = snapshot.CpuECoreUsage;
            RamUsage = snapshot.RamUsage;
            RamUsedMb = snapshot.RamUsedMb;
            BatteryPercent = snapshot.BatteryPercent;
            IsAcConnected = snapshot.IsAcConnected;
            _currentPowerSource = snapshot.PowerSource;

            string sourceLabel = _currentPowerSource switch
            {
                PowerSourceKind.UsbC => "USB-C",
                PowerSourceKind.Battery => "Battery",
                _ => "AC"
            };
            var batteryStatus = $"{Math.Round(BatteryPercent)}% ({sourceLabel})";
            if (!string.Equals(BatteryStatusText, batteryStatus, StringComparison.Ordinal))
                BatteryStatusText = batteryStatus;
        }

        public void SetTelemetryPollingActive(bool windowVisible)
        {
            _telemetryWindowVisible = windowVisible;
            PreySense.Core.Services.TelemetryCoordinator.SetDemand(
                PreySense.Core.Services.TelemetryDemand.MainWindow, windowVisible);
            if (windowVisible)
            {
                TriggerDisplayStateRefresh();
            }
        }

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
                lock (_fanControlGate)
                    ResetFanFollower();
                UpdateFanDemand();
                ActivePowerMode = mode;
                ApplyCpuBoostForMode(mode);
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
            _applicationReady.TrySetResult();
            PreySense.Core.Services.TelemetryCoordinator.SnapshotAvailable -= OnCoordinatorSnapshotAvailable;
            PreySense.Core.Services.TelemetryCoordinator.SetDemand(
                PreySense.Core.Services.TelemetryDemand.MainWindow, false);
            ReleaseCustomFans();
            PreySense.Mode.PawnCpuPowerControl.Dispose();
            PersistSessionSettings();
            DetachSessionLifecycleHooks();
            _hw.PowerModeChanged -= OnPowerModeChanged;
            _hw.PowerSourceChanged -= OnPowerSourceChanged;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            DisposeDisplaySettingsWatcher();
            _turboKeyWatcher.TurboKeyPressed -= OnTurboKeyPressed;
            _turboKeyWatcher.BrightnessKeyPressed -= OnBrightnessKeyPressed;
            _turboKeyWatcher.PowerSourceReceived -= OnPowerSourceReceived;
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
