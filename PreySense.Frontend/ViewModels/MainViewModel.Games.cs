using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PreySense.Core.Acpi;
using PreySense.Core.Hardware;
using PreySense.Frontend.Services;
using PreySense.Mode;

namespace PreySense.Frontend.ViewModels
{
    public partial class MainViewModel
    {
        public ObservableCollection<GameProfile> GameProfiles { get; } = new();
        public string[] GameModeNames { get; } = { "Eco", "Quiet", "Balanced", "Performance", "Turbo" };
        public bool HasSelectedGameProfile => SelectedGameProfile != null;

        [ObservableProperty] private GameProfile? _selectedGameProfile;
        [ObservableProperty] private string _selectedGameModeName = "Performance";
        [ObservableProperty] private int _selectedGamePl1 = 75;
        [ObservableProperty] private int _selectedGamePl2 = 140;
        [ObservableProperty] private int _selectedGameTau = CpuPowerLimitConfiguration.FactoryTauSeconds;
        [ObservableProperty] private int _selectedGameGpuWatts = 130;
        [ObservableProperty] private int _selectedGameGpuDelta;
        [ObservableProperty] private int _selectedGameGpuDynamicBoost;
        private bool _loadingGameProfile;
        [ObservableProperty] private int _selectedGameGpuTrip = GpuPowerLimitConfiguration.FactoryGpuThermalTripPointCelsius;
        [ObservableProperty] private bool _selectedGameEnabled = true;
        [ObservableProperty] private bool _selectedGameCpuBoost = true;

        public int TauSliderMaximum => CpuPowerLimitConfiguration.TauSliderMaximum;

        public int SelectedGameTauIndex
        {
            get => CpuPowerLimitConfiguration.TauStepIndex(SelectedGameTau);
            set
            {
                int seconds = CpuPowerLimitConfiguration.TauSecondsForIndex(value);
                if (SelectedGameTau != seconds)
                    SelectedGameTau = seconds;
            }
        }

        public string SelectedGameTauText =>
            CpuPowerLimitConfiguration.FormatTauSelection(CpuPowerLimitConfiguration.SnapTauSelection(SelectedGameTau));

        private GameLaunchWatcher? _gameLaunchWatcher;
        private byte _modeBeforeGame = 0xFF;
        private string? _appliedGameExe;

        private void StartGameLaunchWatcher()
        {
            foreach (GameProfile profile in GameProfileStore.Load())
                GameProfiles.Add(profile);

            SelectedGameProfile = GameProfiles.FirstOrDefault();
            _gameLaunchWatcher = new GameLaunchWatcher(
                () => GameProfiles.ToList(),
                OnGameProfileLaunchedAsync,
                OnGameProfileExitedAsync);
        }

        partial void OnSelectedGameProfileChanged(GameProfile? value)
        {
            OnPropertyChanged(nameof(HasSelectedGameProfile));
            if (value == null)
                return;
            _loadingGameProfile = true;
            try
            {
                SelectedGameModeName = value.ModeTitle;
                SelectedGamePl1 = value.CpuPl1;
                SelectedGamePl2 = value.CpuPl2;
                SelectedGameTau = value.TauSeconds;
                var offsets = value.ResolveGpuOffsets(GpuBasePowerWatts);
                SelectedGameGpuDelta = offsets.DeltaWatts;
                SelectedGameGpuDynamicBoost = offsets.DynamicBoostWatts;
                SelectedGameGpuWatts = offsets.NominalReferenceWatts(GpuBasePowerWatts);
                SelectedGameGpuTrip = value.GpuThermalTripCelsius;
                SelectedGameCpuBoost = value.CpuBoostEnabled;
                SelectedGameEnabled = value.Enabled;
            }
            finally { _loadingGameProfile = false; }
        }

        partial void OnSelectedGameCpuBoostChanged(bool value)
        {
            if (SelectedGameProfile == null)
                return;
            SelectedGameProfile.CpuBoostEnabled = value;
        }

