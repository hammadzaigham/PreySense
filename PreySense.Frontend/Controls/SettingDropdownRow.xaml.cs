using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace PreySense.Frontend.Controls
{
    [ContentProperty(nameof(Items))]
    public partial class SettingDropdownRow : UserControl
    {
        public SettingDropdownRow()
        {
            InitializeComponent();
        }

        public ItemCollection Items => InnerComboBox.Items;
        public ComboBox ComboBox => InnerComboBox;

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(
                nameof(Title),
                typeof(string),
                typeof(SettingDropdownRow),
                new PropertyMetadata(string.Empty));

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(
                nameof(ItemsSource),
                typeof(IEnumerable),
                typeof(SettingDropdownRow),
                new PropertyMetadata(null));

        public IEnumerable ItemsSource
        {
            get => (IEnumerable)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        public static readonly DependencyProperty SelectedItemProperty =
            DependencyProperty.Register(
                nameof(SelectedItem),
                typeof(object),
                typeof(SettingDropdownRow),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public object? SelectedItem
        {
            get => GetValue(SelectedItemProperty);
            set => SetValue(SelectedItemProperty, value);
        }

        public static readonly DependencyProperty SelectedIndexProperty =
            DependencyProperty.Register(
                nameof(SelectedIndex),
                typeof(int),
                typeof(SettingDropdownRow),
                new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public int SelectedIndex
        {
            get => (int)GetValue(SelectedIndexProperty);
            set => SetValue(SelectedIndexProperty, value);
        }

        public static readonly DependencyProperty DropdownWidthProperty =
            DependencyProperty.Register(
                nameof(DropdownWidth),
                typeof(double),
                typeof(SettingDropdownRow),
                new PropertyMetadata(double.NaN));

        public double DropdownWidth
        {
            get => (double)GetValue(DropdownWidthProperty);
            set => SetValue(DropdownWidthProperty, value);
        }

        public static readonly DependencyProperty DropdownMinWidthProperty =
            DependencyProperty.Register(
                nameof(DropdownMinWidth),
                typeof(double),
                typeof(SettingDropdownRow),
                new PropertyMetadata(110.0));

        public double DropdownMinWidth
        {
            get => (double)GetValue(DropdownMinWidthProperty);
            set => SetValue(DropdownMinWidthProperty, value);
        }

        public event SelectionChangedEventHandler? SelectionChanged;

        private void InnerComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SelectionChanged?.Invoke(this, e);
        }
    }
}
