using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace PreySense.Frontend.Controls;

/// <summary>Shared selectable color swatches for zones, LEDs, and operating-mode colors.</summary>
public partial class ColorSwatchPicker : UserControl
{
    public ColorSwatchPicker()
    {
        InitializeComponent();
        // Swatches.Items tracks ItemsSource replacements and INotifyCollectionChanged updates via weak events,
        // so subscribing to the child ItemCollection avoids holding strong references from external collections.
        ((INotifyCollectionChanged)Swatches.Items).CollectionChanged += OnSwatchesCollectionChanged;
        UpdateColumns();
    }

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(ColorSwatchPicker), new PropertyMetadata(null));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
        nameof(SelectedItem), typeof(object), typeof(ColorSwatchPicker),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register(
        nameof(SelectedIndex), typeof(int), typeof(ColorSwatchPicker),
        new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    public static readonly DependencyProperty MaxColumnsProperty = DependencyProperty.Register(
        nameof(MaxColumns), typeof(int), typeof(ColorSwatchPicker),
        new PropertyMetadata(5, OnMaxColumnsChanged, CoerceMaxColumns));

    public int MaxColumns
    {
        get => (int)GetValue(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    private static readonly DependencyPropertyKey ColumnsPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(Columns), typeof(int), typeof(ColorSwatchPicker), new PropertyMetadata(1));

    public static readonly DependencyProperty ColumnsProperty = ColumnsPropertyKey.DependencyProperty;

    public int Columns
    {
        get => (int)GetValue(ColumnsProperty);
        private set => SetValue(ColumnsPropertyKey, value);
    }

    private static object CoerceMaxColumns(DependencyObject d, object baseValue) =>
        Math.Max(1, (int)baseValue);

    private static void OnMaxColumnsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ColorSwatchPicker)d).UpdateColumns();

    private void OnSwatchesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        UpdateColumns();

    internal static int ComputeBalancedColumns(int itemCount, int maxColumns)
    {
        int cappedMax = Math.Max(1, maxColumns);
        if (itemCount <= 1) return 1;
        if (itemCount <= cappedMax) return itemCount;
        int rows = (itemCount + cappedMax - 1) / cappedMax;
        return (itemCount + rows - 1) / rows;
    }

    private void UpdateColumns()
    {
        int itemCount = Swatches?.Items.Count ?? 0;
        int targetColumns = ComputeBalancedColumns(itemCount, MaxColumns);
        if (Columns != targetColumns)
            Columns = targetColumns;
    }
}
