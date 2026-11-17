using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PreySense.Frontend.Controls
{
    public partial class WindowTitleBar : UserControl
    {
        public const string Brand = "PreySense";

        public WindowTitleBar()
        {
            InitializeComponent();
            UpdateCaption();
        }

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(
                nameof(Title),
                typeof(string),
                typeof(WindowTitleBar),
                new PropertyMetadata(string.Empty, OnCaptionChanged));

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public static readonly DependencyProperty SubtitleProperty =
            DependencyProperty.Register(
                nameof(Subtitle),
                typeof(string),
                typeof(WindowTitleBar),
                new PropertyMetadata(string.Empty, OnCaptionChanged));

        public string Subtitle
        {
            get => (string)GetValue(SubtitleProperty);
            set => SetValue(SubtitleProperty, value);
        }

        public static readonly DependencyProperty ActionsProperty =
            DependencyProperty.Register(
                nameof(Actions),
                typeof(object),
                typeof(WindowTitleBar),
                new PropertyMetadata(null));

        public object? Actions
        {
            get => GetValue(ActionsProperty);
            set => SetValue(ActionsProperty, value);
        }

        public event RoutedEventHandler? CloseClick;

        private static void OnCaptionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((WindowTitleBar)d).UpdateCaption();
        }

        private void UpdateCaption()
        {
            if (TitleText == null || SubtitleText == null)
                return;

            ApplySegment(TitleText, Title);
            ApplySegment(SubtitleText, Subtitle);
        }

        private static void ApplySegment(TextBlock text, string? value)
        {
            string formatted = FormatSegment(value);
            text.SetCurrentValue(TextBlock.TextProperty, formatted);
            text.Visibility = string.IsNullOrEmpty(formatted) ? Visibility.Collapsed : Visibility.Visible;
        }

        internal static string FormatSegment(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string text = value.Trim();
            if (text.StartsWith('•') || text.StartsWith('—') || text.StartsWith('-'))
                return " " + text;

            return " • " + text;
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left || e.LeftButton != MouseButtonState.Pressed || IsInsideButton(e.OriginalSource as DependencyObject))
                return;

            try { Window.GetWindow(this)?.DragMove(); }
            catch (System.InvalidOperationException) { }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (CloseClick != null)
            {
                CloseClick(this, e);
                return;
            }

            Window.GetWindow(this)?.Close();
        }

        private static bool IsInsideButton(DependencyObject? source)
        {
            while (source != null)
            {
                if (source is Button)
                    return true;
                source = VisualTreeHelper.GetParent(source);
            }

            return false;
        }
    }
}
