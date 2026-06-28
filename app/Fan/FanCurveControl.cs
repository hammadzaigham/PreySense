using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.Versioning;
using PreySense.UI;


namespace PreySense.Fan
{
    [SupportedOSPlatform("windows")]
    public class FanCurveControl : Control
    {
        private const int LeftPad = 38;
        private const int RightPad = 15;
        private const int TopPad = 22;
        private const int BottomPad = 28;
        private const int NodeRadius = 6;
        private const int DragRedrawIntervalMs = 33;
        private const float TempMin = 40f;
        private const float TempMax = 110f;
        private const string GridFontFamily = "Segoe UI";
        private const float GridFontSizeWide = 7.5f;
        private const float GridFontSizeNarrow = 7f;

        private PointF[] _points = Array.Empty<PointF>();
        private int _draggedIndex = -1;
        private int _hoveredIndex = -1;
        private bool _isCpu = true;
        private readonly Stopwatch _dragRedrawClock = Stopwatch.StartNew();

        public static int MaxCpuRpmSeen = 6400;
        public static int MaxGpuRpmSeen = 6400;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsCpu
        {
            get => _isCpu;
            set
            {
                if (_isCpu == value) return;
                _isCpu = value;
                Invalidate();
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public PointF[] Points
        {
            get => _points;
            set
            {
                _points = value ?? Array.Empty<PointF>();
                Invalidate();
            }
        }

        public event EventHandler? PointsChanged;

        public FanCurveControl()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);

            BackColor = UiTheme.IsLightTheme() ? Color.FromArgb(250, 250, 250) : Color.FromArgb(20, 20, 22);
            Cursor = Cursors.Hand;
        }

        private int GraphWidth => Width - LeftPad - RightPad;
        private int GraphHeight => Height - TopPad - BottomPad;

        private PointF GetPixelCoords(PointF point)
        {
            float x = LeftPad + ((point.X - TempMin) / (TempMax - TempMin)) * GraphWidth;
            float y = TopPad + ((100f - point.Y) / 100f) * GraphHeight;
            return new PointF(x, y);
        }

        private PointF GetValueCoords(float pixelX, float pixelY)
        {
            float x = TempMin + ((pixelX - LeftPad) / GraphWidth) * (TempMax - TempMin);
            float y = 100f - ((pixelY - TopPad) / GraphHeight) * 100f;
            return new PointF(Math.Clamp(x, TempMin, TempMax), Math.Clamp(y, 0f, 100f));
        }

        private static string GetYLabel(int percentage) => $"{percentage}%";

        private static void DrawCenteredLabel(Graphics g, string text, Font font, Brush brush, float x, float y)
        {
            SizeF size = g.MeasureString(text, font);
            g.DrawString(text, font, brush, x - size.Width / 2, y);
        }

        private void DrawGrid(Graphics g)
        {
            bool light = UiTheme.IsLightTheme();
            using var gridPen = new Pen(light ? Color.FromArgb(220, 220, 220) : Color.FromArgb(40, 40, 45), 1f);
            using var labelBrush = new SolidBrush(light ? Color.FromArgb(80, 80, 80) : Color.FromArgb(120, 120, 135));
            using var font = new Font(GridFontFamily, GraphWidth < 320 ? GridFontSizeNarrow : GridFontSizeWide);

            int tempStep = GraphWidth < 320 ? 20 : 10;
            for (int t = (int)TempMin; t <= (int)TempMax; t += tempStep)
            {
                float x = LeftPad + ((t - TempMin) / (TempMax - TempMin)) * GraphWidth;
                g.DrawLine(gridPen, x, TopPad, x, Height - BottomPad);
                DrawCenteredLabel(g, $"{t}", font, labelBrush, x, Height - BottomPad + 5);
            }

            for (int i = 0; i <= 100; i += 10)
            {
                float y = TopPad + ((100 - i) / 100f) * GraphHeight;
                g.DrawLine(gridPen, LeftPad, y, Width - RightPad, y);

                if (i % 20 == 0 || i == 100)
                {
                    string label = GetYLabel(i);
                    SizeF size = g.MeasureString(label, font);
                    g.DrawString(label, font, labelBrush, Math.Max(0, LeftPad - size.Width - 3), y - size.Height / 2);
                }
            }
        }

