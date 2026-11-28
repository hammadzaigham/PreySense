using System.Windows;

namespace PreySense.Frontend.Views;

public partial class FanSettingsWindow : Window
{
    public FanSettingsWindow()
    {
        InitializeComponent();
        Closing += (_, _) =>
        {
            RowCpuMaxRpm.CommitText();
            RowGpuMaxRpm.CommitText();
        };
    }
}