        partial void OnSelectedGameEnabledChanged(bool value)
        {
            if (SelectedGameProfile == null)
                return;
            SelectedGameProfile.Enabled = value;
        }

        partial void OnSelectedGamePl1Changed(int value)
        {
            if (SelectedGameProfile == null)
                return;
            SelectedGameProfile.CpuPl1 = LimitPl1ToPlatform(value);
        }

        partial void OnSelectedGamePl2Changed(int value)
        {
            if (SelectedGameProfile == null)
                return;
            SelectedGameProfile.CpuPl2 = LimitPl2(SelectedGamePl1, value);
        }

        partial void OnSelectedGameTauChanged(int value)
        {
            int snapped = LimitGameTau(value);
            if (snapped != value)
            {
                SelectedGameTau = snapped;
                return;
            }

            OnPropertyChanged(nameof(SelectedGameTauText));
            OnPropertyChanged(nameof(SelectedGameTauIndex));
            if (SelectedGameProfile == null)
                return;
            SelectedGameProfile.TauSeconds = snapped;
        }

        partial void OnSelectedGameGpuDeltaChanged(int value) => UpdateSelectedGameGpuOffsets();
        partial void OnSelectedGameGpuDynamicBoostChanged(int value) => UpdateSelectedGameGpuOffsets();

        private void UpdateSelectedGameGpuOffsets()
        {
            if (_loadingGameProfile || SelectedGameProfile == null) return;
            var offsets = new GpuPowerOffsets(Math.Clamp(SelectedGameGpuDelta, 0, 255),
                Math.Clamp(SelectedGameGpuDynamicBoost, 0, 255));
            SelectedGameGpuDelta = offsets.DeltaWatts;
            SelectedGameGpuDynamicBoost = offsets.DynamicBoostWatts;
            SelectedGameProfile.GpuDeltaWatts = offsets.DeltaWatts;
            SelectedGameProfile.GpuDynamicBoostWatts = offsets.DynamicBoostWatts;
            SelectedGameGpuWatts = offsets.NominalReferenceWatts(GpuBasePowerWatts);
            SelectedGameProfile.GpuPowerWatts = SelectedGameGpuWatts;
        }

        partial void OnSelectedGameGpuTripChanged(int value)
        {
            if (SelectedGameProfile == null)
                return;
            SelectedGameProfile.GpuThermalTripCelsius = LimitGameGpuTrip(value);
        }

        partial void OnSelectedGameModeNameChanged(string value)
        {
            if (SelectedGameProfile == null)
                return;
            SelectedGameProfile.PowerMode = value switch
            {
                "Quiet" => 0x00,
                "Eco" => 0x06,
                "Performance" => 0x04,
                "Turbo" => 0x05,
                _ => (byte)0x01
            };
        }

        [RelayCommand]
        private async Task SaveSelectedGameProfileAsync()
        {
            if (!IsAcpiOverrideEnabled || SelectedGameProfile == null)
                return;
            UpdateSelectedGameGpuOffsets();
            SaveGameProfiles();
            StatusMessage = $"Saved {SelectedGameProfile.DisplayName} profile.";
            if (string.Equals(_appliedGameExe, SelectedGameProfile.ExeName, StringComparison.OrdinalIgnoreCase))
            {
                await ApplyGameProfileAsync(SelectedGameProfile);
            }
        }