        private void DrawCurveFill(Graphics g, Color themeColor)
        {
            using var path = new GraphicsPath();
            PointF previous = GetPixelCoords(_points[0]);
            path.AddLine(LeftPad, Height - BottomPad, previous.X, previous.Y);

            for (int i = 1; i < _points.Length; i++)
            {
                PointF current = GetPixelCoords(_points[i]);
                path.AddLine(previous, current);
                previous = current;
            }

            path.AddLine(previous.X, previous.Y, Width - RightPad, Height - BottomPad);
            path.CloseFigure();

            using var brush = new LinearGradientBrush(
                new Point(0, TopPad),
                new Point(0, Height - BottomPad),
                Color.FromArgb(60, themeColor.R, themeColor.G, themeColor.B),
                Color.FromArgb(0, themeColor.R, themeColor.G, themeColor.B));

            g.FillPath(brush, path);
        }

        private void DrawCurveLine(Graphics g, Color themeColor)
        {
            using var linePen = new Pen(themeColor, 2f);
            PointF previous = GetPixelCoords(_points[0]);

            for (int i = 1; i < _points.Length; i++)
            {
                PointF current = GetPixelCoords(_points[i]);
                g.DrawLine(linePen, previous, current);
                previous = current;
            }
        }

        private void DrawNodes(Graphics g, Color themeColor)
        {
            bool light = UiTheme.IsLightTheme();
            for (int i = 0; i < _points.Length; i++)
            {
                PointF point = GetPixelCoords(_points[i]);
                bool isHighlighted = i == _hoveredIndex || i == _draggedIndex;
                Color nodeColor = isHighlighted ? (light ? Color.FromArgb(240, 240, 240) : Color.White) : themeColor;
                int radius = isHighlighted ? NodeRadius + 2 : NodeRadius;

                using var nodeBrush = new SolidBrush(nodeColor);
                using var borderPen = new Pen(light ? Color.FromArgb(200, 200, 200) : Color.FromArgb(30, 30, 30), 1.5f);

                g.FillEllipse(nodeBrush, point.X - radius, point.Y - radius, radius * 2, radius * 2);
                g.DrawEllipse(borderPen, point.X - radius, point.Y - radius, radius * 2, radius * 2);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Color themeColor = _isCpu ? Color.FromArgb(58, 174, 239) : Color.FromArgb(255, 32, 32);

            using (var bgBrush = new SolidBrush(BackColor))
            {
                g.FillRectangle(bgBrush, ClientRectangle);
            }

            DrawGrid(g);
            if (_points.Length == 0) return;

            DrawCurveFill(g, themeColor);
            DrawCurveLine(g, themeColor);
            DrawNodes(g, themeColor);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _draggedIndex = FindNodeIndex(e.Location);
                if (_draggedIndex != -1)
                {
                    _dragRedrawClock.Restart();
                    Capture = true;
                    Invalidate();
                }
            }

            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_draggedIndex != -1)
            {
                float finalY = GetValueCoords(e.X, e.Y).Y;
                if ((ModifierKeys & Keys.Control) == Keys.Control)
                {
                    finalY = (float)(Math.Round(finalY / 5f) * 5f);
                }

                PointF current = _points[_draggedIndex];
                if (Math.Abs(current.Y - finalY) >= 0.1f)
                {
                    _points[_draggedIndex] = new PointF(current.X, finalY);
                    if (_dragRedrawClock.ElapsedMilliseconds >= DragRedrawIntervalMs)
                    {
                        _dragRedrawClock.Restart();
                        Invalidate();
                    }
                }
            }
            else
            {
                int newHover = FindNodeIndex(e.Location);
                if (newHover != _hoveredIndex)
                {
                    _hoveredIndex = newHover;
                    Invalidate();
                }
            }

            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            bool wasDragging = _draggedIndex != -1;
            if (wasDragging)
            {
                _draggedIndex = -1;
                Capture = false;
                Invalidate();
                PointsChanged?.Invoke(this, EventArgs.Empty);
            }

            base.OnMouseUp(e);
        }

        private int FindNodeIndex(Point mousePoint)
        {
            for (int i = 0; i < _points.Length; i++)
            {
                PointF point = GetPixelCoords(_points[i]);
                float dx = mousePoint.X - point.X;
                float dy = mousePoint.Y - point.Y;
                float distance = (float)Math.Sqrt(dx * dx + dy * dy);
                if (distance <= NodeRadius + 6)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
