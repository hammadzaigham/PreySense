using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows.Forms;
using PreySense.Helpers;

namespace PreySense.UI
{
    [SupportedOSPlatform("windows")]
    public class PredatorDropDown : ComboBox
    {
        private const int TextLeftPad = 1;
        public bool UseCustomTextPadding = true;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool NativeHeight { get; set; }

        private bool isHovered = false;

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            isHovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            isHovered = false;
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        private Color borderColor = UiTheme.IsLightTheme() ? Color.FromArgb(200, 200, 200) : Color.FromArgb(55, 55, 55);
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color BorderColor
        {
            get => borderColor;
            set { borderColor = value; Invalidate(); }
        }

        private Color buttonColor = UiTheme.IsLightTheme() ? Color.FromArgb(240, 240, 240) : Color.FromArgb(46, 46, 46);
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color ButtonColor
        {
            get => buttonColor;
            set { buttonColor = value; Invalidate(); }
        }

        private Color arrowColor = UiTheme.IsLightTheme() ? Color.FromArgb(20, 20, 20) : Color.FromArgb(240, 240, 240);
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color ArrowColor
        {
            get => arrowColor;
            set { arrowColor = value; Invalidate(); }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new string SelectedText
        {
            get => SelectedIndex >= 0 && SelectedIndex < Items.Count ? Items[SelectedIndex]?.ToString() ?? "" : "";
        }

        public PredatorDropDown()
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            FlatStyle = FlatStyle.Flat;
            DrawMode = UseCustomTextPadding ? DrawMode.OwnerDrawFixed : DrawMode.Normal;
            
            bool light = UiTheme.IsLightTheme();
            BackColor = light ? Color.FromArgb(255, 255, 255) : Color.FromArgb(46, 46, 46);
            ForeColor = light ? Color.FromArgb(20, 20, 20) : Color.FromArgb(240, 240, 240);
            Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            var args = (HandledMouseEventArgs)e;
            if (!DroppedDown)
                args.Handled = true;
            else
                base.OnMouseWheel(e);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            CalibrateItemHeight();
        }

        protected override void OnDpiChangedAfterParent(EventArgs e)
        {
            base.OnDpiChangedAfterParent(e);
            CalibrateItemHeight();
        }

        private void CalibrateItemHeight()
        {
            if (DrawMode != DrawMode.OwnerDrawFixed || NativeHeight) return;
            int chrome = PreferredHeight - ItemHeight;
            int target = (int)Math.Round(44 * (DeviceDpi / 192f));
            ItemHeight = Math.Max(1, target - chrome);
        }

        protected override void OnDropDown(EventArgs e)
        {
            base.OnDropDown(e);
            AdjustDropDownWidth();
        }

