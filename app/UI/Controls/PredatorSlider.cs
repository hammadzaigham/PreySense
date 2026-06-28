using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace PreySense.UI
{
    [SupportedOSPlatform("windows")]
    public class PredatorSlider : Control
    {
        private float _radius;
        private PointF _thumbPos;
        private SizeF _barSize;
        private PointF _barPos;
        private float? _dragX;

        private const float InnerNormal = 12f / 22f;
        private const float InnerHover = 16f / 22f;
        private const float InnerPressed = 10f / 22f;

        private float _innerScale = InnerNormal;
        private float _innerTarget = InnerNormal;
        private float _tickAlpha;
        private float _tickTarget;
        private readonly System.Windows.Forms.Timer _animTimer = new() { Interval = 30 };

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color AccentColor { get; set; } = Color.FromArgb(58, 174, 239); // G-Helper Blue by default
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color BorderColor { get; set; } = Color.White;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int? SelectableMinimum { get; set; } = null;

        public List<int> SupportedValues { get; } = new();

        public event EventHandler? ValueChanged;
        public event EventHandler? ValueCommitted;

        private int _minimum = 0;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Minimum
        {
            get => _minimum;
            set
            {
                _minimum = value;
                RecalculateParameters();
            }
        }

        private int _maximum = 100;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Maximum
        {
            get => _maximum;
            set
            {
                _maximum = value;
                RecalculateParameters();
            }
        }

        private int _step = 1;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Step
        {
            get => _step;
            set { _step = value; }
        }

        private int _value = 50;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Value
        {
            get => _value;
            set
            {
                int minAllowed = SelectableMinimum ?? _minimum;
                int clamped = Math.Clamp(value, minAllowed, _maximum);
                clamped = (int)Math.Round(clamped / (float)_step) * _step;

                if (_value != clamped)
                {
                    _value = clamped;
                    ValueChanged?.Invoke(this, EventArgs.Empty);
                    RecalculateParameters();
                }
            }
        }

        public PredatorSlider()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            DoubleBuffered = true;
            TabStop = false;
            Cursor = Cursors.Hand;
            Size = new Size(200, 28);

            _animTimer.Tick += delegate
            {
                _innerScale += (_innerTarget - _innerScale) * 0.3f;
                _tickAlpha += (_tickTarget - _tickAlpha) * 0.3f;
                if (Math.Abs(_innerTarget - _innerScale) < 0.01f && Math.Abs(_tickTarget - _tickAlpha) < 1f)
                {
                    _innerScale = _innerTarget;
                    _tickAlpha = _tickTarget;
                    _animTimer.Stop();
                }
                Invalidate();
            };
        }

        private void AnimateInner(float target)
        {
            _innerTarget = target;
            _tickTarget = target == InnerNormal ? 0 : 120;
            _animTimer.Start();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Right:
                case Keys.Left:
                case Keys.Up:
                case Keys.Down:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            int nextVal = _value;
            int minAllowed = SelectableMinimum ?? _minimum;
            switch (e.KeyCode)
            {
                case Keys.Right:
                case Keys.Up:
                    nextVal = Math.Min(_maximum, _value + _step);
                    break;
                case Keys.Left:
                case Keys.Down:
                    nextVal = Math.Max(minAllowed, _value - _step);
                    break;
            }

            if (nextVal != _value)
            {
                Value = nextVal;
                ValueCommitted?.Invoke(this, EventArgs.Empty);
            }

            AccessibilityNotifyClients(AccessibleEvents.Focus, 0);
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            // G-Helper styles: background track is dark gray
            using Brush brushAccent = new SolidBrush(AccentColor);
            using Brush brushEmpty = new SolidBrush(Color.FromArgb(70, 70, 70));
            using Brush brushBorder = new SolidBrush(BorderColor);

            float thumbX = _dragX ?? _thumbPos.X;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            // Draw track using lines with rounded caps
            float midY = _barPos.Y + _barSize.Height / 2;
            using (Pen penEmpty = new Pen(Color.FromArgb(70, 70, 70), _barSize.Height))
            {
                penEmpty.StartCap = LineCap.Round;
                penEmpty.EndCap = LineCap.Round;
                e.Graphics.DrawLine(penEmpty, _barPos.X, midY, _barPos.X + _barSize.Width, midY);
            }

            if (thumbX > _barPos.X)
            {
                using (Pen penAccent = new Pen(AccentColor, _barSize.Height))
                {
                    penAccent.StartCap = LineCap.Round;
                    penAccent.EndCap = LineCap.Round;
                    e.Graphics.DrawLine(penAccent, _barPos.X, midY, thumbX, midY);
                }
            }

            if (_tickAlpha >= 1 && SupportedValues.Count > 0)
            {
                using Brush brushMark = new SolidBrush(Color.FromArgb((int)_tickAlpha, Color.FromArgb(240, 240, 240)));
                float tickW = Math.Max(1f, _barSize.Height / 4);
                float tickH = 0.75f * _barSize.Height;
                float gap = 0.5f * _barSize.Height;
                foreach (int value in SupportedValues)
                {
                    float x = ValueToX(value) - tickW / 2;
                    e.Graphics.FillRectangle(brushMark, x, _barPos.Y - gap - tickH, tickW, tickH);
                    e.Graphics.FillRectangle(brushMark, x, _barPos.Y + _barSize.Height + gap, tickW, tickH);
                }
            }

            // Draw thumb circle (white border circle, accented inner circle scaling dynamically)
            e.Graphics.FillEllipse(brushBorder, thumbX - _radius, _thumbPos.Y - _radius, _radius * 2, _radius * 2);
            float innerR = _innerScale * _radius;
            e.Graphics.FillEllipse(brushAccent, thumbX - innerR, _thumbPos.Y - innerR, innerR * 2, innerR * 2);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            RecalculateParameters();
        }

        private float ValueToX(int value)
        {
            if (_maximum <= _minimum) return _barPos.X;
            return _barPos.X + _barSize.Width * (float)(value - _minimum) / (_maximum - _minimum);
        }

        private void RecalculateParameters()
        {
            _radius = 0.4F * ClientSize.Height;
            _barSize = new SizeF(ClientSize.Width - 2 * _radius, ClientSize.Height * 0.15F);
            _barPos = new PointF(_radius, (ClientSize.Height - _barSize.Height) / 2);
            _thumbPos = new PointF(
                ValueToX(_value),
                _barPos.Y + 0.5f * _barSize.Height);
            Invalidate();
        }

        private bool _moving = false;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();

            float dx = e.Location.X - _thumbPos.X;
            float dy = e.Location.Y - _thumbPos.Y;
            if (dx * dx + dy * dy <= _radius * _radius)
            {
                _moving = true;
            }
            else
            {
                // Click on track: jump thumb there
                _moving = true;
            }

            AnimateInner(InnerPressed);
            CalculateValue(e);
        }

        private void CalculateValue(MouseEventArgs e)
        {
            float thumbX = e.Location.X;
            float minX = _barPos.X;
            if (SelectableMinimum.HasValue && _maximum > _minimum)
            {
                minX = _barPos.X + _barSize.Width * (float)(SelectableMinimum.Value - _minimum) / (_maximum - _minimum);
            }

            if (thumbX < minX)
            {
                thumbX = minX;
            }
            else if (thumbX > _barPos.X + _barSize.Width)
            {
                thumbX = _barPos.X + _barSize.Width;
            }

            if (_moving)
            {
                _dragX = thumbX;
                Invalidate();
            }

            float t = _barSize.Width > 0 ? (thumbX - _barPos.X) / _barSize.Width : 0f;
            Value = _minimum + (int)Math.Round(t * (_maximum - _minimum));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_moving)
            {
                CalculateValue(e);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_moving)
            {
                _moving = false;
                _dragX = null;
                ValueCommitted?.Invoke(this, EventArgs.Empty);
            }
            AnimateInner(ClientRectangle.Contains(e.Location) ? InnerHover : InnerNormal);
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            if (!_moving) AnimateInner(InnerHover);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!_moving) AnimateInner(InnerNormal);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _animTimer.Dispose();
            base.Dispose(disposing);
        }
    }
}
