using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace PreySense.Frontend.Services;

/// <summary>
/// Applies Win11 Desktop Acrylic (blurred translucency) to a <see cref="Popup"/> HWND
/// under the Glass theme. Requires <see cref="Popup.AllowsTransparency"/> false so DWM
/// can own the backdrop — the opposite of layered WPF popups.
/// </summary>
public static class PopupGlassBackdrop
{
    public static readonly DependencyProperty EnableProperty = DependencyProperty.RegisterAttached(
        "Enable",
        typeof(bool),
        typeof(PopupGlassBackdrop),
        new PropertyMetadata(false, OnEnableChanged));

    private static readonly DependencyProperty ShadowEffectProperty = DependencyProperty.RegisterAttached(
        "ShadowEffect", typeof(Effect), typeof(PopupGlassBackdrop));

    public static void SetEnable(DependencyObject element, bool value) =>
        element.SetValue(EnableProperty, value);

    public static bool GetEnable(DependencyObject element) =>
        (bool)element.GetValue(EnableProperty);

    private static void OnEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Popup popup)
            return;

        if (e.NewValue is true)
        {
            popup.Opened += Popup_Opened;
            popup.Closed += Popup_Closed;
            // Child may not exist yet when the attached property is set from the template.
            popup.Dispatcher.BeginInvoke(
                () => ConfigureForTheme(popup),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
        else
        {
            popup.Opened -= Popup_Opened;
            popup.Closed -= Popup_Closed;
        }
    }

    private static void Popup_Opened(object? sender, EventArgs e)
    {
        if (sender is not Popup popup)
            return;

        SyncHostEffect(popup);
        if (!ThemeManager.IsGlassTheme || popup.AllowsTransparency)
            return;

        // HWND exists after Opened; apply on the next layout pass.
        popup.Dispatcher.BeginInvoke(() => ApplyAcrylic(popup), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static void Popup_Closed(object? sender, EventArgs e)
    {
        if (sender is Popup popup)
            ConfigureForTheme(popup);
    }

    private static void ConfigureForTheme(Popup popup)
    {
        bool glass = ThemeManager.IsGlassTheme;
        // DWM acrylic needs a non-layered HWND; DropShadowEffect needs AllowsTransparency.
        if (!popup.IsOpen)
            popup.AllowsTransparency = !glass;

        SyncHostEffect(popup);
    }

    private static void SyncHostEffect(Popup popup)
    {
        if (popup.Child is not UIElement host)
            return;

        if (ThemeManager.IsGlassTheme)
        {
            if (host.Effect is not null && popup.GetValue(ShadowEffectProperty) is null)
                popup.SetValue(ShadowEffectProperty, host.Effect);
            host.Effect = null;
        }
        else if (popup.GetValue(ShadowEffectProperty) is Effect saved)
            host.Effect = saved;
    }

    private static void ApplyAcrylic(Popup popup)
    {
        if (!ThemeManager.IsGlassTheme || popup.Child is not Visual child)
            return;

        if (PresentationSource.FromVisual(child) is not HwndSource source)
            return;

        IntPtr hwnd = source.Handle;
        if (hwnd == IntPtr.Zero)
            return;

        int backdrop = DwmSbtTransientWindow;
        int result = DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref backdrop, sizeof(int));
        if (result >= 0)
        {
            var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            _ = DwmExtendFrameIntoClientArea(hwnd, ref margins);
        }

        int darkMode = ThemeManager.IsLightTheme ? 0 : 1;
        _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));

        int preference = DwmWcpRound;
        _ = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref preference, sizeof(int));

        uint borderColor = DwmwaColorNone;
        _ = DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref borderColor, sizeof(uint));

        if (source.CompositionTarget is { } target)
            target.BackgroundColor = result >= 0 ? Colors.Transparent : Color.FromRgb(0x29, 0x2D, 0x33);
    }

    private const int DwmwaSystemBackdropType = 38;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmSbtTransientWindow = 3;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmWcpRound = 2;
    private const int DwmwaBorderColor = 34;
    private const uint DwmwaColorNone = 0xFFFFFFFE;

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
