using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;
using PreySense.Core.Driver;
using PreySense.Core.Hardware;
using PreySense.Core.Services;
using PreySense.Frontend.Services;
using PreySense.Helpers;
using PreySense.Mode;

namespace PreySense.Frontend.ViewModels
{
    public partial class MainViewModel
    {
        private bool _sessionHooksAttached;
        private bool _isRestoringSession;
        private readonly TaskCompletionSource _applicationReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void NotifyApplicationReady() => _applicationReady.TrySetResult();

        private void AttachSessionLifecycleHooks()
        {
            if (_sessionHooksAttached)
                return;

            SystemEvents.PowerModeChanged += OnSystemPowerModeChanged;
            SystemEvents.SessionEnding += OnSessionEnding;
            _sessionHooksAttached = true;
        }

        private void DetachSessionLifecycleHooks()
        {
            if (!_sessionHooksAttached)
                return;

            SystemEvents.PowerModeChanged -= OnSystemPowerModeChanged;
            SystemEvents.SessionEnding -= OnSessionEnding;
            _sessionHooksAttached = false;
        }

        private void LoadSessionPreferences()
        {
            try
            {
                var cfg = PreySense.Core.Configuration.ConfigurationService.Instance.GetCurrent();

                IsAutoRefreshRateEnabled = cfg.Display.AutoRefreshRate;
                IsBatteryLimitEnabled = cfg.Battery.ChargeLimitEnabled;
                SynchronizeFanSpeeds = cfg.Fans.SynchronizeFanSpeeds;
                _usbChargingCutoffPercent = cfg.Battery.UsbPowerOffChargingCutoffPercent;
                IsAcerServicesDebloated = cfg.General.AcerServicesDebloated;

                _suppressPowerProfile = true;
                if (cfg.Session.AcPowerMode is 0 or 1 or 4 or 5 or 6)
                    AcPowerModeIndex = ProfileManager.SourceProfileIndex(cfg.Session.AcPowerMode);
                BatteryPowerModeIndex = cfg.Session.BatteryPowerMode is 0 or 1 or 4 or 5 or 6
                    ? ProfileManager.SourceProfileIndex(cfg.Session.BatteryPowerMode)
                    : ProfileManager.SourceProfileIndex(ProfileManager.DefaultBatterySourceMode);

                // Start from the current source preference, never the previous session's mode.
                try
                {
                    var source = _hw.GetPowerSource();
                    if (source != PowerSourceKind.Unknown)
                    {
                        ActivePowerMode = source == PowerSourceKind.Battery
                            ? ProfileManager.BatterySourceProfileModeOrDefault(BatteryPowerModeIndex)
                            : ProfileManager.SourceProfileMode(AcPowerModeIndex);
                        UpdateModeName(ActivePowerMode);
                    }
                }
                catch (Exception ex) { AppLogger.Log($"Startup power source unavailable: {ex.Message}"); }

                IsBatteryEnergySaverEnabled = cfg.Battery.BatteryEnergySaver;

                _suppressPowerProfile = false;
            }
            catch (Exception ex)
            {
                _suppressPowerProfile = false;
                AppLogger.Log($"LoadSessionPreferences: {ex.Message}");
            }
        }

        public void PersistSessionSettings(bool includeLighting = true)
        {
            if (_disposed || _isRestoringSession)
                return;

            try
            {
                PreySense.Core.Configuration.ConfigurationService.Instance.Update(cfg =>
                {
                    cfg.Session.ActivePowerMode = (byte)ActivePowerMode;
                    cfg.Display.AutoRefreshRate = IsAutoRefreshRateEnabled;
                    cfg.Battery.ChargeLimitEnabled = IsBatteryLimitEnabled;
                    cfg.General.AcerServicesDebloated = IsAcerServicesDebloated;
                    cfg.Session.AcPowerMode = ProfileManager.SourceProfileMode(AcPowerModeIndex);
                    cfg.Session.BatteryPowerMode = ProfileManager.BatterySourceProfileModeOrDefault(BatteryPowerModeIndex);
                    cfg.Battery.BatteryEnergySaver = IsBatteryEnergySaverEnabled;
                });
            }
            catch (Exception ex)
            {
                AppLogger.Log($"PersistSessionSettings: {ex.Message}");
            }

            if (includeLighting)
                SaveLightingSettings();
        }


        private async Task InitializeSessionAsync()
        {
            await _applicationReady.Task;
            if (_disposed) return;
            _ = ApplyStartupLightingAsync();
            if (_disposed) return;
            await RestoreSessionSettingsAsync("startup");
            await _dispatcher.InvokeAsync(InitializeGameMode);
            if (_disposed)
                return;
            int batteryLimit = IsBatteryLimitEnabled ? 1 : 0;

            await Task.Run(() =>
            {
                try { batteryLimit = _hw.GetBatteryChargeLimit(); } catch { }
            });

            if (_disposed)
                return;

            await _dispatcher.InvokeAsync(() =>
            {
                if (_disposed)
                    return;

                IsBatteryLimitEnabled = batteryLimit == 1;
            });

            TriggerDisplayStateRefresh();
        }

