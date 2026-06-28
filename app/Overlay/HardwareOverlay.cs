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
    public class HardwareOverlay : OSDNativeForm
    {
        private const int MinScalePercent  = 35;
        private const int MaxScalePercent  = 300;
        private const int ScaleStepPercent = 10;
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
        private const int BaseLeftColWidth = 118;
        private const int BaseChartColWidth = 78;
        private const int BasePowerGap = 0;
        private const int BasePowerColWidth = 38;
        private const int BaseColGap = 3;
        private const int BaseChartGap = 0;
        private const int CornerRadius = 0;
        private const int MarginFromEdge = 10;
        private const int BaseLightLeftColWidth = 72;
        private const int BaseUsageBarWidth = 4;
        private const int BaseUsageNumGap = 1;
        private const int BaseUsageNumColWidth = 24;
        private const int BaseUsageBarYNudge = 1;
        private const int BaseMemBarGap = -12;
        private const int BaseMemNumColWidth = 38;
        private const int BaseInnerPadX = 3;
        private const int BaseInnerPadY = 2;
        private const int BaseSectionGap = 1;
        private static readonly Color DefaultGpuColor = Color.White;
        private static readonly Color DefaultCpuColor = Color.White;
        private static readonly Color OverlayBorderColor = Color.FromArgb(68, 140, 140, 140);
        private static readonly Color OverlayTopGlowColor = Color.FromArgb(8, 255, 255, 255);
        private static readonly Color OverlayBottomGlowColor = Color.FromArgb(20, 0, 0, 0);

        private const int DragMinAlpha = 110;
        private static readonly SolidBrush _dragBgBrush = new(Color.FromArgb(DragMinAlpha, 0, 0, 0));
        private int _bgAlpha = 185;

        private SolidBrush _bgBrush = new(Color.FromArgb(185, 0, 0, 0));
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
        private readonly float[] _cpuHistory = new float[HistoryLength];
        private readonly float[] _gpuHistory = new float[HistoryLength];
        private int _historyHead = 0;

        private const int OverlayPollIntervalMs = 500;
        private const int FpsZeroLingerMs = 3000;
        private const int FpsPositiveShowDelayMs = 1000;
        private readonly System.Timers.Timer _timer = new(OverlayPollIntervalMs) { AutoReset = true };
        private EtwFpsMonitor? _fps;
        private Task? _fpsTask;
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
        private bool _fgDesktop;
        private const int MinGameFps = 6;

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

        private const int WM_NCDESTROY = 0x0082;
        private const int WM_SETCURSOR  = 0x0020;
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCDESTROY)
            {
                base.WndProc(ref m);
                if (_active)
                    Program.settingsForm?.BeginInvoke(() => { RestorePosition(); base.Show(); });
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

        private float GetScale() => BaseScale * (_scalePercent / 100f);

        private int GetBaseWidth()
        {
            int width = BasePadX;
            width += BaseLeftColWidth;
            width += BaseChartGap + BaseChartColWidth;
            width += BasePowerGap + BasePowerColWidth;
            width += BaseMemBarGap + BaseUsageBarWidth + BaseUsageNumGap + BaseMemNumColWidth;

            width += BasePadX;
            return width;
        }

        private static int S(float sc, int v) => (int)(v * sc);
        private static double D(object? v) { try { return v is null ? 0.0 : Convert.ToDouble(v); } catch { return 0.0; } }

        private static string FmtTemp(double t, bool zeroWhenUnavailable = false) =>
            ((int)Math.Max(0, Math.Round(t)) + "°C").PadLeft(5);

        private static string FmtPow(double p) =>
            Math.Max(0, Math.Round(p, 1)).ToString("F1") + "W";
        private static string FormatFan(int? fan)
        {
            if (fan is null || fan < 0) return "0";
            return fan.Value.ToString();
        }

        private void Tick()
        {
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

            bool screenshotActive = !ownWindow && IsScreenshotApp(fgPid);
            if (UpdateScreenshotVisibility(screenshotActive))
                return;

            if (_fps != null)
            {
                if (!ownWindow && fgPid != _lastFgPid)
                {
                    _lastFgPid = fgPid;
                    ResetFpsDisplayState();
                    _fps.TargetPid = fgPid;
                    _fgDesktop = _gameOnly && IsDesktopApp(fgPid);
                }
                else
                {
                    UpdateCurrentFps((int)Math.Round(_fps.SampleFps()));
                }
            }

            if (_gameOnly)
            {
                if (!ownWindow) UpdateGameVisibility(fgPid);
                if (_hidden) return;
            }

            HardwareControl.ReadSensorsOverlay();

            double gpuTemp = D(HardwareControl.gpuTemp);
            double cpuTemp = D(HardwareControl.cpuTemp);
            bool gpuActive = gpuTemp > 0;

            _cpuHistory[_historyHead] = (float)Math.Max(0, D(HardwareControl.cpuPower));
            _gpuHistory[_historyHead] = gpuActive ? (float)Math.Max(0, D(HardwareControl.gpuPower)) : 0f;
            _historyHead = (_historyHead + 1) % HistoryLength;

            _cpuUsage = HardwareControl.cpuUsage;
            _gpuUsage = gpuActive ? (HardwareControl.gpuUsage ?? 0) : 0;
            _ramUsedMb = HardwareControl.ramUsedMb;
            _vramUsedMb = HardwareControl.vramUsedMb;

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

        private static bool IsDesktopApp(int pid)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                return DesktopApps.Contains(p.ProcessName);
            }
            catch { return false; }
        }

        private static bool IsScreenshotApp(int pid)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                return ScreenshotApps.Contains(p.ProcessName);
            }
            catch { return false; }
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
            int chartColW = S(sc, BaseChartColWidth);
            int powColW = S(sc, BasePowerColWidth);
            int colGap = S(sc, BaseColGap);
            int chartGap = S(sc, BaseChartGap);
            int powGap = S(sc, BasePowerGap);
            int innerPadX = S(sc, BaseInnerPadX);
            int innerPadY = S(sc, BaseInnerPadY);
            int sectionGap = S(sc, BaseSectionGap);

            int innerH = lineH * 2 + lineGap;
            int totalH = padY * 2 + innerH;

            int memNumColW = S(sc, BaseMemNumColWidth);

            int cursor = padX + innerPadX;
            bool showFps = ShouldShowFps();
            if (showFps)
                cursor += fpsColW + colGap;

            int leftX = cursor;
            cursor += S(sc, BaseLeftColWidth);

            int chartX = cursor + chartGap;
            cursor = chartX + chartColW;

            
            int powX = cursor + powGap - 5;
            cursor = powX + powColW;

            int memBarX = cursor + S(sc, BaseMemBarGap) + sectionGap;
            int memNumX = memBarX + S(sc, BaseUsageBarWidth) + S(sc, BaseUsageNumGap);
            cursor = memNumX + memNumColW;

            int width = cursor + padX + innerPadX;

            if (Size.Width != width || Size.Height != totalH)
                Size = new Size(width, totalH);

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = _scalePercent <= 75 ? TextRenderingHint.ClearTypeGridFit : TextRenderingHint.AntiAliasGridFit;

            using var fillBrush = new SolidBrush(_dragModeActive && _bgAlpha < DragMinAlpha ? Color.FromArgb(Math.Max(_bgAlpha, DragMinAlpha), 0, 0, 0) : Color.FromArgb(_bgAlpha, 0, 0, 0));
            using var topGlowBrush = new SolidBrush(OverlayTopGlowColor);
            using var bottomGlowBrush = new SolidBrush(OverlayBottomGlowColor);

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
                    g.FillRectangle(topGlowBrush, Bound.X + 1, Bound.Y + 1, Bound.Width - 2, Math.Max(1, Bound.Height / 3));
                    g.FillRectangle(bottomGlowBrush, Bound.X + 1, Bound.Bottom - Math.Max(2, Bound.Height / 4), Bound.Width - 2, Math.Max(2, Bound.Height / 4));
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
            var fpsBold = _fpsBold!;

            float charW = g.MeasureString("XX", font).Width - g.MeasureString("X", font).Width;

            int topY = padY + innerPadY + 1;
            int textY = topY + (int)Math.Round(sc);

            // FPS
            if (showFps)
            {
                string fpsStr = Math.Max(0, _currentFps).ToString();
                float fpsW = g.MeasureString(fpsStr, fpsBold).Width;
                g.DrawString(fpsStr, fpsBold, _gpuBrush,
                new PointF(padX + innerPadX + (fpsColW - fpsW) / 2f, topY));
            }

            DrawMetricLine(g, font, rpmFont, _unitFont!, sc, leftX, textY, "CPU", _cpuUsage, FmtTemp(D(HardwareControl.cpuTemp)), FormatFan(HardwareControl.cpuFanRPM), _cpuBrush);
            DrawMetricLine(g, font, rpmFont, _unitFont!, sc, leftX, textY + lineH + lineGap, "GPU", _gpuUsage, FmtTemp(D(HardwareControl.gpuTemp)), FormatFan(HardwareControl.gpuFanRPM), _gpuBrush);

            int graphH = Math.Max(1, textY + lineH + lineGap + (int)Math.Round(8f * sc) - topY);
            OverlayChartRenderer.DrawStackedChart(g, chartX, topY, chartColW, graphH, sc, _cpuHistory, _gpuHistory, _historyHead, HistoryLength, _basePts, _gpuPts, _cpuPts, _polyPts, _graphGpuBrush, _graphGpuPen, _graphCpuBrush, _graphCpuPen, _totalPen!, _axPen!);

            
            string cpuPow = FmtPow(D(HardwareControl.cpuPower));
            string gpuPow = FmtPow(D(HardwareControl.gpuPower));
            DrawTextWithUnit(g, font, _unitFont!, cpuPow, _cpuBrush, new PointF(powX + powColW - 6, textY), alignRight: true);
            DrawTextWithUnit(g, font, _unitFont!, gpuPow, _gpuBrush, new PointF(powX + powColW - 6, textY + lineH + lineGap), alignRight: true);

            DrawMemGb(g, font, _unitFont!, memNumX, memNumColW, textY, _ramUsedMb, _cpuBrush);
            DrawMemGb(g, font, _unitFont!, memNumX, memNumColW, textY + lineH + lineGap, _vramUsedMb, _gpuBrush);
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
            if (_currentFps > 0)
            {
                long positiveSince = _fpsPositiveSinceTick;
                return positiveSince != 0 && Environment.TickCount64 - positiveSince >= FpsPositiveShowDelayMs;
            }
            if (_currentFps < 0) return false;
            long zeroSince = _fpsZeroSinceTick;
            bool linger = zeroSince != 0 &&
                          Environment.TickCount64 - zeroSince < FpsZeroLingerMs &&
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

        private static float MeasureTextWithUnit(Graphics g, Font mainFont, Font unitFont, string text)
        {
            if (string.IsNullOrEmpty(text)) return 0f;

            string[] units = { "MHz", "mV", "GB", "W", "%", "v", "Â°C", "Â°" };
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
            if (matchedUnit.Length <= 0)
                return g.MeasureString(text, mainFont, 1000, sf).Width;

            string valuePart = trimmed.Substring(0, trimmed.Length - matchedUnit.Length);
            float valW = g.MeasureString(valuePart, mainFont, 1000, sf).Width;
            float unitW = g.MeasureString(matchedUnit, unitFont, 1000, sf).Width;
            float gap = 0.5f * mainFont.Size / 11f;
            return valW + unitW + gap;
        }

        private static float DrawTextWithUnit(Graphics g, Font mainFont, Font unitFont, string text, Brush brush, PointF pos, bool alignRight = false)
        {
            if (string.IsNullOrEmpty(text)) return 0f;

            string[] units = { "MHz", "mV", "GB", "W", "%", "v", "Â°C", "Â°" };
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
        private static void DrawMetricLine(Graphics g, Font font, Font rpmFont, Font unitFont, float sc,
        float x, float y, string label, int? usage, string tempVal, string fanNum, SolidBrush brush)
        {
            float cursor = x;

            var sf = StringFormat.GenericTypographic;
            g.DrawString(label, font, brush, new PointF(cursor, y), sf);
            cursor += g.MeasureString(label, font, 1000, sf).Width;

            float gap = 9f * sc;
            string usageText = (usage ?? 0) + "%";
            float usageSlotW = g.MeasureString("100%", font, 1000, sf).Width;
            DrawTextWithUnit(g, font, unitFont, usageText, brush, new PointF(cursor + gap, y));
            cursor += gap + usageSlotW;

            float tempGap = 3f * sc;
            float tempW = DrawTextWithUnit(g, font, unitFont, tempVal, brush, new PointF(cursor + tempGap, y));
            float tempSlotW = Math.Max(tempW, MeasureTextWithUnit(g, font, unitFont, "100Â°C"));
            cursor += tempGap + tempSlotW;

            float spacing = 6f * sc;
            float numX = cursor + spacing;
            g.DrawString(fanNum, font, brush, new PointF(numX, y), sf);

            float fanW = g.MeasureString(fanNum, font, 1000, sf).Width;
            float rpmX = numX + fanW + 2f * sc;
            g.DrawString("RPM", rpmFont, brush, new PointF(rpmX, y), sf);
        }

        private void PositionAtTopLeft()
        {
            Screen screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
            Location = new Point(screen.Bounds.X + MarginFromEdge, screen.Bounds.Y + MarginFromEdge);
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
            if (next == _scalePercent) return;

            Point center = new Point(Location.X + Width / 2, Location.Y + Height / 2);
            Screen screen = Screen.FromPoint(center);
            bool isRight  = center.X > screen.Bounds.X + screen.Bounds.Width  / 2;
            bool isBottom = center.Y > screen.Bounds.Y + screen.Bounds.Height / 2;
            int rightEdge  = Location.X + Width;
            int bottomEdge = Location.Y + Height;

            _scalePercent = next;
            AppConfig.Set("overlay_scale_percent", _scalePercent);
            Invalidate();

            int newX = isRight  ? rightEdge  - Width  : Location.X;
            int newY = isBottom ? bottomEdge - Height : Location.Y;
            if (newX != Location.X || newY != Location.Y)
                Location = new Point(newX, newY);
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

        private static Color ParseColor(string key, Color fallback)
        {
            string hex = AppConfig.GetString(key);
            if (string.IsNullOrEmpty(hex)) return fallback;
            try { return ColorTranslator.FromHtml(hex.StartsWith("#") ? hex : "#" + hex); }
            catch { return fallback; }
        }

        private void ApplyColors()
        {
            Color gpu = Color.White;
            Color cpu = Color.White;
            _bgAlpha = Math.Clamp(AppConfig.Get("overlay_alpha", 128), 0, 255);

            _gpuBrush.Dispose();     _gpuBrush = new SolidBrush(gpu);
            _cpuBrush.Dispose();     _cpuBrush = new SolidBrush(cpu);
            _bgBrush.Dispose();      _bgBrush = new SolidBrush(Color.FromArgb(_bgAlpha, 0, 0, 0));
        }

        private void ApplySensorFlags() { }

        /// <summary>Called by MetricsSettingsForm when the user changes checkboxes while overlay is running.</summary>
        public void RefreshDisplayFlags()
        {
            // Writing bool flags is atomic on the CLR â€” safe to call from any thread.
            // The overlay's timer Tick fires every ~1s and will pick up the new flags
            // on the next render cycle automatically.
            EnsureFpsMonitor();
        }


        private void EnsureFpsMonitor()
        {
            if (_fps != null || !(_gameOnly || AppConfig.IsOverlay())) return;
            ResetFpsDisplayState();
            _fps = new EtwFpsMonitor();
            _fpsTask = Task.Run(() => _fps.Start());
        }

        private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            if (!_active) return;
            Program.settingsForm?.BeginInvoke(() => { if (_active) RestorePosition(); });
        }

        private void RestorePosition()
        {
            int anchor = AppConfig.Get("overlay_anchor", -1);
            if (anchor < 0) { PositionAtTopLeft(); return; }
            int offsetX = AppConfig.Get("overlay_offset_x", MarginFromEdge);
            int offsetY = AppConfig.Get("overlay_offset_y", MarginFromEdge);
            Screen screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
            bool isRight  = (anchor & 1) != 0;
            bool isBottom = (anchor & 2) != 0;
            int x = isRight  ? screen.Bounds.Right  - Width  - offsetX : screen.Bounds.X + offsetX;
            int y = isBottom ? screen.Bounds.Bottom - Height - offsetY : screen.Bounds.Y + offsetY;
            const int margin = 5;
            x = Math.Clamp(x, screen.Bounds.Left + margin, screen.Bounds.Right  - Width  - margin);
            y = Math.Clamp(y, screen.Bounds.Top  + margin, screen.Bounds.Bottom - Height - margin);
            Location = new Point(x, y);
        }

        public void StartOverlay()
        {
            _active = true;
            _lastFgPid = 0;
            _gameOnly = AppConfig.IsOverlayGameOnly();
            _hidden = false;
            _shownPid = 0;
            _fgDesktop = false;

            _scalePercent = Math.Clamp(AppConfig.Get("overlay_scale_percent", 100), MinScalePercent, MaxScalePercent);
            ApplyColors();
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            HardwareControl.ResetCPUPowerCounter();

            _fps?.Dispose();
            _fps = null;
            ResetFpsDisplayState();
            EnsureFpsMonitor();

            float sc = GetScale();
            int innerH = S(sc, BaseLineHeight) * 2 + S(sc, BaseLineSpacing);
            Size = new Size(S(sc, GetBaseWidth()), S(sc, BasePadY) * 2 + innerH);

            RestorePosition();
            base.Show();
            if (_gameOnly) { _hidden = true; NativeMethods.ShowWindow(Handle, NativeMethods.SW_HIDE); }
            Tick();
            RestorePosition();
            _timer.Start();
        }

        public void StopOverlay()
        {
            _active = false;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _timer.Stop();
            _dragModeActive = false;
            _dragging = false;

            _fps?.Dispose();
            _fps = null;
            ResetFpsDisplayState();

            Task? task = _fpsTask;
            _fpsTask = null;
            ProcessMemoryHelper.TrimAfter(task);

            _font?.Dispose();     _font     = null;
            _rpmFont?.Dispose();  _rpmFont  = null;
            _unitFont?.Dispose(); _unitFont = null;
            _fpsBold?.Dispose();  _fpsBold  = null;
            _totalPen?.Dispose(); _totalPen = null;
            _axPen?.Dispose();    _axPen    = null;
            _lastScale = 0f;
            base.Hide();

            if (!AppConfig.IsOverlay())
                Program.ScheduleHardwareOverlayUnload();
        }

        public void SuspendForDisplayOff()
        {
            if (_active) StopOverlay();
        }

        public void ResumeForDisplayOn()
        {
            if (!_active && AppConfig.IsOverlay()) StartOverlay();
        }
    }
}









