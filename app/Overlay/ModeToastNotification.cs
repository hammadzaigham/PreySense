using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
namespace PreySense.Overlay
{
    internal sealed class ModeToastNotification : OSDNativeForm
    {
        private readonly System.Windows.Forms.Timer _hideTimer = new();
        private readonly System.Windows.Forms.Timer _fadeTimer = new();
        private string _message = string.Empty;
        private Color _accent = Color.FromArgb(58, 174, 239);
        private Image? _icon;
        private Font? _bodyFont;
        private Font? _captionFont;
        private bool _disposed;
        private DateTime _fadeStartedAt;
        private const int FadeOutDurationMs = 140;

        public ModeToastNotification()
        {
            Alpha = 255;
            _hideTimer.Interval = 1000;
            _hideTimer.Tick += (_, _) =>
            {
                _hideTimer.Stop();
                StartFadeOut();
            };
            _fadeTimer.Interval = 15;
            _fadeTimer.Tick += (_, _) => AdvanceFadeOut();
        }

        public void ShowMode(string message, Color accent, Image? icon)
        {
            if (_disposed) return;

            _message = message;
            _accent = accent;
            _icon = icon;

            Screen screen = Screen.FromPoint(Cursor.Position);
            const int width = 208;
            const int height = 62;
            const int bottomMargin = 64;
            Size = new Size(width, height);
            Location = new Point(
                screen.WorkingArea.Left + (screen.WorkingArea.Width - width) / 2,
                screen.WorkingArea.Bottom - height - bottomMargin);
            Alpha = 255;
            _fadeTimer.Stop();

            Show();
            Invalidate();
            _hideTimer.Stop();
            _hideTimer.Start();
        }

        protected override void PerformPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            _captionFont ??= new Font("Segoe UI", 7.5f, FontStyle.Bold, GraphicsUnit.Point);
            _bodyFont ??= new Font("Segoe UI", 9.5f, FontStyle.Bold, GraphicsUnit.Point);

            Rectangle bounds = Bound;
            using var shadowBrush = new SolidBrush(Color.FromArgb(70, 0, 0, 0));
            using var bgBrush = new SolidBrush(Color.FromArgb(235, 20, 20, 20));
            using var textBrush = new SolidBrush(Color.FromArgb(245, 245, 245));
            using var captionBrush = new SolidBrush(Color.FromArgb(225, _accent));
            using var borderPen = new Pen(_accent, 4f);
            const int cornerRadius = 6;

            var shadowBounds = new Rectangle(bounds.X, bounds.Y + 3, bounds.Width, bounds.Height - 1);
            g.FillRoundedRectangle(shadowBrush, shadowBounds, cornerRadius);
            g.FillRoundedRectangle(bgBrush, bounds, cornerRadius);
            g.DrawRoundedRectangle(borderPen, new Rectangle(bounds.X + 1, bounds.Y + 1, bounds.Width - 3, bounds.Height - 3), cornerRadius);

            const int iconSize = 18;
            const int gap = 9;
            const int captionGap = 1;
            SizeF textSize = g.MeasureString(_message, _bodyFont);
            SizeF captionSize = g.MeasureString("MODE", _captionFont);
            int contentWidth = (_icon != null ? iconSize + gap : 0) + (int)Math.Ceiling(textSize.Width);
            int startX = (bounds.Width - contentWidth) / 2;
            int centerY = bounds.Height / 2;
            int totalTextHeight = (int)Math.Ceiling(captionSize.Height + captionGap + textSize.Height);
            int contentTop = centerY - totalTextHeight / 2;

            if (_icon != null)
            {
                int iconY = centerY - iconSize / 2 + 1;
                g.DrawImage(_icon, new Rectangle(startX, iconY, iconSize, iconSize));
                startX += iconSize + gap;
            }

            float captionX = startX + ((float)Math.Ceiling(textSize.Width) - captionSize.Width) / 2f;
            g.DrawString("MODE", _captionFont, captionBrush, new PointF(captionX, contentTop));
            g.DrawString(_message, _bodyFont, textBrush, new PointF(startX, contentTop + captionSize.Height + captionGap));
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                _hideTimer.Stop();
                _hideTimer.Dispose();
                _fadeTimer.Stop();
                _fadeTimer.Dispose();
                _captionFont?.Dispose();
                _bodyFont?.Dispose();
            }

            _disposed = true;
            base.Dispose(disposing);
        }

        public void Dismiss()
        {
            if (_disposed) return;
            _hideTimer.Stop();
            _fadeTimer.Stop();
            Alpha = 255;
            Hide();
        }

        private void StartFadeOut()
        {
            _fadeStartedAt = DateTime.UtcNow;
            _fadeTimer.Stop();
            _fadeTimer.Start();
        }

        private void AdvanceFadeOut()
        {
            double progress = Math.Clamp((DateTime.UtcNow - _fadeStartedAt).TotalMilliseconds / FadeOutDurationMs, 0d, 1d);
            double eased = 1d - Math.Pow(1d - progress, 2d);
            Alpha = (byte)Math.Round(255 * (1d - eased));

            if (progress < 1d) return;

            _fadeTimer.Stop();
            Alpha = 255;
            Hide();
        }
    }

    internal static class ModeToastGraphicsExtensions
    {
        public static void DrawRoundedRectangle(this Graphics graphics, Pen pen, Rectangle bounds, int cornerRadius)
        {
            using var path = new GraphicsPath();
            int diameter = cornerRadius * 2;
            if (diameter > bounds.Width) diameter = bounds.Width;
            if (diameter > bounds.Height) diameter = bounds.Height;
            if (diameter <= 0) diameter = 1;

            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseAllFigures();
            graphics.DrawPath(pen, path);
        }
    }
}
