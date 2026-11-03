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
        private readonly IHardwareService _hw;
        private readonly Dispatcher _dispatcher;
        private readonly TurboKeyWatcher _turboKeyWatcher;
        private readonly HidModeKeyWatcher _hidModeKeyWatcher;
        private DateTime _lastPhysicalModeKeyUtc;
        private int _pendingBrightnessDelta;
        private int _powerSourceUpdateQueued;
        private bool _disposed;
        private int? _gpuModeDeferredForBattery;

        public IHardwareService HardwareService => _hw;

        public DtsAudioViewModel Dts { get; }

        [ObservableProperty] private bool _isAcConnected = true;

        // Status Feedback
        [ObservableProperty] private string _statusMessage = "Ready";
        [ObservableProperty] private bool _isBusy = false;

        private bool? _lastAcState = null;
        private PowerSourceKind _currentPowerSource = PowerSourceKind.Ac;

        private bool UpdatePowerSourceState(PowerSourceKind source)
        {
            _currentPowerSource = source;
            IsAcConnected = source != PowerSourceKind.Battery;
            return IsAcConnected;
        }

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
                    bool onAc = UpdatePowerSourceState(source);
                    if (_lastAcState.HasValue && _lastAcState.Value != onAc)
                    {
                        NotifyModeSelectionsChanged();
                        ApplyAutoRefreshForPower(onAc);
                        ApplyGpuBatteryAuto(onAc);
                    }

                    _lastAcState = onAc;
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
                _ = Task.Run(RestoreFanMode);
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
                AppLogger.Log($"TurboKey: mode 0x{next:X2} (was 0x{current:X2}); applying.");
                await SetModeAsync(next);
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _applicationReady.TrySetResult();
            DisposeFanTelemetry();
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
            CancelPendingModeSwitch();
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
