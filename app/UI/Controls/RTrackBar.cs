using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace PreySense.UI
{
    public class RTrackBar : TrackBar
    {
        private const int WM_PAINT = 0x000F;

        private const int TBM_GETCHANNELRECT = 0x041A;
        private const int TBM_GETTHUMBRECT = 0x0419;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        public RTrackBar()
        {
            DoubleBuffered = true;
            BackColor = RForm.formBack;
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Announce($"{AccessibleName}, slider, {Value}");
        }

        protected override void OnValueChanged(EventArgs e)
        {
            base.OnValueChanged(e);
            if (Focused) Announce(Value.ToString());
        }

        private void Announce(string text)
        {
            try
            {
                AccessibilityObject.RaiseAutomationNotification(
                    System.Windows.Forms.Automation.AutomationNotificationKind.Other,
                    System.Windows.Forms.Automation.AutomationNotificationProcessing.MostRecent,
                    text);
            }
            catch { }
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            if (m.Msg == WM_PAINT)
            {
                PaintChannel();
            }
        }

        private void PaintChannel()
        {
            RECT channelRect = new();
            SendMessage(Handle, TBM_GETCHANNELRECT, 0, ref channelRect);

            RECT thumbRect = new();
            SendMessage(Handle, TBM_GETTHUMBRECT, 0, ref thumbRect);

            int channelHeight = channelRect.Bottom - channelRect.Top;
            if (channelHeight <= 0 || channelRect.Right <= channelRect.Left)
                return;

            using var g = Graphics.FromHwnd(Handle);
            using var emptyPen = new Pen(RForm.chartGrid, Math.Max(1, channelHeight))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            using var accentPen = new Pen(RForm.colorStandard, Math.Max(1, channelHeight))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };

            float midY = channelRect.Top + channelHeight / 2f;
            float left = channelRect.Left;
            float right = channelRect.Right;
            float thumbCenter = (thumbRect.Left + thumbRect.Right) / 2f;

            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Draw the full inactive track first, then overlay the active segment.
            g.DrawLine(emptyPen, left, midY, right, midY);

            if (thumbCenter > left)
            {
                g.DrawLine(accentPen, left, midY, Math.Min(thumbCenter, right), midY);
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, ref RECT lParam);
    }
}
