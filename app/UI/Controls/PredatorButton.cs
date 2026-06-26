using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.Versioning;
using System.Windows.Forms;
using PreySense.Helpers;

namespace PreySense.UI
{
    [SupportedOSPlatform("windows")]
    public class PredatorButton : Button
    {
        // Design tokens
        private const float HoverShiftAmount = 0.04f;
        private const float ActiveTopLighten = 0.25f;
        private const float RestTopLighten = 0.1f;
        private const int ActiveBgTopAlpha = 32;
        private const float ActiveBgEndFraction = 0.20f;

        private int borderSize = 4;
        private int borderRadius = 4;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int BorderRadius
        {
            get => borderRadius;
            set { borderRadius = value; Invalidate(); }
        }

        private Color borderColor = Color.Transparent;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color BorderColor
        {
            get => borderColor;
            set { borderColor = value; Invalidate(); }
        }

        private bool activated = false;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsActive
        {
            get => activated;
            set
            {
                if (activated != value)
                {
                    activated = value;
                    Invalidate();
                }
            }
        }

        private bool secondary = false;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Secondary
        {
            get => secondary;
            set
            {
                secondary = value;
                if (!zoneColor.HasValue)
                {
                    BackColor = secondary ? Color.FromArgb(36, 36, 36) : Color.FromArgb(46, 46, 46);
                }
                Invalidate();
            }
        }

        private Color? zoneColor = null;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color? ZoneColor
        {
            get => zoneColor;
            set
            {
                zoneColor = value;
                if (zoneColor.HasValue)
                {
                    BackColor = zoneColor.Value;
                }
                else
                {
                    BackColor = secondary ? Color.FromArgb(36, 36, 36) : Color.FromArgb(46, 46, 46);
                }
                Invalidate();
            }
        }

        private Color? customActiveColor = null;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color? CustomActiveColor
        {
            get => customActiveColor;
            set { customActiveColor = value; Invalidate(); }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Borderless { get; set; } = false;

        protected override bool ShowFocusCues => false;

        public PredatorButton()
        {
            DoubleBuffered = true;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI", 9f, FontStyle.Regular);

            // Default dark colors matching G-Helper
            BackColor = Color.FromArgb(46, 46, 46);
            ForeColor = Color.FromArgb(240, 240, 240);
            FlatAppearance.BorderColor = Color.FromArgb(55, 55, 55);

            BackColorChanged += (s, e) => UpdateHoverColor();
            UpdateHoverColor();
        }

        private void UpdateHoverColor()
        {
            if (zoneColor.HasValue)
            {
                int yiq = ((BackColor.R * 299) + (BackColor.G * 587) + (BackColor.B * 114)) / 1000;
                Color targetCol = yiq >= 128 ? Color.Black : Color.White;
                FlatAppearance.MouseOverBackColor = PreySense.Helpers.ControlHelper.Shift(BackColor, targetCol, HoverShiftAmount * 1.5f);
                FlatAppearance.MouseDownBackColor = PreySense.Helpers.ControlHelper.Shift(BackColor, targetCol, HoverShiftAmount * 3f);
            }
            else
            {
                int lum = (BackColor.R * 30 + BackColor.G * 59 + BackColor.B * 11) / 100;
                Color target = lum > 128 ? Color.Black : Color.White;
                FlatAppearance.MouseOverBackColor = PreySense.Helpers.ControlHelper.Shift(BackColor, target, HoverShiftAmount);
                FlatAppearance.MouseDownBackColor = PreySense.Helpers.ControlHelper.Shift(BackColor, target, HoverShiftAmount * 2f);
            }
        }

        private GraphicsPath GetFigurePath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            float curveSize = radius * 2F;
            if (curveSize <= 0) curveSize = 1;

            path.StartFigure();
            path.AddArc(rect.X, rect.Y, curveSize, curveSize, 180, 90);
            path.AddArc(rect.Right - curveSize, rect.Y, curveSize, curveSize, 270, 90);
            path.AddArc(rect.Right - curveSize, rect.Bottom - curveSize, curveSize, curveSize, 0, 90);
            path.AddArc(rect.X, rect.Bottom - curveSize, curveSize, curveSize, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            // Set text color according to zone color or disabled state
            if (!Enabled)
            {
                ForeColor = Color.FromArgb(100, 100, 100);
            }
            else if (zoneColor.HasValue)
            {
                int yiq = ((zoneColor.Value.R * 299) + (zoneColor.Value.G * 587) + (zoneColor.Value.B * 114)) / 1000;
                ForeColor = yiq >= 128 ? Color.Black : Color.White;
            }
            else
            {
                ForeColor = Color.FromArgb(240, 240, 240);
            }

            base.OnPaint(pevent);

            float ratio = pevent.Graphics.DpiX / 192.0f;
            int border = Math.Max(1, (int)(ratio * borderSize));
            int radius = Math.Max(1, (int)(ratio * borderRadius));

            Rectangle rectSurface = ClientRectangle;

            using (GraphicsPath pathSurface = GetFigurePath(rectSurface, radius + border))
            using (Pen penSurface = new Pen(Parent?.BackColor ?? Color.FromArgb(28, 28, 28), border))
            {
                pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Region = new Region(pathSurface);
                pevent.Graphics.DrawPath(penSurface, pathSurface);

                // Determine active border color
                Color activeBorderColor = customActiveColor ?? (borderColor.A > 0 ? borderColor : Color.FromArgb(58, 174, 239));

                bool drawActive = Enabled && !Borderless && activated;
                bool drawRest = Enabled && !Borderless && !activated && FlatAppearance.BorderColor.A > 0;

                if (drawActive)
                {
                    Rectangle borderRect = new Rectangle(border, border, rectSurface.Width - 2 * border, rectSurface.Height - 2 * border);

                    Color bgTop = Color.FromArgb(ActiveBgTopAlpha, activeBorderColor);
                    Color bgTransparent = Color.FromArgb(0, activeBorderColor);
                    float bgEndPos = ActiveBgEndFraction;

                    using (GraphicsPath bgPath = GetFigurePath(borderRect, radius))
                    using (LinearGradientBrush bgBrush = new LinearGradientBrush(
                        new PointF(0, borderRect.Y), new PointF(0, borderRect.Bottom),
                        bgTop, bgTransparent))
                    {
                        bgBrush.InterpolationColors = new ColorBlend
                        {
                            Colors = new[] { bgTop, bgTransparent, bgTransparent },
                            Positions = new[] { 0f, bgEndPos, 1f }
                        };
                        pevent.Graphics.FillPath(bgBrush, bgPath);
                    }

                    ControlHelper.DrawGradientBorder(pevent.Graphics, borderRect, activeBorderColor, radius, border, PenAlignment.Outset, ActiveTopLighten);
                }
                else if (drawRest)
                {
                    int inset = border / 2 + 1;
                    Rectangle borderRect = new Rectangle(inset, inset, rectSurface.Width - 2 * inset, rectSurface.Height - 2 * inset);
                    ControlHelper.DrawGradientBorder(pevent.Graphics, borderRect, FlatAppearance.BorderColor, radius + border - inset, 1f, PenAlignment.Inset, RestTopLighten);
                }
            }

            if (!Enabled)
            {
                var rect = pevent.ClipRectangle;
                if (Image is not null)
                {
                    rect.Y += Image.Height;
                    rect.Height -= Image.Height;
                }
                TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak;
                TextRenderer.DrawText(pevent.Graphics, Text, Font, rect, Color.FromArgb(100, 100, 100), flags);
            }
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
    }
}
