using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PreySense.Core.Driver;
using PreySense.Core.Services;
using PreySense.Frontend.Services;
using PreySense.Gpu;
using PreySense.Helpers;

namespace PreySense.Frontend.ViewModels
{
    public partial class MainViewModel
    {
        private int _gpuProcessRefreshInProgress;
        private int _gpuSwitchInProgress;
        private int _gpuDisplayRevision;

        // GPU Power Target & Driver Hardware Controls
        [ObservableProperty] private int _gpuPowerTargetWatts = 130;
        [ObservableProperty] private int _gpuPowerTargetMin = GpuPowerLimitConfiguration.MinimumPowerLimitWatts;
        [ObservableProperty] private int _gpuPowerTargetMax = GpuPowerLimitConfiguration.MaximumPowerLimitWatts;
        [ObservableProperty] private int? _gpuBasePowerWatts = GpuPowerBaseline.ReadWatts();

        [ObservableProperty] private int _customQuietGpuWatts = 85;
        [ObservableProperty] private int _customEcoGpuWatts = 85;
        [ObservableProperty] private int _customBalancedGpuWatts = 115;
        [ObservableProperty] private int _customPerfGpuWatts = 125;
        [ObservableProperty] private int _customTurboGpuWatts = 130;
        [ObservableProperty] private string _gpuStateDescription = "N/A";
        [ObservableProperty] private string _activeGpuPowerCeiling = "N/A";
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
                int? baseline = GpuPowerBaseline.ReadWatts();
                if (!baseline.HasValue)
                {
                    int? detected = PreySense.Gpu.NvmlHelper.GetGpuDefaultPowerLimitWatts();
                    if (detected.HasValue)
                        baseline = GpuPowerBaseline.SaveOnce(detected.Value);
                }

                if (_disposed)
                    return;

