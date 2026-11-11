using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;

namespace PreySense.Frontend.Services;

/// <summary>One native backdrop setup path shared by windows and dropdown HWNDs.</summary>
internal static class NativeWindowBackdrop
{
    private const int SystemBackdropType = 38, ImmersiveDarkMode = 20, CornerPreference = 33, BorderColor = 34;
    private const int NoBackdrop = 1, DesktopAcrylic = 3, RoundedCorners = 2;
    private const uint NoBorder = 0xFFFFFFFE;

    internal static Color SolidBackground => ThemeManager.IsLightTheme
        ? Color.FromRgb(245, 246, 248) : Color.FromRgb(10, 10, 10);

    internal static bool Apply(HwndSource source, bool acrylic)
    {
        if (source.IsDisposed || source.Handle == IntPtr.Zero) return false;
        IntPtr hwnd = source.Handle;
        // Remove the old material before changing the render surface or applying a new one.
        int none = NoBackdrop;
        _ = DwmSetWindowAttribute(hwnd, SystemBackdropType, ref none, sizeof(int));
        if (source.CompositionTarget is { } target)
            target.BackgroundColor = acrylic ? Colors.Transparent : SolidBackground;

        int dark = ThemeManager.IsLightTheme ? 0 : 1;
        _ = DwmSetWindowAttribute(hwnd, ImmersiveDarkMode, ref dark, sizeof(int));
        var margins = acrylic ? new Margins(-1, -1, -1, -1) : new Margins(0, 0, 0, 1);
        int frameResult = DwmExtendFrameIntoClientArea(hwnd, ref margins);
        int backdrop = acrylic ? DesktopAcrylic : NoBackdrop; // Desktop Acrylic / no system material.
        int result = DwmSetWindowAttribute(hwnd, SystemBackdropType, ref backdrop, sizeof(int));
        bool applied = !acrylic || frameResult >= 0 && result >= 0;
        if (!applied)
        {
            _ = DwmSetWindowAttribute(hwnd, SystemBackdropType, ref none, sizeof(int));
            if (source.CompositionTarget is { } failedTarget) failedTarget.BackgroundColor = SolidBackground;
        }

        int rounded = RoundedCorners;
        _ = DwmSetWindowAttribute(hwnd, CornerPreference, ref rounded, sizeof(int));
        uint noBorder = NoBorder;
        _ = DwmSetWindowAttribute(hwnd, BorderColor, ref noBorder, sizeof(uint));
        return applied;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins(int left, int right, int top, int bottom)
    {
        public int Left = left, Right = right, Top = top, Bottom = bottom;
    }
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref uint value, int size);
}
