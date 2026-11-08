using System.Windows;
using System.Windows.Controls;

namespace PreySense.Frontend.Views
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            LanguageSelector.SelectedIndex = Services.LocalizationService.LanguageIndex;
            MaxHeight = SystemParameters.WorkArea.Height - 40;
                SettingsScroll.MaxHeight = Math.Max(240, MaxHeight - 56);
        }

        private void LanguageSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LanguageSelector.SelectedItem is ComboBoxItem item && item.Tag is string language)
                Services.LocalizationService.SetLanguage(language);
        }

    }
}
