using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PreySense.Core.Configuration;
using PreySense.Frontend.Services;
using PreySense.Mode;

namespace PreySense.Frontend.ViewModels;

public partial class MainViewModel
{
    public ObservableCollection<GameProfile> GameProfiles { get; } = new();
    public double GameListHeight => 5 * 32 + 2;
    public IReadOnlyList<string> GameModeNames => ProfileManager.SourceProfileNames;
    public IReadOnlyList<string> GameCpuBoostNames => CpuBoostModeNames.Select(LocalizationService.Translate).ToArray();
    [ObservableProperty] private bool _isGameModeEnabled;
    [ObservableProperty] private GameProfile? _selectedGameProfile;
    [ObservableProperty] private string _selectedGameModeName = "";
    [ObservableProperty] private bool _selectedGameEnabled = true;
    [ObservableProperty] private string _gameModeStatus = "";
    public bool HasSelectedGameProfile => SelectedGameProfile != null;
    private GameLaunchWatcher? _gameLaunchWatcher;
    private int _gameWatcherRevision;
    private readonly GameModeSession _gameModeSession = new();
    private bool _loadingGameProfile;
    private bool _gameModeReady;
    private bool _gameModeStopping;
    private readonly SemaphoreSlim _gameWatcherGate = new(1, 1);

    private void InitializeGameMode()
    {
        if (_disposed || _gameModeReady) return;
        var saved = ConfigurationService.Instance.GetCurrent().Games ?? new();
        IsGameModeEnabled = saved.Enabled;
        foreach (var profile in saved.Profiles ?? [])
            if (profile != null && ProfileManager.IsSupportedMode(profile.PowerMode)) GameProfiles.Add(profile.Copy());
        SelectedGameProfile = GameProfiles.FirstOrDefault();
        OnPropertyChanged(nameof(GameListHeight));
        OnPropertyChanged(nameof(IsGameModeEnabled));
        _gameModeReady = true;
        _ = UpdateGameWatcherAsync();
    }

    partial void OnIsGameModeEnabledChanged(bool value)
    {
        if (!_gameModeReady || _disposed) return;
        SaveGameProfiles();
        _ = UpdateGameWatcherAsync();
    }

    private async Task UpdateGameWatcherAsync()
    {
        int revision = ++_gameWatcherRevision;
        _gameLaunchWatcher?.Dispose();
        _gameLaunchWatcher = null;
        await _gameWatcherGate.WaitAsync();
        try
        {
            if (_disposed || _gameModeStopping || revision != _gameWatcherRevision) return;
            if (!await RestoreModeAfterGameAsync()) return;
            if (_disposed || _gameModeStopping || !IsGameModeEnabled || revision != _gameWatcherRevision) return;
            _gameLaunchWatcher = new GameLaunchWatcher(
            () => ConfigurationService.Instance.GetCurrent().Games?.Profiles ?? [],
            profile => _dispatcher.InvokeAsync(async () =>
            {
                if (_disposed || _gameModeStopping || !IsGameModeEnabled || revision != _gameWatcherRevision) return;
                bool applied = await ApplyGameProfileWithOverridesAsync(profile);
                if (applied && !_disposed && !_gameModeStopping && revision == _gameWatcherRevision) GameModeStatus = profile.DisplayName;
            }).Task.Unwrap(),
            () => _dispatcher.InvokeAsync(async () =>
            {
                if (revision == _gameWatcherRevision && !_disposed && !_gameModeStopping) await RestoreModeAfterGameAsync();
            }).Task.Unwrap());
        }
        catch (Exception ex) { AppLogger.Error($"Game mode watcher update failed: {ex}"); }
        finally { _gameWatcherGate.Release(); }
    }

    internal async Task<bool> RestoreModeAfterGameAsync()
    {
        if (_disposed || _gameModeStopping || !await TryEnterModeSwitchGateAsync()) return false;
        try
        {
            byte current = PeekPendingOrActiveMode();
            byte? restore = _gameModeSession.RestoreMode(current);
            bool hadTuning = _activeGameTuning.HasValue;
            // A manual/AC mode takes precedence, but an inactive game GPU table still
            // needs its original bytes restored before leaving that mode behind.
            bool cleaned = await RestoreActiveGameTuningAsync(GameRestoreMode(current));
            if (!hadTuning && restore is byte mode) cleaned &= await SwitchGameModeAsync(mode);
            if (cleaned) { _gameModeSession.End(current); GameModeStatus = ""; }
            return cleaned;
        }
        finally { ExitModeSwitchGate(); }
    }

    private void StopGameMode()
    {
        _gameModeStopping = true;
        _gameModeReady = false;
        ++_gameWatcherRevision;
        _gameLaunchWatcher?.Dispose();
        _gameLaunchWatcher = null;
    }

