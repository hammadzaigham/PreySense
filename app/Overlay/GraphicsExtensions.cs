using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace PreySense.Overlay
{
    public static class GraphicsExtensions
    {
        public static void FillRoundedRectangle(this Graphics graphics, Brush brush, Rectangle bounds, int cornerRadius)
        {
            using (var path = new GraphicsPath())
            {
                int diameter = cornerRadius * 2;
                if (diameter > bounds.Width) diameter = bounds.Width;
                if (diameter > bounds.Height) diameter = bounds.Height;
                if (diameter <= 0) diameter = 1;

                path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
                path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
                path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
                path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
                path.CloseAllFigures();

                graphics.FillPath(brush, path);
            }
        }
    }
}
