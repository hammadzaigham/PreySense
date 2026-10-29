using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PreySense.Core.Acpi;
using PreySense.Helpers;

namespace PreySense.Frontend.ViewModels
{
    public partial class MainViewModel
    {
        private const string GpuModeRegistryPath = @"SOFTWARE\PreySense";
        private const string GpuModeRegistryValue = "GpuModePreference";
        private const int DefaultGpuMode = 1;
        private int _gpuProcessRefreshInProgress;
        private int _gpuBatteryTransitionInProgress;

        // GPU Power Target & ACPI Hardware Controls
        [ObservableProperty] private int _gpuPowerTargetWatts = 130;
        [ObservableProperty] private int _gpuPowerTargetMin = GpuPowerLimitConfiguration.MinimumPowerLimitWatts;
        [ObservableProperty] private int _gpuPowerTargetMax = GpuPowerLimitConfiguration.MaximumPowerLimitWatts;
        [ObservableProperty] private int _gpuBasePowerWatts = GpuPowerBaseline.ReadWatts() ?? GpuPowerBaseline.LegacyFallbackWatts;

        partial void OnGpuBasePowerWattsChanged(int value)
        {
            if (SelectedGameProfile != null) OnSelectedGameProfileChanged(SelectedGameProfile);
        }
        [ObservableProperty] private int _customQuietGpuWatts = 85;
        [ObservableProperty] private int _customEcoGpuWatts = 85;
        [ObservableProperty] private int _customBalancedGpuWatts = 115;
        [ObservableProperty] private int _customPerfGpuWatts = 125;
        [ObservableProperty] private int _customTurboGpuWatts = 130;
        [ObservableProperty] private int _gpuThermalTripPointCelsius = GpuPowerLimitConfiguration.FactoryGpuThermalTripPointCelsius; // 79°C ACPI default (WMG3)
        [ObservableProperty] private int _customQuietGpuTripPoint = GpuPowerLimitConfiguration.FactoryGpuThermalTripPointCelsius; // 79°C
        [ObservableProperty] private int _customEcoGpuTripPoint = GpuPowerLimitConfiguration.FactoryGpuThermalTripPointCelsius;
        [ObservableProperty] private int _customBalancedGpuTripPoint = GpuPowerLimitConfiguration.FactoryGpuThermalTripPointCelsius; // 79°C
        [ObservableProperty] private int _customPerfGpuTripPoint = GpuPowerLimitConfiguration.FactoryGpuThermalTripPointCelsius; // 79°C
        [ObservableProperty] private int _customTurboGpuTripPoint = GpuPowerLimitConfiguration.FactoryGpuThermalTripPointCelsius; // 79°C
        [ObservableProperty] private string _gpuStateDescription = "NVIDIA GeForce RTX Mobile";
        [ObservableProperty] private string _activeGpuPowerCeiling = "Max Power Limit: 130W";
        private bool _gpuHardwareInitialized;

        public void EnsureGpuHardwareInitialized()
        {
            if (_gpuHardwareInitialized)
                return;

            _gpuHardwareInitialized = true;
            _ = Task.Run(InitializeGpuHardwareBackground);
        }