        [RelayCommand]
        public void AddGameProfile()
        {
            if (!IsAcpiOverrideEnabled)
                return;

            App.SuppressAutoHide = true;
            try
            {
                var picker = new Microsoft.Win32.OpenFileDialog
                {
                    Title = LocalizationService.Translate("Select a game executable"),
                    Filter = LocalizationService.Translate("Game") + " (*.exe)|*.exe",
                    CheckFileExists = true
                };
                if (picker.ShowDialog() != true)
                    return;

                string exe = GameProfileStore.NormalizeExeName(picker.FileName);
                string executablePath = GameProfileStore.NormalizeExecutablePath(picker.FileName);
                if (GameProfiles.Any(p =>
                    (!string.IsNullOrWhiteSpace(p.ExecutablePath) &&
                     p.ExecutablePath.Equals(executablePath, StringComparison.OrdinalIgnoreCase)) ||
                    (string.IsNullOrWhiteSpace(p.ExecutablePath) &&
                     p.ExeName.Equals(exe, StringComparison.OrdinalIgnoreCase))))
                {
                    SelectedGameProfile = GameProfiles.First(p =>
                        (!string.IsNullOrWhiteSpace(p.ExecutablePath) &&
                         p.ExecutablePath.Equals(executablePath, StringComparison.OrdinalIgnoreCase)) ||
                        (string.IsNullOrWhiteSpace(p.ExecutablePath) &&
                         p.ExeName.Equals(exe, StringComparison.OrdinalIgnoreCase)));
                    return;
                }

                var profile = new GameProfile
                {
                    ExeName = exe,
                    ExecutablePath = executablePath,
                    DisplayName = Path.GetFileNameWithoutExtension(exe),
                    PowerMode = PeekPendingOrActiveMode() is byte current && current is 0x00 or 0x01 or 0x04 or 0x05
                        ? current
                        : (byte)0x04,
                    CpuPl1 = LimitPl1ToPlatform(Pl1Watts),
                    CpuPl2 = LimitPl2(LimitPl1ToPlatform(Pl1Watts), Pl2Watts),
                    TauSeconds = LimitGameTau(TauSeconds),
                    CpuBoostEnabled = CpuBoostForMode(PeekPendingOrActiveMode()),
                    GpuPowerWatts = LimitGameGpuWatts(GpuPowerTargetWatts),
                    GpuThermalTripCelsius = LimitGameGpuTrip(GpuThermalTripPointCelsius),
                    Enabled = true
                };
                var offsets = GpuOffsetsForMode(GpuModeIndex(profile.PowerMode), profile.GpuPowerWatts);
                profile.GpuDeltaWatts = offsets.DeltaWatts;
                profile.GpuDynamicBoostWatts = offsets.DynamicBoostWatts;
                profile.GpuPowerWatts = offsets.NominalReferenceWatts(GpuBasePowerWatts);
                GameProfiles.Add(profile);
                SelectedGameProfile = profile;
                SaveGameProfiles();
            }
            finally
            {
                App.SuppressAutoHide = false;
            }
        }

        [RelayCommand]
        public void RemoveSelectedGameProfile()
        {
            if (!IsAcpiOverrideEnabled || SelectedGameProfile == null)
                return;
            GameProfiles.Remove(SelectedGameProfile);
            SelectedGameProfile = GameProfiles.FirstOrDefault();
            SaveGameProfiles();
        }

        public void SaveGameProfiles() => GameProfileStore.Save(GameProfiles);

