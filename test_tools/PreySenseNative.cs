using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace PreySense.TestTools
{
    public class WindowInfo
    {
        public IntPtr HWnd { get; set; }
        public uint ProcessId { get; set; }
        public string Title { get; set; }
        public string ClassName { get; set; }
        public int Left { get; set; }
        public int Top { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool IsVisible { get; set; }

        public WindowInfo()
        {
            Title = string.Empty;
            ClassName = string.Empty;
        }
    }

    public static class WindowHelper
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr OpenDesktop(string lpszDesktop, uint dwFlags, bool fInherit, uint dwDesiredAccess);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool EnumDesktopWindows(IntPtr hDesktop, EnumWindowsProc lpfn, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

        [DllImport("gdi32.dll")]
        public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        public static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int w, int h, IntPtr hdcSrc, int xSrc, int ySrc, int rop);

        [DllImport("gdi32.dll")]
        private static extern int GetPixel(IntPtr hdc, int x, int y);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

        private const int DwmwaExtendedFrameBounds = 9;
        private const int SrcCopy = 0x00CC0020;
        // Legacy WPF WindowShadowEffect fringe; app windows now use DWM system shadow (no WPF effect).
        private const int ShadowLumaThreshold = 8;

        /// <summary>Drop-shadow insets around the solid window chrome.</summary>
        public struct ShadowInsets
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public int Max
            {
                get { return Math.Max(Math.Max(Left, Right), Math.Max(Top, Bottom)); }
            }

            public bool HasShadow
            {
                get { return Left > 0 || Top > 0 || Right > 0 || Bottom > 0; }
            }
        }

        public static bool TryGetDwmShadowInsets(IntPtr hWnd, out ShadowInsets insets)
        {
            insets = new ShadowInsets();
            RECT window;
            if (!GetWindowRect(hWnd, out window)) return false;
            RECT frame;
            int hr = DwmGetWindowAttribute(hWnd, DwmwaExtendedFrameBounds, out frame, Marshal.SizeOf(typeof(RECT)));
            if (hr != 0) return false;
            insets.Left = Math.Max(0, frame.Left - window.Left);
            insets.Top = Math.Max(0, frame.Top - window.Top);
            insets.Right = Math.Max(0, window.Right - frame.Right);
            insets.Bottom = Math.Max(0, window.Bottom - frame.Bottom);
            return true;
        }

        /// <summary>Kept for callers; prefers DWM, else reports empty (bitmap scan happens during capture).</summary>
        public static bool TryGetShadowInsets(IntPtr hWnd, out ShadowInsets insets)
        {
            return TryGetDwmShadowInsets(hWnd, out insets);
        }

        private static bool IsShadowPixel(int colorRef)
        {
            int r = colorRef & 0xFF;
            int g = (colorRef >> 8) & 0xFF;
            int b = (colorRef >> 16) & 0xFF;
            return r <= ShadowLumaThreshold && g <= ShadowLumaThreshold && b <= ShadowLumaThreshold;
        }

        private static bool IsUniformWindowShadow(ShadowInsets insets)
        {
            if (!insets.HasShadow) return false;
            int min = Math.Min(Math.Min(insets.Left, insets.Right), Math.Min(insets.Top, insets.Bottom));
            int max = insets.Max;
            return min >= 6 && max <= 16 && (max - min) <= 2;
        }

        /// <summary>
        /// Measure WPF DropShadowEffect fringe after PrintWindow (opaque near-black padding).
        /// On this app the measured crop is typically 10px on each side.
        /// </summary>
        public static ShadowInsets MeasureBitmapShadowInsets(IntPtr hdc, int width, int height)
        {
            ShadowInsets insets = new ShadowInsets();
            if (width < 4 || height < 4) return insets;

            int midX = width / 2;
            int midY = height / 2;
            int maxScan = Math.Min(64, Math.Min(width, height) / 2);

            for (int x = 0; x < maxScan; x++)
            {
                if (!IsShadowPixel(GetPixel(hdc, x, midY))) { insets.Left = x; break; }
                if (x == maxScan - 1) insets.Left = maxScan;
            }
            for (int x = 0; x < maxScan; x++)
            {
                if (!IsShadowPixel(GetPixel(hdc, width - 1 - x, midY))) { insets.Right = x; break; }
                if (x == maxScan - 1) insets.Right = maxScan;
            }
            for (int y = 0; y < maxScan; y++)
            {
                if (!IsShadowPixel(GetPixel(hdc, midX, y))) { insets.Top = y; break; }
                if (y == maxScan - 1) insets.Top = maxScan;
            }
            for (int y = 0; y < maxScan; y++)
            {
                if (!IsShadowPixel(GetPixel(hdc, midX, height - 1 - y))) { insets.Bottom = y; break; }
                if (y == maxScan - 1) insets.Bottom = maxScan;
            }
            return insets;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct STARTUPINFO
        {
            public int cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public int dwX;
            public int dwY;
            public int dwXSize;
            public int dwYSize;
            public int dwXCountChars;
            public int dwYCountChars;
            public int dwFillAttribute;
            public int dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool CreateProcess(
            string lpApplicationName,
            string lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            bool bInheritHandles,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string lpCurrentDirectory,
            ref STARTUPINFO lpStartupInfo,
            out PROCESS_INFORMATION lpProcessInformation
        );

        [DllImport("kernel32.dll")]
        public static extern bool CloseHandle(IntPtr hObject);

        public const uint DESKTOP_READOBJECTS = 0x0001;
        public const uint DESKTOP_WRITEOBJECTS = 0x0080;
        public const uint DESKTOP_ALL_ACCESS = 0x01FF;
        public const int SW_RESTORE = 9;
        public const int SW_SHOW = 5;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        public static int LaunchOnInteractiveDesktop(string exePath, string args)
        {
            var si = new STARTUPINFO();
            si.cb = Marshal.SizeOf(si);
            si.lpDesktop = @"WinSta0\Default";
            var pi = new PROCESS_INFORMATION();
            string cmd = "\"" + exePath + "\" " + (args ?? string.Empty);
            bool ok = CreateProcess(null, cmd, IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero, null, ref si, out pi);
            if (!ok)
            {
                int err = Marshal.GetLastWin32Error();
                throw new System.ComponentModel.Win32Exception(err);
            }
            if (pi.hThread != IntPtr.Zero) CloseHandle(pi.hThread);
            if (pi.hProcess != IntPtr.Zero) CloseHandle(pi.hProcess);
            return pi.dwProcessId;
        }

        public static List<WindowInfo> FindWindows(uint pid, string titleFilter)
        {
            var results = new List<WindowInfo>();
            var seen = new HashSet<IntPtr>();

            EnumWindowsProc callback = delegate(IntPtr hWnd, IntPtr lParam)
            {
                if (seen.Contains(hWnd)) return true;
                seen.Add(hWnd);

                uint p;
                GetWindowThreadProcessId(hWnd, out p);
                if (p == pid)
                {
                    var sbTitle = new StringBuilder(256);
                    GetWindowText(hWnd, sbTitle, 256);
                    string title = sbTitle.ToString();

                    var sbClass = new StringBuilder(256);
                    GetClassName(hWnd, sbClass, 256);
                    string cls = sbClass.ToString();

                    // Skip internal system helper windows
                    if (cls.IndexOf("Cicero", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        cls.IndexOf("IME", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        cls.IndexOf("Notify", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        cls.IndexOf("CallBackWindowThread", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        title.IndexOf("CallBackWindowThread", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        title.IndexOf("MediaContext", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }

                    RECT r;
                    GetWindowRect(hWnd, out r);
                    int w = Math.Max(0, r.Right - r.Left);
                    int h = Math.Max(0, r.Bottom - r.Top);

                    if (w > 50 && h > 50)
                    {
                        if (string.IsNullOrEmpty(titleFilter) ||
                            titleFilter == "*" ||
                            title.IndexOf(titleFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            results.Add(new WindowInfo
                            {
                                HWnd = hWnd,
                                ProcessId = p,
                                Title = title,
                                ClassName = cls,
                                Left = r.Left,
                                Top = r.Top,
                                Width = w,
                                Height = h,
                                IsVisible = IsWindowVisible(hWnd)
                            });
                        }
                    }
                }
                return true;
            };

            // 1. Try enumerating interactive desktop "Default"
            IntPtr hDesk = OpenDesktop("Default", 0, false, DESKTOP_ALL_ACCESS);
            if (hDesk != IntPtr.Zero)
            {
                EnumDesktopWindows(hDesk, callback, IntPtr.Zero);
            }

            // 2. Also enumerate current thread desktop
            EnumWindows(callback, IntPtr.Zero);

            return results;
        }

        public static List<WindowInfo> FindPreySenseWindows(string titleFilter)
        {
            var results = new List<WindowInfo>();
            var procs = Process.GetProcessesByName("PreySense");
            for (int i = 0; i < procs.Length; i++)
            {
                results.AddRange(FindWindows((uint)procs[i].Id, titleFilter));
            }
            return results;
        }

        public static WindowInfo GetWindowInfo(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return null;
            var all = FindPreySenseWindows("*");
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].HWnd == hWnd) return all[i];
            }
            return null;
        }

        public static bool FocusWindow(IntPtr hWnd)
        {
            ShowWindow(hWnd, SW_RESTORE);
            return SetForegroundWindow(hWnd);
        }

        public static IntPtr CaptureHBitmap(IntPtr hWnd, out int width, out int height)
        {
            ShadowInsets unused;
            return CaptureHBitmap(hWnd, out width, out height, out unused);
        }

        /// <summary>
        /// Captures with PrintWindow, then crops drop shadow. Prefers DWM frame bounds;
        /// for AllowsTransparency WPF windows (PreySense) DWM is empty, so the black
        /// DropShadowEffect fringe is measured from the bitmap (typically 10px/side).
        /// </summary>
        public static IntPtr CaptureHBitmap(IntPtr hWnd, out int width, out int height, out ShadowInsets shadow)
        {
            width = 0;
            height = 0;
            shadow = new ShadowInsets();

            RECT window;
            if (!GetWindowRect(hWnd, out window)) return IntPtr.Zero;
            int fullW = window.Right - window.Left;
            int fullH = window.Bottom - window.Top;
            if (fullW <= 0 || fullH <= 0) return IntPtr.Zero;

            IntPtr hdcScreen = GetDC(IntPtr.Zero);
            IntPtr hdcFull = CreateCompatibleDC(hdcScreen);
            IntPtr hFull = CreateCompatibleBitmap(hdcScreen, fullW, fullH);
            IntPtr hOldFull = SelectObject(hdcFull, hFull);

            PrintWindow(hWnd, hdcFull, 2); // PW_RENDERFULLCONTENT

            TryGetDwmShadowInsets(hWnd, out shadow);
            if (!shadow.HasShadow)
            {
                ShadowInsets measured = MeasureBitmapShadowInsets(hdcFull, fullW, fullH);
                // WPF WindowShadowEffect flattens to a uniform ~10px black fringe.
                // Skip irregular crops (e.g. dark translucent overlay HUDs).
                if (IsUniformWindowShadow(measured))
                    shadow = measured;
            }

            int cropL = shadow.Left;
            int cropT = shadow.Top;
            int cropW = Math.Max(1, fullW - shadow.Left - shadow.Right);
            int cropH = Math.Max(1, fullH - shadow.Top - shadow.Bottom);

            IntPtr hdcCrop = CreateCompatibleDC(hdcScreen);
            IntPtr hCrop = CreateCompatibleBitmap(hdcScreen, cropW, cropH);
            IntPtr hOldCrop = SelectObject(hdcCrop, hCrop);
            BitBlt(hdcCrop, 0, 0, cropW, cropH, hdcFull, cropL, cropT, SrcCopy);

            SelectObject(hdcCrop, hOldCrop);
            SelectObject(hdcFull, hOldFull);
            DeleteDC(hdcCrop);
            DeleteDC(hdcFull);
            DeleteObject(hFull);
            ReleaseDC(IntPtr.Zero, hdcScreen);

            width = cropW;
            height = cropH;
            return hCrop;
        }
    }
}
