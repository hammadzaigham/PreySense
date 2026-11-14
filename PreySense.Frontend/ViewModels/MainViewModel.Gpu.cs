using System;
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
        private int _gpuSwitchInProgress;
        private int _gpuDisplayRevision;

        // GPU Power Target & Driver Hardware Controls
        [ObservableProperty] private int _gpuPowerTargetWatts = 130;
        [ObservableProperty] private int _gpuPowerTargetMin = GpuPowerBaseline.ReadWatts() ?? 70;
        [ObservableProperty] private int _gpuPowerTargetMax = (GpuPowerBaseline.ReadWatts() ?? 85)
            + PreySense.Core.Hardware.DriverFactoryCalibration.MaxGpuDeltaWatts
            + PreySense.Core.Hardware.DriverFactoryCalibration.MaxGpuDynamicBoostWatts;
        [ObservableProperty] private int? _gpuBasePowerWatts = GpuPowerBaseline.ReadWatts();

        [ObservableProperty] private int? _customEcoGpuRuntimeWatts;
        [ObservableProperty] private int? _customQuietGpuRuntimeWatts;
        [ObservableProperty] private int? _customBalancedGpuRuntimeWatts;
        [ObservableProperty] private int? _customPerfGpuRuntimeWatts;
        [ObservableProperty] private int? _customTurboGpuRuntimeWatts;

        public void SetGpuRuntimeLimitForModeIndex(int modeIndex, int? watts)
        {
            switch (modeIndex)
            {
                case 0: CustomEcoGpuRuntimeWatts = watts; break;
                case 1: CustomQuietGpuRuntimeWatts = watts; break;
                case 2: CustomBalancedGpuRuntimeWatts = watts; break;
                case 3: CustomPerfGpuRuntimeWatts = watts; break;
                case 4: CustomTurboGpuRuntimeWatts = watts; break;
            }
        }

        public int GetFactoryDefaultGpuWatts(int modeIndex)
        {
            byte mode = ModeIndexToByte(modeIndex);
            var factory = FactoryTuningMode(modeIndex);
            return factory?.GpuWatts ?? (GpuBasePowerWatts.HasValue && factory?.HasGpuLimitValues == true
                ? GpuBasePowerWatts.Value + factory.GpuDeltaWatts!.Value + factory.GpuDynamicBoostWatts!.Value
                : GpuPowerTargetMin);
        }

        public async Task<bool> ResetGpuRuntimeLimitForModeAsync(int modeIndex)
        {
            byte mode = ModeIndexToByte(modeIndex);
            SetGpuRuntimeLimitForModeIndex(modeIndex, null);
            if (!IsGpuProfileTuningAvailable)
            {
                DiagnosticMessage = "GPU profile table was not found on this laptop.";
                return false;
            }
            bool applied = await ApplyTuningProfileAsync(mode, 0, 0, 0, GetFactoryDefaultGpuWatts(modeIndex),
                persist: false, applyCpuLimits: false);
            if (applied) TuningStore.Update(cfg => ProfileForMode(cfg.Tuning, mode).GpuRuntimeLimitWatts = null);
            return applied;
        }

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
                int? baseline = ReadOrCaptureGpuBaseline();

                if (_disposed)
                    return;

                _dispatcher.BeginInvoke(() =>
                {
                    if (_disposed)
                        return;

                    GpuBasePowerWatts = baseline;
                    if (baseline.HasValue)
                        GpuPowerTargetMin = baseline.Value;
                    ActiveGpuPowerCeiling = "N/A";
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
        [NotifyPropertyChangedFor(nameof(IsGpuAdvancedOptimusActive))]
        private int _activeGpuMuxMode = -1; // 0 = Optimus (iGPU), 1 = Automatic (Auto DDS), 2 = NVIDIA GPU Only (dGPU)
        [ObservableProperty] private string _gpuMuxModeName = "N/A";
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsGpuEnduranceActive))]
        [NotifyPropertyChangedFor(nameof(IsGpuStandardActive))]
        [NotifyPropertyChangedFor(nameof(IsGpuUltimateActive))]
        [NotifyPropertyChangedFor(nameof(GpuModeTileColumns))]
        [NotifyPropertyChangedFor(nameof(IsGpuOptimusActive))]
        [NotifyPropertyChangedFor(nameof(IsGpuAutoActive))]
        [NotifyPropertyChangedFor(nameof(IsGpuNvidiaOnlyActive))]
        [NotifyPropertyChangedFor(nameof(IsGpuAdvancedOptimusActive))]
        [NotifyPropertyChangedFor(nameof(ShowDdsGpuTiles))]
        private bool _isBiosDiscreteGpuActive;

        public bool ShowDdsGpuTiles => !IsBiosDiscreteGpuActive;
        public int GpuModeTileColumns => IsBiosDiscreteGpuActive ? 2 : 4;

        // GPU selection flags (0=Optimus, 1=Automatic, 2=NVIDIA Only)
        public bool IsGpuEnduranceActive => !IsBiosDiscreteGpuActive && ActiveGpuMuxMode == 0;
        public bool IsGpuStandardActive => !IsBiosDiscreteGpuActive && ActiveGpuMuxMode == 1;
        public bool IsGpuUltimateActive => !IsBiosDiscreteGpuActive && ActiveGpuMuxMode == 2;
        public bool IsGpuOptimusActive => !IsBiosDiscreteGpuActive && ActiveGpuMuxMode == 0;
        public bool IsGpuAutoActive => !IsBiosDiscreteGpuActive && ActiveGpuMuxMode == 1;
        public bool IsGpuNvidiaOnlyActive => !IsBiosDiscreteGpuActive && ActiveGpuMuxMode == 2;
        public bool IsGpuAdvancedOptimusActive => ActiveGpuMuxMode == 2 && !IsBiosDiscreteGpuActive;
        partial void OnIsBiosDiscreteGpuActiveChanged(bool value)
        {
            if (value)
                GpuMuxModeName = "dGPU Only";
        }

        private async Task RefreshGpuModeStateAsync(bool duringSwitch = false)
        {
            using var readLease = duringSwitch ? null : HardwareOperationGate.TryEnterTelemetry();
            if (!duringSwitch && readLease == null) return;
            int revision = System.Threading.Volatile.Read(ref _gpuDisplayRevision);
            var state = await Task.Run(() =>
            {
                bool biosDiscrete = _hw.IsBiosDiscreteGpuActive;
                return (biosDiscrete, dds: biosDiscrete ? null : _hw.GetDdsStatus());
            });
            if (_disposed) return;
            await _dispatcher.InvokeAsync(() =>
            {
                if (_disposed || revision != System.Threading.Volatile.Read(ref _gpuDisplayRevision)) return;
                IsBiosDiscreteGpuActive = state.biosDiscrete;
                if (state.biosDiscrete) GpuMuxModeName = "dGPU Only";
                ApplyDdsStatus(state.dds);
                NotifyGpuSelectionsChanged();
            });
        }

        private void NotifyGpuSelectionsChanged()
        {
            OnPropertyChanged(nameof(ShowDdsGpuTiles));
            OnPropertyChanged(nameof(GpuModeTileColumns));
            OnPropertyChanged(nameof(IsGpuEnduranceActive));
            OnPropertyChanged(nameof(IsGpuStandardActive));
            OnPropertyChanged(nameof(IsGpuUltimateActive));
            OnPropertyChanged(nameof(IsGpuOptimusActive));
            OnPropertyChanged(nameof(IsGpuAutoActive));
            OnPropertyChanged(nameof(IsGpuNvidiaOnlyActive));
            OnPropertyChanged(nameof(IsGpuAdvancedOptimusActive));
        }

        private void ApplyDdsStatus(DdsStatus? status)
        {
            if (IsBiosDiscreteGpuActive) return;
            ActiveGpuMuxMode = status switch
            {
                { IsSupported: true, KmdResponse: 0, IsAutomatic: true } => 1,
                { IsSupported: true, KmdResponse: 0, ActiveMux: DdsActiveMux.dGPU } => 2,
                { IsSupported: true, KmdResponse: 0, ActiveMux: DdsActiveMux.Optimus } => 0,
                _ => -1
            };
            GpuMuxModeName = GetGpuModeName(ActiveGpuMuxMode);
            NotifyGpuSelectionsChanged();
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
            int modeIndex = GpuModeIndex(ActivePowerMode);
            int? limit = GpuRuntimeLimitForModeIndex(modeIndex);
            await ApplyTuningProfileAsync(ActivePowerMode, Pl1Watts, Pl2Watts,
                TauForMode(ActivePowerMode), limit, persist: false);
        }

        [RelayCommand]
        public async Task SetGpuMuxModeAsync(object? muxParam)
        {
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
            System.Threading.Interlocked.Increment(ref _gpuDisplayRevision);
            DiagnosticMessage = $"Switching GPU mode to {GetGpuModeName(muxVal)}...";
            IsBusy = true;
            try
            {
                using var hardwareLease = await HardwareOperationGate.EnterDisplaySwitchAsync();
                var current = await Task.Run(() => _hw.IsDdsAvailable ? _hw.GetDdsStatus() : null);
                if (current?.IsSupported != true)
                {
                    DiagnosticMessage = "NVIDIA Advanced Optimus is not available.";
                    return;
                }
                var targetDdsMode = (DdsDisplayMode)muxVal;
                if (current.Matches(targetDdsMode))
                {
                    // Auto may currently route through either GPU. Do not reset its MUX
                    // to the Optimus base or COMMIT an unchanged live driver preference.
                    ApplyDdsStatus(current);
                    if (persistPreference) SaveGpuModePreference(muxVal);
                    DiagnosticMessage = $"GPU mode already set to {GetGpuModeName(muxVal)}.";
                    return;
                }
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
                    await RefreshGpuModeStateAsync(duringSwitch: true);
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


    }
}
