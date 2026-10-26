using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace PreySense.Frontend.Services
{
    public sealed class TrayIconManager : IDisposable
    {
        private const int WM_USER = 0x0400;
        public const int WM_TRAY_CALLBACK = WM_USER + 2048;

        private const int NIM_ADD = 0x00000000;
        private const int NIM_MODIFY = 0x00000001;
        private const int NIM_DELETE = 0x00000002;
        private const int NIM_SETVERSION = 0x00000004;

        private const int NIF_MESSAGE = 0x00000001;
        private const int NIF_ICON = 0x00000002;
        private const int NIF_TIP = 0x00000004;

        private const int NOTIFYICON_VERSION_4 = 4;

        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_LBUTTONDBLCLK = 0x0203;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_CONTEXTMENU = 0x007B;

        private const uint TPM_RETURNCMD = 0x0100;
        private const uint TPM_RIGHTBUTTON = 0x0002;
        private const uint MF_STRING = 0x00000000;
        private const uint MF_SEPARATOR = 0x00000800;
        private const uint MIIM_BITMAP = 0x00000080;
        private const uint DIB_RGB_COLORS = 0;
        private const uint DI_NORMAL = 0x0003;

        private const uint CMD_OPEN = 2001;
        private const uint CMD_QUIT = 2002;

        private const int IMAGE_ICON = 1;
        private const uint LR_LOADFROMFILE = 0x00000010;
        private const int IDI_APPLICATION = 32512;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public int uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public int dwState;
            public int dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public int uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public int dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
            public uint bmiColors;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MENUITEMINFO
        {
            public uint cbSize;
            public uint fMask;
            public uint fType;
            public uint fState;
            public uint wID;
            public IntPtr hSubMenu;
            public IntPtr hbmpChecked;
            public IntPtr hbmpUnchecked;
            public UIntPtr dwItemData;
            public IntPtr dwTypeData;
            public uint cch;
            public IntPtr hbmpItem;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern uint ExtractIconEx(string szFileName, int nIconIndex, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIcons);

        [DllImport("user32.dll")]
        private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll")]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, uint uIDNewItem, string lpNewItem);

        [DllImport("user32.dll")]
        private static extern bool SetMenuItemInfo(IntPtr hMenu, uint item, bool fByPosition, ref MENUITEMINFO lpmii);

        [DllImport("user32.dll")]
        private static extern bool DrawIconEx(IntPtr hdc, int xLeft, int yTop, IntPtr hIcon, int cxWidth, int cyWidth, uint istepIfAniCur, IntPtr hbrFlickerFreeDraw, uint diFlags);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll")]
        private static extern uint TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hWnd, IntPtr lpTPMParams);

        [DllImport("user32.dll")]
        private static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true)]
        private static extern int SetPreferredAppMode(int appMode);

        [DllImport("uxtheme.dll", EntryPoint = "#136")]
        private static extern void FlushMenuThemes();

        internal static void ApplyMenuTheme(bool isLight)
        {
            try
            {
                SetPreferredAppMode(isLight ? 3 : 2); // ForceLight / ForceDark, matching the app rather than Windows.
                FlushMenuThemes();
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        private readonly IntPtr _hWnd;
        private readonly Action _onOpen;
        private readonly Action _onQuit;
        private readonly uint _taskbarCreatedMsg;
        private NOTIFYICONDATA _nid;
        private IntPtr _hIcon = IntPtr.Zero;
        private bool _isCreated = false;
        private bool _disposed = false;

        public TrayIconManager(IntPtr hWnd, Action onOpen, Action onQuit)
        {
            _hWnd = hWnd;
            _onOpen = onOpen;
            _onQuit = onQuit;
            _taskbarCreatedMsg = RegisterWindowMessage("TaskbarCreated");

            ApplyMenuTheme(ThemeManager.IsLightTheme);

            InitTrayIcon();
        }

        private void InitTrayIcon()
        {
            _hIcon = LoadAppIcon();

            _nid = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hWnd,
                uID = 1001,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = WM_TRAY_CALLBACK,
                hIcon = _hIcon,
                szTip = "PreySense"
            };

            _isCreated = Shell_NotifyIcon(NIM_ADD, ref _nid);
            if (_isCreated)
            {
                _nid.uTimeoutOrVersion = NOTIFYICON_VERSION_4;
                Shell_NotifyIcon(NIM_SETVERSION, ref _nid);
            }
        }

        private static IntPtr LoadAppIcon()
        {
            try
            {
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appicon.ico");
                if (File.Exists(iconPath))
                {
                    IntPtr h = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);
                    if (h != IntPtr.Zero) return h;
                }
            }
            catch { }

            try
            {
                string? exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath))
                {
                    ExtractIconEx(exePath, 0, out _, out IntPtr hSmall, 1);
                    if (hSmall != IntPtr.Zero) return hSmall;
                }
            }
            catch { }

            return LoadIcon(IntPtr.Zero, (IntPtr)IDI_APPLICATION);
        }

        public IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (_disposed) return IntPtr.Zero;

            if (_taskbarCreatedMsg != 0 && msg == (int)_taskbarCreatedMsg)
            {
                Shell_NotifyIcon(NIM_ADD, ref _nid);
                _nid.uTimeoutOrVersion = NOTIFYICON_VERSION_4;
                Shell_NotifyIcon(NIM_SETVERSION, ref _nid);
                handled = true;
                return IntPtr.Zero;
            }

            if (msg == WM_TRAY_CALLBACK)
            {
                uint evt = (uint)(lParam.ToInt64() & 0xFFFF);
                if (evt == WM_LBUTTONUP || evt == WM_LBUTTONDBLCLK)
                {
                    _onOpen?.Invoke();
                    handled = true;
                }
                else if (evt == WM_RBUTTONUP || evt == WM_CONTEXTMENU)
                {
                    ShowContextMenu(hwnd);
                    handled = true;
                }
            }

            return IntPtr.Zero;
        }

        private static IntPtr CreateAlphaMenuBitmap(IntPtr icon)
        {
            const int size = 16;
            var bitmapInfo = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = size,
                    biHeight = -size,
                    biPlanes = 1,
                    biBitCount = 32
                }
            };

            IntPtr bitmap = CreateDIBSection(IntPtr.Zero, ref bitmapInfo, DIB_RGB_COLORS, out IntPtr bits, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero || bits == IntPtr.Zero) return IntPtr.Zero;

            Marshal.Copy(new byte[size * size * 4], 0, bits, size * size * 4);
            IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
            if (dc == IntPtr.Zero)
            {
                DeleteObject(bitmap);
                return IntPtr.Zero;
            }

            IntPtr previous = SelectObject(dc, bitmap);
            bool drawn = DrawIconEx(dc, 0, 0, icon, size, size, 0, IntPtr.Zero, DI_NORMAL);
            SelectObject(dc, previous);
            DeleteDC(dc);

            if (!drawn)
            {
                DeleteObject(bitmap);
                return IntPtr.Zero;
            }

            return bitmap;
        }

        private void ShowContextMenu(IntPtr hwnd)
        {
            ApplyMenuTheme(ThemeManager.IsLightTheme);
            GetCursorPos(out POINT pt);
            SetForegroundWindow(hwnd);

            IntPtr hMenu = CreatePopupMenu();
            if (hMenu == IntPtr.Zero) return;

            AppendMenu(hMenu, MF_STRING, CMD_OPEN, "PreySense");
            AppendMenu(hMenu, MF_SEPARATOR, 0, string.Empty);
            AppendMenu(hMenu, MF_STRING, CMD_QUIT, LocalizationService.Translate("Quit"));

            IntPtr menuBitmap = _hIcon != IntPtr.Zero ? CreateAlphaMenuBitmap(_hIcon) : IntPtr.Zero;
            if (menuBitmap != IntPtr.Zero)
            {
                var menuItem = new MENUITEMINFO
                {
                    cbSize = (uint)Marshal.SizeOf<MENUITEMINFO>(),
                    fMask = MIIM_BITMAP,
                    hbmpItem = menuBitmap
                };
                SetMenuItemInfo(hMenu, CMD_OPEN, false, ref menuItem);
            }

            uint cmd = TrackPopupMenuEx(hMenu, TPM_RETURNCMD | TPM_RIGHTBUTTON, pt.x, pt.y, hwnd, IntPtr.Zero);
            DestroyMenu(hMenu);
            if (menuBitmap != IntPtr.Zero) DeleteObject(menuBitmap);
            PostMessage(hwnd, 0 /* WM_NULL */, IntPtr.Zero, IntPtr.Zero);

            if (cmd == CMD_OPEN)
            {
                _onOpen?.Invoke();
            }
            else if (cmd == CMD_QUIT)
            {
                _onQuit?.Invoke();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_isCreated)
            {
                Shell_NotifyIcon(NIM_DELETE, ref _nid);
                _isCreated = false;
            }

            if (_hIcon != IntPtr.Zero)
            {
                DestroyIcon(_hIcon);
                _hIcon = IntPtr.Zero;
            }
        }
    }
}
