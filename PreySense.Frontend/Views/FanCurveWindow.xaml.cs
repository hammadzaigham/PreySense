using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using PreySense.Frontend.Controls;
using PreySense.Frontend.ViewModels;
using PreySense.Mode;

namespace PreySense.Frontend.Views;

public partial class FanCurveWindow : Window
{
    private static readonly byte[] Modes = [6, 0, 1, 4, 5];
    private readonly Dictionary<byte, FanCurveProfile> _drafts = new();
    private readonly List<int> _visibleStageIndices = new();
    private int[] _dutyValues = Array.Empty<int>();
    private int[] _temperatureLabels = Array.Empty<int>();
    private int _leadInDuty;
    private byte _mode = 1;
    private bool _gpu, _loading, _applying;
    private FanCurveProfile _profile = new();
    private MainViewModel? Vm => DataContext as MainViewModel;
    private bool SynchronizeFanSpeeds => SyncFansRow.IsChecked ?? Vm?.SynchronizeFanSpeeds ?? true;

    public FanCurveWindow()
    {
        InitializeComponent();
        Curve.Edited += (_, _) => OnCurveEdited();
        Curve.SelectionChanged += (_, _) => SyncSelectedStage();
        System.ComponentModel.DependencyPropertyDescriptor.FromProperty(
            SettingSwitchRow.IsCheckedProperty, typeof(SettingSwitchRow))
            ?.AddValueChanged(SyncFansRow, (_, _) => OnSyncFansChanged());
        UpdateEditor();
        Loaded += (_, _) =>
        {
            if (Vm == null) return;
            _loading = true;
            _mode = Vm.ActivePowerMode;
            ProfileRow.SelectedItem = ProfileManager.ModeToProfileName(_mode);
            _loading = false;
            LoadProfile();
        };
        ProfileRow.AddHandler(ComboBox.SelectionChangedEvent, new SelectionChangedEventHandler(Profile_Changed));
    }

    private void SaveDraft()
    {
        _profile.ControlMode = MaxOption.IsChecked == true ? FanControlMode.Max
            : CustomOption.IsChecked == true ? FanControlMode.Custom : FanControlMode.Auto;
        _profile.RampUpSeconds = Math.Clamp((int)Math.Round(DelayRow.Value), 0, 5);
        _drafts[_mode] = _profile;
    }

    private void LoadProfile()
    {
        _loading = true;
        _profile = _drafts.TryGetValue(_mode, out var draft) ? draft : Vm?.LoadFanProfile(_mode) ?? FanCurveProfile.CreateDefault(_mode);
        AutoOption.IsChecked = _profile.ControlMode == FanControlMode.Auto;
        MaxOption.IsChecked = _profile.ControlMode == FanControlMode.Max;
        CustomOption.IsChecked = _profile.ControlMode == FanControlMode.Custom;
        DelayRow.Value = Math.Clamp(_profile.RampUpSeconds, 0, 5);
        SyncFansRow.IsChecked = Vm?.SynchronizeFanSpeeds ?? FanCurveStorage.LoadSynchronizeFanSpeeds();
        SyncDelayValueText();
        Curve.SelectedIndex = -1;
        _loading = false;
        UpdateEditor();
        SyncCurve();
    }

    private void OnSyncFansChanged()
    {
        if (_loading) return;
        bool sync = SyncFansRow.IsChecked == true;
        if (Vm != null)
            Vm.SynchronizeFanSpeeds = sync;
        else
            FanCurveStorage.SaveSynchronizeFanSpeeds(sync);

        if (sync)
        {
            for (int i = 0; i < _profile.Stages.Length; i++)
            {
                _profile.Stages[i].SetDuty(!_gpu, _profile.Stages[i].DutyPercent(_gpu));
                _profile.Stages[i].Normalize();
            }
            if (_visibleStageIndices.Count > 0 && _visibleStageIndices.Count == _temperatureLabels.Length)
            {
                FanStage.ApplyEntryTemperatures(_profile.Stages, _visibleStageIndices, _temperatureLabels, !_gpu);
            }
            SyncCurve();
            SaveDraft();
        }
    }

