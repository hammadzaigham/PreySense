using PreySense.Core.Configuration;
using System.Drawing;
using Microsoft.Win32;

namespace PreySense.Overlay;

public partial class HardwareOverlay
{
    private void EnsureFpsMonitor()
    {
        if (!_showFps || !IsShowing)
        {
            _fps?.Dispose(); _fps = null; ResetFpsDisplayState(); return;
        }
        if (_fps != null || !_active) return;
        ResetFpsDisplayState();
        var fps = new EtwFpsMonitor();
        _fps = fps;
        _ = Task.Run(() =>
        {
            if (!_active || !ReferenceEquals(_fps, fps)) return;
            fps.Start();
            if (!_active || !ReferenceEquals(_fps, fps)) { fps.Dispose(); return; }
            _foregroundEvents?.Refresh(); // Start resets TargetPid; reapply the current target once ready.
        });
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (_active) RestorePosition();
    }

    private void OnOverlayPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (_active && e.Mode == PowerModes.Resume) _foregroundEvents?.Refresh();
    }

    private void RestorePosition(OverlayConfig? config = null)
    {
        config ??= ConfigurationService.Instance.GetCurrent().Overlay;
        string hPos = config.PositionHorizontal;
        string vPos = config.PositionVertical;
        if (!string.IsNullOrEmpty(hPos) && !string.IsNullOrEmpty(vPos)) { PlaceConfiguredPosition(hPos, vPos); return; }
        int anchor = config.Anchor;
        if (anchor < 0) { PositionAtTopLeft(); return; }
        int offsetX = config.OffsetX;
        int offsetY = config.OffsetY;
        Screen screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
        bool isRight = (anchor & 1) != 0, isBottom = (anchor & 2) != 0;
        int x = isRight ? screen.Bounds.Right - Width - offsetX : screen.Bounds.X + offsetX;
        int y = isBottom ? screen.Bounds.Bottom - Height - offsetY : screen.Bounds.Y + offsetY;
        const int margin = 5;
        Location = new Point(
            Math.Clamp(x, screen.Bounds.Left + margin, screen.Bounds.Right - Width - margin),
            Math.Clamp(y, screen.Bounds.Top + margin, screen.Bounds.Bottom - Height - margin));
    }

    public void StartOverlay()
    {
        ClearGraphHistory();
        var config = LoadDisplayConfiguration();
        HardwareControl.ResetMetrics();
        _active = true; _lastFgPid = 0; _hidden = false;
        _shownPid = 0; _classifiedPid = 0; _classifiedProcessName = null; _fgDesktop = false;
        _wasOwnWindow = false; _keepOverlayAfterUiHide = false;
        _scalePercent = Math.Clamp(config.ScalePercent, MinScalePercent, MaxScalePercent);
        ApplyColors(config); SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += OnOverlayPowerModeChanged;
        _fps?.Dispose(); _fps = null; ResetFpsDisplayState(); Size = CalculateOverlaySize(_scalePercent);
        RestorePosition(config); base.Show();
        Invalidate();
        EnsureFpsMonitor();
        StartForegroundEvents();
        PreySense.Core.Services.TelemetryCoordinator.ConfigureFrameSampler(SampleFrameTelemetry);
        PreySense.Core.Services.TelemetryCoordinator.Heartbeat -= Tick;
        PreySense.Core.Services.TelemetryCoordinator.Heartbeat += Tick;
        PreySense.Core.Services.TelemetryCoordinator.SnapshotAvailable -= OnTelemetrySnapshot;
        PreySense.Core.Services.TelemetryCoordinator.SnapshotAvailable += OnTelemetrySnapshot;
        SyncOverlayTelemetryDemand();
    }

    private void SyncOverlayTelemetryDemand()
    {
        EnsureFpsMonitor();
        PreySense.Core.Services.TelemetryCoordinator.SetOverlayActive(IsShowing && _showFps);
        PreySense.Core.Services.TelemetryCoordinator.SetDemand(PreySense.Core.Services.TelemetryDemand.Overlay,
            IsShowing && _selectedMetrics != PreySense.Core.Services.TelemetryMetrics.None);
    }

    private OverlayConfig LoadDisplayConfiguration()
    {
        var config = PreySense.Core.Configuration.ConfigurationService.Instance.GetCurrent().Overlay;
        var slots = config.Slots;
        _columnCpu = slots.Select(slot => slot.CpuMetric).ToArray();
        _columnGpu = slots.Select(slot => slot.GpuMetric).ToArray();
        _columnGraphs = slots.Select(slot => slot.ShowGraph).ToArray();
        var previousGraphs = _graphMetrics.ToHashSet();
        _graphMetrics.Clear();
        for (int i = 0; i < _columnCpu.Length; i++)
        {
            if (!_columnGraphs[i]) continue;
            AddGraph(_columnCpu[i], false);
            AddGraph(_columnGpu[i], true);
        }
        void AddGraph(string metric, bool gpu)
        {
            if (metric == "None") return;
            if (metric == "Max Core Usage") metric = "Max Core";
            var graph = (metric, gpu);
            if (_graphMetrics.Contains(graph)) return;
            _graphMetrics.Add(graph);
            if (!previousGraphs.Contains(graph)) Array.Fill(HistoryFor(metric, gpu), float.NaN);
        }
        var metrics = PreySense.Core.Services.TelemetryMetrics.None;
        for (int i = 0; i < _columnCpu.Length; i++)
            metrics |= PreySense.Core.Services.TelemetrySelection.Cpu(_columnCpu[i]) | PreySense.Core.Services.TelemetrySelection.Gpu(_columnGpu[i]);
        PreySense.Core.Services.TelemetryCoordinator.SetOverlayMetrics(metrics);
        _selectedMetrics = metrics;
        _showFps = config.ShowFps;
        return config;
    }

    public void StopOverlay()
    {
        _active = false; SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged -= OnOverlayPowerModeChanged;
        StopForegroundEvents();
        PreySense.Core.Services.TelemetryCoordinator.Heartbeat -= Tick;
        PreySense.Core.Services.TelemetryCoordinator.SetOverlayActive(false);
        PreySense.Core.Services.TelemetryCoordinator.ConfigureFrameSampler(null);
        PreySense.Core.Services.TelemetryCoordinator.SnapshotAvailable -= OnTelemetrySnapshot;
        PreySense.Core.Services.TelemetryCoordinator.SetDemand(PreySense.Core.Services.TelemetryDemand.Overlay, false);
        PreySense.Core.Services.TelemetryCoordinator.SetOverlayMetrics(PreySense.Core.Services.TelemetryMetrics.None);
        ClearGraphHistory();
        _dragModeActive = false; _dragging = false; _fps?.Dispose(); _fps = null; ResetFpsDisplayState();
        _font?.Dispose(); _font = null; _rpmFont?.Dispose(); _rpmFont = null; _unitFont?.Dispose(); _unitFont = null;
        _fpsBold?.Dispose(); _fpsBold = null; _axPen?.Dispose(); _axPen = null;
        _lastScale = 0f; base.Hide();
    }
}
