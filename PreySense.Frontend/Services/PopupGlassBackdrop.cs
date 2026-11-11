using System;
using System.Collections.Generic;
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
    // Closed popup HWNDs are recreated on their next opening. Configure their window mode
    // when the theme changes, before WPF creates that HWND; Opened is too late.
    private static readonly List<WeakReference<Popup>> Popups = new();

    internal static void RefreshTheme()
    {
        for (int index = Popups.Count - 1; index >= 0; index--)
        {
            if (!Popups[index].TryGetTarget(out var popup))
            {
                Popups.RemoveAt(index);
                continue;
            }
            if (!popup.Dispatcher.CheckAccess() || !GetEnable(popup)) continue;
            ConfigureForTheme(popup);
            if (popup.IsOpen && ThemeManager.IsGlassTheme && !popup.AllowsTransparency)
                ApplyAcrylic(popup);
        }
    }
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
            Popups.Add(new WeakReference<Popup>(popup));
            ConfigureForTheme(popup);
            popup.Opened += Popup_Opened;
            popup.Closed += Popup_Closed;
            // Child may not exist yet when the attached property is set from the template.
            popup.Dispatcher.BeginInvoke(
                () => { if (GetEnable(popup)) ConfigureForTheme(popup); },
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
        else
        {
            Popups.RemoveAll(reference => !reference.TryGetTarget(out var target) || ReferenceEquals(target, popup));
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

        _ = NativeWindowBackdrop.Apply(source, acrylic: true);
    }
}
