using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using PreySense.Core.Configuration;

namespace PreySense.Frontend.Services;

public static class ThemeManager
{
    public static ApplicationTheme CurrentTheme { get; private set; }
    public static bool IsLightTheme => CurrentTheme == ApplicationTheme.Light;
    public static bool IsGlassTheme => CurrentTheme == ApplicationTheme.Glass && IsGlassSupported;
    public static bool IsGlassSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621);

    private static readonly IReadOnlyDictionary<string, Color> GlassPalette = new Dictionary<string, Color>
    {
        ["WindowBgColor"] = Parse("#30141820"),
        ["ToastSurfaceColor"] = Parse("#D821252A"),
        ["CardBgColor"] = Parse("#181C2632"),
        ["ButtonMainColor"] = Parse("#25D8E8FF"),
        ["ButtonSecondColor"] = Parse("#18B7CEE8"),
        ["ButtonHoverColor"] = Parse("#42D8E8FF"),
        ["BorderMainColor"] = Parse("#60E1EFFF"),
        ["BorderSecondColor"] = Parse("#28E1EFFF"),
        ["ControlTrackColor"] = Parse("#30506078"),
        ["DisabledControlColor"] = Parse("#38636C80"),
        ["ControlTrackFillColor"] = Parse("#A078879D"),
        ["ControlSelectedColor"] = Parse("#58677790"),
        ["ControlActiveColor"] = Parse("#70778DA9")
    };

    private static readonly IReadOnlyDictionary<string, (Color Dark, Color Light)> Palette =
        new Dictionary<string, (Color, Color)>
        {
            ["WindowBgColor"] = (Parse("#0A0A0A"), Parse("#F5F6F8")),
            ["CardBgColor"] = (Parse("#0A0A0A"), Parse("#FFFFFF")),
            ["ButtonMainColor"] = (Parse("#141414"), Parse("#FFFFFF")),
            ["ButtonSecondColor"] = (Parse("#101010"), Parse("#EEF0F3")),
            ["ButtonHoverColor"] = (Parse("#1C1C1C"), Parse("#E2E6EB")),
            ["BorderMainColor"] = (Parse("#2A2A2A"), Parse("#B9C0C9")),
            ["BorderSecondColor"] = (Parse("#1F1F1F"), Parse("#D8DDE3")),
            ["TextPrimaryColor"] = (Parse("#F0F0F0"), Parse("#17191C")),
            ["IconForegroundColor"] = (Parse("#FFFFFF"), Parse("#000000")),
            ["TextSecondaryColor"] = (Parse("#A8A8A8"), Parse("#535B66")),
            ["TextMutedColor"] = (Parse("#787878"), Parse("#747E8A")),
            ["TextOnAccentColor"] = (Parse("#FFFFFF"), Parse("#17191C")),
            ["ControlTrackColor"] = (Parse("#141414"), Parse("#DDE1E6")),
            ["DisabledControlColor"] = (Parse("#2A2A2A"), Parse("#C5CAD1")),
            ["ControlTrackFillColor"] = (Parse("#3A3A3A"), Parse("#98A3B0")),
            ["ControlThumbColor"] = (Parse("#5C6576"), Parse("#FFFFFF")),
            ["ControlThumbHoverColor"] = (Parse("#758197"), Parse("#F7F9FB")),
            ["ControlThumbPressedColor"] = (Parse("#8B97AF"), Parse("#E9EDF2")),
            ["ControlSelectedColor"] = (Parse("#1A1A1A"), Parse("#DFEAFE")),
            ["ControlSelectedBorderColor"] = (Parse("#3B82F6"), Parse("#3B82F6")),
            ["ControlFocusedBorderColor"] = (Parse("#3B82F6"), Parse("#3B82F6")),
            ["ControlActiveColor"] = (Parse("#222222"), Parse("#DCE8FD")),
            ["ControlThumbBorderColor"] = (Parse("#EAEFF5"), Parse("#66717E")),
            ["ToastSurfaceColor"] = (Parse("#0A0A0A"), Parse("#F2FFFFFF")),
            ["WarningSurfaceColor"] = (Parse("#2A1810"), Parse("#FFF4E8")),
            ["WarningSurfaceSubtleColor"] = (Parse("#251A10"), Parse("#FFF8EF")),
            ["WarningTextColor"] = (Parse("#F5B041"), Parse("#8A4500")),
            ["SuccessSurfaceColor"] = (Parse("#142618"), Parse("#EAF8EF")),
            ["SuccessTextColor"] = (Parse("#82E0AA"), Parse("#176B38")),
            ["PlaceholderTextColor"] = (Parse("#8F8F8F"), Parse("#69727D"))
        };

    public static ApplicationTheme LoadAndApply()
    {
        var theme = ApplicationTheme.Dark;
        try { theme = ResolveTheme(ConfigurationService.Instance.GetCurrent().General); }
        catch { }
        Apply(theme, persist: false);
        return theme;
    }

    public static ApplicationTheme ResolveTheme(GeneralConfig general) =>
        general.Theme ?? (general.LightTheme ? ApplicationTheme.Light : ApplicationTheme.Dark);

    public static void Apply(bool isLight, bool persist = true) =>
        Apply(isLight ? ApplicationTheme.Light : ApplicationTheme.Dark, persist);

    public static void Apply(ApplicationTheme theme, bool persist = true)
    {
        if (!Enum.IsDefined(theme)) throw new ArgumentOutOfRangeException(nameof(theme));
        CurrentTheme = theme;
        bool isLight = IsLightTheme;
        bool glass = IsGlassTheme;
        TrayIconManager.ApplyMenuTheme(isLight);
        if (Application.Current is not null)
        {
            foreach (var (key, colors) in Palette)
            {
                var color = glass && GlassPalette.TryGetValue(key, out var translucent) ? translucent : isLight ? colors.Light : colors.Dark;
                Application.Current.Resources[key] = color;
                SetBrush(key.Replace("Color", "Brush", StringComparison.Ordinal), color);
            }

            SetBrush("SelectedTileGradientBrush", glass ? Parse("#22FFFFFF") : isLight ? Parse("#EAF1FE") : Parse("#141414"));
            SetBrush("ButtonRaisedBgBrush", glass ? Parse("#25D8E8FF") : isLight ? Parse("#FFFFFF") : Parse("#141414"));
            SetBrush("ButtonRaisedBorderBrush", glass ? Parse("#60E1EFFF") : isLight ? Parse("#C8CED6") : Parse("#2A2A2A"));
            SetBrush("WindowHostBgBrush", glass ? Colors.Transparent : isLight ? Parse("#F5F6F8") : Parse("#0A0A0A"));
            // Glass popups use a translucent tint over DWM acrylic (PopupGlassBackdrop).
            SetBrush("PopupBgBrush", glass ? Parse("#8021252A") : isLight ? Parse("#F5F6F8") : Parse("#0A0A0A"));
            SetBrush("PopupBorderBrush", glass ? Parse("#40FFFFFF") : isLight ? Parse("#C8CED6") : Parse("#2A2A2A"));
            SetBrush("PopupItemHoverBrush", glass ? Parse("#36FFFFFF") : isLight ? Palette["ButtonHoverColor"].Light : Palette["ButtonHoverColor"].Dark);
            // Glass: selected items keep the same fill; Accent border marks active state.
            SetBrush("PopupItemSelectedBrush", glass ? Parse("#36FFFFFF") : isLight ? Palette["ControlSelectedColor"].Light : Palette["ControlSelectedColor"].Dark);
            SetBrush("BorderSubtleBrush", glass ? GlassPalette["BorderSecondColor"] : isLight ? Palette["BorderSecondColor"].Light : Palette["BorderSecondColor"].Dark);
            SetBrush("ElevatedCardBgBrush", glass ? GlassPalette["ButtonSecondColor"] : isLight ? Palette["ButtonSecondColor"].Light : Palette["ButtonSecondColor"].Dark);

            SetBrush("WindowShellBgBrush", isLight ? Parse("#F5F6F8") : Parse("#0A0A0A"));
            SetBrush("WindowShellBorderBrush", Colors.Transparent);
            SetBrush("SeparatorBrush", glass ? Parse("#28FFFFFF") : isLight ? Palette["BorderMainColor"].Light : Palette["BorderMainColor"].Dark);
            Application.Current.Resources["WindowShellBorderThickness"] = new Thickness(0);
            Application.Current.Resources["SelectedControlBorderThickness"] = new Thickness(2);
            SetBrush("InputBgBrush", isLight ? Palette["ButtonMainColor"].Light : Palette["ButtonMainColor"].Dark);
            SetBrush("InputFocusBgBrush", isLight ? Palette["ButtonMainColor"].Light : Palette["ButtonMainColor"].Dark);
            Application.Current.Resources["MainSectionMargin"] = new Thickness(0, 0, 0, glass ? 12 : 8);
            if (glass)
            {
                // A regular reading surface beneath the thinner control/navigation layer.
                SetBrush("WindowShellBgBrush", Parse("#8021252A"));
                SetBrush("InputBgBrush", Parse("#22FFFFFF"));
                SetBrush("InputFocusBgBrush", Parse("#36FFFFFF"));

                foreach (string key in new[] { "ButtonMainBrush", "ButtonRaisedBgBrush", "ElevatedCardBgBrush" })
                    SetBrush(key, Parse("#22FFFFFF"));

                SetBrush("SelectedTileGradientBrush", Parse("#22FFFFFF"));
                SetBrush("ControlActiveBrush", Parse("#22FFFFFF"));
                SetBrush("ControlSelectedBrush", Parse("#22FFFFFF"));
                SetBrush("ControlSelectedBorderBrush", Parse("#3B82F6"));
                SetBrush("ControlFocusedBorderBrush", Parse("#3B82F6"));
                SetBrush("ControlThumbBrush", Parse("#C0DCE4EC"));
                SetBrush("ControlThumbHoverBrush", Parse("#F0F4F8FC"));
                SetBrush("ControlThumbPressedBrush", Parse("#FFE3EFF8"));
                SetBrush("TextPrimaryBrush", Parse("#F5F5F7"));
                SetBrush("ButtonHoverBrush", Parse("#36FFFFFF"));
                SetBrush("CardBgBrush", Parse("#10FFFFFF"));
                SetBrush("TextSecondaryBrush", Parse("#C9D4E2"));
                SetBrush("TextMutedBrush", Parse("#92A0B3"));
            }

            // Idle surfaces are borderless in every theme. Selection and focus keep their accent brushes.
            foreach (string key in new[] { "ButtonRaisedBorderBrush", "BorderMainBrush", "BorderSecondBrush",
                "BorderSubtleBrush", "ControlThumbBorderBrush", "PopupBorderBrush", "WindowShellBorderBrush" })
                SetBrush(key, Colors.Transparent);

            // Active performance modes keep the ordinary fill; colored/Accent border identifies selection.
            SetBrush("PerformanceModeActiveBrush", glass ? Parse("#22FFFFFF") : isLight ? Parse("#EAF1FE") : Parse("#141414"));

            foreach (Window window in Application.Current.Windows)
            {
                WindowSystemShadow.RefreshTheme(window);
                window.InvalidateVisual();
            }
        }

        if (!persist)
            return;

        try
        {
            ConfigurationService.Instance.Update(cfg =>
            {
                cfg.General.Theme = theme;
                cfg.General.LightTheme = isLight;
            });
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
