using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PreySense.Helpers
{
    [SupportedOSPlatform("windows")]
    public static class ControlHelper
    {
        private const float GradientHeightFraction = 0.3f;
        private const float LightGradientHeightFraction = 0.9f;
        private const int TopFadeAlpha = 64;

        public static GraphicsPath RoundedRect(Rectangle bounds, int radiusL, int radiusR)
        {
            int diameterL = Math.Max(1, radiusL * 2);
            int diameterR = Math.Max(1, radiusR * 2);

            Size sizeL = new Size(diameterL, diameterL);
            Size sizeR = new Size(diameterR, diameterR);

            Rectangle arcL = new Rectangle(bounds.Location, sizeL);
            Rectangle arcR = new Rectangle(bounds.Location, sizeR);

            GraphicsPath path = new GraphicsPath();

            // top left arc  
            path.AddArc(arcL, 180, 90);

            // top right arc  
            arcR.X = bounds.Right - diameterR;
            arcR.Y = bounds.Top;
            path.AddArc(arcR, 270, 90);

            // bottom right arc  
            arcR.Y = bounds.Bottom - diameterR;
            arcR.X = bounds.Right - diameterR;
            path.AddArc(arcR, 0, 90);

            // bottom left arc 
            arcL.X = bounds.Left;
            arcL.Y = bounds.Bottom - diameterL;
            path.AddArc(arcL, 90, 90);

            path.CloseFigure();
            return path;
        }

        public static void DrawGradientBorder(Graphics g, Rectangle bounds, Color sideColor, int radius, float strokeWidth = 1f, PenAlignment alignment = PenAlignment.Center, float topLighten = 0.1f)
        {
            Color topColor = Color.FromArgb(sideColor.A,
                (int)Math.Clamp(sideColor.R + (255 - sideColor.R) * topLighten, 0, 255),
                (int)Math.Clamp(sideColor.G + (255 - sideColor.G) * topLighten, 0, 255),
                (int)Math.Clamp(sideColor.B + (255 - sideColor.B) * topLighten, 0, 255));

            float flatHeight = Math.Max(1f, strokeWidth);
            float gradHeight = (float)Math.Round(bounds.Height * GradientHeightFraction);
            float pad = strokeWidth;
            float axisStart = bounds.Y - pad;
            float axisEnd = bounds.Y + bounds.Height + pad;
            float axisLen = axisEnd - axisStart;
            if (axisLen <= 0) axisLen = 1;
            float p1 = Math.Max(0f, Math.Min(0.98f, (pad + flatHeight) / axisLen));
            float p2 = Math.Max(p1 + 0.01f, Math.Min(1f, (pad + flatHeight + gradHeight) / axisLen));

            using (GraphicsPath path = RoundedRect(bounds, radius, radius))
            using (LinearGradientBrush brush = new LinearGradientBrush(
                new PointF(0, axisStart), new PointF(0, axisEnd),
                topColor, sideColor))
            {
                brush.InterpolationColors = new ColorBlend(4)
                {
                    Colors = new[] { topColor, topColor, sideColor, sideColor },
                    Positions = new[] { 0f, p1, p2, 1f }
                };
                using (Pen pen = new Pen(brush, strokeWidth) { Alignment = alignment })
                {
                    SmoothingMode prev = g.SmoothingMode;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.DrawPath(pen, path);
                    g.SmoothingMode = prev;
                }
            }
        }

        public static Color Shift(Color from, Color target, float amount)
        {
            return Color.FromArgb(from.A,
                (int)(from.R + (target.R - from.R) * amount),
                (int)(from.G + (target.G - from.G) * amount),
                (int)(from.B + (target.B - from.B) * amount));
        }
    }
}
