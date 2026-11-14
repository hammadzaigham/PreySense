using System.Windows;
using PreySense.Frontend.ViewModels;

namespace PreySense.Frontend.Views;

public partial class LightingSettingsWindow : Window
{
    public LightingSettingsWindow()
    {
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height - 32;
    }

    private void KeyboardModeColor_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && sender is FrameworkElement { Tag: ModePowerModeColorEditor editor })
            vm.SelectedKeyboardModeColor = editor;
    }

}
