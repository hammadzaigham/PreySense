using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace PreySense.Frontend.Services;

public static class ThemeManager
{
    public static bool IsLightTheme { get; private set; }
    private const string SettingsKey = @"SOFTWARE\PreySense\AdvancedSettings";

    private static readonly IReadOnlyDictionary<string, (Color Dark, Color Light)> Palette =
        new Dictionary<string, (Color, Color)>
        {
            ["WindowBgColor"] = (Parse("#1C1C1C"), Parse("#F5F6F8")),
            ["CardBgColor"] = (Parse("#1C1C1C"), Parse("#FFFFFF")),
            ["ButtonMainColor"] = (Parse("#2E2E2E"), Parse("#FFFFFF")),
            ["ButtonSecondColor"] = (Parse("#242424"), Parse("#EEF0F3")),
            ["ButtonHoverColor"] = (Parse("#383838"), Parse("#E2E6EB")),
            ["BorderMainColor"] = (Parse("#373737"), Parse("#B9C0C9")),
            ["BorderSecondColor"] = (Parse("#2A2A2A"), Parse("#D8DDE3")),
            ["TextPrimaryColor"] = (Parse("#F0F0F0"), Parse("#17191C")),
            ["IconForegroundColor"] = (Parse("#FFFFFF"), Parse("#000000")),
            ["TextSecondaryColor"] = (Parse("#A8A8A8"), Parse("#535B66")),
            ["TextMutedColor"] = (Parse("#787878"), Parse("#747E8A")),
            ["TextOnAccentColor"] = (Parse("#FFFFFF"), Parse("#17191C")),
            ["ControlTrackColor"] = (Parse("#282B32"), Parse("#DDE1E6")),
            ["DisabledControlColor"] = (Parse("#3A3A3A"), Parse("#C5CAD1")),
            ["ControlTrackFillColor"] = (Parse("#4A5260"), Parse("#98A3B0")),
            ["ControlThumbColor"] = (Parse("#5C6576"), Parse("#FFFFFF")),
            ["ControlThumbHoverColor"] = (Parse("#758197"), Parse("#F7F9FB")),
            ["ControlThumbPressedColor"] = (Parse("#8B97AF"), Parse("#E9EDF2")),
            ["ControlSelectedColor"] = (Parse("#333742"), Parse("#DCEEFF")),
            ["ControlSelectedBorderColor"] = (Parse("#505766"), Parse("#78BDF0")),
            ["ControlFocusedBorderColor"] = (Parse("#4E5564"), Parse("#65ADE5")),
            ["ControlActiveColor"] = (Parse("#3E4450"), Parse("#D7E9F7")),
            ["ControlThumbBorderColor"] = (Parse("#EAEFF5"), Parse("#66717E")),
            ["ToastSurfaceColor"] = (Parse("#D8121216"), Parse("#F2FFFFFF")),
            ["WarningSurfaceColor"] = (Parse("#2A1810"), Parse("#FFF4E8")),
            ["WarningSurfaceSubtleColor"] = (Parse("#251A10"), Parse("#FFF8EF")),
            ["WarningTextColor"] = (Parse("#F5B041"), Parse("#8A4500")),
            ["SuccessSurfaceColor"] = (Parse("#142618"), Parse("#EAF8EF")),
            ["SuccessTextColor"] = (Parse("#82E0AA"), Parse("#176B38")),
            ["PlaceholderTextColor"] = (Parse("#8F8F8F"), Parse("#69727D"))
        };

    public static bool LoadAndApply()
    {
        var isLight = false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SettingsKey);
            isLight = key?.GetValue("LightTheme") is int value && value == 1;
        }
        catch { }

        Apply(isLight, persist: false);
        return isLight;
    }

    public static void Apply(bool isLight, bool persist = true)
    {
        IsLightTheme = isLight;
        TrayIconManager.ApplyMenuTheme(isLight);
        if (Application.Current is not null)
        {
            foreach (var (key, colors) in Palette)
            {
                var color = isLight ? colors.Light : colors.Dark;
                Application.Current.Resources[key] = color;
                SetBrush(key.Replace("Color", "Brush", StringComparison.Ordinal), color);
            }

            SetBrush("SelectedTileGradientBrush", isLight ? Parse("#E7F3FC") : Parse("#2E2E2E"));
            SetBrush("ButtonRaisedBgBrush", isLight ? Parse("#FFFFFF") : Parse("#2E2E2E"));
            SetBrush("ButtonRaisedBorderBrush", isLight ? Parse("#C8CED6") : Parse("#424242"));

            foreach (Window window in Application.Current.Windows)
                window.InvalidateVisual();
        }

        if (!persist)
            return;

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(SettingsKey);
            key?.SetValue("LightTheme", isLight ? 1 : 0, RegistryValueKind.DWord);
        }
        catch { }
    }

    private static Color Parse(string value) => (Color)ColorConverter.ConvertFromString(value);

    private static void SetBrush(string key, Color color)
    {
        if (Application.Current.TryFindResource(key) is SolidColorBrush brush && !brush.IsFrozen)
        {
            brush.Color = color;
            return;
        }

        Application.Current.Resources[key] = new SolidColorBrush(color);
    }
}
