using System.Drawing;
using Microsoft.Win32;

namespace PreySense.Overlay;

public partial class HardwareOverlay
{
    private void EnsureFpsMonitor()
    {
        bool needFps = _showFps || _gameOnly;
        if (!needFps)
        {
            _fps?.Dispose(); _fps = null; ResetFpsDisplayState(); return;
        }
        if (_fps != null || !(_gameOnly || AppConfig.IsOverlay())) return;
        ResetFpsDisplayState();
        _fps = new EtwFpsMonitor();
        _ = Task.Run(() => _fps.Start());
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (_active) RestorePosition();
    }

    private void RestorePosition()
    {
        string hPos = AppConfig.GetString("overlay_pos_horizontal");
        string vPos = AppConfig.GetString("overlay_pos_vertical");
        if (!string.IsNullOrEmpty(hPos) && !string.IsNullOrEmpty(vPos)) { SetConfiguredPosition(hPos, vPos); return; }
        int anchor = AppConfig.Get("overlay_anchor", -1);
        if (anchor < 0) { PositionAtTopLeft(); return; }
        int offsetX = AppConfig.Get("overlay_offset_x", MarginFromEdge);
        int offsetY = AppConfig.Get("overlay_offset_y", MarginFromEdge);
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
        AppConfig.EnsureDefaultOverlayColumns(); LoadDisplayConfiguration();
        _active = true; _lastFgPid = 0; _gameOnly = AppConfig.IsOverlayGameOnly(); _hidden = false;
        _shownPid = 0; _classifiedPid = 0; _classifiedProcessName = null; _fgDesktop = false;
        _wasOwnWindow = false; _keepOverlayAfterUiHide = false;
        _scalePercent = Math.Clamp(AppConfig.Get("overlay_scale_percent", 100), MinScalePercent, MaxScalePercent);
        ApplyColors(); SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        _fps?.Dispose(); _fps = null; ResetFpsDisplayState(); Size = CalculateOverlaySize(_scalePercent);
        RestorePosition(); base.Show();
        if (_gameOnly) { _hidden = true; NativeMethods.ShowWindow(Handle, NativeMethods.SW_HIDE); } else Invalidate();
        RestorePosition(); _timer.Interval = PollIntervalMs; _timer.Start(); EnsureFpsMonitor();
        _ = Task.Run(() => { try { HardwareControl.Warmup(); } catch { } if (_active) { try { Tick(); } catch { } } });
    }

    private void LoadDisplayConfiguration()
    {
        for (int i = 1; i <= _columnCpu.Length; i++)
        {
            var pair = AppConfig.ParseOverlayColumn(AppConfig.GetString($"overlay_col_{i}", AppConfig.DefaultOverlayColumn(i)));
            _columnCpu[i - 1] = pair.Cpu; _columnGpu[i - 1] = pair.Gpu;
            _columnGraphs[i - 1] = AppConfig.Get($"overlay_col_{i}_graph", i == 5 ? 1 : 0) == 1;
        }
        _showFps = AppConfig.Get("overlay_show_fps", 1) == 1;
    }

    public void StopOverlay()
    {
        _active = false; SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged; _timer.Stop();
        _dragModeActive = false; _dragging = false; _fps?.Dispose(); _fps = null; ResetFpsDisplayState();
        _font?.Dispose(); _font = null; _rpmFont?.Dispose(); _rpmFont = null; _unitFont?.Dispose(); _unitFont = null;
        _fpsBold?.Dispose(); _fpsBold = null; _totalPen?.Dispose(); _totalPen = null; _axPen?.Dispose(); _axPen = null;
        _lastScale = 0f; base.Hide();
    }
}
