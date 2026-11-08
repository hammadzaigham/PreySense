using System;
using System.Collections.Generic;
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

    public FanCurveWindow()
    {
        InitializeComponent();
        Curve.Edited += (_, _) => OnCurveEdited();
        Curve.SelectionChanged += (_, _) => SyncSelectedStage();
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
        SyncDelayValueText();
        Curve.SelectedIndex = -1;
        _loading = false;
        UpdateEditor();
        SyncCurve();
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

    private void RebuildVisibleStages()
    {
        // Reachable via this fan's rise. Disabled (127) rise stops the chain.
        // Final EC stage is not plotted — curve continues flat at last duty.
        var candidates = new List<(int Stage, int EntryC, int Duty)>();
        var reachable = new List<int>();
        int lastIndex = _profile.Stages.Length - 1;
        for (int i = 0; i < _profile.Stages.Length; i++)
        {
            if (i > 0 && !_profile.Stages[i - 1].HasActiveRise(_gpu))
                break;
            reachable.Add(i);
        }

        _leadInDuty = reachable.Count > 0 ? _profile.Stages[0].SharedDutyPercent : 0;
        for (int v = 0; v < reachable.Count; v++)
        {
            int stageIndex = reachable[v];
            if (stageIndex == 0 || stageIndex == lastIndex)
                continue;

            var stage = _profile.Stages[stageIndex];
            int duty = stage.SharedDutyPercent;
            if (!_profile.Stages[stageIndex - 1].HasActiveRise(_gpu))
                continue;
            int entryC = Math.Clamp(
                _profile.Stages[stageIndex - 1].RiseCelsius(_gpu),
                WpfFanCurveControl.MinCelsius,
                WpfFanCurveControl.MaxCelsius);

            // Unique X: bump past a colliding previous entry instead of overlaying.
            if (candidates.Count > 0 && entryC <= candidates[^1].EntryC)
                entryC = Math.Min(WpfFanCurveControl.MaxCelsius, candidates[^1].EntryC + 1);
            if (entryC > WpfFanCurveControl.MaxCelsius)
                continue;
            candidates.Add((stageIndex, entryC, duty));
        }

        _visibleStageIndices.Clear();
        _dutyValues = new int[candidates.Count];
        _temperatureLabels = new int[candidates.Count];
        for (int v = 0; v < candidates.Count; v++)
        {
            _visibleStageIndices.Add(candidates[v].Stage);
            _dutyValues[v] = candidates[v].Duty;
            _temperatureLabels[v] = candidates[v].EntryC;
        }

        WpfFanCurveControl.EnforceNondecreasing(_dutyValues);
        FanStage.EnforceStrictlyIncreasing(_temperatureLabels);
        for (int v = 0; v < _temperatureLabels.Length; v++)
            _temperatureLabels[v] = Math.Clamp(_temperatureLabels[v], WpfFanCurveControl.MinCelsius, WpfFanCurveControl.MaxCelsius);

        if (_visibleStageIndices.Count > 0)
            FanStage.ApplyEntryTemperatures(_profile.Stages, _visibleStageIndices, _temperatureLabels, _gpu);

        for (int v = 0; v < _visibleStageIndices.Count; v++)
        {
            int stageIndex = _visibleStageIndices[v];
            _profile.Stages[stageIndex].SetSharedDuty(_dutyValues[v]);
            _profile.Stages[stageIndex].Normalize();
            _dutyValues[v] = _profile.Stages[stageIndex].SharedDutyPercent;
        }
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

        int duty = Math.Clamp(_dutyValues[visible], 0, 100);
        _profile.Stages[stageIndex].SetSharedDuty(duty);
        _profile.Stages[stageIndex].Normalize();
        _dutyValues[visible] = _profile.Stages[stageIndex].SharedDutyPercent;
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
        // Tabs only switch temperature thresholds; duty/RPM are shared by both fans.
        RowTemp.Title = $"{fan} Temp";
        RowDuty.Title = "Duty";
        RowRpm.Title = "RPM";
        var (minDuty, maxDuty) = WpfFanCurveControl.DutyBounds(_dutyValues, visible);
        RowDuty.Minimum = minDuty;
        RowDuty.Maximum = maxDuty;
        var (minTemp, maxTemp) = WpfFanCurveControl.TemperatureBounds(_temperatureLabels, visible);
        RowTemp.Minimum = minTemp;
        RowTemp.Maximum = maxTemp;
        RowTemp.Value = celsius;
        RowDuty.Value = stage.SharedDutyPercent;
        RowRpm.Value = FanStage.DutyToRpm(stage.SharedDutyPercent, FanStage.CpuMaxRpm);
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
        }
        else if (ReferenceEquals(sender, RowDuty))
        {
            WpfFanCurveControl.SetDuty(_dutyValues, visible, (int)Math.Round(RowDuty.Value));
            stage.SetSharedDuty(_dutyValues[visible]);
        }
        else if (ReferenceEquals(sender, RowRpm))
        {
            int duty = FanStage.RpmToDuty((int)Math.Round(RowRpm.Value), FanStage.CpuMaxRpm);
            WpfFanCurveControl.SetDuty(_dutyValues, visible, duty);
            stage.SetSharedDuty(_dutyValues[visible]);
        }

        stage.Normalize();
        SyncCurve();
        SaveDraft();
    }

    private async void FanMode_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        SaveDraft();
        UpdateEditor();
        SyncSelectedStage();
        if (Vm != null && !_applying)
        {
            _applying = true;
            try
            {
                Vm.SaveFanProfile(_mode, _profile);
                if (await EnsureModeActiveAsync(Vm))
                    await Vm.ApplyFanProfileAsync();
            }
            catch (Exception) { }
            finally { _applying = false; }
        }
    }

    private void UpdateEditor()
    {
        if (CurveEditor == null) return;
        bool isCustom = CustomOption.IsChecked == true;
        CurveEditor.IsEnabled = isCustom;
        CurveEditor.Opacity = isCustom ? 1.0 : 0.45;
        if (FooterPanel != null)
            FooterPanel.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Profile_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || !IsLoaded || Vm == null) return;
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
        _applying = true;
        try
        {
            var defaultProfile = FanCurveProfile.CreateDefault(_mode);
            defaultProfile.ControlMode = FanControlMode.Custom;
            _drafts[_mode] = defaultProfile;
            Vm.SaveFanProfile(_mode, defaultProfile);
            LoadProfile();
            if (!await EnsureModeActiveAsync(Vm)) return;
            await Vm.ApplyFanProfileAsync();
        }
        catch (Exception) { }
        finally { _applying = false; }
    }

    private void SaveAll()
    {
        SaveDraft();
        foreach (var (mode, profile) in _drafts) Vm?.SaveFanProfile(mode, profile);
        _drafts.Clear();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try { SaveAll(); }
        catch (Exception) { }
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (Vm == null || _applying) return;
        _applying = true;
        try
        {
            SaveAll();
            if (!await EnsureModeActiveAsync(Vm)) return;
            await Vm.ApplyFanProfileAsync();
        }
        catch (Exception) { }
        finally { _applying = false; }
    }
}
