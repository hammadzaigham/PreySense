using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PreySense.Overlay
{
    public partial class HardwareOverlay : OSDNativeForm
    {
        private const int MinScalePercent  = 50;
        private const int MaxScalePercent  = 200;
        private const int ScaleStepPercent = 25;
        private int _scalePercent = 100;

        // Fixed base scale, deliberately independent of Windows DPI. 2.0 reproduces
        // the size the overlay had at 200% display scaling â€” the desired default.
        private const float BaseScale = 2.0f;

        private bool _dragging;
        private Point _dragCursorStart;
        private Point _dragWindowStart;
        private bool _dragModeActive;

        // â”€â”€ Layout constants (base = 96 dpi) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private const float BaseFontSize = 9.0f;
        private const float BaseRpmFontSize = 6.0f;
        private const int BaseLineHeight = 15;
        private const int BaseLineSpacing = 1;
        private const int BasePadX = 3;
        private const int BasePadY = 2;
        private const int BaseFpsColWidth = 52;
        private const int CornerRadius = 0;
        private const int MarginFromEdge = 10;
        private const int BaseInnerPadX = 3;
        private const int BaseInnerPadY = 2;
        private const int BaseSectionGap = 1;
        private static readonly Color DefaultGpuColor = Color.White;
        private static readonly Color DefaultCpuColor = Color.White;
        private static readonly Color OverlayTopGlowColor = Color.FromArgb(8, 255, 255, 255);
        private static readonly Color OverlayBottomGlowColor = Color.FromArgb(20, 0, 0, 0);

        private const int DragMinAlpha = 110;
        private static readonly SolidBrush _dragBgBrush = new(Color.FromArgb(DragMinAlpha, 0, 0, 0));
        private static readonly SolidBrush _topGlowBrush = new(OverlayTopGlowColor);
        private static readonly SolidBrush _bottomGlowBrush = new(OverlayBottomGlowColor);
        private int _bgAlpha = 100;

        private SolidBrush _bgBrush = new(Color.FromArgb(100, 0, 0, 0));
        private SolidBrush _gpuBrush = new(DefaultGpuColor);
        private SolidBrush _cpuBrush = new(DefaultCpuColor);
        private static readonly Pen _graphCpuPen = new(Color.FromArgb(44, 124, 180), 1.5f);
        private static readonly Pen _graphGpuPen = new(Color.FromArgb(58, 170, 84), 1.5f);
        private static readonly SolidBrush _graphCpuBrush = new(Color.FromArgb(24, 44, 124, 180));
        private static readonly SolidBrush _graphGpuBrush = new(Color.FromArgb(34, 58, 170, 84));

        private float _lastScale = 0f;
        private Font? _font;
        private Font? _rpmFont;
        private Font? _unitFont;
        private Font? _fpsBold;
        private Pen? _totalPen;
        private Pen? _axPen;

        private readonly PointF[] _basePts = new PointF[HistoryLength];
        private readonly PointF[] _cpuPts  = new PointF[HistoryLength];
        private readonly PointF[] _gpuPts  = new PointF[HistoryLength];
        private readonly PointF[] _polyPts = new PointF[HistoryLength * 2];

        private int? _gpuUsage;
        private int? _cpuUsage;
        private int? _vramUsedMb;
        private int? _ramUsedMb;
        private const int HistoryLength = 60;
        private int _historyHead = 0;
        private int _lastIdlePaintStamp = int.MinValue;
        private readonly float[] _cpuUsageHistory = new float[HistoryLength];
        private readonly float[] _gpuUsageHistory = new float[HistoryLength];
        private readonly float[] _cpuTempHistory = new float[HistoryLength];
        private readonly float[] _gpuTempHistory = new float[HistoryLength];
        private readonly float[] _cpuClockHistory = new float[HistoryLength];
        private readonly float[] _eCoreHistory = new float[HistoryLength];
        private readonly float[] _gpuClockHistory = new float[HistoryLength];
        private readonly float[] _cpuPowerHistory = new float[HistoryLength];
        private readonly float[] _gpuPowerHistory = new float[HistoryLength];
        private readonly float[] _ramHistory = new float[HistoryLength];
        private readonly float[] _vramHistory = new float[HistoryLength];
        private readonly float[] _cpuFanHistory = new float[HistoryLength];
        private readonly float[] _gpuFanHistory = new float[HistoryLength];
        private readonly float[] _gpuVoltageHistory = new float[HistoryLength];
        private readonly float[] _cpuVoltageHistory = new float[HistoryLength];
        private readonly float[] _ramSpeedHistory = new float[HistoryLength];
        private readonly float[] _vramSpeedHistory = new float[HistoryLength];
        private static readonly float[] EmptyHistory = new float[HistoryLength];

        private const int FpsShowDelayMs = 3000;
        private const int FpsPositiveShowDelayMs = 3000;
        private const int PollIntervalMs = 1000;
        private readonly System.Timers.Timer _timer = new(PollIntervalMs) { AutoReset = true };
        private int _tickRunning;
        private EtwFpsMonitor? _fps;
        private volatile int _currentFps = -1;
        private long _fpsZeroSinceTick;
        private long _fpsPositiveSinceTick;
        private bool _fpsWasVisible;
        private int _lastFgPid;
        private bool _active;
        public bool IsActive => _active;
        private bool _gameOnly;
        private bool _hidden;
        private bool _screenshotHidden;
        private int _shownPid;
        private int _classifiedPid;
        private string? _classifiedProcessName;
        private bool _fgDesktop;
        private bool _wasOwnWindow;
        private bool _keepOverlayAfterUiHide;
        private const int MinGameFps = 6;
        private readonly string[] _columnCpu = ["None", "None", "None", "None", "None", "None", "None", "None"];
        private readonly string[] _columnGpu = ["None", "None", "None", "None", "None", "None", "None", "None"];
        private readonly bool[] _columnGraphs = new bool[8];
        private bool _showFps = true;

        private static readonly HashSet<string> DesktopApps = new(StringComparer.OrdinalIgnoreCase)
        {
            "chrome", "msedge", "firefox", "opera", "brave", "vivaldi", "iexplore", "chromium", "librewolf",
            "WindowsTerminal", "conhost", "cmd", "powershell", "pwsh", "alacritty", "wezterm-gui", "mintty",
            "discord", "slack", "Teams", "ms-teams", "Spotify", "WhatsApp", "Signal", "Telegram", "Code", "Notion", "obsidian", "zoom", "Skype",
            "steam", "steamwebhelper", "EpicGamesLauncher", "Battle.net", "GalaxyClient", "EADesktop", "UbisoftConnect",
            "vlc", "mpv", "mpc-hc64", "mpc-be64", "PotPlayerMini64", "wmplayer", "smplayer",
            "WINWORD", "EXCEL", "POWERPNT", "OUTLOOK", "Acrobat", "AcroRd32", "SumatraPDF",
            "explorer", "ShellExperienceHost", "SearchHost", "StartMenuExperienceHost", "ApplicationFrameHost", "SystemSettings", "Taskmgr",
        };

        private static readonly HashSet<string> ScreenshotApps = new(StringComparer.OrdinalIgnoreCase)
        {
            "SnippingTool", "ScreenSketch", "SnipSketch", "SnipAndSketch", "Photos", "Microsoft.Photos",
            "PhotosApp", "mspaint", "ShareX", "Greenshot", "Lightshot", "PicPick", "Snagit32", "SnagitEditor",
            "SnagitCapture", "ScreenClippingHost"
        };

        private static readonly IntPtr HWND_TOPMOST = new(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;

        public HardwareOverlay()
        {
            Alpha = 255;
            _timer.Elapsed += (_, _) => Tick();
        }

        public void Prepare()
        {
            LoadDisplayConfiguration();
            _scalePercent = Math.Clamp(AppConfig.Get("overlay_scale_percent", 100), MinScalePercent, MaxScalePercent);
            Size = CalculateOverlaySize(_scalePercent);
            EnsureHandleCreated();
        }

        private const int WM_NCDESTROY = 0x0082;
        private const int WM_SETCURSOR  = 0x0020;
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCDESTROY)
            {
                base.WndProc(ref m);
                if (_active)
                {
                    RestorePosition();
                    base.Show();
                }
                return;
            }

            if (m.Msg == WM_SETCURSOR)
            {
                Cursor.Current = _dragging ? Cursors.SizeAll : Cursors.Hand;
                m.Result = (IntPtr)1;
                return;
            }

            if (m.Msg == NativeMethods.WM_MOUSEWHEEL && AreDragKeysDown())
            {
                int delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
                ApplyScale(_scalePercent + (delta > 0 ? ScaleStepPercent : -ScaleStepPercent));
                m.Result = IntPtr.Zero;
                return;
            }

            if (m.Msg == NativeMethods.WM_MBUTTONDOWN && AreDragKeysDown())
            {
                ApplyScale(100);
                m.Result = IntPtr.Zero;
                return;
            }

            if (m.Msg == NativeMethods.WM_LBUTTONDOWN && _dragModeActive)
            {
                _dragging = true;
                _dragCursorStart = Cursor.Position;
                _dragWindowStart = Location;
                NativeMethods.SetCapture(Handle);
                m.Result = IntPtr.Zero;
                return;
            }

            if (m.Msg == NativeMethods.WM_MOUSEMOVE && _dragging)
            {
                Point cursor = Cursor.Position;
                int newX = _dragWindowStart.X + cursor.X - _dragCursorStart.X;
                int newY = _dragWindowStart.Y + cursor.Y - _dragCursorStart.Y;
                Screen screen = Screen.FromPoint(cursor);
                const int margin = 5;
                newX = Math.Clamp(newX, screen.Bounds.Left + margin, screen.Bounds.Right  - Width  - margin);
                newY = Math.Clamp(newY, screen.Bounds.Top  + margin, screen.Bounds.Bottom - Height - margin);
                Location = new Point(newX, newY);
                m.Result = IntPtr.Zero;
                return;
            }

            if (m.Msg == NativeMethods.WM_LBUTTONUP && _dragging)
            {
                _dragging = false;
                NativeMethods.ReleaseCapture();

                Point upCursor = Cursor.Position;
                bool wasClick = Math.Abs(upCursor.X - _dragCursorStart.X) <= 5 &&
                                Math.Abs(upCursor.Y - _dragCursorStart.Y) <= 5;
                if (wasClick)
                {
                    Point center = new Point(Location.X + Width / 2, Location.Y + Height / 2);
                    Screen screen = Screen.FromPoint(center);
                    bool isRight = center.X > screen.Bounds.X + screen.Bounds.Width / 2;
                    int rightEdge = Location.X + Width;

                    Invalidate();

                    if (isRight)
                    {
                        Location = new Point(rightEdge - Width, Location.Y);
                        SavePosition();
                    }
                }
                else
                {
                    SavePosition();
                }

                if (!AreDragKeysDown())
                {
                    _dragModeActive = false;
                    SetTransparentStyle(true);
                }
                m.Result = IntPtr.Zero;
                return;
            }

            base.WndProc(ref m);
        }

        private void Tick()
        {
            if (System.Threading.Interlocked.Exchange(ref _tickRunning, 1) != 0)
                return;

            try
            {
                TickCore();
            }
            finally
            {
                System.Threading.Volatile.Write(ref _tickRunning, 0);
            }
        }

        private void TickCore()
        {
            // A timer callback may already be queued when StopOverlay stops the
            // timer. Do not allow that stale callback to perform another sensor,
            // NVML, or render update after the overlay has been disabled.
            if (!_active)
                return;

            bool mouseOver = new Rectangle(Location, Size).Contains(Cursor.Position);
            bool keysDown = mouseOver && AreDragKeysDown();
            if (keysDown != _dragModeActive && !_dragging)
            {
                _dragModeActive = keysDown;
                SetTransparentStyle(!keysDown);
                Cursor.Current = keysDown ? Cursors.Hand : Cursors.Default;
                if (_bgAlpha < DragMinAlpha) Invalidate();
            }

            if (Handle != nint.Zero && NativeMethods.GetWindow(Handle, NativeMethods.GW_HWNDPREV) != IntPtr.Zero)
                NativeMethods.SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

            NativeMethods.GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), out uint fgPidRaw);
            int fgPid = (int)fgPidRaw;
            bool ownWindow = fgPid == 0 || fgPid == Environment.ProcessId;
            string? foregroundProcessName = ownWindow ? null : GetForegroundProcessName(fgPid);
            bool desktopFg = foregroundProcessName != null && DesktopApps.Contains(foregroundProcessName);
            if (_wasOwnWindow && desktopFg)
                _keepOverlayAfterUiHide = true;
            else if (!ownWindow && !desktopFg)
                _keepOverlayAfterUiHide = false;

            bool screenshotActive = foregroundProcessName != null && ScreenshotApps.Contains(foregroundProcessName);
            if (UpdateScreenshotVisibility(screenshotActive))
            {
                _wasOwnWindow = ownWindow;
                return;
            }

            if (_fps != null)
            {
                if (ownWindow)
                {
                    if (_shownPid != 0)
                        _fps.TargetPid = _shownPid;
                }
                else if (_keepOverlayAfterUiHide && desktopFg)
                {
                    if (_shownPid != 0)
                        _fps.TargetPid = _shownPid;
                }
                else if (fgPid != _lastFgPid)
                {
                    _lastFgPid = fgPid;
                    if (fgPid != _shownPid)
                        ResetFpsDisplayState();
                    _fps.TargetPid = fgPid;
                    _fgDesktop = _gameOnly && desktopFg;
                }
                else
                {
                    UpdateCurrentFps((int)Math.Round(_fps.SampleFps()));
                }
            }

            if (_gameOnly)
            {
                // PreySense auto-hide focuses the desktop/shell. Keep the overlay as-is
                // instead of treating that as leaving a game.
                if (!ownWindow && !(_keepOverlayAfterUiHide && desktopFg))
                    UpdateGameVisibility(fgPid);
                _wasOwnWindow = ownWindow;
                if (_hidden) return;
            }
            else
            {
                _wasOwnWindow = ownWindow;
            }

            HardwareControl.ReadSensorsOverlay();

            double gpuTemp = D(HardwareControl.gpuTemp);
            double cpuTemp = D(HardwareControl.cpuTemp);
            bool gpuActive = HardwareControl.gpuInUse;

            _cpuUsage = HardwareControl.cpuUsage;
            _gpuUsage = gpuActive ? (HardwareControl.gpuUsage ?? 0) : 0;
            _ramUsedMb = HardwareControl.ramUsedMb;
            _vramUsedMb = HardwareControl.vramUsedMb;

            _cpuUsageHistory[_historyHead] = Graphs("Usage", false) ? (_cpuUsage ?? 0) : 0f;
            _gpuUsageHistory[_historyHead] = Graphs("Usage", true) && gpuActive ? (_gpuUsage ?? 0) : 0f;

            _cpuTempHistory[_historyHead] = Graphs("Temperature", false) ? (float)cpuTemp : 0f;
            _gpuTempHistory[_historyHead] = Graphs("Temperature", true) && gpuActive ? (float)gpuTemp : 0f;

            _cpuClockHistory[_historyHead] = Graphs("P-Core", false) ? (HardwareControl.cpuMhz ?? 0) : 0f;
            _eCoreHistory[_historyHead] = Graphs("E-Core", false) ? (HardwareControl.eCoreMhz ?? 0) : 0f;
            _gpuClockHistory[_historyHead] = Graphs("Clock", true) && gpuActive ? (HardwareControl.gpuMhz ?? 0) : 0f;

            _cpuPowerHistory[_historyHead] = Graphs("Power", false) ? (float)Math.Max(0, D(HardwareControl.cpuPower)) : 0f;
            _gpuPowerHistory[_historyHead] = Graphs("Power", true) && gpuActive ? (float)Math.Max(0, D(HardwareControl.gpuPower)) : 0f;

            _ramHistory[_historyHead] = Graphs("Memory", false) ? (float)D(_ramUsedMb) : 0f;
            _vramHistory[_historyHead] = Graphs("Memory", true) && gpuActive ? (float)D(_vramUsedMb) : 0f;

            _cpuFanHistory[_historyHead] = Graphs("Fan Speed", false) ? (HardwareControl.cpuFanRPM ?? 0) : 0f;
            _gpuFanHistory[_historyHead] = Graphs("Fan Speed", true) && gpuActive ? (HardwareControl.gpuFanRPM ?? 0) : 0f;

            _gpuVoltageHistory[_historyHead] = Graphs("Voltage", true) && gpuActive ? (HardwareControl.gpuVoltage ?? 0f) : 0f;
            _cpuVoltageHistory[_historyHead] = Graphs("Voltage", false) ? (HardwareControl.cpuVoltage ?? 0f) : 0f;

            _ramSpeedHistory[_historyHead] = Graphs("Memory Speed", false) ? (HardwareControl.ramSpeedMhz ?? 0) : 0f;
            _vramSpeedHistory[_historyHead] = Graphs("Memory Speed", true) && gpuActive ? (HardwareControl.vramSpeedMhz ?? 0) : 0f;

            _historyHead = (_historyHead + 1) % HistoryLength;

            bool anyGraph = false;
            for (int i = 0; i < 8; i++)
            {
                if (_columnGraphs[i] && (_columnCpu[i] != "None" || _columnGpu[i] != "None"))
                    anyGraph = true;
            }
            if (anyGraph)
            {
                _lastIdlePaintStamp = int.MinValue;
            }
            else
            {
                int stamp = HashCode.Combine(
                    HardwareControl.cpuTemp,
                    HardwareControl.gpuTemp,
                    HardwareControl.cpuUsage,
                    HardwareControl.gpuUsage,
                    HardwareControl.cpuMhz,
                    HardwareControl.gpuMhz,
                    HardwareControl.cpuPower,
                    HardwareControl.gpuPower);
                stamp = HashCode.Combine(
                    stamp,
                    HardwareControl.cpuFanRPM,
                    HardwareControl.gpuFanRPM,
                    HardwareControl.ramUsage,
                    HardwareControl.vramUsage,
                    HardwareControl.eCoreMhz,
                    HardwareControl.gpuVoltage,
                    _currentFps);
                stamp = HashCode.Combine(stamp, HardwareControl.cpuVoltage);
                if (stamp == _lastIdlePaintStamp)
                    return;
                _lastIdlePaintStamp = stamp;
            }

            Invalidate();
        }

        private void UpdateGameVisibility(int fgPid)
        {
            if (_currentFps >= MinGameFps && !_fgDesktop) _shownPid = fgPid;
            bool show = fgPid == _shownPid;
            if (show != _hidden) return;
            _hidden = !show;
            if (Handle != nint.Zero)
                NativeMethods.ShowWindow(Handle, (short)(_hidden ? NativeMethods.SW_HIDE : NativeMethods.SW_SHOWNOACTIVATE));
        }

        private bool UpdateScreenshotVisibility(bool screenshotActive)
        {
            if (Handle == nint.Zero)
                return screenshotActive;

            if (screenshotActive)
            {
                if (!_screenshotHidden)
                {
                    _screenshotHidden = true;
                    NativeMethods.ShowWindow(Handle, NativeMethods.SW_HIDE);
                }
                return true;
            }

            if (_screenshotHidden)
            {
                _screenshotHidden = false;
                if (!_hidden)
                    NativeMethods.ShowWindow(Handle, NativeMethods.SW_SHOWNOACTIVATE);
            }

            return false;
        }

        private string? GetForegroundProcessName(int pid)
        {
            if (pid == _classifiedPid)
                return _classifiedProcessName;

            _classifiedPid = pid;
            _classifiedProcessName = null;
            try
            {
                using var p = Process.GetProcessById(pid);
                _classifiedProcessName = p.ProcessName;
            }
            catch { }
            return _classifiedProcessName;
        }

        protected override void PerformPaint(PaintEventArgs e)
        {
            float sc = GetScale();

            int padX = S(sc, BasePadX);
            int padY = S(sc, BasePadY);
            int lineH = S(sc, BaseLineHeight);
            int lineGap = S(sc, BaseLineSpacing);
            int radius = S(sc, CornerRadius);
            int fpsColW = S(sc, BaseFpsColWidth);
            int innerPadX = S(sc, BaseInnerPadX);
            int innerPadY = S(sc, BaseInnerPadY);
            int innerH = lineH * 2 + lineGap;
            int totalH = padY * 2 + innerH;
            int labelColW = S(sc, 18);
            int colGap = 0;

            int cursor = padX + innerPadX;
            bool showFps = ShouldShowFps();
            if (showFps)
                cursor += fpsColW + colGap;

            int leftX = cursor;
            cursor += labelColW + colGap;

            // Compute dynamic columns positions to determine form size beforehand
            int columnsStartX = cursor;
            for (int i = 1; i <= 8; i++)
            {
                int colW = ColumnWidth(i - 1, sc);
                if (colW <= 0) continue;

                if (colW > 0)
                {
                    cursor += colW + colGap;
                    bool hasGraph = _columnGraphs[i - 1];
                    if (hasGraph)
                    {
                        cursor += S(sc, 6) + S(sc, 72) + colGap;
                    }
                }
            }

            int width = cursor + padX + innerPadX;

            if (Size.Width != width || Size.Height != totalH)
                _size = new Size(width, totalH);

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = _scalePercent <= 75 ? TextRenderingHint.ClearTypeGridFit : TextRenderingHint.AntiAliasGridFit;

            SolidBrush fillBrush = _dragModeActive && _bgAlpha < DragMinAlpha ? _dragBgBrush : _bgBrush;

            if (radius > 0)
            {
                g.FillRoundedRectangle(fillBrush, Bound, radius);
                using (var path = new GraphicsPath())
                {
                    int diameter = Math.Max(1, radius * 2);
                    Rectangle bounds = Bound;
                    path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
                    path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
                    path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
                    path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
                    path.CloseAllFigures();
                    g.SetClip(path);
                    g.FillRectangle(_topGlowBrush, Bound.X + 1, Bound.Y + 1, Bound.Width - 2, Math.Max(1, Bound.Height / 3));
                    g.FillRectangle(_bottomGlowBrush, Bound.X + 1, Bound.Bottom - Math.Max(2, Bound.Height / 4), Bound.Width - 2, Math.Max(2, Bound.Height / 4));
                    g.ResetClip();
                }
            }
            else
            {
                g.FillRectangle(fillBrush, Bound);
            }

            if (sc != _lastScale)
            {
                _lastScale = sc;
                _font?.Dispose();     _font     = new Font("Bahnschrift SemiBold", BaseFontSize * sc, FontStyle.Regular, GraphicsUnit.Pixel);
                _rpmFont?.Dispose();  _rpmFont  = new Font("Bahnschrift", BaseRpmFontSize * sc, FontStyle.Regular, GraphicsUnit.Pixel);
                _unitFont?.Dispose(); _unitFont = new Font("Bahnschrift SemiBold", (BaseFontSize - 1f) * sc, FontStyle.Regular, GraphicsUnit.Pixel);
                _fpsBold?.Dispose();  _fpsBold  = new Font("Bahnschrift", innerH / 1.15f, FontStyle.Bold, GraphicsUnit.Pixel);
                _totalPen?.Dispose(); _totalPen = new Pen(Color.FromArgb(255, 170, 170, 170), sc * 0.75f);
                _axPen?.Dispose();    _axPen    = new Pen(Color.FromArgb(255, 60, 60, 60), sc * 0.5f);
            }

            var font    = _font!;
            var rpmFont = _rpmFont!;
            var unitFont = _unitFont!;
            var fpsBold = _fpsBold!;
            var sf = StringFormat.GenericTypographic;

            float charW = g.MeasureString("XX", font).Width - g.MeasureString("X", font).Width;

            int topY = padY + innerPadY + 1;
            int textY = topY + (int)Math.Round(sc);
            int graphH = Math.Max(1, textY + lineH + lineGap + (int)Math.Round(8f * sc) - topY);

            // FPS
            if (showFps)
            {
                string fpsStr = Math.Max(0, _currentFps).ToString();
                float fpsX = padX + innerPadX - S(sc, 4);
                float fpsW = g.MeasureString(fpsStr, fpsBold).Width;
                g.DrawString(fpsStr, fpsBold, _gpuBrush, new PointF(fpsX + (fpsColW - fpsW) / 2f, topY));
            }

            // Labels
            g.DrawString("CPU", font, _cpuBrush, new PointF(leftX, textY), sf);
            g.DrawString("GPU", font, _gpuBrush, new PointF(leftX, textY + lineH + lineGap), sf);

            // Columns
            int colCursor = columnsStartX;

            for (int i = 1; i <= 8; i++)
            {
                int colW = ColumnWidth(i - 1, sc);
                if (colW <= 0) continue;

                string cpuMetric = _columnCpu[i - 1];
                string gpuMetric = _columnGpu[i - 1];
                DrawMetricColumn(g, font, _unitFont!, rpmFont, sc, cpuMetric, gpuMetric, colCursor, colW, textY, lineH, lineGap);
                colCursor += colW + colGap;

                if (_columnGraphs[i - 1])
                {
                    int graphGap = S(sc, 6);
                    int graphW = S(sc, 72);
                    OverlayChartRenderer.DrawStackedChart(g, colCursor + graphGap, topY, graphW, graphH, sc, HistoryFor(cpuMetric, false), HistoryFor(gpuMetric, true), _historyHead, HistoryLength, _basePts, _gpuPts, _cpuPts, _polyPts, _graphGpuBrush, _graphGpuPen, _graphCpuBrush, _graphCpuPen, _totalPen!, _axPen!, drawCpu: cpuMetric != "None", drawGpu: gpuMetric != "None");
                    colCursor += graphGap + graphW + colGap;
                }
            }
        }

        private void UpdateCurrentFps(int fps)
        {
            fps = Math.Max(0, fps);
            _currentFps = fps;

            if (fps > 0)
            {
                _fpsZeroSinceTick = 0;
                if (_fpsPositiveSinceTick == 0)
                    _fpsPositiveSinceTick = Environment.TickCount64;
                if (Environment.TickCount64 - _fpsPositiveSinceTick >= FpsPositiveShowDelayMs)
                    _fpsWasVisible = true;
            }
            else
            {
                _fpsPositiveSinceTick = 0;
                if (_fpsZeroSinceTick == 0)
                    _fpsZeroSinceTick = Environment.TickCount64;
            }
        }

        private bool ShouldShowFps()
        {
            if (!_showFps) return false;
            if (_currentFps > 0)
            {
                long positiveSince = _fpsPositiveSinceTick;
                return positiveSince != 0 && Environment.TickCount64 - positiveSince >= FpsPositiveShowDelayMs;
            }
            if (_currentFps < 0) return false;
            long zeroSince = _fpsZeroSinceTick;
            bool linger = zeroSince != 0 &&
                          Environment.TickCount64 - zeroSince < FpsShowDelayMs &&
                          _fpsWasVisible;
            if (!linger)
                _fpsWasVisible = false;
            return linger;
        }

        private void ResetFpsDisplayState()
        {
            _currentFps = -1;
            _fpsZeroSinceTick = 0;
            _fpsPositiveSinceTick = 0;
            _fpsWasVisible = false;
        }

        private static void DrawMemGb(Graphics g, Font font, Font unitFont, int x, int colW, int y, int? usedMb, SolidBrush brush)
        {
            double gb = usedMb.HasValue ? usedMb.Value / 1024.0 : 0.0;
            string s = gb.ToString("F1") + "GB";
            DrawTextWithUnit(g, font, unitFont, s, brush, new PointF(x + colW, y), alignRight: true);
        }

        private static float DrawTextWithUnit(Graphics g, Font mainFont, Font unitFont, string text, Brush brush, PointF pos, bool alignRight = false)
        {
            if (string.IsNullOrEmpty(text)) return 0f;

            string[] units = { "GHz", "MHz", "mV", "GB", "W", "%", "v", "Â°C", "Â°" };
            string matchedUnit = "";
            string trimmed = text.Trim();
            foreach (var u in units)
            {
                if (trimmed.EndsWith(u))
                {
                    matchedUnit = u;
                    break;
                }
            }

            var sf = StringFormat.GenericTypographic;
            if (matchedUnit.Length > 0)
            {
                string valuePart = trimmed.Substring(0, trimmed.Length - matchedUnit.Length);
                
                float valW = g.MeasureString(valuePart, mainFont, 1000, sf).Width;
                float unitW = g.MeasureString(matchedUnit, unitFont, 1000, sf).Width;
                
                float gap = 0.5f * mainFont.Size / 11f; // proportional to font size
                float totalW = valW + unitW + gap;

                float x = alignRight ? pos.X - totalW : pos.X;
                g.DrawString(valuePart, mainFont, brush, new PointF(x, pos.Y), sf);
                g.DrawString(matchedUnit, unitFont, brush, new PointF(x + valW + gap, pos.Y), sf);
                
                return totalW;
            }
            else
            {
                float totalW = g.MeasureString(text, mainFont, 1000, sf).Width;
                if (alignRight)
                {
                    g.DrawString(text, mainFont, brush, new PointF(pos.X - totalW, pos.Y), sf);
                }
                else
                {
                    g.DrawString(text, mainFont, brush, pos, sf);
                }
                return totalW;
            }
        }
        private void DrawMetricColumn(Graphics g, Font font, Font unitFont, Font rpmFont, float sc, string cpuMetric, string gpuMetric, float colX, float colW, int textY, int lineH, int lineGap)
        {
            DrawMetricValue(g, font, unitFont, rpmFont, sc, cpuMetric, false, colX, colW, textY);
            DrawMetricValue(g, font, unitFont, rpmFont, sc, gpuMetric, true, colX, colW, textY + lineH + lineGap);
        }

        private void DrawMetricValue(Graphics g, Font font, Font unitFont, Font rpmFont, float sc, string metric, bool gpu, float colX, float colW, int y)
        {
            if (metric == "None")
                return;

            SolidBrush brush = gpu ? _gpuBrush : _cpuBrush;
            switch (metric)
            {
                case "Usage":
                    DrawTextWithUnit(g, font, unitFont, ((gpu ? _gpuUsage : _cpuUsage) ?? 0) + "%", brush, new PointF(colX + colW, y), alignRight: true);
                    break;
                case "Temperature":
                    DrawTextWithUnit(g, font, unitFont, FmtTemp(gpu ? D(HardwareControl.gpuTemp) : D(HardwareControl.cpuTemp)), brush, new PointF(colX + colW, y), alignRight: true);
                    break;
                case "P-Core":
                    DrawTextWithUnit(g, font, unitFont, $"{(HardwareControl.cpuMhz ?? 0)}MHz", brush, new PointF(colX + colW, y), alignRight: true);
                    break;
                case "E-Core":
                    DrawTextWithUnit(g, font, unitFont, $"{(HardwareControl.eCoreMhz ?? 0)}MHz", brush, new PointF(colX + colW, y), alignRight: true);
                    break;
                case "Clock":
                    DrawTextWithUnit(g, font, unitFont, $"{(HardwareControl.gpuMhz ?? 0)}MHz", brush, new PointF(colX + colW, y), alignRight: true);
                    break;
                case "Voltage":
                    float? voltage = gpu ? HardwareControl.gpuVoltage : HardwareControl.cpuVoltage;
                    string volts = $"{Math.Max(0f, voltage ?? 0f):F3}v";
                    DrawTextWithUnit(g, font, unitFont, volts, brush, new PointF(colX + colW, y), alignRight: true);
                    break;
                case "Power":
                    DrawTextWithUnit(g, font, unitFont, FmtPow(gpu ? D(HardwareControl.gpuPower) : D(HardwareControl.cpuPower)), brush, new PointF(colX + colW, y), alignRight: true);
                    break;
                case "Memory":
                    DrawMemGb(g, font, unitFont, (int)colX, (int)colW, y, gpu ? _vramUsedMb : _ramUsedMb, brush);
                    break;
                case "Memory Speed":
                    int? speed = gpu ? HardwareControl.vramSpeedMhz : HardwareControl.ramSpeedMhz;
                    DrawTextWithUnit(g, font, unitFont, $"{(speed ?? 0)}MHz", brush, new PointF(colX + colW, y), alignRight: true);
                    break;
                case "Fan Speed":
                    int? fan = gpu ? HardwareControl.gpuFanRPM : HardwareControl.cpuFanRPM;
                    DrawFanSpeed(g, font, rpmFont, FormatFan(fan), brush, colX, colW, y, sc);
                    break;
            }
        }

        private static void DrawFanSpeed(Graphics g, Font font, Font rpmFont, string fanNum, SolidBrush brush, float colX, float colW, int y, float sc)
        {
            var sf = StringFormat.GenericTypographic;
            float rpmW = g.MeasureString("RPM", rpmFont, 1000, sf).Width;
            float rpmX = colX + colW - rpmW;
            g.DrawString("RPM", rpmFont, brush, new PointF(rpmX, y), sf);
            
            float numW = g.MeasureString(fanNum, font, 1000, sf).Width;
            float numX = rpmX - numW - 2f * sc;
            g.DrawString(fanNum, font, brush, new PointF(numX, y), sf);
        }

        private int ColumnWidth(int index, float sc)
        {
            int width = Math.Max(MetricWidth(_columnCpu[index]), MetricWidth(_columnGpu[index]));
            return width == 0 ? 0 : S(sc, width);
        }

        private static int MetricWidth(string metric) => metric switch
        {
            "Usage" => 22,
            "Temperature" => 32,
            "P-Core" or "E-Core" or "Clock" or "Voltage" or "Memory Speed" => 44,
            "Power" => 38,
            "Memory" => 32,
            "Fan Speed" => 50,
            _ => 0
        };

        private bool Graphs(string metric, bool gpu)
        {
            for (int i = 0; i < 8; i++)
            {
                if (!_columnGraphs[i])
                    continue;
                if ((gpu ? _columnGpu[i] : _columnCpu[i]) == metric)
                    return true;
            }
            return false;
        }

        private float[] HistoryFor(string metric, bool gpu) => metric switch
        {
            "Usage" => gpu ? _gpuUsageHistory : _cpuUsageHistory,
            "Temperature" => gpu ? _gpuTempHistory : _cpuTempHistory,
            "P-Core" => _cpuClockHistory,
            "E-Core" => _eCoreHistory,
            "Clock" => _gpuClockHistory,
            "Voltage" => gpu ? _gpuVoltageHistory : _cpuVoltageHistory,
            "Power" => gpu ? _gpuPowerHistory : _cpuPowerHistory,
            "Memory" => gpu ? _vramHistory : _ramHistory,
            "Memory Speed" => gpu ? _vramSpeedHistory : _ramSpeedHistory,
            "Fan Speed" => gpu ? _gpuFanHistory : _cpuFanHistory,
            _ => EmptyHistory
        };

        private void PositionAtTopLeft()
        {
            Screen screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
            Location = new Point(screen.Bounds.X + MarginFromEdge, screen.Bounds.Y + MarginFromEdge);
        }

        public void SetConfiguredPosition(string horizontal, string vertical)
        {
            if (string.IsNullOrWhiteSpace(horizontal)) horizontal = "Left";
            if (string.IsNullOrWhiteSpace(vertical)) vertical = "Top";

            AppConfig.Set("overlay_pos_horizontal", horizontal);
            AppConfig.Set("overlay_pos_vertical", vertical);

            Screen screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
            const int edgeMargin = 12;

            int x = horizontal.Trim().ToLowerInvariant() switch
            {
                "center" => screen.Bounds.Left + (screen.Bounds.Width - Width) / 2,
                "right"  => screen.Bounds.Right - Width - edgeMargin,
                _        => screen.Bounds.Left + edgeMargin
            };

            int y = vertical.Trim().ToLowerInvariant() switch
            {
                "center" => screen.Bounds.Top + (screen.Bounds.Height - Height) / 2,
                "bottom" => screen.Bounds.Bottom - Height - edgeMargin,
                _        => screen.Bounds.Top + edgeMargin
            };

            const int margin = 5;
            x = Math.Clamp(x, screen.Bounds.Left + margin, screen.Bounds.Right - Width - margin);
            y = Math.Clamp(y, screen.Bounds.Top + margin, screen.Bounds.Bottom - Height - margin);

            Location = new Point(x, y);
            SavePosition();
        }

        private bool AreDragKeysDown() =>
            (NativeMethods.GetAsyncKeyState(NativeMethods.VK_CONTROL) & 0x8000) != 0 &&
            (NativeMethods.GetAsyncKeyState(NativeMethods.VK_SHIFT)   & 0x8000) != 0 &&
            (NativeMethods.GetAsyncKeyState(NativeMethods.VK_MENU)    & 0x8000) != 0;

        private void SetTransparentStyle(bool transparent)
        {
            if (Handle == nint.Zero) return;
            int style = NativeMethods.GetWindowLong(Handle, NativeMethods.GWL_EXSTYLE);
            style = transparent ? (style | NativeMethods.WS_EX_TRANSPARENT_FLAG) : (style & ~NativeMethods.WS_EX_TRANSPARENT_FLAG);
            NativeMethods.SetWindowLong(Handle, NativeMethods.GWL_EXSTYLE, style);
        }

        private void ApplyScale(int next)
        {
            next = Math.Clamp(next, MinScalePercent, MaxScalePercent);
            next = (int)Math.Round(next / 25.0) * 25;
            next = Math.Clamp(next, MinScalePercent, MaxScalePercent);
            if (next == _scalePercent && Size == CalculateOverlaySize(next)) return;

            Point center = new Point(Location.X + Width / 2, Location.Y + Height / 2);
            Screen screen = Screen.FromPoint(center);
            bool isRight  = center.X > screen.Bounds.X + screen.Bounds.Width  / 2;
            bool isBottom = center.Y > screen.Bounds.Y + screen.Bounds.Height / 2;
            int rightEdge  = Location.X + Width;
            int bottomEdge = Location.Y + Height;

            _scalePercent = next;
            AppConfig.Set("overlay_scale_percent", _scalePercent);

            Size newSize = CalculateOverlaySize(_scalePercent);
            int newX = isRight  ? rightEdge  - newSize.Width  : Location.X;
            int newY = isBottom ? bottomEdge - newSize.Height : Location.Y;

            const int margin = 5;
            newX = Math.Clamp(newX, screen.Bounds.Left + margin, screen.Bounds.Right  - newSize.Width  - margin);
            newY = Math.Clamp(newY, screen.Bounds.Top  + margin, screen.Bounds.Bottom - newSize.Height - margin);

            SetBounds(newX, newY, newSize.Width, newSize.Height);
            SavePosition();
        }

        private void SavePosition()
        {
            Point center = new Point(Location.X + Width / 2, Location.Y + Height / 2);
            Screen screen = Screen.FromPoint(center);
            bool isRight  = center.X > screen.Bounds.X + screen.Bounds.Width  / 2;
            bool isBottom = center.Y > screen.Bounds.Y + screen.Bounds.Height / 2;
            int anchor  = (isBottom ? 2 : 0) | (isRight ? 1 : 0);
            int offsetX = isRight  ? screen.Bounds.Right  - Location.X - Width  : Location.X - screen.Bounds.X;
            int offsetY = isBottom ? screen.Bounds.Bottom - Location.Y - Height : Location.Y - screen.Bounds.Y;
            AppConfig.Set("overlay_anchor",   anchor);
            AppConfig.Set("overlay_offset_x", offsetX);
            AppConfig.Set("overlay_offset_y", offsetY);
        }

        private void ApplyColors()
        {
            Color gpu = Color.White;
            Color cpu = Color.White;
            int rawAlpha = AppConfig.Get("overlay_alpha", 50);
            if (rawAlpha <= 100)
            {
                _bgAlpha = (int)Math.Round(rawAlpha * 2.55);
            }
            else
            {
                _bgAlpha = Math.Clamp(rawAlpha, 0, 255);
            }

            _gpuBrush?.Dispose();     _gpuBrush = new SolidBrush(gpu);
            _cpuBrush?.Dispose();     _cpuBrush = new SolidBrush(cpu);
            _bgBrush?.Dispose();      _bgBrush = new SolidBrush(Color.FromArgb(_bgAlpha, 0, 0, 0));
        }

        /// <summary>Called by MetricsWindow when the user changes settings while the overlay is running.</summary>
        public void RefreshDisplayFlags()
        {
            LoadDisplayConfiguration();
            _gameOnly = AppConfig.IsOverlayGameOnly();
            int configuredScale = Math.Clamp(AppConfig.Get("overlay_scale_percent", 100), MinScalePercent, MaxScalePercent);
            if (configuredScale != _scalePercent)
            {
                ApplyScale(configuredScale);
            }
            else
            {
                Size newSize = CalculateOverlaySize(_scalePercent);
                if (Size != newSize)
                {
                    Point center = new Point(Location.X + Width / 2, Location.Y + Height / 2);
                    Screen screen = Screen.FromPoint(center);
                    bool isRight  = center.X > screen.Bounds.X + screen.Bounds.Width  / 2;
                    bool isBottom = center.Y > screen.Bounds.Y + screen.Bounds.Height / 2;
                    int rightEdge  = Location.X + Width;
                    int bottomEdge = Location.Y + Height;

                    int newX = isRight  ? rightEdge  - newSize.Width  : Location.X;
                    int newY = isBottom ? bottomEdge - newSize.Height : Location.Y;

                    const int margin = 5;
                    newX = Math.Clamp(newX, screen.Bounds.Left + margin, screen.Bounds.Right  - newSize.Width  - margin);
                    newY = Math.Clamp(newY, screen.Bounds.Top  + margin, screen.Bounds.Bottom - newSize.Height - margin);

                    SetBounds(newX, newY, newSize.Width, newSize.Height);
                    SavePosition();
                }
            }

            ApplyColors();
            EnsureFpsMonitor();

            if (_active)
            {
                if (!_gameOnly)
                {
                    _keepOverlayAfterUiHide = false;
                    if (_hidden)
                    {
                        _hidden = false;
                        if (Handle != nint.Zero)
                            NativeMethods.ShowWindow(Handle, NativeMethods.SW_SHOWNOACTIVATE);
                    }
                }
                else
                {
                    NativeMethods.GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), out uint fgPidRaw);
                    int fgPid = (int)fgPidRaw;
                    bool ownWindow = fgPid == 0 || fgPid == Environment.ProcessId;
                    if (!ownWindow)
                        UpdateGameVisibility(fgPid);
                    else if (_shownPid == 0)
                    {
                        _hidden = true;
                        if (Handle != nint.Zero)
                            NativeMethods.ShowWindow(Handle, NativeMethods.SW_HIDE);
                    }
                }
                Invalidate();
            }
        }


    }
}









