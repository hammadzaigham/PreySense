using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Shell;
using System.Windows.Threading;

namespace PreySense.Frontend.Services;

/// <summary>
/// Enables the Windows DWM drop shadow (and Win11 rounded corners) for borderless
/// opaque windows. Replaces WPF <see cref="System.Windows.Media.Effects.DropShadowEffect"/>,
/// which cannot match system chrome when <c>AllowsTransparency</c> is true.
/// </summary>
public static class WindowSystemShadow
{
    private static readonly DependencyProperty PendingRefreshProperty = DependencyProperty.RegisterAttached(
        "PendingRefresh", typeof(DispatcherOperation), typeof(WindowSystemShadow));
    private static readonly DependencyProperty NativeHookProperty = DependencyProperty.RegisterAttached(
        "NativeHook", typeof(NativeHook), typeof(WindowSystemShadow));
    private sealed record NativeHook(HwndSource Source, HwndSourceHook Callback);
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
            window.Loaded -= Window_Loaded;
            window.Activated -= Window_Activated;
            window.IsVisibleChanged -= Window_IsVisibleChanged;
            window.Closed -= Window_Closed;
            CancelRefresh(window);
            DetachNativeHook(window);
            WindowChrome.SetWindowChrome(window, null!);
            return;
        }

        window.Loaded += Window_Loaded;
        window.Activated += Window_Activated;
        window.IsVisibleChanged += Window_IsVisibleChanged;
        window.Closed += Window_Closed;
        window.SourceInitialized += Window_SourceInitialized;

        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
            Apply(window);
    }

    private static void Window_SourceInitialized(object? sender, EventArgs e)
    {
        if (sender is not Window window)
            return;
        Apply(window);
    }

    public static void RefreshTheme(Window window)
    {
        if (GetEnable(window) && new WindowInteropHelper(window).Handle != IntPtr.Zero)
            Apply(window);
    }

    private static void Window_Loaded(object sender, RoutedEventArgs e) => QueueRefresh((Window)sender);
    private static void Window_Activated(object? sender, EventArgs e) => QueueRefresh((Window)sender!);
    private static void Window_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        var window = (Window)sender;
        if (e.NewValue is true) QueueRefresh(window);
        else CancelRefresh(window);
    }

    private static void Window_Closed(object? sender, EventArgs e)
    {
        var window = (Window)sender!;
        CancelRefresh(window);
        DetachNativeHook(window);
        window.SourceInitialized -= Window_SourceInitialized;
    }

    private static void DetachNativeHook(Window window)
    {
        if (window.GetValue(NativeHookProperty) is NativeHook hook && !hook.Source.IsDisposed)
            hook.Source.RemoveHook(hook.Callback);
        window.ClearValue(NativeHookProperty);
    }

    private static void AttachNativeHook(Window window, HwndSource source)
    {
        if (window.GetValue(NativeHookProperty) is NativeHook existing && existing.Source == source) return;
        DetachNativeHook(window);
        IntPtr OnMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // ShowInTaskbar uses SetWindowPos to hide/show the HWND; WPF IsVisible
            // need not change. Composition/theme/DPI changes also invalidate its frame.
            bool nativeShow = message == WmShowWindow && wParam != IntPtr.Zero
                || message == WmWindowPosChanged && lParam != IntPtr.Zero
                    && (Marshal.PtrToStructure<WindowPosition>(lParam).Flags & SwpShowWindow) != 0;
            if (nativeShow || message is WmDwmCompositionChanged or WmThemeChanged or WmDpiChanged)
                QueueRefresh(window);
            return IntPtr.Zero;
        }
        HwndSourceHook callback = OnMessage;
        source.AddHook(callback);
        window.SetValue(NativeHookProperty, new NativeHook(source, callback));
    }

    private static void CancelRefresh(Window window)
    {
        (window.GetValue(PendingRefreshProperty) as DispatcherOperation)?.Abort();
        window.ClearValue(PendingRefreshProperty);
    }

    private static void QueueRefresh(Window window)
    {
        if (!ThemeManager.IsGlassTheme || !window.IsVisible || window.AllowsTransparency
            || window.GetValue(PendingRefreshProperty) is DispatcherOperation { Status: DispatcherOperationStatus.Pending }) return;
        // WPF and taskbar/owner changes can update the native frame during Show. Renew
        // the material after that work, once per activation/show burst, without a timer.
        window.SetValue(PendingRefreshProperty, window.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            window.ClearValue(PendingRefreshProperty);
            if (GetEnable(window) && window.IsVisible && ThemeManager.IsGlassTheme)
                Apply(window, renewBackdrop: true);
        })));
    }

    private static void Apply(Window window, bool renewBackdrop = false)
    {
        // AllowsTransparency disables DWM shadows; leave those windows alone.
        if (window.AllowsTransparency)
            return;

        bool glass = ThemeManager.IsGlassTheme;
        var chrome = WindowChrome.GetWindowChrome(window);
        if (chrome is null)
        {
            chrome = new WindowChrome
            {
                CaptionHeight = 0, ResizeBorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false
            };
            WindowChrome.SetWindowChrome(window, chrome);
        }
        chrome.GlassFrameThickness = glass ? new Thickness(-1) : new Thickness(0, 0, 0, 1);

        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        if (HwndSource.FromHwnd(hwnd) is { } source)
            AttachNativeHook(window, source);

        int backdrop = glass ? DwmSbtTransientWindow : DwmSbtNone; // Desktop Acrylic / none.
        if (glass && renewBackdrop)
        {
            int none = DwmSbtNone;
            _ = DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref none, sizeof(int));
        }
        int backdropResult = DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref backdrop, sizeof(int));
        if (glass)
        {
            var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            int frameResult = DwmExtendFrameIntoClientArea(hwnd, ref margins);
            if (frameResult < 0) backdropResult = frameResult;
        }
        int darkMode = ThemeManager.IsLightTheme ? 0 : 1;
        _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));
        if (glass && backdropResult < 0)
        {
            // A failed backdrop must leave a readable solid surface, never an empty transparent HWND.
            window.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(28, 28, 28));
        }
        else
            window.ClearValue(Window.BackgroundProperty);
        if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target)
            target.BackgroundColor = glass && backdropResult >= 0 ? System.Windows.Media.Colors.Transparent
                : ThemeManager.IsLightTheme ? System.Windows.Media.Color.FromRgb(245, 246, 248)
                : System.Windows.Media.Color.FromRgb(28, 28, 28);

        // Win11: round the HWND like a normal app window (~8px). Ignored on older OS builds.
        int preference = DwmWcpRound;
        _ = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref preference, sizeof(int));

        // Hide the system 1px border (same look as matching border to window background).
        uint borderColor = DwmwaColorNone;
        _ = DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref borderColor, sizeof(uint));
    }

    private const int DwmwaSystemBackdropType = 38;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmSbtTransientWindow = 3;
    private const int DwmSbtNone = 1;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmWcpRound = 2;
    private const int DwmwaBorderColor = 34;
    private const uint DwmwaColorNone = 0xFFFFFFFE;
    private const int WmShowWindow = 0x0018;
    private const int WmWindowPosChanged = 0x0047;
    private const int WmDwmCompositionChanged = 0x031E;
    private const int WmThemeChanged = 0x031A;
    private const int WmDpiChanged = 0x02E0;
    private const uint SwpShowWindow = 0x0040;

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPosition
    {
        public IntPtr Hwnd, InsertAfter;
        public int X, Y, Width, Height;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left, Right, Top, Bottom; }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int attr, ref uint attrValue, int attrSize);
}
