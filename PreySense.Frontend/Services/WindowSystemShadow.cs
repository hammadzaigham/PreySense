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
            window.IsVisibleChanged -= Window_IsVisibleChanged;
            window.Closed -= Window_Closed;
            CancelRefresh(window);
            DetachNativeHook(window);
            WindowChrome.SetWindowChrome(window, null!);
            return;
        }

        window.Loaded += Window_Loaded;
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
        if (!GetEnable(window) || new WindowInteropHelper(window).Handle == IntPtr.Zero) return;
        // A visible theme change applies once, after WPF has updated its themed surface.
        if (ThemeManager.IsGlassTheme && window.IsVisible) QueueRefresh(window);
        else { CancelRefresh(window); Apply(window); }
    }

    public static void RefreshAfterRestore(Window window) => QueueRefresh(window);

    private static void Window_Loaded(object sender, RoutedEventArgs e) => QueueRefresh((Window)sender);
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
        if (!GetEnable(window) || !ThemeManager.IsGlassTheme || !window.IsVisible || window.AllowsTransparency
            || window.GetValue(PendingRefreshProperty) is DispatcherOperation { Status: DispatcherOperationStatus.Pending }) return;
        // WPF and taskbar/owner changes can update the native frame during Show. Renew
        // the material after that work, once per activation/show burst, without a timer.
        window.SetValue(PendingRefreshProperty, window.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            window.ClearValue(PendingRefreshProperty);
            if (GetEnable(window) && window.IsVisible && ThemeManager.IsGlassTheme)
                Apply(window);
        })));
    }

    private static void Apply(Window window)
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
        if (HwndSource.FromHwnd(hwnd) is not { } source) return;
        AttachNativeHook(window, source);
        // SetCurrentValue preserves resource expressions while removing any opaque local fill.
        if (glass) window.SetCurrentValue(Window.BackgroundProperty, System.Windows.Media.Brushes.Transparent);
        else window.ClearValue(Window.BackgroundProperty);
        if (!NativeWindowBackdrop.Apply(source, glass))
            window.SetCurrentValue(Window.BackgroundProperty, new System.Windows.Media.SolidColorBrush(NativeWindowBackdrop.SolidBackground));
    }

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

}