    public async Task PrepareGameModeShutdownAsync()
    {
        ++_gameWatcherRevision;
        _gameModeReady = false;
        _gameLaunchWatcher?.Dispose();
        _gameLaunchWatcher = null;
        try
        {
            if (!await RestoreModeAfterGameAsync())
                AppLogger.Error($"Game tuning shutdown restoration incomplete: {GameModeStatus}");
        }
        finally { StopGameMode(); }
    }

    partial void OnSelectedGameProfileChanged(GameProfile? value)
    {
        OnPropertyChanged(nameof(HasSelectedGameProfile));
        if (value == null)
        {
            SelectedGameModeName = "";
            return;
        }
        _loadingGameProfile = true;
        try { SelectedGameModeName = value.ModeTitle; SelectedGameEnabled = value.Enabled; }
        finally { _loadingGameProfile = false; }
    }

    partial void OnSelectedGameModeNameChanged(string value)
    {
        if (_loadingGameProfile || SelectedGameProfile == null) return;
        int index = Array.IndexOf(ProfileManager.SourceProfileNames, value);
        if (index >= 0) SelectedGameProfile.PowerMode = ProfileManager.SourceProfileMode(index);
    }
    partial void OnSelectedGameEnabledChanged(bool value)
    {
        if (!_loadingGameProfile && SelectedGameProfile != null) SelectedGameProfile.Enabled = value;
    }

    private void SaveGameProfiles() => ConfigurationService.Instance.Update(config => config.Games = new()
    {
        Enabled = IsGameModeEnabled,
        Profiles = GameProfiles.Select(profile => profile.Copy()).ToList()
    });

    [RelayCommand]
    public void AddGameProfile()
    {
        App.SuppressAutoHide = true;
        try
        {
            var picker = new Microsoft.Win32.OpenFileDialog
            {
                Title = LocalizationService.Translate("Select a game executable"),
                Filter = "Executable (*.exe)|*.exe", CheckFileExists = true
            };
            if (picker.ShowDialog() != true) return;
            AddGameProfilePath(picker.FileName);
        }
        finally { App.SuppressAutoHide = false; }
    }

    internal void AddGameProfilePath(string executablePath)
    {
        string path = GameProfileStore.NormalizeExecutablePath(executablePath);
        if (string.IsNullOrWhiteSpace(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return;
        var existing = GameProfiles.FirstOrDefault(p => string.Equals(p.ExecutablePath, path, StringComparison.OrdinalIgnoreCase));
        if (existing != null) { SelectedGameProfile = existing; return; }
        var profile = new GameProfile { ExecutablePath = path, DisplayName = System.IO.Path.GetFileNameWithoutExtension(path) };
        var tuning = TuningStore.GetCurrent().Tuning;
        var saved = ProfileForMode(tuning, profile.PowerMode);
        tuning.FactoryDefaults.TryGetValue(ProfileManager.ModeToProfileName(profile.PowerMode), out var factory);
        profile.CpuPl1Watts = saved.CpuPl1Watts ?? factory?.CpuPl1Watts;
        profile.CpuPl2Watts = saved.CpuPl2Watts ?? factory?.CpuPl2Watts;
        profile.TauSeconds = saved.TauSeconds ?? factory?.TauSeconds;
        profile.GpuPowerWatts = saved.GpuRuntimeLimitWatts ?? saved.GpuWatts ?? factory?.GpuWatts;
        profile.GpuDeltaWatts = saved.GpuDeltaWatts ?? factory?.GpuDeltaWatts;
        profile.GpuDynamicBoostWatts = saved.GpuDynamicBoostWatts ?? factory?.GpuDynamicBoostWatts;
        profile.PlatformPowerOffsetWatts = saved.PlatformPowerOffsetWatts ?? factory?.PlatformPowerOffsetWatts;
        profile.CpuBoostMode = saved.EffectiveCpuBoostMode ?? 1;
        GameProfiles.Add(profile);
        OnPropertyChanged(nameof(GameListHeight));
        SelectedGameProfile = profile;
        SaveGameProfiles();
        if (IsGameModeEnabled && _gameModeReady) _ = UpdateGameWatcherAsync();
    }

    [RelayCommand]
    public void SaveSelectedGameProfile()
    {
        if (SelectedGameProfile is { } game && !TryCreateGameTuningPlan(game, out _, out string status))
        { GameModeStatus = status; return; }
        SaveGameProfiles();
        // Recreate only on explicit edits; not on every foreground notification.
        if (IsGameModeEnabled && _gameModeReady) _ = UpdateGameWatcherAsync();
    }

    [RelayCommand]
    public void RemoveSelectedGameProfile()
    {
        if (SelectedGameProfile == null) return;
        GameProfiles.Remove(SelectedGameProfile);
        OnPropertyChanged(nameof(GameListHeight));
        SelectedGameProfile = GameProfiles.FirstOrDefault();
        SaveGameProfiles();
        if (_gameModeReady) _ = UpdateGameWatcherAsync();
    }
}