        internal async Task ApplyGameProfileAsync(GameProfile game)
        {
            if (!IsAcpiOverrideEnabled || !IsFactoryCalibrationComplete || IsFactoryCalibrationRunning)
                return;
            if (!ProfileManager.IsSupportedMode(game.PowerMode))
            {
                StatusMessage = "Game profile has an unsupported performance mode.";
                return;
            }

            CancelPendingModeSwitch();
            await _modeSwitchGate.WaitAsync();
            try
            {
            if (_disposed || !IsAcpiOverrideEnabled || !IsFactoryCalibrationComplete || IsFactoryCalibrationRunning) return;

            int pl1 = LimitPl1ToPlatform(game.CpuPl1);
            int pl2 = LimitPl2(pl1, game.CpuPl2);
            int tau = LimitGameTau(game.TauSeconds);
            var offsets = game.ResolveGpuOffsets(GpuBasePowerWatts);
            int gpu = offsets.NominalReferenceWatts(GpuBasePowerWatts);
            int trip = LimitGameGpuTrip(game.GpuThermalTripCelsius);
            game.CpuPl1 = pl1;
            game.CpuPl2 = pl2;
            game.TauSeconds = tau;
            game.GpuPowerWatts = gpu;
            game.GpuDeltaWatts = offsets.DeltaWatts;
            game.GpuDynamicBoostWatts = offsets.DynamicBoostWatts;
            game.GpuThermalTripCelsius = trip;

            if (!IsGpuHardwareModeActive(game.PowerMode))
                await ApplyModeSwitchCoreAsync(game.PowerMode, applyCustomLimits: false);

            if (!IsGpuHardwareModeActive(game.PowerMode))
            {
                StatusMessage = "Game profile mode switch failed; settings were not applied.";
                return;
            }

            Pl1Watts = pl1;
            Pl2Watts = pl2;
            TauSeconds = tau;
            GpuPowerTargetWatts = gpu;
            GpuThermalTripPointCelsius = trip;
            bool cpuApplied = false;
            bool gpuApplied = false;
            bool tripApplied = false;
            string cpuResult = string.Empty;
            await Task.Run(() =>
            {
                cpuApplied = PowerLimitController.SetCpuPowerLimits(pl1, pl2, true, tau);
                cpuResult = PowerLimitController.LastCpuPowerLimitStatus;
                PowerLimitController.SetCpuBoost(game.CpuBoostEnabled ? 1 : 0);
                gpuApplied = PreySenseWmiDeviceClient.SetGpuPowerOffsets(offsets);
                tripApplied = PreySenseWmiDeviceClient.SetGpuThermalTripPoint((uint)trip);
            });
            string boost = game.CpuBoostEnabled ? "on" : "off";
            StatusMessage = $"{game.DisplayName}: {game.ModeTitle}  PL1 {pl1}W / PL2 {pl2}W / Tau {CpuPowerLimitConfiguration.FormatTauSelection(tau)}  CPU Boost {boost}  GPU {gpu}W / {trip}°C";
            if (!gpuApplied || !tripApplied) StatusMessage += " | GPU settings failed or readback differed.";
            if (!cpuApplied) StatusMessage += $" | CPU limits partial: {cpuResult}";
            }
            finally { _modeSwitchGate.Release(); }
        }

        private static int LimitGameTau(int tau) =>
            CpuPowerLimitConfiguration.SnapTauSelection(tau);

        private int LimitGameGpuWatts(int watts)
        {
            return Math.Max(GpuPowerLimitConfiguration.MinimumPowerLimitWatts, watts);
        }

        private static int LimitGameGpuTrip(int celsius) => Math.Clamp(celsius, 70, 89);

        private Task OnGameProfileLaunchedAsync(GameProfile game)
        {
            return _dispatcher.InvokeAsync(async () =>
            {
                OverlayBridge.GameStarted();
                if (_disposed || !IsAcpiOverrideEnabled) return;
                if (_appliedGameExe == null)
                    _modeBeforeGame = PeekPendingOrActiveMode();
                _appliedGameExe = game.ExeName;
                await ApplyGameProfileAsync(game);
            }).Task.Unwrap();
        }

        private Task OnGameProfileExitedAsync()
        {
            return _dispatcher.InvokeAsync(async () =>
            {
                OverlayBridge.GameExited();
                if (_disposed) return;
                string? applied = _appliedGameExe;
                byte restore = _modeBeforeGame;
                _appliedGameExe = null;
                _modeBeforeGame = 0xFF;
                if (applied == null || restore == 0xFF || !IsAcpiOverrideEnabled)
                    return;
                CancelPendingModeSwitch();
                // Game limits can differ even when the original Acer mode is unchanged.
                await ApplyModeSwitchAsync(restore);
                StatusMessage = $"{Path.GetFileNameWithoutExtension(applied)} closed. Restored {GetModeTitle(restore)}.";
            }).Task.Unwrap();
        }
    }
}
