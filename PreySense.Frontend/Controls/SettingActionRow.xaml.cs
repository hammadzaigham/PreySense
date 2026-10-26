using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PreySense.Frontend.Controls
{
    public partial class SettingActionRow : UserControl
    {
        public SettingActionRow()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(
                nameof(Title),
                typeof(string),
                typeof(SettingActionRow),
                new PropertyMetadata(string.Empty));

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public static readonly DependencyProperty DescriptionProperty =
            DependencyProperty.Register(
                nameof(Description),
                typeof(string),
                typeof(SettingActionRow),
                new PropertyMetadata(string.Empty));

        public string Description
        {
            get => (string)GetValue(DescriptionProperty);
            set => SetValue(DescriptionProperty, value);
        }

        public static readonly DependencyProperty ActionTextProperty =
            DependencyProperty.Register(
                nameof(ActionText),
                typeof(string),
                typeof(SettingActionRow),
                new PropertyMetadata("Action"));

        public string ActionText
        {
            get => (string)GetValue(ActionTextProperty);
            set => SetValue(ActionTextProperty, value);
        }

        public static readonly DependencyProperty CommandProperty =
            DependencyProperty.Register(
                nameof(Command),
                typeof(ICommand),
                typeof(SettingActionRow),
                new PropertyMetadata(null));

        public ICommand? Command
        {
            get => (ICommand?)GetValue(CommandProperty);
            set => SetValue(CommandProperty, value);
        }

        public static readonly DependencyProperty CommandParameterProperty =
            DependencyProperty.Register(
                nameof(CommandParameter),
                typeof(object),
                typeof(SettingActionRow),
                new PropertyMetadata(null));

        public object? CommandParameter
        {
            get => GetValue(CommandParameterProperty);
            set => SetValue(CommandParameterProperty, value);
        }

        public event RoutedEventHandler? Click;

        public static readonly DependencyProperty SecondaryActionTextProperty =
            DependencyProperty.Register(nameof(SecondaryActionText), typeof(string), typeof(SettingActionRow), new PropertyMetadata(string.Empty));
        public string SecondaryActionText
        {
            get => (string)GetValue(SecondaryActionTextProperty);
            set => SetValue(SecondaryActionTextProperty, value);
        }

        public static readonly DependencyProperty SecondaryCommandProperty =
            DependencyProperty.Register(nameof(SecondaryCommand), typeof(ICommand), typeof(SettingActionRow), new PropertyMetadata(null));
        public ICommand? SecondaryCommand
        {
            get => (ICommand?)GetValue(SecondaryCommandProperty);
            set => SetValue(SecondaryCommandProperty, value);
        }

        public static readonly DependencyProperty SecondaryActionVisibilityProperty =
            DependencyProperty.Register(nameof(SecondaryActionVisibility), typeof(Visibility), typeof(SettingActionRow), new PropertyMetadata(Visibility.Collapsed));
        public Visibility SecondaryActionVisibility
        {
            get => (Visibility)GetValue(SecondaryActionVisibilityProperty);
            set => SetValue(SecondaryActionVisibilityProperty, value);
        }

        private void InnerButton_Click(object sender, RoutedEventArgs e)
        {
            Click?.Invoke(this, e);
        }
    }
}
