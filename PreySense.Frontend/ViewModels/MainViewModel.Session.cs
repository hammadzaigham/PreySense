using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;
using PreySense.Core.Acpi;
using PreySense.Core.Hardware;
using PreySense.Core.Services;
using PreySense.Frontend.Services;
using PreySense.Helpers;
using PreySense.Mode;

namespace PreySense.Frontend.ViewModels
{
    public partial class MainViewModel
    {
        private const string SessionRegistryPath = @"SOFTWARE\PreySense\Session";
        private bool _sessionHooksAttached;
        private bool _isRestoringSession;
        private readonly TaskCompletionSource _startupWindowReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void NotifyMainWindowLoaded() => _startupWindowReady.TrySetResult();
        private DateTime _lastSessionRestoreUtc = DateTime.MinValue;

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
                using var key = Registry.CurrentUser.OpenSubKey(SessionRegistryPath);
                if (key == null)
                {
                    _currentPowerSource = _hw.GetPowerSource();
                    return;
                }

                if (key.GetValue("PowerMode") is int savedMode)
                {
                    byte mode = (byte)Math.Clamp(savedMode, 0, 6);
                    if (mode is 0 or 1 or 4 or 5 or 6)
                    {
                        ActivePowerMode = mode;
                        UpdateModeName(mode);
                    }
                }

                if (key.GetValue("AutoRefreshRate") is int autoHz)
                    IsAutoRefreshRateEnabled = autoHz == 1;

                if (key.GetValue("UsbChargingPercent") is int usb)
                    UsbChargingPercent = usb is 0 or 10 or 20 or 30 ? usb : UsbChargingPercent;

                if (key.GetValue("BatteryLimit") is int batteryLimit)
                    IsBatteryLimitEnabled = batteryLimit == 1;

                if (key.GetValue("AcerServicesDisabled") is int debloated)
                    IsAcerServicesDebloated = debloated == 1;
                else if (key.GetValue("AcerServicesEnabled") is int enabled)
                    IsAcerServicesDebloated = enabled == 0;

