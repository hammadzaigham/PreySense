using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PreySense.Frontend.Controls
{
    /// <summary>
    /// Interactive WPF Color Wheel Control.
    /// Renders a circular HSL/HSV hue-saturation wheel and updates the SelectedColor/SelectedHex.
    /// </summary>
    public class ColorWheelControl : FrameworkElement
    {
        private WriteableBitmap? _bitmap;
        private Point _center;
        private double _radius;
        private bool _isDragging;
        private Color _previewColor;

        public static readonly DependencyProperty SelectedColorProperty =
            DependencyProperty.Register(nameof(SelectedColor), typeof(Color), typeof(ColorWheelControl),
                new FrameworkPropertyMetadata(Color.FromRgb(0, 229, 255), FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender, OnSelectedColorChanged));

        public static readonly DependencyProperty SelectedHexProperty =
            DependencyProperty.Register(nameof(SelectedHex), typeof(string), typeof(ColorWheelControl),
                new FrameworkPropertyMetadata("#00E5FF", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public Color SelectedColor
        {
            get => (Color)GetValue(SelectedColorProperty);
            set => SetValue(SelectedColorProperty, value);
        }

        public string SelectedHex
        {
            get => (string)GetValue(SelectedHexProperty);
            set => SetValue(SelectedHexProperty, value);
        }

        public event EventHandler<Color>? ColorChanged;
        public event EventHandler<Color>? ColorPicked;

        static ColorWheelControl()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(ColorWheelControl), new FrameworkPropertyMetadata(typeof(ColorWheelControl)));
        }

        public ColorWheelControl()
        {
            ClipToBounds = true;
            Cursor = Cursors.Hand;
            SizeChanged += OnSizeChanged;
        }

        private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ColorWheelControl wheel && e.NewValue is Color c)
            {
                wheel.SelectedHex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
                wheel.ColorChanged?.Invoke(wheel, c);
            }
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            int size = (int)Math.Min(ActualWidth, ActualHeight);
            if (size <= 10) return;

            _center = new Point(ActualWidth / 2.0, ActualHeight / 2.0);
            _radius = Math.Min(ActualWidth, ActualHeight) / 2.0 - 6.0;

            RenderWheelBitmap((int)ActualWidth, (int)ActualHeight);
            InvalidateVisual();
        }

        private void RenderWheelBitmap(int width, int height)
        {
            if (width <= 0 || height <= 0 || _radius <= 0) return;

            _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            int stride = width * 4;
            byte[] pixels = new byte[height * stride];

            double cx = _center.X;
            double cy = _center.Y;
            double rad = _radius;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    double dx = x - cx;
                    double dy = y - cy;
                    double dist = Math.Sqrt(dx * dx + dy * dy);

                    int index = y * stride + x * 4;

                    if (dist <= rad)
                    {
                        // Angle from -PI to PI
                        double angle = Math.Atan2(dy, dx);
                        if (angle < 0) angle += 2 * Math.PI;

                        double hue = (angle * 180.0 / Math.PI); // 0 to 360
                        double sat = Math.Clamp(dist / rad, 0.0, 1.0); // 0 at center (white) to 1 at edge
                        double val = 1.0;

                        Color c = ColorFromHsv(hue, sat, val);

                        // Anti-alias edge
                        byte alpha = 255;
                        if (dist > rad - 1.5)
                        {
                            alpha = (byte)Math.Clamp((rad - dist) / 1.5 * 255, 0, 255);
                        }

                        pixels[index] = c.B;
                        pixels[index + 1] = c.G;
                        pixels[index + 2] = c.R;
                        pixels[index + 3] = alpha;
                    }
                    else
                    {
                        pixels[index] = 0;
                        pixels[index + 1] = 0;
                        pixels[index + 2] = 0;
                        pixels[index + 3] = 0;
                    }
                }
            }

            _bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            if (_bitmap != null)
            {
                dc.DrawImage(_bitmap, new Rect(0, 0, ActualWidth, ActualHeight));
            }

            // Draw outer border ring

            Color thumbColor = _isDragging ? _previewColor : SelectedColor;
            Point thumbPos = GetPositionFromColor(thumbColor);

            dc.DrawEllipse(new SolidColorBrush(thumbColor), new Pen(Brushes.Black, 2.5), thumbPos, 7, 7);
            dc.DrawEllipse(null, new Pen(Brushes.White, 1.5), thumbPos, 7, 7);
        }

        private Point GetPositionFromColor(Color c)
        {
            ColorToHsv(c, out double h, out double s, out _);
            double angleRad = h * Math.PI / 180.0;
            double dist = s * _radius;
            return new Point(_center.X + dist * Math.Cos(angleRad), _center.Y + dist * Math.Sin(angleRad));
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.ChangedButton == MouseButton.Left)
            {
                _isDragging = true;
                CaptureMouse();
                PickColorAtPoint(e.GetPosition(this), commit: false);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_isDragging)
            {
                PickColorAtPoint(e.GetPosition(this), commit: false);
            }
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
            if (_isDragging && e.ChangedButton == MouseButton.Left)
            {
                PickColorAtPoint(e.GetPosition(this), commit: true);
                _isDragging = false;
                ReleaseMouseCapture();
                InvalidateVisual();
            }
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            if (!_isDragging)
                return;

            PickColorAtPoint(e.GetPosition(this), commit: true);
            _isDragging = false;
            InvalidateVisual();
        }

        private void PickColorAtPoint(Point pt, bool commit)
        {
            if (_radius <= 0) return;

            double dx = pt.X - _center.X;
            double dy = pt.Y - _center.Y;

            double angle = Math.Atan2(dy, dx);
            if (angle < 0) angle += 2 * Math.PI;

            double hue = angle * 180.0 / Math.PI;
            double sat = Math.Clamp(Math.Sqrt(dx * dx + dy * dy) / _radius, 0.0, 1.0);
            Color color = ColorFromHsv(hue, sat, 1.0);

            _previewColor = color;
            InvalidateVisual();

            if (commit)
            {
                SetCurrentValue(SelectedColorProperty, color);
                ColorPicked?.Invoke(this, color);
            }
        }

        public static Color ColorFromHsv(double hue, double saturation, double value)
        {
            int hi = Convert.ToInt32(Math.Floor(hue / 60)) % 6;
            double f = hue / 60 - Math.Floor(hue / 60);

            value = value * 255;
            byte v = (byte)Math.Clamp(value, 0, 255);
            byte p = (byte)Math.Clamp(value * (1 - saturation), 0, 255);
            byte q = (byte)Math.Clamp(value * (1 - f * saturation), 0, 255);
            byte t = (byte)Math.Clamp(value * (1 - (1 - f) * saturation), 0, 255);

            return hi switch
            {
                0 => Color.FromRgb(v, t, p),
                1 => Color.FromRgb(q, v, p),
                2 => Color.FromRgb(p, v, t),
                3 => Color.FromRgb(p, q, v),
                4 => Color.FromRgb(t, p, v),
                _ => Color.FromRgb(v, p, q),
            };
        }

        public static void ColorToHsv(Color color, out double hue, out double saturation, out double value)
        {
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;

            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;

            hue = 0;
            if (delta > 1e-5)
            {
                if (max == r) hue = 60 * (((g - b) / delta) % 6);
                else if (max == g) hue = 60 * (((b - r) / delta) + 2);
                else if (max == b) hue = 60 * (((r - g) / delta) + 4);
                if (hue < 0) hue += 360;
            }

            saturation = max == 0 ? 0 : delta / max;
            value = max;
        }
    }
}
