using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using PreySense.Frontend.Controls;
using PreySense.Frontend.ViewModels;
using PreySense.Mode;
using PreySense.Core.Services;
using PreySense.Frontend.Services;

namespace PreySense.Frontend.Views;

public partial class FanCurveWindow : Window
{
    private readonly FanWindowTelemetry _fanTelemetry = new();
    private int _fanTelemetryRevision;
    private static readonly byte[] Modes = [6, 0, 1, 4, 5];
    private readonly Dictionary<byte, FanCurveProfile> _drafts = new();
    private readonly List<int> _visibleStageIndices = new();
    private int[] _dutyValues = Array.Empty<int>();
    private int[] _temperatureLabels = Array.Empty<int>();
    private int _leadInDuty;
    private byte _mode = 1;
    private bool _gpu, _loading, _applying;
    private FanCurveProfile _profile = new();
    private readonly FanReferenceResolution _fanReference = FanReferenceResolver.BundledFallback;
    private MainViewModel? Vm => DataContext as MainViewModel;

    public FanCurveWindow()
    {
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height - 40;
        _fanTelemetry.SnapshotAvailable += OnFanWindowSnapshot;
        IsVisibleChanged += (_, _) =>
        {
            _fanTelemetryRevision++;
            ApplyFanReadings(TelemetrySnapshot.Empty);
            _fanTelemetry.SetVisible(IsVisible);
            if (IsVisible && IsLoaded && Vm != null && !_applying)
            {
                _drafts.Clear();
                _loading = true;
                _mode = Modes.Contains(Vm.ActivePowerMode) ? Vm.ActivePowerMode : (byte)1;
                ProfileRow.SelectedItem = ProfileManager.ModeToProfileName(_mode);
                _loading = false;
                LoadProfile();
                if (!Dispatcher.HasShutdownStarted) { UpdateRpmEstimate(); }
            }
        };
        Closed += (_, _) =>
        {
            _fanTelemetryRevision++;
            _fanTelemetry.Dispose();
            _fanTelemetry.SnapshotAvailable -= OnFanWindowSnapshot;
        };
        Curve.Edited += (_, _) => OnCurveEdited();
        Curve.SelectionChanged += (_, _) => SyncSelectedStage();
        UpdateEditor();
        Loaded += (_, _) =>
        {
            if (Vm == null) return;
            _loading = true;
            // Unknown hardware mode still opens an explicitly labelled Balanced editor.
            // Selecting an editor profile never changes the hardware operating mode.
            _mode = Modes.Contains(Vm.ActivePowerMode) ? Vm.ActivePowerMode : (byte)1;
            ProfileRow.SelectedItem = ProfileManager.ModeToProfileName(_mode);
            _loading = false;
            LoadProfile();
            if (!Dispatcher.HasShutdownStarted) { UpdateRpmEstimate(); }
        };
        ProfileRow.AddHandler(ComboBox.SelectionChangedEvent, new SelectionChangedEventHandler(Profile_Changed));
    }

    private void OnFanWindowSnapshot(TelemetrySnapshot snapshot)
    {
        if (Dispatcher.HasShutdownStarted) return;
        int revision = _fanTelemetryRevision;
        Dispatcher.BeginInvoke(() =>
        {
            if (IsVisible && revision == _fanTelemetryRevision) ApplyFanReadings(snapshot);
        });
    }

    private void ApplyFanReadings(TelemetrySnapshot snapshot)
    {
        var readings = FanWindowTelemetry.Format(snapshot);
        CpuTemperatureReading.Text = readings.CpuTemperature;
        CpuFanReading.Text = readings.CpuFan;
        GpuTemperatureReading.Text = readings.GpuTemperature;
        GpuFanReading.Text = readings.GpuFan;
    }

    private void SaveDraft()
    {
        _profile.ControlMode = MaxOption.IsChecked == true ? FanControlMode.Max
            : CustomOption.IsChecked == true ? FanControlMode.Custom : FanControlMode.Auto;
        _profile.UseSlider = Vm?.FanCustomUseSlider ?? _profile.UseSlider;
        _drafts[_mode] = _profile;
    }

    private void LoadProfile()
    {
        _loading = true;
        _profile = _drafts.TryGetValue(_mode, out var draft) ? draft : Vm?.LoadFanProfile(_mode) ?? FanCurveProfile.CreateDefault(_mode);
        AutoOption.IsChecked = _profile.ControlMode == FanControlMode.Auto;
        MaxOption.IsChecked = _profile.ControlMode == FanControlMode.Max;
        CustomOption.IsChecked = _profile.ControlMode == FanControlMode.Custom;
        Curve.SelectedIndex = -1;
        SyncManualSlider();
        _loading = false;
        UpdateEditor();
        SyncCurve();
    }