                _suppressPowerProfile = true;
                int? legacyPlugged = key.GetValue("UsbCAcPowerMode") is int legacyMode && legacyMode is 0 or 1 or 4 or 5 or 6
                    ? legacyMode
                    : null;
                if (key.GetValue("AcPowerMode") is int acMode && acMode is 0 or 1 or 4 or 5 or 6)
                    AcPowerModeIndex = ProfileManager.SourceProfileIndex((byte)acMode);
                else if (legacyPlugged is int legacyAc)
                    AcPowerModeIndex = ProfileManager.SourceProfileIndex((byte)legacyAc);
                if (key.GetValue("UsbCPowerMode") is int usbCMode && usbCMode is 0 or 1 or 4 or 5 or 6)
                    UsbCPowerModeIndex = ProfileManager.SourceProfileIndex((byte)usbCMode);
                else if (legacyPlugged is int legacyUsb)
                    UsbCPowerModeIndex = ProfileManager.SourceProfileIndex((byte)legacyUsb);
                if (key.GetValue("BatteryPowerMode") is int batteryMode && batteryMode is 0 or 1 or 4 or 5 or 6)
                    BatteryPowerModeIndex = ProfileManager.SourceProfileIndex((byte)batteryMode);
                if (key.GetValue("BatteryEnergySaver") is int energySaver)
                    IsBatteryEnergySaverEnabled = energySaver == 1;
                if (key.GetValue("LastPluggedSource") is int lastPlugged && lastPlugged is 1 or 4)
                    _hw.NotePluggedSource(lastPlugged == 4 ? PowerSourceKind.UsbC : PowerSourceKind.Ac);
                _currentPowerSource = _hw.GetPowerSource();
                _suppressPowerProfile = false;
            }
            catch (Exception ex)
            {
                _suppressPowerProfile = false;
                AppLogger.Log($"LoadSessionPreferences: {ex.Message}");
            }
        }

        public void PersistSessionSettings()
        {
            if (_disposed || _isRestoringSession)
                return;

            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(SessionRegistryPath);
                if (key == null)
                    return;

                key.SetValue("PowerMode", (int)ActivePowerMode, RegistryValueKind.DWord);
                key.SetValue("AutoRefreshRate", IsAutoRefreshRateEnabled ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("UsbChargingPercent", UsbChargingPercent, RegistryValueKind.DWord);
                key.SetValue("BatteryLimit", IsBatteryLimitEnabled ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("AcerServicesDisabled", IsAcerServicesDebloated ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("AcerServicesEnabled", IsAcerServicesDebloated ? 0 : 1, RegistryValueKind.DWord);
                key.SetValue("AcPowerMode", (int)ProfileManager.SourceProfileMode(AcPowerModeIndex), RegistryValueKind.DWord);
                key.SetValue("UsbCPowerMode", (int)ProfileManager.SourceProfileMode(UsbCPowerModeIndex), RegistryValueKind.DWord);
                key.SetValue("BatteryPowerMode", (int)ProfileManager.SourceProfileMode(BatteryPowerModeIndex), RegistryValueKind.DWord);
                key.SetValue("BatteryEnergySaver", IsBatteryEnergySaverEnabled ? 1 : 0, RegistryValueKind.DWord);
                if (_currentPowerSource is PowerSourceKind.Ac or PowerSourceKind.UsbC)
                    key.SetValue("LastPluggedSource", _currentPowerSource == PowerSourceKind.UsbC ? 4 : 1, RegistryValueKind.DWord);
            }
            catch (Exception ex)
            {
                AppLogger.Log($"PersistSessionSettings: {ex.Message}");
            }

            SaveRgbSettings();
            SaveCoverLightingSettings();
        }

        private async Task ValidateOpenCoreIntegrationAsync(bool promptForUpdate = true)
        {
            if (_disposed)
                return;

            OpenCoreSecurityValidator.OpenCoreIntegrationState state = OpenCoreSecurityValidator.OpenCoreIntegrationState.NotLoaded;
            string detail = string.Empty;
            bool partitionPresent = false;
            await Task.Run(() =>
            {
                try
                {
                    string mofPath = Path.Combine(Path.GetTempPath(), "preysense.mof");
                    File.WriteAllBytes(mofPath, ReadEmbeddedAcpiPayload("PreySense.ACPI.preysense.mof"));
                    bool knownVersion = PreySenseWmiRepair.TryParseInterfaceVersion(File.ReadAllText(mofPath), out uint expected);
                    bool loaded = OpenCoreSecurityValidator.TryGetPreyDevOemRevision(out uint loadedRevision);
                    bool loadedMatches = knownVersion && loaded && loadedRevision == expected;

                    bool wmiReady = false;
                    if (loadedMatches)
                        wmiReady = PreySenseWmiRepair.TryRegister(mofPath, out detail);

                    // A pending WMI registration flag does not prove the EFI files are current.
                    bool installedRead = OpenCoreSecurityValidator.TryReadInstalledPreyDevRevision(out uint installedRevision);
                    bool installedCurrent = knownVersion && installedRead && installedRevision == expected;

                    try { partitionPresent = OpenCoreSecurityValidator.IsPreySensePartitionPresent(); } catch { }
                    bool integrationPresent = partitionPresent || loaded || installedCurrent;
                    state = OpenCoreSecurityValidator.DecideIntegrationState(loadedMatches, wmiReady, installedCurrent, integrationPresent,
                        installedPackageKnownOutdated: knownVersion && partitionPresent && installedRead && !installedCurrent);
                }
                catch (Exception ex)
                {
                    detail = ex.Message;
                }
            });

            if (_disposed)
                return;

            await _dispatcher.InvokeAsync(() =>
            {
                if (_disposed)
                    return;

                IsOpenCorePartitionPresent = partitionPresent;
                ApplyOpenCoreIntegrationState(state);
                if (state == OpenCoreSecurityValidator.OpenCoreIntegrationState.RestartRequired)
                    StatusMessage = "Restart Required";
                else if (state != OpenCoreSecurityValidator.OpenCoreIntegrationState.Active &&
                         !string.IsNullOrWhiteSpace(detail))
                    StatusMessage = detail;
                else if (state == OpenCoreSecurityValidator.OpenCoreIntegrationState.Active &&
                         !string.IsNullOrWhiteSpace(detail))
                    StatusMessage = detail;
            });

            if (promptForUpdate && state == OpenCoreSecurityValidator.OpenCoreIntegrationState.UpdateRequired && !_disposed)
                await _dispatcher.InvokeAsync(PromptOpenCoreReinstall, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        private async Task InitializeSessionAsync()
        {
            await _startupWindowReady.Task;
            if (_disposed) return;
            var pawnInstallation = await _dispatcher.InvokeAsync(() => PawnIoInstallService.EnsureInstalledAsync(
                System.Windows.Application.Current.MainWindow, message => StatusMessage = message));
            await pawnInstallation;
            if (_disposed) return;
            await ValidateOpenCoreIntegrationAsync();
            if (_disposed) return;
            // Migrate only the missing baseline metadata for already locked profiles.
            if (IsOpenCoreLoaded && FactoryCalibrationIsLocked()) await CaptureGpuBaselineOnceAsync();
            await PromptStartupFactoryCalibrationAsync();
            if (_disposed || IsOpenCoreLoaded && !IsFactoryCalibrationComplete) return;
            await RestoreSessionSettingsAsync("startup");
            if (_disposed)
                return;

            int batteryLimit = IsBatteryLimitEnabled ? 1 : 0;
            int usbLevel = UsbChargingPercent;

            await Task.Run(() =>
            {
                try { batteryLimit = _hw.GetBatteryChargeLimit(); } catch { }
                try
                {
                    if (usbLevel == 0)
                        usbLevel = _hw.GetUsbChargingLevel();
                }
                catch { }
            });

            if (_disposed)
                return;

            await _dispatcher.InvokeAsync(() =>
            {
                if (_disposed)
                    return;

                IsBatteryLimitEnabled = batteryLimit == 1;
                if (UsbChargingPercent == 0 && usbLevel is 10 or 20 or 30)
                    UsbChargingPercent = usbLevel;
            });

        }

        public async Task RestoreSessionSettingsAsync(string reason)
        {
            if (_disposed || IsFactoryCalibrationRunning || IsOpenCoreLoaded && !IsFactoryCalibrationComplete)
                return;

            if ((DateTime.UtcNow - _lastSessionRestoreUtc).TotalSeconds < 3 &&
                !string.Equals(reason, "startup", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _lastSessionRestoreUtc = DateTime.UtcNow;
            _isRestoringSession = true;
            bool gateHeld = false;
            try
            {
                await _modeSwitchGate.WaitAsync();
                gateHeld = true;
                AppLogger.Log($"Restoring PreySense session settings ({reason}).");
                byte mode = ActivePowerMode;
                PowerSourceKind source = PowerSourceKind.Ac;
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

                    mode = ProfileManager.SourceProfileMode(source switch
                    {
                        PowerSourceKind.UsbC => UsbCPowerModeIndex,
                        PowerSourceKind.Battery => BatteryPowerModeIndex,
                        _ => AcPowerModeIndex
                    });
                    if (IsBatteryEnergySaverEnabled)
                        PowerLimitController.SetEnergySaverOnBattery(true);

                    restoreWarning = ApplyPersistedHardwareSettings(mode);
                    if (string.Equals(reason, "startup", StringComparison.OrdinalIgnoreCase))
                        ApplyStartupBatteryRefreshRate();
                });

                if (string.Equals(reason, "resume", StringComparison.OrdinalIgnoreCase) && OverlayBridge.ShouldRun)
                    _ = App.Current?.Dispatcher.InvokeAsync(OverlayBridge.StartIfNeeded);

                _ = App.Current?.Dispatcher.InvokeAsync(() =>
                {
                    if (_disposed)
                        return;
                    _currentPowerSource = source;
                    ActivePowerMode = mode;
                    UpdateModeName(mode);
                    NotifyModeSelectionsChanged();
                    StatusMessage = reason.Equals("startup", StringComparison.OrdinalIgnoreCase)
                        ? "Saved settings applied."
                        : "Saved settings reapplied after wake.";
                    if (!string.IsNullOrEmpty(restoreWarning))
                        StatusMessage = "Settings partially applied: " + restoreWarning;
                });
            }
            catch (Exception ex)
            {
                AppLogger.Log($"RestoreSessionSettingsAsync ({reason}): {ex.Message}");
                await _dispatcher.InvokeAsync(() =>
                {
                    if (!_disposed) StatusMessage = $"Settings restore failed: {ex.Message}";
                });
            }
            finally
            {
                _isRestoringSession = false;
                if (gateHeld)
                {
                    try { _modeSwitchGate.Release(); }
                    catch (ObjectDisposedException) { }
                }
            }
        }

        private string ApplyPersistedHardwareSettings(byte? modeOverride = null)
        {
            byte mode = modeOverride ?? ActivePowerMode;
            var failures = new System.Collections.Generic.List<string>();
            ApplyKeyboardLightingNow();
            ApplyCoverLighting(saveSettings: false);
            if (!_hw.SetPowerMode(mode))
                throw new InvalidOperationException("The saved Acer mode could not be activated; its custom limits were not applied.");

            int pl1 = mode switch
            {
                0x05 => CustomTurboPl1,
                0x04 => CustomPerfPl1,
                0x01 => CustomBalancedPl1,
                0x06 => CustomEcoPl1,
                _ => CustomQuietPl1
            };
            int pl2 = mode switch
            {
                0x05 => CustomTurboPl2,
                0x04 => CustomPerfPl2,
                0x01 => CustomBalancedPl2,
                0x06 => CustomEcoPl2,
                _ => CustomQuietPl2
            };
            int gpuWatts = mode switch
            {
                0x05 => CustomTurboGpuWatts,
                0x04 => CustomPerfGpuWatts,
                0x01 => CustomBalancedGpuWatts,
                0x06 => CustomEcoGpuWatts,
                _ => CustomQuietGpuWatts
            };
            int gpuTrip = mode switch
            {
                0x05 => CustomTurboGpuTripPoint,
                0x04 => CustomPerfGpuTripPoint,
                0x01 => CustomBalancedGpuTripPoint,
                0x06 => CustomEcoGpuTripPoint,
                _ => CustomQuietGpuTripPoint
            };

            int limitedPl1 = LimitPl1ToPlatform(pl1);
            TauSeconds = TauForMode(mode);
            if (IsFactoryCalibrationComplete && IsOpenCoreLoaded && IsAcpiOverrideEnabled)
            {
                if (!PowerLimitController.SetCpuPowerLimits(limitedPl1, LimitPl2(limitedPl1, pl2), true, TauSeconds))
                    failures.Add("CPU limits: " + PowerLimitController.LastCpuPowerLimitStatus);
                ApplyCpuBoostForMode(mode);
                if (!ApplyGpuModePower(mode, gpuWatts))
                    failures.Add("GPU offsets could not be verified");
                if (!PreySense.Core.Hardware.PreySenseWmiDeviceClient.SetGpuThermalTripPoint((uint)gpuTrip))
                    failures.Add("GPU thermal trip point could not be applied");
            }

            RestoreFanMode();

            if (IsLcdOverdriveEnabled)
                _hw.SetLcdOverdrive(true);
            _hw.SetBlueLightShield(BlueLightShieldMode);
            _hw.SetUsbChargingLevel(UsbChargingPercent);
            _hw.SetBatteryChargeLimit(IsBatteryLimitEnabled ? 1 : 0);
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
                onBattery = !_hw.GetCurrentTelemetry().IsAcConnected;
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
