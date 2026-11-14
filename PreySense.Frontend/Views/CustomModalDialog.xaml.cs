using System.Windows;

namespace PreySense.Frontend.Views
{
    public partial class CustomModalDialog : Window
    {
        private bool? _result;
        public bool AllowClose { get; set; } = true;

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);
            if (!AllowClose) e.Cancel = true;
        }

        public CustomModalDialog()
        {
            // Use the application's live theme. Standalone diagnostics/tests get a fallback
            // only when there is no application resource dictionary to inherit.
            if (Application.Current?.TryFindResource("AppWindowShellStyle") is null)
            {
                foreach (string name in new[] { "Colors", "Typography", "Spacing", "Cards", "Controls" })
                    Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri($"pack://application:,,,/PreySense;component/Styles/{name}.xaml")
                    });
            }
            InitializeComponent();
        }

        /// <summary>
        /// Shows the window modally and returns the choice recorded by its custom buttons.
        /// Keeping the result ourselves avoids assigning Window.DialogResult after WPF has
        /// already begun closing the borderless window.
        /// </summary>
        public new bool? ShowDialog()
        {
            bool previousSuppression = App.SuppressAutoHide;
            App.SuppressAutoHide = true;
            try
            {
                _result = null;
                base.ShowDialog();
                return _result;
            }
            finally { App.SuppressAutoHide = previousSuppression; }
        }

        public string HeaderSubtitle
        {
            get => HeaderBar.Subtitle;
            set => HeaderBar.Subtitle = value;
        }

        public string Message
        {
            get => MessageText.Text;
            set => MessageText.Text = value;
        }

        public string ConfirmText
        {
            get => ConfirmButton.Content?.ToString() ?? string.Empty;
            set => ConfirmButton.Content = value;
        }

        public string CancelText
        {
            get => CancelButton.Content?.ToString() ?? string.Empty;
            set => CancelButton.Content = value;
        }

        public bool ConfirmEnabled
        {
            get => ConfirmButton.IsEnabled;
            set => ConfirmButton.IsEnabled = value;
        }

        public bool ShowCancelButton
        {
            get => CancelButton.Visibility == Visibility.Visible;
            set
            {
                CancelButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
                ButtonsPanel.Columns = value ? 2 : 1;
            }
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            _result = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _result = false;
            Close();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _result = null;
            Close();
        }
    }
}
