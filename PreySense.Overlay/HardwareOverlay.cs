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
        private const int ScaleStepPercent = 5;
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
        private Pen? _axPen;

        private readonly PointF[] _basePts = new PointF[HistoryLength];
        private readonly PointF[] _cpuPts  = new PointF[HistoryLength];
        private readonly PointF[] _gpuPts  = new PointF[HistoryLength];

        private const int HistoryLength = 60;
        private int _historyHead = 0;
        private readonly float[] _cpuUsageHistory = new float[HistoryLength];
        private readonly float[] _gpuUsageHistory = new float[HistoryLength];
        private readonly float[] _cpuTempHistory = new float[HistoryLength];
        private readonly float[] _gpuTempHistory = new float[HistoryLength];
        private readonly float[] _gpuActiveClockHistory = new float[HistoryLength];
        private readonly float[] _cpuActiveClockHistory = new float[HistoryLength];
        private readonly float[] _cpuEffectiveClockHistory = new float[HistoryLength];
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
        private readonly float[] _gpuHotspotHistory = new float[HistoryLength];
        private readonly float[] _gpuMemoryTempHistory = new float[HistoryLength];
        private static readonly float[] EmptyHistory = new float[HistoryLength];

        private const int FpsShowDelayMs = 3000;
        private const int FpsPositiveShowDelayMs = 3000;
        private int _tickRunning;
        private EtwFpsMonitor? _fps;
        private volatile int _currentFps = -1;
        private long _fpsZeroSinceTick;
        private long _fpsPositiveSinceTick;
        private bool _fpsWasVisible;
        private int _lastFgPid;
        private volatile bool _active;
        public bool IsActive => _active;
        public bool IsShowing => _active && !_hidden && !_screenshotHidden;
        private volatile bool _hidden;
        private volatile bool _screenshotHidden;
        private int _shownPid;
        private int _classifiedPid;
        private string? _classifiedProcessName;
        private bool _fgDesktop;
        private bool _wasOwnWindow;
        private bool _keepOverlayAfterUiHide;
        private const int MinGameFps = 6;
        private string[] _columnCpu = [];
        private readonly List<(string Metric, bool Gpu)> _graphMetrics = new();
        private string[] _columnGpu = [];
        private bool[] _columnGraphs = [];
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
        }

        private const int WM_NCDESTROY = 0x0082;
        private const int WM_SETCURSOR  = 0x0020;
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == ForegroundChangedMessage)
            {
                Interlocked.Exchange(ref _foregroundUpdateQueued, 0);
                if (_active && _foregroundEvents != null)
                    lock (_foregroundGate) UpdateForegroundState(_foregroundEvents.CurrentWindow);
                return;
            }
            if (m.Msg == WM_NCDESTROY)
            {
                Interlocked.Exchange(ref _foregroundUpdateQueued, 0);
                base.WndProc(ref m);
                if (_active)
                {
                    RestorePosition();
                    base.Show();
                    QueueForegroundUpdate();
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
            // A shared heartbeat may already be queued when the overlay stops.
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

            lock (_foregroundGate)
            {
                if (_foregroundEvents?.IsAvailable != true)
                    UpdateForegroundState(NativeMethods.GetForegroundWindow());
                if (_screenshotHidden || _hidden) return;
                if (_fps != null && !_ownForeground && !(_keepOverlayAfterUiHide && _desktopForeground))
                    UpdateCurrentFps(_fps.SampleFps());
            }
            if (IsShowing && PreySense.Core.Services.TelemetryCoordinator.RequestedMetrics == PreySense.Core.Services.TelemetryMetrics.None)
                Invalidate(); // FPS-only overlays still repaint without collecting hardware metrics.
        }

        private void OnTelemetrySnapshot(PreySense.Core.Services.TelemetrySnapshot snapshot)
        {
            if (IsDisposed || !_active || !IsShowing || snapshot.IsFanOnly) return;

            HardwareControl.ApplySnapshot(snapshot);

            if (_graphMetrics.Count > 0)
            {
                foreach (var (metric, gpu) in _graphMetrics)
                    HistoryFor(metric, gpu)[_historyHead] = TelemetryGraphValue.Read(snapshot, metric, gpu);
                _historyHead = (_historyHead + 1) % HistoryLength;
            }

            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Microsoft.Win32.SystemEvents.PowerModeChanged -= OnOverlayPowerModeChanged;
                StopForegroundEvents();
                PreySense.Core.Services.TelemetryCoordinator.Heartbeat -= Tick;
                PreySense.Core.Services.TelemetryCoordinator.SetOverlayActive(false);
                PreySense.Core.Services.TelemetryCoordinator.SnapshotAvailable -= OnTelemetrySnapshot;
                PreySense.Core.Services.TelemetryCoordinator.SetDemand(PreySense.Core.Services.TelemetryDemand.Overlay, false);
            }
            base.Dispose(disposing);
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
                    SyncOverlayTelemetryDemand();
                }
                return true;
            }

            if (_screenshotHidden)
            {
                _screenshotHidden = false;
                if (!_hidden)
                    NativeMethods.ShowWindow(Handle, NativeMethods.SW_SHOWNOACTIVATE);
                SyncOverlayTelemetryDemand();
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

            bool showFps = ShouldShowFps();
            int cursor = padX + innerPadX;
            if (showFps)
                cursor += fpsColW + colGap;

            int leftX = cursor;
            cursor += labelColW + colGap;

            // Compute dynamic columns positions to determine form size beforehand
            int columnsStartX = cursor;
            for (int i = 1; i <= _columnCpu.Length; i++)
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

            int width = cursor + RightInset(sc);

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
                string fpsStr = _currentFps >= 0 ? _currentFps.ToString() : "N/A";
                float fpsX = padX + innerPadX - S(sc, 4);
                float fpsW = g.MeasureString(fpsStr, fpsBold).Width;
                g.DrawString(fpsStr, fpsBold, _gpuBrush, new PointF(fpsX + (fpsColW - fpsW) / 2f, topY));
            }

            // Headers are always visible.
            g.DrawString("CPU", font, _cpuBrush, new PointF(leftX, textY), sf);
            g.DrawString("GPU", font, _gpuBrush, new PointF(leftX, textY + lineH + lineGap), sf);

            // Columns
            int colCursor = columnsStartX;

            for (int i = 1; i <= _columnCpu.Length; i++)
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
                    OverlayChartRenderer.DrawHistoryChart(g, colCursor + graphGap, topY, graphW, graphH, HistoryFor(cpuMetric, false), HistoryFor(gpuMetric, true), _historyHead, HistoryLength, _basePts, _gpuPts, _cpuPts, _graphGpuBrush, _graphGpuPen, _graphCpuBrush, _graphCpuPen, _axPen!, drawCpu: cpuMetric != "None", drawGpu: gpuMetric != "None");
                    colCursor += graphGap + graphW + colGap;
                }
            }
        }

        private void UpdateCurrentFps(double sample)
        {
            int fps = double.IsFinite(sample) && sample >= 0 ? (int)Math.Round(sample) : -1;
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
            double? gb = usedMb.HasValue ? usedMb.Value / 1024.0 : null;
            string s = TelemetryText.Format(gb, "GB", 1);
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
                    DrawTextWithUnit(g, font, unitFont, TelemetryText.Format(gpu ? HardwareControl.gpuUsage : HardwareControl.cpuUsage, "%"), brush, new PointF(colX + colW, y), alignRight: true);
                    break;
                case "Temperature":
                    DrawTextWithUnit(g, font, unitFont, FmtTemp(gpu ? HardwareControl.gpuTemp : HardwareControl.cpuTemp), brush, new PointF(colX + colW, y), alignRight: true);
                    break;
                case "Hotspot":
                    DrawTextWithUnit(g, font, unitFont, FmtTemp(HardwareControl.gpuHotspotTemp), brush, new PointF(colX + colW, y), alignRight: true);
                    break;
                case "Memory Temperature":
                    DrawTextWithUnit(g, font, unitFont, FmtTemp(HardwareControl.gpuMemoryTemp), brush, new PointF(colX + colW, y), alignRight: true);
                    break;
                case "Clock":
                case "Active Clock":
                    DrawTextWithUnit(g, font, unitFont, TelemetryText.Format(gpu ? HardwareControl.gpuActiveMhz : HardwareControl.cpuActiveMhz, "MHz"), brush, new PointF(colX + colW, y), alignRight: true);
                    break;
                case "Effective Clock":
                    DrawTextWithUnit(g, font, unitFont, TelemetryText.Format(gpu ? null : HardwareControl.cpuEffectiveMhz, "MHz"), brush, new PointF(colX + colW, y), alignRight: true);
                    break;
                case "Power Limit":
                    DrawTextWithUnit(g, font, unitFont, FmtPow(HardwareControl.gpuPowerLimit), brush, new PointF(colX + colW, y), alignRight: true);
                    break;
                case "PerfCap Reason":
                    DrawTextWithUnit(g, font, unitFont, GpuThrottleReasonText.Format(HardwareControl.gpuThrottleReasons), brush, new PointF(colX + colW, y), alignRight: true);
                    break;
                case "Voltage":
                    float? voltage = gpu ? HardwareControl.gpuVoltage : HardwareControl.cpuVoltage;
                    string volts = TelemetryText.Format(voltage, "v", 3);
                    DrawTextWithUnit(g, font, unitFont, volts, brush, new PointF(colX + colW, y), alignRight: true);
                    break;
                case "Power":
                    DrawTextWithUnit(g, font, unitFont, FmtPow(gpu ? HardwareControl.gpuPower : HardwareControl.cpuPower), brush, new PointF(colX + colW, y), alignRight: true);
                    break;
                case "Memory":
                    DrawMemGb(g, font, unitFont, (int)colX, (int)colW, y, gpu ? HardwareControl.vramUsedMb : HardwareControl.ramUsedMb, brush);
                    break;
                case "Memory Speed":
                    int? speed = gpu ? HardwareControl.vramSpeedMhz : HardwareControl.ramSpeedMhz;
                    DrawTextWithUnit(g, font, unitFont, TelemetryText.Format(speed, "MHz"), brush, new PointF(colX + colW, y), alignRight: true);
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
            if (fanNum == "N/A")
            {
                DrawTextWithUnit(g, font, rpmFont, fanNum, brush, new PointF(colX + colW, y), alignRight: true);
                return;
            }
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
            "Temperature" or "Hotspot" or "Memory Temperature" => 32,
            "Clock" or "Active Clock" or "Effective Clock" or "Voltage" or "Memory Speed" => 44,
            "Power" or "Power Limit" => 38,
            "PerfCap Reason" => 32,
            "Memory" => 32,
            "Fan Speed" => 50,
            _ => 0
        };

        private float[] HistoryFor(string metric, bool gpu) => metric switch
        {
            "Usage" => gpu ? _gpuUsageHistory : _cpuUsageHistory,
            "Temperature" => gpu ? _gpuTempHistory : _cpuTempHistory,
            "Hotspot" => _gpuHotspotHistory,
            "Memory Temperature" => _gpuMemoryTempHistory,
            "Clock" or "Active Clock" => gpu ? _gpuActiveClockHistory : _cpuActiveClockHistory,
            "Effective Clock" => gpu ? EmptyHistory : _cpuEffectiveClockHistory,
            "Voltage" => gpu ? _gpuVoltageHistory : _cpuVoltageHistory,
            "Power" => gpu ? _gpuPowerHistory : _cpuPowerHistory,
            "Memory" => gpu ? _vramHistory : _ramHistory,
            "Memory Speed" => gpu ? _vramSpeedHistory : _ramSpeedHistory,
            "Fan Speed" => gpu ? _gpuFanHistory : _cpuFanHistory,
            _ => EmptyHistory
        };

        private void ClearGraphHistory()
        {
            _historyHead = 0;
            _graphMetrics.Clear();
            foreach (float[] history in new[]
            {
                _cpuUsageHistory, _gpuUsageHistory, _cpuTempHistory, _gpuTempHistory,
                _gpuActiveClockHistory, _cpuActiveClockHistory, _cpuEffectiveClockHistory,
                _cpuPowerHistory, _gpuPowerHistory, _ramHistory, _vramHistory,
                _cpuFanHistory, _gpuFanHistory, _gpuVoltageHistory, _cpuVoltageHistory,
                _ramSpeedHistory, _vramSpeedHistory, _gpuHotspotHistory, _gpuMemoryTempHistory
            })
                Array.Fill(history, float.NaN);
        }

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
            next = (int)Math.Round(next / 5.0) * 5;
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


            int rawAlpha = AppConfig.Get("overlay_alpha", 50);
            if (rawAlpha <= 100)
            {
                _bgAlpha = (int)Math.Round(rawAlpha * 2.55);
            }
            else
            {
                _bgAlpha = Math.Clamp(rawAlpha, 0, 255);
            }

            int rawTextAlpha = AppConfig.Get("overlay_text_alpha", 100);
            int textAlpha = rawTextAlpha <= 100
                ? (int)Math.Round(rawTextAlpha * 2.55)
                : Math.Clamp(rawTextAlpha, 0, 255);

            Color gpu = Color.FromArgb(textAlpha, 255, 255, 255);
            Color cpu = Color.FromArgb(textAlpha, 255, 255, 255);

            _gpuBrush?.Dispose();     _gpuBrush = new SolidBrush(gpu);
            _cpuBrush?.Dispose();     _cpuBrush = new SolidBrush(cpu);
            _bgBrush?.Dispose();      _bgBrush = new SolidBrush(Color.FromArgb(_bgAlpha, 0, 0, 0));
        }

        /// <summary>Called by MetricsWindow when the user changes settings while the overlay is running.</summary>
        public void RefreshDisplayFlags()
        {
            LoadDisplayConfiguration();
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
                _keepOverlayAfterUiHide = false;
                if (_hidden)
                {
                    _hidden = false;
                    if (Handle != nint.Zero)
                        NativeMethods.ShowWindow(Handle, NativeMethods.SW_SHOWNOACTIVATE);
                }
                SyncOverlayTelemetryDemand();
                Invalidate();
            }
        }
    }
}