    private void DelayRow_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || DelayRow == null) return;
        SyncDelayValueText();
        if (CustomOption.IsChecked == true)
            SaveDraft();
    }

    private void SyncDelayValueText()
    {
        int seconds = Math.Clamp((int)Math.Round(DelayRow.Value), 0, 5);
        DelayRow.ValueText = $"{seconds} s";
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
            if (SynchronizeFanSpeeds)
            {
                FanStage.ApplyEntryTemperatures(_profile.Stages, _visibleStageIndices, _temperatureLabels, !_gpu);
            }
        }

        int duty = Math.Clamp(_dutyValues[visible], 0, 100);
        _profile.Stages[stageIndex].SetDuty(_gpu, duty);
        if (SynchronizeFanSpeeds)
        {
            _profile.Stages[stageIndex].SetDuty(!_gpu, duty);
        }
        _profile.Stages[stageIndex].Normalize();
        _dutyValues[visible] = _profile.Stages[stageIndex].DutyPercent(_gpu);
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
            RowRpm.IsEnabled = false;
            RowTemp.Title = "Temp";
            RowDuty.Title = "Duty";
            RowRpm.Title = "RPM";
            return;
        }

        bool isCustom = CustomOption.IsChecked == true;
        _loading = true;
        RowTemp.IsEnabled = isCustom;
        RowDuty.IsEnabled = isCustom;
        RowRpm.IsEnabled = isCustom;
        string fan = _gpu ? "GPU" : "CPU";
        int visible = Curve.SelectedIndex;
        int celsius = visible >= 0 && visible < _temperatureLabels.Length
            ? _temperatureLabels[visible]
            : WpfFanCurveControl.MinCelsius;
        RowTemp.Title = $"{fan} Temp";
        RowDuty.Title = $"{fan} Duty";
        RowRpm.Title = $"{fan} RPM";
        var (minDuty, maxDuty) = WpfFanCurveControl.DutyBounds(_dutyValues, visible);
        RowDuty.Minimum = minDuty;
        RowDuty.Maximum = maxDuty;
        var (minTemp, maxTemp) = WpfFanCurveControl.TemperatureBounds(_temperatureLabels, visible);
        RowTemp.Minimum = minTemp;
        RowTemp.Maximum = maxTemp;
        RowTemp.Value = celsius;
        int duty = stage.DutyPercent(_gpu);
        int maxRpm = _gpu ? FanStage.GpuMaxRpm : FanStage.CpuMaxRpm;
        RowDuty.Value = duty;
        RowRpm.Value = FanStage.DutyToRpm(duty, maxRpm);
        _loading = false;
        UpdateEditor();
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
            if (SynchronizeFanSpeeds)
            {
                FanStage.ApplyEntryTemperatures(_profile.Stages, _visibleStageIndices, _temperatureLabels, !_gpu);
            }
        }
        else if (ReferenceEquals(sender, RowDuty))
        {
            WpfFanCurveControl.SetDuty(_dutyValues, visible, (int)Math.Round(RowDuty.Value));
            stage.SetDuty(_gpu, _dutyValues[visible]);
            if (SynchronizeFanSpeeds)
            {
                stage.SetDuty(!_gpu, _dutyValues[visible]);
            }
        }
        else if (ReferenceEquals(sender, RowRpm))
        {
            int maxRpm = _gpu ? FanStage.GpuMaxRpm : FanStage.CpuMaxRpm;
            int duty = FanStage.RpmToDuty((int)Math.Round(RowRpm.Value), maxRpm);
            WpfFanCurveControl.SetDuty(_dutyValues, visible, duty);
            stage.SetDuty(_gpu, _dutyValues[visible]);
            if (SynchronizeFanSpeeds)
            {
                stage.SetDuty(!_gpu, _dutyValues[visible]);
            }
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
        if (Vm != null && !_applying)
        {
            SetApplying(true);
            try
            {
                await SaveProfileAsync(Vm, _mode, _profile);
                if (await EnsureModeActiveAsync(Vm))
                    await Vm.ApplyFanProfileAsync();
            }
            catch (Exception ex) { ReportFailure(ex); }
            finally { SetApplying(false); }
        }
    }

    private void UpdateEditor()
    {
        if (CurveEditor == null) return;
        bool isCustom = CustomOption.IsChecked == true;
        CurveEditor.IsEnabled = isCustom && !_applying;
        CurveEditor.Opacity = isCustom ? 1.0 : 0.45;
        if (FooterPanel != null)
            FooterPanel.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Profile_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _applying || !IsLoaded || Vm == null) return;
        int index = Array.IndexOf(Vm.FanCurveModeNames, ProfileRow.SelectedItem as string);
        if (index < 0 || Modes[index] == _mode) return;
        SaveDraft();
        _mode = Modes[index];
        LoadProfile();
        await Vm.SetModeAsync(_mode);
    }

    private void FanTab_Changed(object sender, RoutedEventArgs e)
    {
        if (Curve == null) return;
        Curve.SelectedIndex = -1;
        _gpu = FanTabs.SelectedIndex == 1;
        SyncCurve();
    }

    private async System.Threading.Tasks.Task<bool> EnsureModeActiveAsync(MainViewModel vm)
    {
        if (await System.Threading.Tasks.Task.Run(() => vm.IsGpuHardwareModeActive(_mode))) return true;
        await vm.ApplyModeSwitchAsync(_mode);
        return await System.Threading.Tasks.Task.Run(() => vm.IsGpuHardwareModeActive(_mode));
    }

    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (Vm == null || _applying) return;
        SetApplying(true);
        try
        {
            var defaultProfile = FanCurveProfile.CreateDefault(_mode);
            defaultProfile.ControlMode = FanControlMode.Custom;
            _drafts[_mode] = defaultProfile;
            await SaveProfileAsync(Vm, _mode, defaultProfile);
            LoadProfile();
            if (!await EnsureModeActiveAsync(Vm)) return;
            await Vm.ApplyFanProfileAsync();
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

    private static System.Threading.Tasks.Task SaveProfileAsync(MainViewModel vm, byte mode, FanCurveProfile profile)
    {
        var snapshot = profile.Copy();
        return System.Threading.Tasks.Task.Run(() => vm.SaveFanProfile(mode, snapshot));
    }

    private async System.Threading.Tasks.Task SaveAllAsync()
    {
        if (Vm is not { } vm) return;
        SaveDraft();
        vm.SynchronizeFanSpeeds = SynchronizeFanSpeeds;
        var snapshots = _drafts.Select(entry => (entry.Key, Profile: entry.Value.Copy())).ToArray();
        await System.Threading.Tasks.Task.Run(() =>
        {
            foreach (var (mode, profile) in snapshots) vm.SaveFanProfile(mode, profile);
        });
        _drafts.Clear();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_applying) return;
        SetApplying(true);
        try { await SaveAllAsync(); }
        catch (Exception ex) { ReportFailure(ex); }
        finally { SetApplying(false); }
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (Vm == null || _applying) return;
        SetApplying(true);
        try
        {
            await SaveAllAsync();
            if (!await EnsureModeActiveAsync(Vm)) return;
            await Vm.ApplyFanProfileAsync();
        }
        catch (Exception ex) { ReportFailure(ex); }
        finally { SetApplying(false); }
    }
}