        public async Task RestoreSessionSettingsAsync(string reason)
        {
            if (_disposed)
                return;

            bool gateHeld = false;
            try
            {
                if (!await TryEnterModeSwitchGateAsync()) return;
                gateHeld = true;
                _isRestoringSession = true;
                AppLogger.Log($"Restoring PreySense session settings ({reason}).");
                byte mode = ActivePowerMode;
                PowerSourceKind source = PowerSourceKind.Unknown;
                string restoreWarning = string.Empty;
                await Task.Run(() =>
                {
                    try
                    {
                        source = _hw.GetPowerSource();
                    }
                    catch
                    {
                    }

                    if (source == PowerSourceKind.Unknown)
                    {
                        restoreWarning = "Power source: N/A. Source-specific settings were not applied.";
                        return;
                    }
                    mode = source == PowerSourceKind.Battery
                        ? ProfileManager.BatterySourceProfileModeOrDefault(BatteryPowerModeIndex)
                        : ProfileManager.SourceProfileMode(AcPowerModeIndex);
                    if (IsBatteryEnergySaverEnabled)
                        PowerLimitController.SetEnergySaverOnBattery(true);

                    if (!string.Equals(reason, "startup", StringComparison.OrdinalIgnoreCase))
                        ApplySavedLighting();
                    restoreWarning = ApplyPersistedHardwareSettings(mode);
                    if (_hw.TryGetPowerMode(out byte confirmedMode)) mode = confirmedMode;
                    if (string.Equals(reason, "startup", StringComparison.OrdinalIgnoreCase))
                        ApplyStartupBatteryRefreshRate();
                });

                if (string.Equals(reason, "resume", StringComparison.OrdinalIgnoreCase) && OverlayBridge.ShouldRun)
                    await _dispatcher.InvokeAsync(OverlayBridge.StartIfNeeded);

                await _dispatcher.InvokeAsync(() =>
                {
                    if (_disposed)
                        return;
                    UpdatePowerSourceState(source);
                    ActivePowerMode = mode;
                    UpdateModeName(mode);
                    NotifyModeSelectionsChanged();
                    if (source == PowerSourceKind.Unknown) { DiagnosticMessage = restoreWarning; return; }
                    ScheduleHotkeyLightingRestoreAfterModeSwitch();
                    bool onAc = source == PowerSourceKind.Ac;
                    _lastAcState = onAc;
                    ApplyAutoRefreshForPower(onAc);
                    DiagnosticMessage = reason.Equals("startup", StringComparison.OrdinalIgnoreCase)
                        ? "Saved settings applied."
                        : "Saved settings reapplied after wake.";
                    if (!string.IsNullOrEmpty(restoreWarning))
                        DiagnosticMessage = "Some settings could not be restored: " + restoreWarning;
                });
            }
            catch (Exception ex)
            {
                AppLogger.Log($"RestoreSessionSettingsAsync ({reason}): {ex.Message}");
                await _dispatcher.InvokeAsync(() =>
                {
                    if (!_disposed) DiagnosticMessage = $"Could not restore settings: {ex.Message}";
                });
            }
            finally
            {
                if (gateHeld)
                {
                    _isRestoringSession = false;
                    ExitModeSwitchGate();
                }
            }
        }

        private string ApplyPersistedHardwareSettings(byte? modeOverride = null)
        {
            byte mode = modeOverride ?? ActivePowerMode;
            var failures = new System.Collections.Generic.List<string>();
            var restored = RestoreSavedPowerMode(mode);
            if (!restored.Success) failures.Add("Power profile: " + restored.Status);
            if (_hw.TryGetPowerMode(out byte confirmedMode)) mode = confirmedMode;
            TauSeconds = TauForMode(mode);
            if (!ApplyCpuBoostForMode(mode)) failures.Add("CPU boost could not be applied by Windows");
            RestoreFanMode();

            _hw.SetBatteryChargeLimit(IsBatteryLimitEnabled ? 1 : 0);
            try { ApplyUsbChargingCutoff(_usbChargingCutoffPercent); }
            catch (Exception ex) { failures.Add(ex.Message); }
            _hw.SetKbdBacklightTimeoutSeconds(KbdTimeoutSeconds);
            string warning = string.Join("; ", failures);
            if (failures.Count > 0) AppLogger.Log("Settings restore partial: " + warning);
            return warning;
        }

        private void ApplyStartupBatteryRefreshRate()
        {
            if (!IsAutoRefreshRateEnabled)
                return;

            bool onBattery = false;
            int hz = 0;
            try
            {
                onBattery = _hw.GetPowerSource() == PowerSourceKind.Battery;
                hz = _hw.GetDisplayRefreshRate();
            }
            catch
            {
                return;
            }

            if (onBattery && hz != 60)
                _hw.SetDisplayRefreshRate(60);
        }

        private void OnSystemPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode != PowerModes.Resume)
                return;

            PreySense.Core.Services.CpuUsageMonitor.InvalidateBaseline();
            PreySense.Core.Services.TelemetryCoordinator.InvalidateCurrentSnapshot();
            TriggerDisplayStateRefresh();

            _ = App.Current?.Dispatcher.InvokeAsync(async () =>
            {
                if (_disposed)
                    return;
                await RestoreSessionSettingsAsync("resume");
            });
        }

        private void OnSessionEnding(object sender, SessionEndingEventArgs e)
        {
            PersistSessionSettings();
        }
    }
}
