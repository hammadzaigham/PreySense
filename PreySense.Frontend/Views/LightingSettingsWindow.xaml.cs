using System.Windows;

namespace PreySense.Frontend.Views;

public partial class LightingSettingsWindow : Window
{
    public LightingSettingsWindow()
    {
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height - 32;
    }
}