        private void AdjustDropDownWidth()
        {
            int maxWidth = 0;
            using (var g = CreateGraphics())
            {
                foreach (var item in Items)
                {
                    string text = item.ToString() ?? "";
                    int width = (int)g.MeasureString(text, Font).Width;
                    if (width > maxWidth)
                        maxWidth = width;
                }
            }
            int padding = (int)Math.Round(32 * (DeviceDpi / 96f)); // padding for arrow + scrollbar
            DropDownWidth = Math.Max(Width, maxWidth + padding);
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (!UseCustomTextPadding || e.Index < 0)
            {
                base.OnDrawItem(e);
                return;
            }

            bool selected = (e.State & DrawItemState.Selected) != 0;
            Color bg = selected ? UiTheme.Accent : BackColor;
            Color fg = selected ? Color.White : ForeColor;

            using (var bgBrush = new SolidBrush(bg))
                e.Graphics.FillRectangle(bgBrush, e.Bounds);

            int pad = (int)Math.Round(TextLeftPad * e.Graphics.DpiX / 96f);
            var textRect = new Rectangle(e.Bounds.X + pad, e.Bounds.Y,
                e.Bounds.Width - pad, e.Bounds.Height);

            TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), e.Font, textRect, fg,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        public static GraphicsPath RoundedRect(Rectangle bounds, int radiusL, int radiusR)
        {
            int diameterL = radiusL * 2;
            int diameterR = radiusR * 2;

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

        public static void DrawRoundedRectangle(Graphics graphics, Pen pen, Rectangle bounds, int cornerRadiusL = 5, int cornerRadiusR = 5)
        {
            using (GraphicsPath path = RoundedRect(bounds, cornerRadiusL, cornerRadiusR))
            {
                graphics.DrawPath(pen, path);
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_PAINT && DropDownStyle != ComboBoxStyle.Simple)
            {
                var clientRect = ClientRectangle;
                var dropDownButtonWidth = SystemInformation.HorizontalScrollBarArrowWidth;
                var outerBorder = new Rectangle(clientRect.Location,
                    new Size(clientRect.Width - 1, clientRect.Height - 1));
                var innerBorder = new Rectangle(outerBorder.X + 1, outerBorder.Y + 1,
                    outerBorder.Width - dropDownButtonWidth - 2, outerBorder.Height - 2);
                var innerInnerBorder = new Rectangle(innerBorder.X + 1, innerBorder.Y + 1,
                    innerBorder.Width - 2, innerBorder.Height - 2);
                var dropDownRect = new Rectangle(innerBorder.Right + 1, innerBorder.Y,
                    dropDownButtonWidth, innerBorder.Height + 1);

                if (RightToLeft == RightToLeft.Yes)
                {
                    innerBorder.X = clientRect.Width - innerBorder.Right;
                    innerInnerBorder.X = clientRect.Width - innerInnerBorder.Right;
                    dropDownRect.X = clientRect.Width - dropDownRect.Right;
                    dropDownRect.Width += 1;
                }

                var innerBorderColor = BackColor;
                var outerBorderColor = Focused ? UiTheme.Accent : (isHovered ? (UiTheme.IsLightTheme() ? Color.FromArgb(160, 160, 160) : Color.FromArgb(90, 90, 90)) : BorderColor);
                var middle = new Point(dropDownRect.Left + dropDownRect.Width / 2,
                    dropDownRect.Top + dropDownRect.Height / 2);
                
                var arrow = new Point[]
                {
                    new Point(middle.X - 3, middle.Y - 2),
                    new Point(middle.X + 4, middle.Y - 2),
                    new Point(middle.X, middle.Y + 2)
                };

                var ps = new PAINTSTRUCT();
                bool shoulEndPaint = false;
                nint dc;
                if (m.WParam == nint.Zero)
                {
                    dc = BeginPaint(Handle, ref ps);
                    m.WParam = dc;
                    shoulEndPaint = true;
                }
                else
                {
                    dc = m.WParam;
                }

                var rgn = CreateRectRgn(innerInnerBorder.Left, innerInnerBorder.Top,
                    innerInnerBorder.Right, innerInnerBorder.Bottom);

                SelectClipRgn(dc, rgn);
                DefWndProc(ref m);
                DeleteObject(rgn);

                rgn = CreateRectRgn(clientRect.Left, clientRect.Top,
                    clientRect.Right, clientRect.Bottom);
                SelectClipRgn(dc, rgn);

                float ratio = DeviceDpi / 192f;
                int innerRadiusL = Math.Max(1, (int)Math.Round(3 * ratio));
                int innerRadiusR = Math.Max(1, (int)Math.Round(1 * ratio));
                int outerRadius = Math.Max(1, (int)Math.Round(4 * ratio));

                using (var g = Graphics.FromHdc(dc))
                {
                    using (var b = new SolidBrush(buttonColor))
                    {
                        g.FillRectangle(b, dropDownRect);
                    }
                    using (var b = new SolidBrush(arrowColor))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.FillPolygon(b, arrow);
                    }
                    using (var p = new Pen(innerBorderColor, 2))
                    {
                        DrawRoundedRectangle(g, p, innerBorder, innerRadiusL, innerRadiusR);
                    }
                    if (DropDownStyle == ComboBoxStyle.DropDown)
                    {
                        using (var b = new SolidBrush(innerBorderColor))
                            g.FillRectangle(b, innerInnerBorder);
                    }

                    ControlHelper.DrawGradientBorder(g, outerBorder, outerBorderColor, outerRadius);
                }

                if (shoulEndPaint)
                    EndPaint(Handle, ref ps);
                DeleteObject(rgn);
            }
            else
            {
                base.WndProc(ref m);
            }
        }

        private const int WM_PAINT = 0xF;

        [StructLayout(LayoutKind.Sequential)]
        public struct PAINTSTRUCT
        {
            public nint hdc;
            public bool fErase;
            public int rcPaint_left;
            public int rcPaint_top;
            public int rcPaint_right;
            public int rcPaint_bottom;
            public bool fRestore;
            public bool fIncUpdate;
            public int reserved1;
            public int reserved2;
            public int reserved3;
            public int reserved4;
            public int reserved5;
            public int reserved6;
            public int reserved7;
            public int reserved8;
        }

        [DllImport("user32.dll")]
        private static extern nint BeginPaint(nint hWnd, [In, Out] ref PAINTSTRUCT lpPaint);

        [DllImport("user32.dll")]
        private static extern bool EndPaint(nint hWnd, ref PAINTSTRUCT lpPaint);

        [DllImport("gdi32.dll")]
        public static extern int SelectClipRgn(nint hDC, nint hRgn);

        [DllImport("gdi32.dll")]
        internal static extern bool DeleteObject(nint hObject);

        [DllImport("gdi32.dll")]
        private static extern nint CreateRectRgn(int x1, int y1, int x2, int y2);
    }
}