                _dispatcher.BeginInvoke(() =>
                {
                    if (_disposed)
                        return;

                    GpuBasePowerWatts = baseline;
                    ActiveGpuPowerCeiling = "N/A";
                });
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[MainViewModel] Lazy GPU initialization error: {ex.Message}");
            }
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowIgpuOnlyTile))]
        [NotifyPropertyChangedFor(nameof(GpuModeTileColumns))]
        [NotifyPropertyChangedFor(nameof(IsGpuOptimusActive))]
        [NotifyPropertyChangedFor(nameof(IsGpuAutoActive))]
        [NotifyPropertyChangedFor(nameof(IsGpuNvidiaOnlyActive))]
        [NotifyPropertyChangedFor(nameof(IsGpuEnduranceActive))]
        [NotifyPropertyChangedFor(nameof(IsGpuStandardActive))]
        [NotifyPropertyChangedFor(nameof(IsGpuUltimateActive))]
        [NotifyPropertyChangedFor(nameof(IsGpuAdvancedOptimusActive))]
        private int _activeGpuMuxMode = -1; // 0 = Optimus (iGPU), 1 = Automatic (Auto DDS), 2 = NVIDIA GPU Only (dGPU)
        [ObservableProperty] private string _gpuMuxModeName = "N/A";
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsGpuEnduranceActive))]
        [NotifyPropertyChangedFor(nameof(IsGpuStandardActive))]
        [NotifyPropertyChangedFor(nameof(IsGpuUltimateActive))]
        [NotifyPropertyChangedFor(nameof(ShowIgpuOnlyTile))]
        [NotifyPropertyChangedFor(nameof(ShowAdvancedOptimusTile))]
        [NotifyPropertyChangedFor(nameof(ShowDgpuOnlyTile))]
        [NotifyPropertyChangedFor(nameof(GpuModeTileColumns))]
        [NotifyPropertyChangedFor(nameof(IsGpuOptimusActive))]
        [NotifyPropertyChangedFor(nameof(IsGpuAutoActive))]
        [NotifyPropertyChangedFor(nameof(IsGpuNvidiaOnlyActive))]
        [NotifyPropertyChangedFor(nameof(IsGpuAdvancedOptimusActive))]
        [NotifyPropertyChangedFor(nameof(ShowDdsGpuTiles))]
        private bool _isBiosDiscreteGpuActive;

        public bool ShowDdsGpuTiles => !IsBiosDiscreteGpuActive;
        public bool ShowIgpuOnlyTile => ShowDdsGpuTiles && !IsGpuNvidiaOnlyActive;
        public bool ShowAdvancedOptimusTile => ShowDdsGpuTiles && !IsGpuIgpuOnlyActive;
        public bool ShowDgpuOnlyTile => !IsGpuIgpuOnlyActive;
        public int GpuModeTileColumns
        {
            get
            {
                int count = 1; // Auto
                if (ShowIgpuOnlyTile) count += 1; // iGPU Only
                if (ShowDdsGpuTiles) count += 1; // Hybrid
                if (ShowAdvancedOptimusTile) count += 1; // Advanced Optimus
                if (ShowDgpuOnlyTile) count += 1; // dGPU Only
                return Math.Max(1, count);
            }
        }

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
        private async Task RefreshGpuDeviceModeStateAsync(int attempts = 1, bool duringSwitch = false, bool? expectedDisabled = null)
        {
            using var readLease = duringSwitch ? null : HardwareOperationGate.TryEnterTelemetry();
            if (!duringSwitch && readLease == null) return;
            int revision = System.Threading.Volatile.Read(ref _gpuDisplayRevision);
            for (int attempt = 0; attempt < Math.Max(1, attempts); attempt++)
            {
                if (attempt > 0)
                    await Task.Delay(350);

                var state = await Task.Run(() =>
                {
                    bool biosDiscrete = false;
                    bool? nvidiaDisabled = null;
                    try { biosDiscrete = _hw.IsBiosDiscreteGpuActive; } catch { }
                    try { nvidiaDisabled = _hw.IsDiscreteGpuDisabled; } catch { }
                    DdsStatus? dds = null;
                    if (nvidiaDisabled != true && !biosDiscrete)
                        try { dds = _hw.GetDdsStatus(); } catch { }
                    return (biosDiscrete, nvidiaDisabled, dds);
                });

                if (_disposed)
                    return;

                await _dispatcher.InvokeAsync(() =>
                {
                    if (_disposed || revision != System.Threading.Volatile.Read(ref _gpuDisplayRevision)) return;
                    IsBiosDiscreteGpuActive = state.biosDiscrete;
                    // Do not clear a confirmed state when WMI is temporarily
                    // unavailable during a device restart.
                    if (state.nvidiaDisabled.HasValue)
                        IsGpuIgpuOnlyActive = state.nvidiaDisabled.Value && !state.biosDiscrete;
                    if (IsBiosDiscreteGpuActive)
                        GpuMuxModeName = "dGPU Only";
                    else if (IsGpuIgpuOnlyActive)
                        GpuMuxModeName = "iGPU Only";
                    ApplyDdsStatus(state.dds);
                    NotifyGpuSelectionsChanged();
                });

                if (!expectedDisabled.HasValue || state.nvidiaDisabled == expectedDisabled.Value)
                    break;
            }
        }

        // dGPU Battery Sentinel
        public ObservableCollection<PreySense.Gpu.GpuProcessInfo> ActiveGpuProcesses { get; } = new();

        private void NotifyGpuSelectionsChanged()
        {
            OnPropertyChanged(nameof(ShowDdsGpuTiles));
            OnPropertyChanged(nameof(ShowIgpuOnlyTile));
            OnPropertyChanged(nameof(ShowAdvancedOptimusTile));
            OnPropertyChanged(nameof(ShowDgpuOnlyTile));
            OnPropertyChanged(nameof(GpuModeTileColumns));
            OnPropertyChanged(nameof(IsGpuEnduranceActive));
            OnPropertyChanged(nameof(IsGpuStandardActive));
            OnPropertyChanged(nameof(IsGpuUltimateActive));
            OnPropertyChanged(nameof(IsGpuOptimusActive));
            OnPropertyChanged(nameof(IsGpuAutoActive));
            OnPropertyChanged(nameof(IsGpuNvidiaOnlyActive));
            OnPropertyChanged(nameof(IsGpuAdvancedOptimusActive));
            OnPropertyChanged(nameof(IsGpuIgpuOnlyActive));
        }

        private void ApplyDdsStatus(DdsStatus? status)
        {
            if (!IsBiosDiscreteGpuActive && !IsGpuIgpuOnlyActive && status is { IsSupported: true, KmdResponse: 0 })
            {
                if (status.IsAutomatic) ActiveGpuMuxMode = 1;
                else if (status.ActiveMux != DdsActiveMux.Unknown)
                    ActiveGpuMuxMode = status.ActiveMux == DdsActiveMux.dGPU ? 2 : 0;
                GpuMuxModeName = GetGpuModeName(ActiveGpuMuxMode);
                NotifyGpuSelectionsChanged();
            }
            else if (!IsBiosDiscreteGpuActive && !IsGpuIgpuOnlyActive)
            {
                ActiveGpuMuxMode = -1;
                GpuMuxModeName = "N/A";
                NotifyGpuSelectionsChanged();
            }
        }

        private static string GetGpuModeName(int mode) => mode switch
        {
            2 => "NVIDIA GPU Only",
            1 => "Automatic",
            0 => "Optimus",
            _ => "N/A"
        };

        private static void SaveGpuModePreference(int mode)
        {
            try
            {
                PreySense.Core.Configuration.ConfigurationService.Instance.Update(cfg =>
                {
                    cfg.Session.GpuModePreference = Math.Clamp(mode, 0, 2);
                });
            }
            catch { }
        }

        [RelayCommand]
        public async Task ApplyGpuClocksAsync()
        {
            var offsets = GpuOffsetsForMode(GpuModeIndex(ActivePowerMode));
            if (offsets is null) { DiagnosticMessage = "Set GPU offsets before applying."; return; }
            await ApplyTuningProfileAsync(ActivePowerMode, Pl1Watts, Pl2Watts,
                TauForMode(ActivePowerMode), offsets, persist: false);
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
            if (System.Threading.Interlocked.CompareExchange(ref _gpuSwitchInProgress, 1, 0) != 0)
            {
                DiagnosticMessage = "GPU mode switch already in progress.";
                return;
            }
            bool leavingIgpuOnly = IsGpuIgpuOnlyActive;
            System.Threading.Interlocked.Increment(ref _gpuDisplayRevision);
            DiagnosticMessage = $"Switching GPU mode to {GetGpuModeName(muxVal)}...";
            IsBusy = true;
            try
            {
                using var hardwareLease = await HardwareOperationGate.EnterDisplaySwitchAsync();
                if (leavingIgpuOnly)
                {
                    bool enabled = await Task.Run(() => _hw.EnableDiscreteGpu());
                    if (!enabled) { DiagnosticMessage = "Could not enable the NVIDIA GPU."; return; }
                    await RefreshGpuDeviceModeStateAsync(attempts: 3, duringSwitch: true, expectedDisabled: false);
                    if (IsGpuIgpuOnlyActive) { DiagnosticMessage = "Windows still shows the NVIDIA GPU as disabled."; return; }
                }
                bool available = false;
                for (int attempt = 0; attempt < (leavingIgpuOnly ? 8 : 1); attempt++)
                {
                    available = await Task.Run(() => _hw.IsDdsAvailable && _hw.IsDdsSupported());
                    if (available) break;
                    if (leavingIgpuOnly && attempt < 7) await Task.Delay(250);
                }
                if (!available)
                {
                    DiagnosticMessage = "NVIDIA Advanced Optimus is not available.";
                    return;
                }
                var targetDdsMode = (DdsDisplayMode)muxVal;
                var prevRenderMode = System.Windows.Media.RenderOptions.ProcessRenderMode;
                try
                {
                    System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
                    if (System.Windows.Application.Current != null)
                        await _dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                    if (_hw is ProductionHardwareService) await Task.Run(NvmlHelper.Shutdown);
                    var (ok, msg) = await Task.Run(() => _hw.SetDdsMode(targetDdsMode));
                    if (!ok)
                    {
                        ApplyDdsStatus(await Task.Run(() => _hw.GetDdsStatus()));
                        DiagnosticMessage = $"Could not switch GPU mode: {msg}";
                        return;
                    }
                    DdsStatus? confirmed = null;
                    for (int attempt = 0; attempt < 20; attempt++)
                    {
                        confirmed = await Task.Run(() => _hw.GetDdsStatus());
                        if (confirmed?.Matches(targetDdsMode) == true) break;
                        if (attempt < 19) await Task.Delay(250);
                    }
                    ApplyDdsStatus(confirmed);
                    if (confirmed?.Matches(targetDdsMode) != true)
                    {
                        bool blocked = await Task.Run(() => _hw.HasDdsBlockers());
                        DiagnosticMessage = blocked
                            ? "GPU switch blocked by an app. Close it and try again."
                            : "GPU mode change was not confirmed.";
                        return;
                    }
                    // Adapter handles acquired before re-enable may still be invalid.
                    await Task.Run(_hw.RefreshGpuAdapter);
                    if (persistPreference)
                        SaveGpuModePreference(muxVal);
                    DiagnosticMessage = $"GPU mode set to {GetGpuModeName(muxVal)}.";
                    var display = await Task.Run(() => (_hw.GetSupportedRefreshRates(), _hw.GetDisplayRefreshRate()));
                    ApplyRefreshRateOptions(display.Item1, display.Item2);
                }
                finally { System.Windows.Media.RenderOptions.ProcessRenderMode = prevRenderMode; }
            }
            catch (Exception ex) { DiagnosticMessage = $"GPU mode error: {ex.Message}"; }
            finally
            {
                IsBusy = false;
                System.Threading.Volatile.Write(ref _gpuSwitchInProgress, 0);
            }
        }

        private async Task ApplyIgpuOnlyAsync()
        {
            if (IsBiosDiscreteGpuActive || IsGpuIgpuOnlyActive)
                return;
            if (!ConfirmGpuBatterySwitchIfNeeded())
                return;

            if (System.Threading.Interlocked.CompareExchange(ref _gpuSwitchInProgress, 1, 0) != 0) return;
            System.Threading.Interlocked.Increment(ref _gpuDisplayRevision);
            IsBusy = true;
            try
            {
                using var hardwareLease = await HardwareOperationGate.EnterDisplaySwitchAsync();
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
                    await RefreshGpuDeviceModeStateAsync(attempts: 3, duringSwitch: true, expectedDisabled: true);
                    if (!IsGpuIgpuOnlyActive)
                    {
                        DiagnosticMessage = "Windows did not confirm disabling the NVIDIA GPU.";
                        return;
                    }
                    DiagnosticMessage = "GPU mode set to iGPU Only.";
                }
                else
                {
                    DiagnosticMessage = "Could not disable the NVIDIA GPU.";
                }
            }
            catch (Exception ex) { DiagnosticMessage = $"GPU mode error: {ex.Message}"; }
            finally
            {
                IsBusy = false;
                System.Threading.Volatile.Write(ref _gpuSwitchInProgress, 0);
            }
        }

        /// <summary>
        /// Shows CustomModalDialog when user-facing processes still hold the dGPU.
        /// </summary>
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
                    bool previousAutoHideSuppression = App.SuppressAutoHide;
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
                        App.SuppressAutoHide = previousAutoHideSuppression;
                    }
                }

                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher == null) return false;
                if (dispatcher.CheckAccess()) Show(); else dispatcher.Invoke(Show);
                return result == true;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"iGPU-only safety confirmation failed: {ex.Message}");
                return false;
            }
        }

        [RelayCommand]
        public async Task RestartDiscreteGpuAsync()
        {
            if (System.Threading.Interlocked.CompareExchange(ref _gpuSwitchInProgress, 1, 0) != 0) return;
            System.Threading.Interlocked.Increment(ref _gpuDisplayRevision);
            IsBusy = true;
            DiagnosticMessage = "Power-cycling and restarting Discrete GPU (reboot-free)...";
            try
            {
                bool ok;
                string msg;
                using (await HardwareOperationGate.EnterDisplaySwitchAsync())
                {
                    (ok, msg) = await Task.Run(() => _hw.RestartDiscreteGpu());
                    await RefreshGpuDeviceModeStateAsync(attempts: 3, duringSwitch: true, expectedDisabled: false);
                }
                DiagnosticMessage = msg;
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
                DiagnosticMessage = $"Could not restart the NVIDIA GPU: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
                System.Threading.Volatile.Write(ref _gpuSwitchInProgress, 0);
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
                    DiagnosticMessage = "Restarting to apply the GPU mode...";
                    RestartToApplyGpuMode();
                }
                else
                {
                    DiagnosticMessage = "Could not set the GPU mode.";
                }
            }
            catch (Exception ex)
            {
                DiagnosticMessage = $"GPU mode error: {ex.Message}";
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
                bool previousAutoHideSuppression = App.SuppressAutoHide;
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
                    App.SuppressAutoHide = previousAutoHideSuppression;
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

                DiagnosticMessage = ActiveGpuProcesses.Count > 0
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
