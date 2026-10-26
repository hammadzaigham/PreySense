using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PreySense.Frontend.ViewModels;
using PreySense.Mode;

namespace PreySense.Frontend.Views;

public partial class FanCurveWindow : Window
{
    private static readonly byte[] Modes = [6, 0, 1, 4, 5];
    private readonly Dictionary<byte, FanCurveProfile> _drafts = new();
    private readonly DispatcherTimer _liveTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private byte _mode = 1;
    private bool _gpu, _loading;
    private bool _applying;
    private FanCurveProfile _profile = new();
    private MainViewModel? Vm => DataContext as MainViewModel;

    public FanCurveWindow()
    {
        InitializeComponent();
        Curve.Edited += (_, _) => SyncPoint();
        Curve.SelectionChanged += (_, _) => SyncPoint();
        UpdateEditor();
        Loaded += (_, _) =>
        {
            if (Vm == null) return;
            _loading = true;
            _mode = Vm.ActivePowerMode;
            ProfileRow.SelectedItem = ProfileManager.ModeToProfileName(_mode);
            _loading = false;
            LoadProfile();
            _liveTimer.Start();
        };
        ProfileRow.AddHandler(ComboBox.SelectionChangedEvent, new SelectionChangedEventHandler(Profile_Changed));
        RampRow.AddHandler(System.Windows.Controls.Primitives.RangeBase.ValueChangedEvent,
            new RoutedPropertyChangedEventHandler<double>((_, _) =>
            {
                RampRow.ValueText = $"{Math.Round(RampRow.Value)} s";
                if (!_loading) SaveDraft();
            }));
        _liveTimer.Tick += (_, _) => UpdateLive();
        Closed += (_, _) => _liveTimer.Stop();
    }
    private void SaveDraft()
    {
        _profile.ControlMode = MaxOption.IsChecked == true ? FanControlMode.Max
            : CustomOption.IsChecked == true ? FanControlMode.Custom : FanControlMode.Auto;
        _profile.RampUpSeconds = (int)Math.Round(RampRow.Value);
        _drafts[_mode] = _profile;
    }
    private void LoadProfile()
    {
        _loading = true;
        _profile = _drafts.TryGetValue(_mode, out var draft) ? draft : Vm?.LoadFanProfile(_mode) ?? FanCurveProfile.CreateDefault(_mode);
        AutoOption.IsChecked = _profile.ControlMode == FanControlMode.Auto;
        MaxOption.IsChecked = _profile.ControlMode == FanControlMode.Max;
        CustomOption.IsChecked = _profile.ControlMode == FanControlMode.Custom;
        RampRow.Value = _profile.RampUpSeconds;
        RampRow.ValueText = $"{_profile.RampUpSeconds} s";
        if (Curve != null) Curve.SelectedIndex = -1;
        _loading = false;
        UpdateEditor();
        SyncCurve();
    }
    private async void FanMode_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        SaveDraft();
        UpdateEditor();
        if (Vm != null && !_applying)
        {
            _applying = true;
            try
            {
                Vm.SaveFanProfile(_mode, _profile);
                if (!Vm.IsGpuHardwareModeActive(_mode))
                    await Vm.ApplyModeSwitchAsync(_mode);
                if (Vm.IsGpuHardwareModeActive(_mode))
                {
                    await Vm.ApplyFanProfileAsync();
                    string modeTitle = ProfileManager.ModeToProfileName(_mode);
                    Vm.StatusMessage = CustomOption.IsChecked == true
                        ? $"{modeTitle} custom fan curve activated."
                        : $"{modeTitle} fans set to {(MaxOption.IsChecked == true ? "Max" : "Auto")}.";
                }
            }
            catch (Exception ex)
            {
                Vm.StatusMessage = $"Fan mode apply failed: {ex.Message}";
            }
            finally
            {
                _applying = false;
            }
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
        if (PointRow != null)
            PointRow.IsEnabled = isCustom && Curve != null && Curve.SelectedIndex >= 0;
    }
    private async void Profile_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || !IsLoaded || Vm == null) return;
        int index = Array.IndexOf(Vm.GameModeNames, ProfileRow.SelectedItem as string);
        if (index < 0 || Modes[index] == _mode) return;
        SaveDraft();
        _mode = Modes[index];
        LoadProfile();
        await Vm.SetModeAsync(_mode);
    }
    private void SyncCurve()
    {
        Curve.Values = _gpu ? _profile.Gpu : _profile.Cpu;
        SyncPoint();
        UpdateLive();
    }
    private void SyncPoint()
    {
        if (Curve == null || Curve.Values == null || Curve.Values.Length != FanCurveProfile.PointCount || PointRow == null) return;
        _loading = true;
        if (Curve.SelectedIndex < 0 || Curve.SelectedIndex >= FanCurveProfile.PointCount)
        {
            PointRow.IsEnabled = false;
            PointRow.Title = "Selected Point";
            PointRow.Minimum = 0;
            PointRow.Maximum = 100;
            PointRow.Value = 0;
            _loading = false;
            return;
        }
        bool isCustom = CustomOption.IsChecked == true;
        PointRow.IsEnabled = isCustom;
        PointRow.Title = $"{FanCurveProfile.MinimumTemperature + Curve.SelectedIndex * FanCurveProfile.TemperatureStep}°C";
        PointRow.Minimum = Curve.SelectedIndex == 0 ? 0 : Curve.Values[Curve.SelectedIndex - 1];
        PointRow.Maximum = Curve.SelectedIndex == Curve.Values.Length - 1 ? 100 : Curve.Values[Curve.SelectedIndex + 1];
        PointRow.Value = Curve.Values[Curve.SelectedIndex];
        _loading = false;
    }
    private void Point_Committed(object? sender, EventArgs e)
    {
        if (_loading || CustomOption.IsChecked != true || Curve == null || Curve.Values == null || Curve.Values.Length != FanCurveProfile.PointCount || Curve.SelectedIndex < 0) return;
        FanCurveProfile.SetPoint(Curve.Values, Curve.SelectedIndex, (int)Math.Round(PointRow.Value));
        Curve.InvalidateVisual();
        SyncPoint();
    }
    private void UpdateLive()
    {
        if (Vm == null) return;
        int temp = FanCurveProfile.CoolingTemperature(Vm.CpuTemp, Vm.GpuTemp);
        Curve.Temperature = temp;

        int? duty = _gpu ? Vm.AppliedGpuFanPercent : Vm.AppliedCpuFanPercent;
        if (duty.HasValue && duty.Value is >= 0 and <= 100)
        {
            Curve.CurrentDuty = duty.Value;
        }
        else if (MaxOption.IsChecked == true)
        {
            Curve.CurrentDuty = 100;
        }
        else if (Curve.Values != null && Curve.Values.Length == FanCurveProfile.PointCount)
        {
            int clampedTemp = Math.Clamp(temp, FanCurveProfile.MinimumTemperature, FanCurveProfile.MaximumTemperature);
            int idx = FanCurveProfile.PointIndex(clampedTemp);
            int baseDuty = Curve.Values[idx];
            if (idx < Curve.Values.Length - 1 && clampedTemp < FanCurveProfile.MaximumTemperature)
            {
                int nextDuty = Curve.Values[idx + 1];
                int rem = (clampedTemp - FanCurveProfile.MinimumTemperature) % FanCurveProfile.TemperatureStep;
                Curve.CurrentDuty = Math.Round(baseDuty + (nextDuty - baseDuty) * rem / (double)FanCurveProfile.TemperatureStep);
            }
            else
            {
                Curve.CurrentDuty = baseDuty;
            }
        }
        else
        {
            int rpm = _gpu ? Vm.GpuRpm : Vm.CpuRpm;
            Curve.CurrentDuty = rpm > 0 ? Math.Clamp(Math.Round(rpm * 100.0 / 5000.0), 0, 100) : -1;
        }
    }
    private void FanTab_Changed(object sender, RoutedEventArgs e)
    {
        if (Curve == null) return;
        Curve.SelectedIndex = -1;
        _gpu = FanTabs.SelectedIndex == 1;
        SyncCurve();
    }
    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (Vm == null || _applying) return;
        _applying = true;
        try
        {
            var defaultProfile = FanCurveProfile.CreateDefault(_mode);
            _drafts[_mode] = defaultProfile;
            Vm?.SaveFanProfile(_mode, defaultProfile);
            LoadProfile();
            Curve.InvalidateVisual();
            if (Vm != null && !Vm.IsGpuHardwareModeActive(_mode))
                await Vm.ApplyModeSwitchAsync(_mode);
            if (Vm == null || !Vm.IsGpuHardwareModeActive(_mode))
            {
                if (Vm != null) Vm.StatusMessage = $"{ProfileManager.ModeToProfileName(_mode)} fans reset to factory defaults (Auto); mode could not be activated on hardware.";
                return;
            }
            await Vm.ApplyFanProfileAsync();
            Vm.StatusMessage = $"{ProfileManager.ModeToProfileName(_mode)} fans reset to factory defaults (Auto).";
        }
        catch (Exception ex) { if (Vm != null) Vm.StatusMessage = $"Fan reset failed: {ex.Message}"; }
        finally { _applying = false; }
    }
    private void SaveAll()
    {
        SaveDraft();
        foreach (var (mode, profile) in _drafts) Vm?.SaveFanProfile(mode, profile);
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try { SaveAll(); }
        catch (Exception ex) { if (Vm != null) Vm.StatusMessage = $"Fan curve save failed: {ex.Message}"; }
    }
    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (Vm == null || _applying) return;
        _applying = true;
        try
        {
            SaveAll();
            if (Vm != null && !Vm.IsGpuHardwareModeActive(_mode))
                await Vm.ApplyModeSwitchAsync(_mode);
            if (Vm == null || !Vm.IsGpuHardwareModeActive(_mode)) { if (Vm != null) Vm.StatusMessage = "Fan profile mode could not be activated."; return; }
            await Vm.ApplyFanProfileAsync();
        }
        catch (Exception ex) { if (Vm != null) Vm.StatusMessage = $"Fan curve apply failed: {ex.Message}"; }
        finally { _applying = false; }
    }
}