        private void InitializeGpuHardwareBackground()
        {
            try
            {
                byte[]? liveDsdt = OpenCorePowerLimitManager.GetLiveAcpiTable("DSDT");
                var config = GpuPowerLimitConfiguration.DetectFromHardware(liveDsdt);

                if (_disposed)
                    return;

                _dispatcher.BeginInvoke(() =>
                {
                    if (_disposed)
                        return;

                    GpuBasePowerWatts = config.BasePowerWatts;
                    GpuPowerTargetMin = config.MinPowerWatts;
                    GpuPowerTargetMax = config.MaxPowerWatts;
                    ActiveGpuPowerCeiling = $"GPU Power Ceiling: {GpuPowerTargetWatts}W (ACPI Max: {GpuPowerTargetMax}W)";
                });
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[MainViewModel] Lazy GPU initialization error: {ex.Message}");
            }
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsGpuOptimusActive))]
        [NotifyPropertyChangedFor(nameof(IsGpuAutoActive))]
        [NotifyPropertyChangedFor(nameof(IsGpuNvidiaOnlyActive))]
        [NotifyPropertyChangedFor(nameof(IsGpuEnduranceActive))]
        [NotifyPropertyChangedFor(nameof(IsGpuStandardActive))]
        [NotifyPropertyChangedFor(nameof(IsGpuUltimateActive))]
        private int _activeGpuMuxMode = 1; // 0 = Optimus (iGPU), 1 = Automatic (Auto DDS), 2 = NVIDIA GPU Only (dGPU)
        [ObservableProperty] private string _gpuMuxModeName = "Automatic";
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsGpuEnduranceActive))]
        [NotifyPropertyChangedFor(nameof(IsGpuStandardActive))]
        [NotifyPropertyChangedFor(nameof(IsGpuUltimateActive))]
        [NotifyPropertyChangedFor(nameof(ShowIgpuOnlyTile))]
        [NotifyPropertyChangedFor(nameof(IsGpuOptimusActive))]
        [NotifyPropertyChangedFor(nameof(IsGpuAutoActive))]
        [NotifyPropertyChangedFor(nameof(IsGpuNvidiaOnlyActive))]
        [NotifyPropertyChangedFor(nameof(IsGpuAdvancedOptimusActive))]
        [NotifyPropertyChangedFor(nameof(ShowDdsGpuTiles))]
        [NotifyPropertyChangedFor(nameof(GpuModeTileColumns))]
        private bool _isBiosDiscreteGpuActive;

        public bool ShowDdsGpuTiles => !IsBiosDiscreteGpuActive;
        public bool ShowIgpuOnlyTile => ShowDdsGpuTiles && !IsGpuNvidiaOnlyActive;
        public int GpuModeTileColumns => IsBiosDiscreteGpuActive ? 2 : 5;

        // GPU selection flags (0=Optimus, 1=Automatic, 2=NVIDIA Only)
        public bool IsGpuEnduranceActive => !IsBiosDiscreteGpuActive && !IsGpuIgpuOnlyActive && ActiveGpuMuxMode == 0;
        public bool IsGpuStandardActive => !IsBiosDiscreteGpuActive && !IsGpuIgpuOnlyActive && ActiveGpuMuxMode == 1;
        public bool IsGpuUltimateActive => !IsBiosDiscreteGpuActive && !IsGpuIgpuOnlyActive && ActiveGpuMuxMode == 2;
        public bool IsGpuOptimusActive => !IsBiosDiscreteGpuActive && !IsGpuIgpuOnlyActive && ActiveGpuMuxMode == 0;
        public bool IsGpuAutoActive => !IsBiosDiscreteGpuActive && !IsGpuIgpuOnlyActive && ActiveGpuMuxMode == 1;
        public bool IsGpuNvidiaOnlyActive => !IsBiosDiscreteGpuActive && !IsGpuIgpuOnlyActive && ActiveGpuMuxMode == 2;
        public bool IsGpuAdvancedOptimusActive => ActiveGpuMuxMode == 2 && !IsBiosDiscreteGpuActive && !IsGpuIgpuOnlyActive;
        [ObservableProperty]
        private bool _isGpuIgpuOnlyActive;

        partial void OnIsGpuIgpuOnlyActiveChanged(bool value)
        {
            // iGPU-only is a separate state from the Advanced Optimus mode
            // index, so all normal GPU tiles must be refreshed when it changes.
            NotifyGpuSelectionsChanged();
        }

        partial void OnIsBiosDiscreteGpuActiveChanged(bool value)
        {
            if (value)
                GpuMuxModeName = "dGPU Only";
        }

        /// <summary>
        /// Re-reads the actual Windows PnP state after a GPU transition.  The
        /// device enable/disable operation is asynchronous from Windows' point
        /// of view, so the command result alone is not reliable enough to drive
        /// the selected tile.
        /// </summary>
        private async Task RefreshGpuDeviceModeStateAsync(int attempts = 1)
        {
            for (int attempt = 0; attempt < Math.Max(1, attempts); attempt++)
            {
                if (attempt > 0)
                    await Task.Delay(350);

                var state = await Task.Run(() =>
                {
                    bool biosDiscrete = false;
                    bool? nvidiaDisabled = null;
                    try { biosDiscrete = _hw.IsBiosDiscreteGpuActive; } catch { }
                    try { nvidiaDisabled = DeviceHelper.IsDisplayDeviceDisabled("VEN_10DE"); } catch { }
                    return (biosDiscrete, nvidiaDisabled);
                });

                if (_disposed)
                    return;

                await _dispatcher.InvokeAsync(() =>
                {
                    if (_disposed) return;
                    IsBiosDiscreteGpuActive = state.biosDiscrete;
                    // Do not clear a confirmed state when WMI is temporarily
                    // unavailable during a device restart.
                    if (state.nvidiaDisabled.HasValue)
                        IsGpuIgpuOnlyActive = state.nvidiaDisabled.Value && !state.biosDiscrete;
                    if (IsBiosDiscreteGpuActive)
                        GpuMuxModeName = "dGPU Only";
                    else if (IsGpuIgpuOnlyActive)
                        GpuMuxModeName = "iGPU Only";
                    NotifyGpuSelectionsChanged();
                });

                // Keep the small bounded retry window: PnP may lag the device
                // command by one or two notifications, including the disabled
                // -> enabled transition.
            }
        }

        // dGPU Battery Sentinel
        public ObservableCollection<PreySense.Gpu.GpuProcessInfo> ActiveGpuProcesses { get; } = new();

        private void NotifyGpuSelectionsChanged()
        {
            OnPropertyChanged(nameof(IsGpuEnduranceActive));
            OnPropertyChanged(nameof(IsGpuStandardActive));
            OnPropertyChanged(nameof(IsGpuUltimateActive));
            OnPropertyChanged(nameof(IsGpuOptimusActive));
            OnPropertyChanged(nameof(IsGpuAutoActive));
            OnPropertyChanged(nameof(IsGpuNvidiaOnlyActive));
            OnPropertyChanged(nameof(IsGpuAdvancedOptimusActive));
            OnPropertyChanged(nameof(IsGpuIgpuOnlyActive));
        }

        private static string GetGpuModeName(int mode) => mode switch
        {
            2 => "NVIDIA GPU Only",
            1 => "Automatic",
            _ => "Optimus"
        };

        private static int LoadGpuModePreference()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(GpuModeRegistryPath);
                object? savedValue = key?.GetValue(GpuModeRegistryValue);
                int mode = savedValue is null
                    ? DefaultGpuMode
                    : Math.Clamp(Convert.ToInt32(savedValue), 0, 2);

                if (savedValue is null || savedValue is not int || (int)savedValue != mode)
                    key?.SetValue(GpuModeRegistryValue, mode, Microsoft.Win32.RegistryValueKind.DWord);

                return mode;
            }
            catch
            {
                return DefaultGpuMode;
            }
        }

        private static void SaveGpuModePreference(int mode)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(GpuModeRegistryPath);
                key?.SetValue(GpuModeRegistryValue, Math.Clamp(mode, 0, 2), Microsoft.Win32.RegistryValueKind.DWord);
            }
            catch { }
        }

        [RelayCommand]
        public async Task ApplyGpuClocksAsync()
        {
            IsBusy = true;
            StatusMessage = "Applying GPU Power Limits via ACPI...";
            try
            {
                // Apply GPU power limit ceiling dynamically via dedicated ACPI-WMI device
                var offsets = GpuOffsetsForMode(GpuModeIndex(ActivePowerMode), GpuPowerTargetWatts);
                bool pwrSuccess = await Task.Run(() => PreySense.Core.Hardware.PreySenseWmiDeviceClient.SetGpuPowerOffsets(offsets));

                // Apply GPU thermal trip point dynamically via dedicated ACPI-WMI device
                bool tripSuccess = await Task.Run(() => PreySense.Core.Hardware.PreySenseWmiDeviceClient.SetGpuThermalTripPoint((uint)GpuThermalTripPointCelsius));

                if (!pwrSuccess || !tripSuccess)
                {
                    StatusMessage = "GPU settings failed or ACPI readback differed. Independent offsets require PREYDEV 0x0201 and its WMI schema; update the OpenCore payload and reboot.";
                    return;
                }
                ActiveGpuPowerCeiling = $"ACPI offsets: CT {offsets.DeltaWatts}W / DB {offsets.DynamicBoostWatts}W (verified)";
                StatusMessage = $"ACPI offsets verified: Delta {offsets.DeltaWatts}W, Dynamic Boost {offsets.DynamicBoostWatts}W. Actual GPU power remains driver controlled.";
                AppLogger.Log(StatusMessage);
            }
            catch (Exception ex)
            {
                StatusMessage = $"GPU Control error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task SetGpuMuxModeAsync(object? muxParam)
        {
            if (muxParam is string text && string.Equals(text, "igpu", StringComparison.OrdinalIgnoreCase))
            {
                await ApplyIgpuOnlyAsync();
                return;
            }

            int muxVal = muxParam switch
            {
                int i => i,
                byte b => (int)b,
                string s when int.TryParse(s, out int parsed) => parsed,
                _ => -1
            };
            if (muxVal < 0 || muxVal > 2) return;

            if (IsBiosDiscreteGpuActive)
            {
                if (muxVal == 1)
                    await SetBiosGpuModeAsync(PreySense.Helpers.AcerWmi.GpuMux.Automatic, "Would you like to switch to Auto and restart now?", "PreySense • Auto", " • Auto");
                return;
            }

            await ApplyGpuMuxModeCoreAsync(muxVal, persistPreference: true);
        }

        private async Task ApplyGpuMuxModeCoreAsync(int muxVal, bool persistPreference)
        {
            // Any normal GPU mode requires the NVIDIA adapter to be enabled
            // again. Previously only Hybrid did this, leaving Auto and dGPU-only
            // selected in the UI while the adapter remained disabled.
            bool leavingIgpuOnly = IsGpuIgpuOnlyActive;
            ActiveGpuMuxMode = muxVal;
            IsGpuIgpuOnlyActive = false;
            IsBiosDiscreteGpuActive = false;
            GpuMuxModeName = GetGpuModeName(muxVal);
            NotifyGpuSelectionsChanged();

            if (persistPreference)
            {
                SaveGpuModePreference(muxVal);
                _gpuModeDeferredForBattery = null;
            }

            StatusMessage = $"Switching GPU Mode to {GpuMuxModeName}...";

            IsBusy = true;
            try
            {
                if (leavingIgpuOnly)
                {
                    await Task.Run(() =>
                    {
                        PreySense.Gpu.NvidiaGpuControl.RestartNVService();
                        PreySense.Helpers.DeviceHelper.SetDeviceState("VEN_10DE", true);
                    });
                }

                if (!_hw.IsDdsAvailable || !_hw.IsDdsSupported())
                {
                    StatusMessage = "GPU Mode change failed: NVIDIA Advanced Optimus is not available.";
                    return;
                }

                var targetDdsMode = muxVal switch
                {
                    2 => Gpu.DdsDisplayMode.NvidiaGpuOnly,
                    1 => Gpu.DdsDisplayMode.Automatic,
                    _ => Gpu.DdsDisplayMode.Optimus
                };

                if (_hw.HasDdsBlockers())
                {
                    AppLogger.Log("MainViewModel.SetGpuMuxMode: Active 3D application detected during switch.");
                }

                // Temporary WPF swapchain release mitigation (dotnet/wpf #9286)
                var prevRenderMode = System.Windows.Media.RenderOptions.ProcessRenderMode;
                try
                {
                    System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
                    var (ok, msg) = await Task.Run(() => _hw.SetDdsMode(targetDdsMode));
                    StatusMessage = ok ? $"GPU Mode set to {GpuMuxModeName} (Advanced Optimus)" : $"GPU switch failed: {msg}";
                }
                finally
                {
                    System.Windows.Media.RenderOptions.ProcessRenderMode = prevRenderMode;
                }

                // Enabling the adapter and restarting NV service can complete in
                // either order.  Reconcile the tile with PnP after both settle.
                if (leavingIgpuOnly)
                    await RefreshGpuDeviceModeStateAsync(attempts: 3);
            }
            catch (Exception ex)
            {
                StatusMessage = $"GPU Mode error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ApplyIgpuOnlyAsync()
        {
            if (IsBiosDiscreteGpuActive || IsGpuIgpuOnlyActive)
                return;
            if (!ConfirmGpuBatterySwitchIfNeeded())
                return;

            IsBusy = true;
            try
            {
                bool ok = await Task.Run(() =>
                {
                    try { PreySense.Gpu.NvidiaGpuControl.Shared.KillGPUApps(); } catch { }
                    try { PreySense.Gpu.NvidiaGpuControl.StopNVService(); } catch { }
                    return PreySense.Helpers.DeviceHelper.SetDeviceState("VEN_10DE", false);
                });
                if (ok)
                {
                    // PnP state is authoritative. Give Windows a moment to
                    // publish the disable and then refresh the tile from it.
                    await RefreshGpuDeviceModeStateAsync(attempts: 3);
                    if (!IsGpuIgpuOnlyActive)
                    {
                        StatusMessage = "NVIDIA dGPU disable was not confirmed by Windows.";
                        return;
                    }
                    StatusMessage = "GPU Mode set to iGPU Only";
                }
                else
                {
                    StatusMessage = "Could not disable the NVIDIA dGPU.";
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ApplyGpuBatteryAuto(bool isAcConnected)
        {
            if (!IsGpuBatteryAutoEnabled || IsBiosDiscreteGpuActive)
                return;

            if (!isAcConnected)
            {
                // Battery automation applies to Auto and Hybrid/Optimus. Do
                // not override NVIDIA-only or Advanced Optimus modes.
                if ((ActiveGpuMuxMode is 0 or 1) && !IsGpuIgpuOnlyActive)
                    _ = ApplyGpuBatteryOnlyAsync();
                return;
            }

            // Restore the eligible mode that was active before battery mode:
            // Auto returns to Auto and Hybrid/Optimus returns to Hybrid.
            if (_gpuModeDeferredForBattery is 0 or 1)
            {
                int restoreMode = _gpuModeDeferredForBattery.Value;
                _gpuModeDeferredForBattery = null;
                if (IsGpuIgpuOnlyActive)
                    _ = ApplyGpuMuxModeCoreAsync(restoreMode, persistPreference: false);
            }
        }

        private async Task ApplyGpuBatteryOnlyAsync()
        {
            if (Interlocked.Exchange(ref _gpuBatteryTransitionInProgress, 1) != 0)
                return;

            if (!IsGpuBatteryAutoEnabled || IsBiosDiscreteGpuActive || ActiveGpuMuxMode is not (0 or 1) || IsGpuIgpuOnlyActive)
            {
                Volatile.Write(ref _gpuBatteryTransitionInProgress, 0);
                return;
            }

            try
            {
            int restoreMode = ActiveGpuMuxMode;
            _gpuModeDeferredForBattery = restoreMode;
                await ApplyIgpuOnlyAsync();
                if (!IsGpuIgpuOnlyActive)
                    _gpuModeDeferredForBattery = null;
                else
                    StatusMessage = "iGPU on Battery: switched to iGPU Only";
            }
            finally
            {
                Volatile.Write(ref _gpuBatteryTransitionInProgress, 0);
            }
        }

        [RelayCommand]
        public void ToggleGpuBatteryAuto()
        {
            IsGpuBatteryAutoEnabled = !IsGpuBatteryAutoEnabled;
        }

        partial void OnIsGpuBatteryAutoEnabledChanged(bool value)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"SOFTWARE\PreySense\AdvancedSettings");
                key?.SetValue("GpuBatteryAuto", value ? 1 : 0);
            }
            catch { }

            PersistSessionSettings();
            StatusMessage = value
                ? "iGPU on Battery: Enabled (iGPU-only while unplugged)"
                : "iGPU on Battery: Disabled";

            if (value)
                ApplyGpuBatteryAuto(IsAcConnected);
        }

        private bool ConfirmGpuBatterySwitchIfNeeded()
        {
            try
            {
                var processes = _hw.GetActiveGpuProcesses();
                if (processes.Count == 0)
                    return true;

                bool? result = false;
                void Show()
                {
                    App.SuppressAutoHide = true;
                    try
                    {
                        var names = string.Join(", ", processes.Take(5).Select(p => p.ProcessName));
                        if (processes.Count > 5) names += $" and {processes.Count - 5} more";
                        var dialog = new Views.CustomModalDialog
                        {
                            Owner = System.Windows.Application.Current?.MainWindow,
                            Title = "PreySense • dGPU Processes Active",
                            HeaderSubtitle = " • Switch to iGPU Only",
                            Message = $"The following processes are currently using the dGPU:\n\n{names}\n\nSwitching to iGPU-only mode may interrupt or close GPU workloads. Do you still want to switch?",
                            ConfirmText = "Switch Anyway",
                            CancelText = "Cancel",
                            ShowCancelButton = true
                        };
                        result = dialog.ShowDialog();
                    }
                    finally
                    {
                        App.SuppressAutoHide = false;
                    }
                }

                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher == null) return false;
                if (dispatcher.CheckAccess()) Show(); else dispatcher.Invoke(Show);
                return result == true;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"GPU battery safety confirmation failed: {ex.Message}");
                return false;
            }
        }

        [RelayCommand]
        public async Task RestartDiscreteGpuAsync()
        {
            IsBusy = true;
            StatusMessage = "Power-cycling and restarting Discrete GPU (reboot-free)...";
            try
            {
                var (ok, msg) = await Task.Run(() => _hw.RestartDiscreteGpu());
                StatusMessage = msg;
                var showModal = () =>
                {
                    var dialog = new Views.CustomModalDialog
                    {
                        Owner = System.Windows.Application.Current?.MainWindow,
                        Title = "PreySense • Discrete GPU Power Cycle",
                        HeaderSubtitle = " • Discrete GPU Power Cycle",
                        Message = msg,
                        ConfirmText = "OK",
                        ShowCancelButton = false
                    };
                    dialog.ShowDialog();
                };

                if (System.Windows.Application.Current?.Dispatcher != null && !System.Windows.Application.Current.Dispatcher.CheckAccess())
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(showModal);
                }
                else
                {
                    showModal();
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"dGPU restart error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
                PollTelemetry();
            }
        }

        [RelayCommand]
        public async Task SetBiosDiscreteGpuAsync()
        {
            if (IsBiosDiscreteGpuActive)
                return;

            await SetBiosGpuModeAsync(
                PreySense.Helpers.AcerWmi.GpuMux.Discrete,
                "Would you like to switch to dGPU Only mode and restart now?",
                "PreySense • Hardware Discrete GPU",
                " • Hardware Display MUX");
        }

        private async Task SetBiosGpuModeAsync(int muxMode, string message, string title, string subtitle)
        {
            if (!ConfirmGpuRestart(message, title, subtitle))
                return;

            IsBusy = true;
            try
            {
                bool ok = await Task.Run(() => _hw.SetGpuMuxMode(muxMode));
                if (ok)
                {
                    IsBiosDiscreteGpuActive = muxMode == PreySense.Helpers.AcerWmi.GpuMux.Discrete;
                    NotifyGpuSelectionsChanged();
                    StatusMessage = "Restarting to apply the GPU mode...";
                    RestartToApplyGpuMode();
                }
                else
                {
                    StatusMessage = "Failed to set the GPU mode via Acer ACPI.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"ACPI MUX error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private static bool ConfirmGpuRestart(string message, string title, string subtitle)
        {
            if (System.Windows.Application.Current?.Dispatcher == null)
                return false;

            bool? dialogResult = false;
            void Show()
            {
                App.SuppressAutoHide = true;
                try
                {
                    var dialog = new Views.CustomModalDialog
                    {
                        Owner = System.Windows.Application.Current?.MainWindow,
                        Title = title,
                        HeaderSubtitle = subtitle,
                        Message = message,
                        ConfirmText = "Apply & Restart Now"
                    };
                    dialogResult = dialog.ShowDialog();
                }
                finally
                {
                    App.SuppressAutoHide = false;
                }
            }

            if (System.Windows.Application.Current.Dispatcher.CheckAccess())
                Show();
            else
                System.Windows.Application.Current.Dispatcher.Invoke(Show);

            return dialogResult == true;
        }

        private static void RestartToApplyGpuMode()
        {
            // /t 0 and no comment. A delayed shutdown with /c opens a second Windows dialog.
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "shutdown.exe",
                Arguments = "/r /t 0 /f",
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }

        public void SaveGpuLimitsToRegistry()
        {
            try
            {
                SaveGpuOffsets();
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"SOFTWARE\PreySense\GpuLimits");
                if (key != null)
                {
                    key.SetValue("QuietGpuWatts", CustomQuietGpuWatts);
                    key.SetValue("EcoGpuWatts", CustomEcoGpuWatts);
                    key.SetValue("BalancedGpuWatts", CustomBalancedGpuWatts);
                    key.SetValue("PerfGpuWatts", CustomPerfGpuWatts);
                    key.SetValue("TurboGpuWatts", CustomTurboGpuWatts);
                    key.SetValue("QuietGpuTripPoint", CustomQuietGpuTripPoint);
                    key.SetValue("EcoGpuTripPoint", CustomEcoGpuTripPoint);
                    key.SetValue("BalancedGpuTripPoint", CustomBalancedGpuTripPoint);
                    key.SetValue("PerfGpuTripPoint", CustomPerfGpuTripPoint);
                    key.SetValue("TurboGpuTripPoint", CustomTurboGpuTripPoint);
                    AppLogger.Log("[MainViewModel] Saved GPU power & thermal limits to registry.");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[MainViewModel] SaveGpuLimitsToRegistry error: {ex.Message}");
            }
        }

        [RelayCommand]
        public async Task RefreshGpuProcessesAsync()
        {
            // Process enumeration can be expensive (NVML + Process APIs). Do not
            // allow a second refresh to start while one is already in flight.
            if (System.Threading.Interlocked.Exchange(ref _gpuProcessRefreshInProgress, 1) != 0)
                return;

            try
            {
                var processes = await Task.Run(() => _hw.GetActiveGpuProcesses());
                var byPid = new Dictionary<uint, PreySense.Gpu.GpuProcessInfo>(processes.Count);
                foreach (var process in processes)
                    byPid[process.ProcessId] = process;

                for (int i = ActiveGpuProcesses.Count - 1; i >= 0; i--)
                {
                    if (!byPid.ContainsKey(ActiveGpuProcesses[i].ProcessId))
                        ActiveGpuProcesses.RemoveAt(i);
                }

                var existingByPid = new Dictionary<uint, PreySense.Gpu.GpuProcessInfo>(ActiveGpuProcesses.Count);
                foreach (var existing in ActiveGpuProcesses)
                    existingByPid[existing.ProcessId] = existing;

                foreach (var process in processes)
                {
                    if (!existingByPid.TryGetValue(process.ProcessId, out var existing))
                        ActiveGpuProcesses.Add(process);
                    else if (!string.Equals(existing.ProcessName, process.ProcessName, StringComparison.Ordinal))
                        existing.ProcessName = process.ProcessName;
                }

                StatusMessage = ActiveGpuProcesses.Count > 0
                    ? $"{ActiveGpuProcesses.Count} process(es) holding discrete GPU active"
                    : "No background processes keeping discrete GPU awake (0W D3Cold ready)";
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref _gpuProcessRefreshInProgress, 0);
            }
        }

        [RelayCommand]
        public async Task KillGpuProcessAsync(object? param)
        {
            if (param is uint pid)
            {
                await Task.Run(() => _hw.KillProcess(pid));
                await RefreshGpuProcessesAsync();
            }
        }
    }
}
