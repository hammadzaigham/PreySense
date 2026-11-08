using CommunityToolkit.Mvvm.ComponentModel;
using PreySense.Frontend.Services;

namespace PreySense.Frontend.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty]
    private bool _isLightTheme;

    partial void OnIsLightThemeChanged(bool value)
    {
        ThemeManager.Apply(value);
        DiagnosticMessage = value ? "Theme: Light" : "Theme: Dark";
    }
}