    internal static (int[] StageIndices, int[] Temperatures, int[] Duties, int LeadInDuty)
        BuildGraph(FanStage[] stages, bool gpu)
    {
        // Reachable via this fan's rise. Disabled (127) rise stops the chain.
        var candidates = new List<(int Stage, int EntryC, int Duty)>();
        var reachable = new List<int>();
        for (int i = 0; i < stages.Length; i++)
        {
            if (i > 0 && !stages[i - 1].HasActiveRise(gpu))
                break;
            reachable.Add(i);
        }

        int leadInDuty = reachable.Count > 0 ? stages[0].DutyPercent(gpu) : 0;
        for (int v = 0; v < reachable.Count; v++)
        {
            int stageIndex = reachable[v];
            if (stageIndex == 0)
                continue;

            var stage = stages[stageIndex];
            int duty = stage.DutyPercent(gpu);
            if (!stages[stageIndex - 1].HasActiveRise(gpu))
                continue;
            int entryC = Math.Clamp(
                stages[stageIndex - 1].RiseCelsius(gpu),
                WpfFanCurveControl.MinCelsius,
                WpfFanCurveControl.MaxCelsius);

            // Unique X: bump past a colliding previous entry instead of overlaying.
            if (candidates.Count > 0 && entryC <= candidates[^1].EntryC)
                entryC = Math.Min(WpfFanCurveControl.MaxCelsius, candidates[^1].EntryC + 1);
            if (entryC > WpfFanCurveControl.MaxCelsius)
                continue;
            candidates.Add((stageIndex, entryC, duty));
        }

        return (candidates.Select(point => point.Stage).ToArray(),
            candidates.Select(point => point.EntryC).ToArray(),
            candidates.Select(point => point.Duty).ToArray(), leadInDuty);
    }

    private void RebuildVisibleStages()
    {
        var graph = BuildGraph(_profile.Stages, _gpu);
        _visibleStageIndices.Clear();
        _visibleStageIndices.AddRange(graph.StageIndices);
        _temperatureLabels = graph.Temperatures;
        _dutyValues = graph.Duties;
        _leadInDuty = graph.LeadInDuty;
    }

    private void SyncCurve()
    {
        int previousStage = SelectedStageIndex;
        RebuildVisibleStages();
        Curve.Temperatures = _temperatureLabels;
        Curve.Values = _dutyValues;
        Curve.LeadInDuty = _leadInDuty;
        int visible = previousStage < 0 ? -1 : _visibleStageIndices.IndexOf(previousStage);
        Curve.SelectedIndex = visible;
        Curve.InvalidateVisual();
        SyncSelectedStage();
    }

    private int SelectedStageIndex
    {
        get
        {
            int visible = Curve.SelectedIndex;
            return visible >= 0 && visible < _visibleStageIndices.Count
                ? _visibleStageIndices[visible]
                : -1;
        }
    }

    private void OnCurveEdited()
    {
        if (_loading || CustomOption.IsChecked != true || Curve.SelectedIndex < 0) return;
        int stageIndex = SelectedStageIndex;
        if (stageIndex < 0) return;

        int visible = Curve.SelectedIndex;
        if (visible < _temperatureLabels.Length)
        {
            WpfFanCurveControl.SetTemperature(_temperatureLabels, visible, _temperatureLabels[visible]);
            FanStage.ApplyEntryTemperatures(_profile.Stages, _visibleStageIndices, _temperatureLabels, _gpu);
        }

        ApplyGraphDuties(_profile.Stages, _visibleStageIndices, _dutyValues, _gpu);
        SyncSelectedStage();
        SaveDraft();
    }

    private FanStage? SelectedStage
    {
        get
        {
            int index = SelectedStageIndex;
            return index >= 0 ? _profile.Stages[index] : null;
        }
    }

    private void SyncSelectedStage()
    {
        if (SelectedStage is not FanStage stage)
        {
            RowTemp.IsEnabled = false;
            RowDuty.IsEnabled = false;
            RowTemp.Title = "Temp";
            RowDuty.Title = LocalizationService.Format("{0} Fan Boost", _gpu ? "GPU" : "CPU");
            UpdateRpmEstimate();
            return;
        }

        bool isCustom = CustomOption.IsChecked == true;
        _loading = true;
        RowTemp.IsEnabled = isCustom;
        RowDuty.IsEnabled = isCustom;
        string fan = _gpu ? "GPU" : "CPU";
        int visible = Curve.SelectedIndex;
        int celsius = visible >= 0 && visible < _temperatureLabels.Length
            ? _temperatureLabels[visible]
            : WpfFanCurveControl.MinCelsius;
        RowTemp.Title = $"{fan} Temp";
        RowDuty.Title = LocalizationService.Format("{0} Fan Boost", _gpu ? "GPU" : "CPU");
        RowDuty.Minimum = 0;
        RowDuty.Maximum = 100;
        var (minTemp, maxTemp) = WpfFanCurveControl.TemperatureBounds(_temperatureLabels, visible);
        RowTemp.Minimum = minTemp;
        RowTemp.Maximum = maxTemp;
        RowTemp.Value = celsius;
        int duty = stage.DutyPercent(_gpu);
        RowDuty.Value = duty;
        UpdateRpmEstimate();
        _loading = false;
        UpdateEditor();
    }

