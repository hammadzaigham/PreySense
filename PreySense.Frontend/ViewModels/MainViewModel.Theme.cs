using CommunityToolkit.Mvvm.ComponentModel;
using PreySense.Frontend.Services;

namespace PreySense.Frontend.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty]
    private int _selectedThemeIndex;

    partial void OnSelectedThemeIndexChanged(int value)
    {
        if (value is < 0 or > 2) return;
        var theme = (PreySense.Core.Configuration.ApplicationTheme)value;
        ThemeManager.Apply(theme);
        DiagnosticMessage = "Theme: " + LocalizationService.Translate(theme.ToString());
    }
}
