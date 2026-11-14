using System.ComponentModel;
using System.Windows;
using PreySense.Core.Driver;
using PreySense.Frontend.ViewModels;

namespace PreySense.Frontend.Views;

public partial class GamesWindow : Window
{
    private bool _updatingEditor;
    private MainViewModel? _viewModel;

    public GamesWindow()
    {
        InitializeComponent();
        MaxHeight = Math.Max(400, SystemParameters.WorkArea.Height - 48);
        UpdateScrollLimit();
        Loaded += (_, _) => UpdateScrollLimit();
        DataContextChanged += (_, _) =>
        {
            if (_viewModel is not null) _viewModel.PropertyChanged -= ViewModelChanged;
            _viewModel = DataContext as MainViewModel;
            if (_viewModel is not null) _viewModel.PropertyChanged += ViewModelChanged;
            LoadGameControls();
        };
        Closed += (_, _) => { if (_viewModel is not null) _viewModel.PropertyChanged -= ViewModelChanged; };
    }

    private void UpdateScrollLimit()
    {
        var available = new Size(Width, double.PositiveInfinity);
        GamesTitleBar.Measure(available);
        GamesFooter.Measure(available);
        GamesScroll.MaxHeight = Math.Max(1, MaxHeight - GamesTitleBar.DesiredSize.Height - GamesFooter.DesiredSize.Height);
        InvalidateMeasure();
    }

    private void ViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedGameProfile)) LoadGameControls();
    }

    private void LoadGameControls()
    {
        _updatingEditor = true;
        try
        {
            var game = _viewModel?.SelectedGameProfile;
            if (game is not null && _viewModel is not null)
            {
                game.CpuBoostMode ??= _viewModel.CpuBoostModeForMode(game.PowerMode);
                if (!game.HasGpuOffsets && game.GpuPowerWatts is int watts
                    && _viewModel.TryResolveGpuTarget(game.PowerMode, watts, out var offsets, out _))
                {
                    game.GpuDeltaWatts = offsets!.DeltaWatts;
                    game.GpuDynamicBoostWatts = offsets.DynamicBoostWatts;
                    game.PlatformPowerOffsetWatts = offsets.PlatformOffsetWatts;
                }
            }
            GameTauSlider.Maximum = CpuPowerLimitConfiguration.TauSliderMaximum;
            GameTauSlider.Value = CpuPowerLimitConfiguration.TauStepIndex(game?.TauSeconds ?? 0);
            GameDeltaSlider.Value = game?.GpuDeltaWatts ?? 0;
            GameDynamicBoostSlider.Value = game?.GpuDynamicBoostWatts ?? 0;
            GamePlatformSlider.Value = game?.PlatformPowerOffsetWatts ?? 0;
            UpdateLabels();
        }
        finally { _updatingEditor = false; }
    }

    private void UpdateLabels()
    {
        if (GameTauLabel is null || GameDeltaLabel is null || GameDynamicBoostLabel is null || GamePlatformLabel is null) return;
        int seconds = CpuPowerLimitConfiguration.TauSecondsForIndex((int)Math.Round(GameTauSlider.Value));
        string tau = CpuPowerLimitConfiguration.FormatTauSelection(seconds);
        GameTauLabel.Text = tau == "Off" ? Services.LocalizationService.Translate(tau) : tau;
        GameDeltaLabel.Text = $"{GameDeltaSlider.Value:0} W";
        GameDynamicBoostLabel.Text = $"{GameDynamicBoostSlider.Value:0} W";
        GamePlatformLabel.Text = $"{GamePlatformSlider.Value:0} W";
    }

    private void GameTau_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateLabels();
        if (!_updatingEditor && _viewModel?.SelectedGameProfile is { } game)
            game.TauSeconds = CpuPowerLimitConfiguration.TauSecondsForIndex((int)Math.Round(GameTauSlider.Value));
    }

    private void GameGpuOffsets_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateLabels();
        if (_updatingEditor || _viewModel?.SelectedGameProfile is not { } game) return;
        game.GpuDeltaWatts = (int)Math.Round(GameDeltaSlider.Value);
        game.GpuDynamicBoostWatts = (int)Math.Round(GameDynamicBoostSlider.Value);
        game.PlatformPowerOffsetWatts = (int)Math.Round(GamePlatformSlider.Value);
        game.GpuPowerWatts = null;
    }

    private void SaveGame_Click(object sender, RoutedEventArgs e)
    {
        GamePl1.CommitText(); GamePl2.CommitText();
        if (_viewModel?.SelectedGameProfile is { } game)
        {
            game.CpuPl1Watts = double.IsFinite(GamePl1.Value) ? (int)Math.Round(GamePl1.Value) : null;
            game.CpuPl2Watts = double.IsFinite(GamePl2.Value) ? (int)Math.Round(GamePl2.Value) : null;
        }
        _viewModel?.SaveSelectedGameProfile();
    }
}
