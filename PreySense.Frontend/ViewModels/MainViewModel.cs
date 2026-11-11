using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Win32;
using PreySense.Core.Driver;
using PreySense.Core.Hardware;
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

        public IHardwareService HardwareService => _hw;


        [ObservableProperty] private bool _isAcConnected = true;

        // Status Feedback
        private string _diagnosticMessage = string.Empty;
        // Operation details are logged, not exposed as application status UI.
        public string DiagnosticMessage
        {
            get => _diagnosticMessage;
            private set { _diagnosticMessage = value; AppLogger.Log(value); }
        }
        [ObservableProperty] private bool _isBusy = false;

        private bool? _lastAcState = null;
        private PowerSourceKind _currentPowerSource = PowerSourceKind.Unknown;
        private PowerSourceKind? _latestPowerSourceEvent;
        private readonly object _powerSourceEventLock = new();

        private bool UpdatePowerSourceState(PowerSourceKind source)
        {
            _currentPowerSource = source;
            if (source == PowerSourceKind.Unknown)
            {
                DiagnosticMessage = "Power source: N/A. Automatic power-source changes are paused.";
                return IsAcConnected;
            }
            IsAcConnected = source == PowerSourceKind.Ac;
            return IsAcConnected;
        }

        // Acer APGeEvent (category 0x08) is the authority for AC/battery transitions.
        private void OnAcerPowerSourceReceived(object? sender, PowerSourceKind source) =>
            QueuePowerSourceChange(source);

        private void QueuePowerSourceChange(PowerSourceKind source)
        {
            if (_disposed || source == PowerSourceKind.Unknown)
                return;

            lock (_powerSourceEventLock)
                _latestPowerSourceEvent = source;

            if (Interlocked.Exchange(ref _powerSourceUpdateQueued, 1) != 0)
                return;

            _dispatcher.BeginInvoke(() =>
            {
                try
                {
                    while (!_disposed)
                    {
                        PowerSourceKind pending;
                        lock (_powerSourceEventLock)
                        {
                            if (_latestPowerSourceEvent is not PowerSourceKind queued)
                                break;
                            pending = queued;
                            _latestPowerSourceEvent = null;
                        }

                        ApplyPowerSourceTransition(pending);
                    }
                }
                finally
                {
                    Volatile.Write(ref _powerSourceUpdateQueued, 0);
                    PowerSourceKind? again;
                    lock (_powerSourceEventLock)
                        again = _latestPowerSourceEvent;
                    if (again is PowerSourceKind pending && !_disposed)
                        QueuePowerSourceChange(pending);
                }
            });
        }

        private void ApplyPowerSourceTransition(PowerSourceKind source)
        {
            bool onAc = UpdatePowerSourceState(source);
            if (source == PowerSourceKind.Unknown)
            {
                _lastAcState = null;
                return;
            }

            if (_lastAcState.HasValue && _lastAcState.Value != onAc)
            {
                NotifyModeSelectionsChanged();
                ApplyAutoRefreshForPower(onAc);
            }

            _lastAcState = onAc;
            AppLogger.Log($"Power source transition: {source}; applying {(onAc ? "AC" : "battery")} profile.");
            ApplyPowerProfileForSource(source);
        }

        public MainViewModel(IHardwareService hw)
        {
            _hw = hw;
            _lightingWorker = new(ApplyLightingRequest,
                MergeLightingRequests);
            _selectedThemeIndex = (int)ThemeManager.LoadAndApply();
            AttachSessionLifecycleHooks();

            _dispatcher = Dispatcher.CurrentDispatcher;
            _hw.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            _turboKeyWatcher = new TurboKeyWatcher();
            _turboKeyWatcher.TurboKeyPressed += OnTurboKeyPressed;
            _turboKeyWatcher.PowerSourceReceived += OnAcerPowerSourceReceived;
            if (_hw is not MockHardwareService)
            {
                _turboKeyWatcher.Start();
            }
            // Mode key only — do not monitor Fn RGB brightness keys; firmware owns those.
            _hidModeKeyWatcher = new HidModeKeyWatcher(HandleModeKeyPressed);

            UpdateFanDemand();

            _ = RefreshStartupPreferenceAsync();

            try
            {
                var cfg = PreySense.Core.Configuration.ConfigurationService.Instance.GetCurrent();
                _isHardwareTuningEnabled = cfg.General.HardwareTuningEnabled && cfg.Tuning.HasFactoryDefaults;

                var boostProfile = cfg.Session.ActivePowerMode switch
                {
                    0 => cfg.Tuning.Quiet,
                    1 => cfg.Tuning.Balanced,
                    4 => cfg.Tuning.Performance,
                    5 => cfg.Tuning.Turbo,
                    6 => cfg.Tuning.Eco,
                    _ => cfg.Tuning.Balanced
                };
                CpuBoostMode = boostProfile.EffectiveCpuBoostMode ?? 1;
                CpuBoostModeName = GetCpuBoostModeName(CpuBoostMode);

                // JSON preference paints the UI immediately; reconcile against live
                // SCM state so a default `false` does not hide an already-debloated machine.
                IsAcerServicesDebloated = cfg.General.AcerServicesDebloated;
                DebloaterStatusText = IsAcerServicesDebloated
                    ? "Acer OEM Services: Stopped & Disabled (~400MB Saved)"
                    : "Acer OEM Services: Active";
                _ = Task.Run(() =>
                {
                    bool acerDisabled = AcerServiceDebloater.AreAllServicesDisabled();
                    _dispatcher.InvokeAsync(() =>
                    {
                        if (_disposed)
                            return;
                        if (IsAcerServicesDebloated != acerDisabled)
                        {
                            IsAcerServicesDebloated = acerDisabled;
                            DebloaterStatusText = acerDisabled
                                ? "Acer OEM Services: Stopped & Disabled (~400MB Saved)"
                                : "Acer OEM Services: Active";
                            try { AcerServiceDebloater.SaveState(acerDisabled); } catch { }
                        }
                    });
                });
            }
            catch
            {
                IsHardwareTuningEnabled = false;
            }

            ActiveGpuMuxMode = -1;
            GpuMuxModeName = GetGpuModeName(ActiveGpuMuxMode);

            LoadCoverLightingSettings();
            LoadRgbSettings();
            LoadTuningProfilesFromJson();
            LoadSessionPreferences();
            UpdateModeName(ActivePowerMode);

            NotifyModeSelectionsChanged();
            NotifyGpuSelectionsChanged();
            NotifyRefreshRateChanged();
            // Lighting is loaded from JSON and applied once at session start — do not rewrite it here.
            PersistSessionSettings(includeLighting: false);

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

            _ = InitializeTuningAndSessionAsync();
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
                System.Collections.Generic.List<int>? rates = null;
                int refreshRate = 0;
                try
                {
                    using var gpuLease = HardwareOperationGate.TryEnterTelemetry();
                    if (gpuLease != null && _hw.IsDdsAvailable && _hw.IsDdsSupported())
                    {
                        var status = _hw.GetDdsStatus();
                        liveDdsStatus = status;
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
                return (liveDdsMode, liveDdsStatus, biosDiscrete, rates, refreshRate);
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
                ApplyDdsStatus(result.liveDdsStatus);
                ApplyRefreshRateOptions(result.rates, result.refreshRate);
            });

        }

        private void OnPowerModeChanged(object? sender, byte mode)
        {
            App.Current?.Dispatcher?.InvokeAsync(async () =>
            {
                if (_disposed) return;
                if (IsBusy || _isRestoringSession || _modeSwitchGate.CurrentCount == 0) return;
                bool confirmed = await Task.Run(() => _hw.TryGetPowerMode(out byte actual) && actual == mode);
                if (!confirmed || _disposed || IsBusy || _isRestoringSession || _modeSwitchGate.CurrentCount == 0) return;
                // PreySense owns the chassis Mode / Turbo key. Ignore OEM firmware
                // echoes while a local cycle is pending or just queued.
                if (IsModeSelectionPending()
                    || (DateTime.UtcNow - _lastPhysicalModeKeyUtc).TotalMilliseconds < AcerOperatingModeTiming.TransitionMilliseconds
                    || ActivePowerMode == mode)
                    return;
                if (IsHardwareTuningEnabled)
                {
                    // ApplyModeSwitchCoreAsync already calls RestoreFanMode after the switch.
                    await ApplyModeSwitchAsync(mode);
                    return;
                }
                NotifyConfirmedPowerMode(mode);
                await ApplyCpuBoostForModeAsync(mode);
                await Task.Run(RestoreFanMode);
                PersistSessionSettings();
            });
        }

        private void OnTurboKeyPressed(object? sender, HotKeyEventArgs e) => HandleModeKeyPressed();

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
            // Save before the disposed guard suppresses persistence; lighting is drained below.
            PersistSessionSettings(includeLighting: false);
            SetMainWindowTelemetryVisible(false);
            // Capture the last edit, including a mode still waiting on the trailing apply.
            // Only explicit shutdown waits: the worker never awaits the dispatcher.
            _lightingDebouncer.Cancel();
            _ = QueueLighting(LightingWork.Save);
            _lightingWorker.StopAsync(drainPending: true).GetAwaiter().GetResult();
            StopExtendedLightingAsync().GetAwaiter().GetResult();
            _disposed = true;
            // Stop mode-switch work before tearing down hardware / the gate itself.
            DrainAndDisposeModeSwitchGate();
            _applicationReady.TrySetResult();
            DisposeFanTelemetry();
            ReleaseCustomFans();
            PreySense.Core.Hardware.CpuDriverRuntime.Dispose();
            DetachSessionLifecycleHooks();
            _hw.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            DisposeDisplaySettingsWatcher();
            _turboKeyWatcher.TurboKeyPressed -= OnTurboKeyPressed;
            _turboKeyWatcher.PowerSourceReceived -= OnAcerPowerSourceReceived;
            _turboKeyWatcher.Dispose();
            _hidModeKeyWatcher.Dispose();
            _lightingDebouncer.Dispose();
            _energySaverWorker.StopAsync().GetAwaiter().GetResult();
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
            _ => "N/A"
        });
    }
}