    private void UpdateRpmEstimate()
    {
        if (!Modes.Contains(_mode) || SelectedStage is not FanStage stage)
        {
            EstimatedRpmReading.Text = "—";
            return;
        }
        int visible = Curve.SelectedIndex;
        int temperature = _temperatureLabels[visible];
        var estimate = _fanReference.EstimateAtTemperature(_mode, temperature, stage.DutyPercent(_gpu), _gpu);
        // Selected-temperature reference only; do not display unrelated hotter stages.
        int roundedRpm = (int)Math.Round(estimate.MinimumRpm / 100.0,
            MidpointRounding.AwayFromZero) * 100;
        EstimatedRpmReading.Text = $"{roundedRpm:N0}";
    }

    internal static void ApplyGraphDuties(FanStage[] stages, IReadOnlyList<int> stageIndices, int[] values, bool gpu)
    {
        for (int i = 0; i < stageIndices.Count && i < values.Length; i++)
        {
            var stage = stages[stageIndices[i]];
            stage.SetDuty(gpu, values[i]);
            stage.Normalize();
        }
    }

    private void StageField_Committed(object? sender, EventArgs e)
    {
        if (_loading || CustomOption.IsChecked != true || SelectedStage is not FanStage stage) return;
        int visible = Curve.SelectedIndex;
        int stageIndex = SelectedStageIndex;
        if (visible < 0 || stageIndex < 0) return;

        if (ReferenceEquals(sender, RowTemp))
        {
            WpfFanCurveControl.SetTemperature(_temperatureLabels, visible, (int)Math.Round(RowTemp.Value));
            FanStage.ApplyEntryTemperatures(_profile.Stages, _visibleStageIndices, _temperatureLabels, _gpu);
        }
        else if (ReferenceEquals(sender, RowDuty))
        {
            WpfFanCurveControl.SetDuty(_dutyValues, visible, (int)Math.Round(RowDuty.Value));
            ApplyGraphDuties(_profile.Stages, _visibleStageIndices, _dutyValues, _gpu);
        }
        stage.Normalize();
        SyncCurve();
        SaveDraft();
    }

