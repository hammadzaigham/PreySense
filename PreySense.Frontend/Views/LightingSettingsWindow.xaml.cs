using System.Windows;

namespace PreySense.Frontend.Views;

public partial class LightingSettingsWindow : Window
{
    public LightingSettingsWindow()
    {
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height - 32;
    }

    private void ModeColorWheel_DraftColorChanged(object? sender, System.Windows.Media.Color c)
    {
        TextModeHexColor.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }
}
