using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Windows.Forms;

namespace PreySense.Overlay
{
    public class OSDNativeForm : NativeWindow, IDisposable
    {
        private bool _disposed = false;
        private byte _alpha = 250;
        private Size _size = new Size(350, 50);
        private Point _location = new Point(50, 50);
        private readonly object _paintLock = new();

        protected virtual void PerformPaint(PaintEventArgs e)
        {
        }

        protected internal void Invalidate()
        {
            UpdateLayeredWindow();
        }

        private void UpdateLayeredWindow()
        {
            if (!Monitor.TryEnter(_paintLock)) return;
            try
            {
                using Bitmap bitmap1 = new Bitmap(Size.Width, Size.Height, PixelFormat.Format32bppArgb);
                using (Graphics graphics1 = Graphics.FromImage(bitmap1))
                {
                    Rectangle rectangle1 = new Rectangle(0, 0, Size.Width, Size.Height);
                    PerformPaint(new PaintEventArgs(graphics1, rectangle1));

                    IntPtr ptr1 = NativeMethods.GetDC(IntPtr.Zero);
                    IntPtr ptr2 = NativeMethods.CreateCompatibleDC(ptr1);
                    IntPtr ptr3 = bitmap1.GetHbitmap(Color.FromArgb(0));
                    IntPtr ptr4 = NativeMethods.SelectObject(ptr2, ptr3);

                    var size1 = new NativeMethods.SIZE { cx = Size.Width, cy = Size.Height };
                    var point1 = new NativeMethods.POINT { x = Location.X, y = Location.Y };
                    var point2 = new NativeMethods.POINT { x = 0, y = 0 };

                    var blendfunction1 = new NativeMethods.BLENDFUNCTION
                    {
                        BlendOp = 0,
                        BlendFlags = 0,
                        SourceConstantAlpha = _alpha,
                        AlphaFormat = 1
                    };

                    NativeMethods.UpdateLayeredWindow(Handle, ptr1, ref point1, ref size1, ptr2, ref point2, 0, ref blendfunction1, 2); // 2 = ULW_ALPHA

                    NativeMethods.SelectObject(ptr2, ptr4);
                    NativeMethods.ReleaseDC(IntPtr.Zero, ptr1);
                    NativeMethods.DeleteObject(ptr3);
                    NativeMethods.DeleteDC(ptr2);
                }
            }
            finally
            {
                Monitor.Exit(_paintLock);
            }
        }

        public virtual void Show()
        {
            if (Handle == IntPtr.Zero) // if handle is zero, create the window
                CreateWindowOnly();
            NativeMethods.ShowWindow(Handle, NativeMethods.SW_SHOWNOACTIVATE);
        }

        public virtual void Hide()
        {
            if (Handle == IntPtr.Zero)
                return;
            NativeMethods.ShowWindow(Handle, NativeMethods.SW_HIDE);
            DestroyHandle();
        }

        public virtual void Close()
        {
            Hide();
            Dispose();
        }

        private void CreateWindowOnly()
        {
            CreateParams params1 = new CreateParams();
            params1.Caption = "FloatingNativeWindow";
            int nX = _location.X;
            int nY = _location.Y;
            Screen screen1 = Screen.FromHandle(Handle);
            if (nX + _size.Width > screen1.Bounds.Width)
            {
                nX = screen1.Bounds.Width - _size.Width;
            }
            if (nY + _size.Height > screen1.Bounds.Height)
            {
                nY = screen1.Bounds.Height - _size.Height;
            }
            _location = new Point(nX, nY);
            params1.X = nX;
            params1.Y = nY;
            params1.Height = _size.Height;
            params1.Width = _size.Width;
            params1.Parent = IntPtr.Zero;

            params1.Style = unchecked((int)NativeMethods.WS_POPUP);
            params1.ExStyle = NativeMethods.WS_EX_TOPMOST | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TRANSPARENT;
            
            CreateHandle(params1);
            UpdateLayeredWindow();
        }

        protected virtual void SetBoundsCore(int x, int y, int width, int height)
        {
            if (X != x || Y != y || Width != width || Height != height)
            {
                if (Handle != IntPtr.Zero)
                {
                    int num1 = 20; // SWP_NOZORDER (4) | SWP_NOACTIVATE (16) = 20
                    if (X == x && Y == y)
                    {
                        num1 |= 2; // SWP_NOSIZE
                    }
                    if (Width == width && Height == height)
                    {
                        num1 |= 1; // SWP_NOMOVE
                    }
                    NativeMethods.SetWindowPos(Handle, IntPtr.Zero, x, y, width, height, (uint)num1);
                }
                else
                {
                    Location = new Point(x, y);
                    Size = new Size(width, height);
                }
            }
        }

        #region Properties
        public virtual Point Location
        {
            get { return _location; }
            set
            {
                if (Handle != IntPtr.Zero)
                {
                    SetBoundsCore(value.X, value.Y, _size.Width, _size.Height);
                    var rect = new NativeMethods.RECT();
                    NativeMethods.GetWindowRect(Handle, ref rect);
                    _location = new Point(rect.left, rect.top);
                    UpdateLayeredWindow();
                }
                else
                {
                    _location = value;
                }
            }
        }

        public virtual Size Size
        {
            get { return _size; }
            set
            {
                if (Handle != IntPtr.Zero)
                {
                    SetBoundsCore(_location.X, _location.Y, value.Width, value.Height);
                    var rect = new NativeMethods.RECT();
                    NativeMethods.GetWindowRect(Handle, ref rect);
                    _size = new Size(rect.right - rect.left, rect.bottom - rect.top);
                    UpdateLayeredWindow();
                }
                else
                {
                    _size = value;
                }
            }
        }

        public int Height
        {
            get { return _size.Height; }
            set
            {
                _size = new Size(_size.Width, value);
            }
        }

        public int Width
        {
            get { return _size.Width; }
            set
            {
                _size = new Size(value, _size.Height);
            }
        }

        public int X
        {
            get { return _location.X; }
            set
            {
                Location = new Point(value, Location.Y);
            }
        }

        public int Y
        {
            get { return _location.Y; }
            set
            {
                Location = new Point(Location.X, value);
            }
        }

        public Rectangle Bound
        {
            get
            {
                return new Rectangle(new Point(0, 0), _size);
            }
        }

        public byte Alpha
        {
            get { return _alpha; }
            set
            {
                if (_alpha == value) return;
                _alpha = value;
                UpdateLayeredWindow();
            }
        }
        #endregion

        #region IDisposable Members
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                DestroyHandle();
                _disposed = true;
            }
        }
        #endregion
    }
}