    private async void FanMode_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || _applying) return;
        SaveDraft();
        UpdateEditor();
        SyncSelectedStage();
        if (_profile.ControlMode == FanControlMode.Custom || Vm is not { } vm) return;

        byte mode = _mode;
        var snapshot = _profile.Copy();
        SetApplying(true);
        try
        {
            await System.Threading.Tasks.Task.Run(() => vm.SaveFanProfile(mode, snapshot));
            _drafts.Remove(mode);
            if (mode == vm.ActivePowerMode) await vm.ApplyFanProfileAsync();
        }
        catch (Exception ex) { ReportFailure(ex); }
        finally { SetApplying(false); }
    }

    private void UpdateEditor()
    {
        if (CurveEditor == null) return;
        bool isCustom = CustomOption.IsChecked == true;
        bool useSlider = Vm?.FanCustomUseSlider ?? _profile.UseSlider;
        if (CustomContent != null)
            CustomContent.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
        if (SliderEditor != null)
        {
            SliderEditor.Visibility = isCustom && useSlider ? Visibility.Visible : Visibility.Collapsed;
            SliderEditor.IsEnabled = isCustom && useSlider && !_applying;
        }
        CurveEditor.Visibility = isCustom && !useSlider ? Visibility.Visible : Visibility.Collapsed;
        CurveEditor.IsEnabled = isCustom && !useSlider && !_applying;
        if (FooterPanel != null)
            FooterPanel.Visibility = isCustom && !useSlider ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SyncManualSlider()
    {
        if (ManualDutySlider == null) return;
        int duty = Math.Clamp(_gpu ? _profile.ManualGpuDutyPercent : _profile.ManualCpuDutyPercent, 0, 100);
        ManualDutySlider.Value = duty;
        UpdateManualSliderLabels(duty);
    }

    private void UpdateManualSliderLabels(int duty)
    {
        if (ManualSliderPercentText != null)
            ManualSliderPercentText.Text = $"{duty}%";
        if (ManualEstimatedRpmReading != null)
        {
            int maxRpm = _gpu ? (Vm?.FanGpuMaxRpm ?? _profile.GpuMaxRpm) : (Vm?.FanCpuMaxRpm ?? _profile.CpuMaxRpm);
            int estimated = (int)Math.Round(duty / 100.0 * maxRpm);
            ManualEstimatedRpmReading.Text = estimated.ToString();
        }
    }

    private void ManualSlider_DraftValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int duty = Math.Clamp((int)Math.Round(e.NewValue), 0, 100);
        UpdateManualSliderLabels(duty);
    }

    private async void ManualSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int duty = Math.Clamp((int)Math.Round(e.NewValue), 0, 100);
        UpdateManualSliderLabels(duty);
        if (_loading || _applying || CustomOption.IsChecked != true || Vm?.FanCustomUseSlider != true) return;
        if (_gpu) _profile.ManualGpuDutyPercent = duty;
        else _profile.ManualCpuDutyPercent = duty;
        SaveDraft();
        byte mode = _mode;
        var draft = _profile;
        var snapshot = draft.Copy();
        try
        {
            await Vm.ApplyManualFanDutyAsync(mode, snapshot);
            if (_drafts.TryGetValue(mode, out var current) && ReferenceEquals(current, draft)
                && current.ControlMode == snapshot.ControlMode && current.UseSlider == snapshot.UseSlider
                && current.ManualCpuDutyPercent == snapshot.ManualCpuDutyPercent
                && current.ManualGpuDutyPercent == snapshot.ManualGpuDutyPercent)
                _drafts.Remove(mode);
        }
        catch (Exception ex) { ReportFailure(ex); }
    }

    private void Profile_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _applying || !IsLoaded || Vm == null) return;
        int index = Array.IndexOf(Vm.FanCurveModeNames, ProfileRow.SelectedItem as string);
        if (index < 0 || Modes[index] == _mode) return;
        SaveDraft();
        _mode = Modes[index];
        LoadProfile();
    }

    private void FanTab_Changed(object sender, RoutedEventArgs e)
    {
        if (Curve == null) return;
        Curve.SelectedIndex = -1;
        _gpu = FanTabs.SelectedIndex == 1;
        _loading = true;
        SyncManualSlider();
        _loading = false;
        SyncCurve();
    }

    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (_applying) return;
        var defaultProfile = FanCurveProfile.CreateDefault(_mode);
        defaultProfile.ControlMode = _profile.ControlMode;
        _drafts[_mode] = defaultProfile;
        LoadProfile();
        SetApplying(true);
        try
        {
            var snapshot = defaultProfile.Copy();
            if (Vm is { } vm)
            {
                await System.Threading.Tasks.Task.Run(() => vm.SaveFanProfile(_mode, snapshot));
                if (_mode == vm.ActivePowerMode) await vm.ApplyFanProfileAsync();
            }
        }
        catch (Exception ex) { ReportFailure(ex); }
        finally { SetApplying(false); }
    }

    private void ReportFailure(Exception error)
    {
        AppLogger.Error($"Fan operation failed: {error}");
        OperationError.Text = $"Fan operation failed: {error.Message}";
        OperationError.Visibility = Visibility.Visible;
    }

    private void SetApplying(bool applying)
    {
        if (applying) OperationError.Visibility = Visibility.Collapsed;
        _applying = applying;
        ProfileRow.IsEnabled = AutoOption.IsEnabled = MaxOption.IsEnabled = CustomOption.IsEnabled = !applying;
        FooterPanel.IsEnabled = !applying;
        UpdateEditor();
    }

    private async System.Threading.Tasks.Task SaveAllAsync()
    {
        if (Vm is not { } vm) return;
        SaveDraft();
        var snapshots = _drafts.Select(entry => (entry.Key, Profile: entry.Value.Copy())).ToArray();
        await System.Threading.Tasks.Task.Run(() =>
        {
            PreySense.Core.Configuration.ConfigurationService.Instance.Update(cfg =>
            {
                foreach (var (mode, profile) in snapshots)
                {
                    var saved = profile.Copy();
                    saved.Normalize(mode);
                    var fanMode = mode switch
                    {
                        0 => cfg.Fans.Quiet,
                        6 => cfg.Fans.Eco,
                        4 => cfg.Fans.Performance,
                        5 => cfg.Fans.Turbo,
                        _ => cfg.Fans.Balanced
                    };
                    fanMode.ControlMode = (int)saved.ControlMode;
                    fanMode.Stages = saved.Stages.Select(stage =>
                        new PreySense.Core.Configuration.FanStageConfig(stage)).ToArray();
                }
            });
        });
        _drafts.Clear();
        if (snapshots.Any(entry => entry.Key == vm.ActivePowerMode))
            await vm.ApplyFanProfileAsync();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_applying) return;
        SetApplying(true);
        try { await SaveAllAsync(); }
        catch (Exception ex) { ReportFailure(ex); }
        finally { SetApplying(false); }
    }

}
