using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Shell;

namespace PreySense.Frontend.Services;

/// <summary>
/// Enables the Windows DWM drop shadow (and Win11 rounded corners) for borderless
/// opaque windows. Replaces WPF <see cref="System.Windows.Media.Effects.DropShadowEffect"/>,
/// which cannot match system chrome when <c>AllowsTransparency</c> is true.
/// </summary>
public static class WindowSystemShadow
{
    public static readonly DependencyProperty EnableProperty = DependencyProperty.RegisterAttached(
        "Enable",
        typeof(bool),
        typeof(WindowSystemShadow),
        new PropertyMetadata(false, OnEnableChanged));

    public static void SetEnable(DependencyObject element, bool value) =>
        element.SetValue(EnableProperty, value);

    public static bool GetEnable(DependencyObject element) =>
        (bool)element.GetValue(EnableProperty);

    private static void OnEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Window window)
            return;

        if (e.NewValue is not true)
        {
            window.SourceInitialized -= Window_SourceInitialized;
            WindowChrome.SetWindowChrome(window, null!);
            return;
        }

        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
            Apply(window);
        else
            window.SourceInitialized += Window_SourceInitialized;
    }

    private static void Window_SourceInitialized(object? sender, EventArgs e)
    {
        if (sender is not Window window)
            return;
        window.SourceInitialized -= Window_SourceInitialized;
        Apply(window);
    }

    private static void Apply(Window window)
    {
        // AllowsTransparency disables DWM shadows; leave those windows alone.
        if (window.AllowsTransparency)
            return;

        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(0),
            // 1px glass frame is the usual borderless trick so DWM still draws the system shadow.
            GlassFrameThickness = new Thickness(0, 0, 0, 1),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false
        });

        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        // Win11: round the HWND like a normal app window (~8px). Ignored on older OS builds.
        int preference = DwmWcpRound;
        _ = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref preference, sizeof(int));

        // Hide the system 1px border (same look as matching border to window background).
        uint borderColor = DwmwaColorNone;
        _ = DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref borderColor, sizeof(uint));
    }

    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmWcpRound = 2;
    private const int DwmwaBorderColor = 34;
    private const uint DwmwaColorNone = 0xFFFFFFFE;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int attr, ref uint attrValue, int attrSize);
}
