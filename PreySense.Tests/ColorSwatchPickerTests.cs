using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using PreySense.Frontend.Controls;
using PreySense.Frontend.ViewModels;
using PreySense.Rgb;

namespace PreySense.Tests;

[TestClass]
[DoNotParallelize]
public class ColorSwatchPickerTests
{
    private static void OnSta(Action test)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { test(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(5)));
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static LightingLedEditor[] CreateEditors(int count, string prefix = "LED") =>
        Enumerable.Range(0, count).Select(i => new LightingLedEditor(new LightingLedInfo(i, $"{prefix} {i + 1}"), "#FFFFFF")).ToArray();

    private static ColorSwatchPicker CreateLaidOutPicker(System.Collections.IEnumerable? itemsSource = null, double width = 500)
    {
        var picker = new ColorSwatchPicker { ItemsSource = itemsSource, SelectedIndex = 0 };
        picker.Resources["AccentBrush"] = Brushes.Magenta;
        picker.Resources["WindowBgBrush"] = Brushes.Black;
        picker.Resources["BorderMainBrush"] = Brushes.Gray;
        MeasureAndArrange(picker, width);
        return picker;
    }

    private static void MeasureAndArrange(ColorSwatchPicker picker, double width = 500)
    {
        picker.Measure(new Size(width, double.PositiveInfinity));
        picker.Arrange(new Rect(0, 0, width, picker.DesiredSize.Height));
        picker.UpdateLayout();
    }

    private sealed class SelectionModel : ObservableObject
    {
        private object? _item;
        private int _index;
        public object? Item { get => _item; set => SetProperty(ref _item, value); }
        public int Index { get => _index; set => SetProperty(ref _index, value); }
    }

    [TestMethod]
    public void ItemAndIndexSelectionStaySynchronizedInBothDirections() => OnSta(() =>
    {
        var editors = CreateEditors(8);
        var picker = new ColorSwatchPicker { ItemsSource = editors };
        var list = (ListBox)picker.FindName("Swatches");
        var model = new SelectionModel { Item = editors[0], Index = 0 };
        picker.SetBinding(ColorSwatchPicker.SelectedIndexProperty, new Binding(nameof(SelectionModel.Index)) { Source = model, Mode = BindingMode.TwoWay });
        picker.SetBinding(ColorSwatchPicker.SelectedItemProperty, new Binding(nameof(SelectionModel.Item)) { Source = model, Mode = BindingMode.TwoWay });
        Assert.AreEqual(0, picker.SelectedIndex);
        Assert.AreSame(editors[0], picker.SelectedItem);
        model.Index = 6;
        Assert.AreEqual(6, list.SelectedIndex);
        Assert.AreSame(editors[6], picker.SelectedItem);
        model.Item = editors[2];
        Assert.AreEqual(2, picker.SelectedIndex);
        Assert.AreSame(editors[2], list.SelectedItem);
        list.SetCurrentValue(Selector.SelectedIndexProperty, 4);
        Assert.AreEqual(4, picker.SelectedIndex);
        Assert.AreSame(editors[4], picker.SelectedItem);
        Assert.AreEqual(4, model.Index);
        Assert.AreSame(editors[4], model.Item);
        Assert.IsTrue(BindingOperations.IsDataBound(picker, ColorSwatchPicker.SelectedIndexProperty));
        Assert.IsTrue(BindingOperations.IsDataBound(picker, ColorSwatchPicker.SelectedItemProperty));
    });

    [TestMethod]
    public void SwatchesWrapAfterFiveAndKeepAnEqualOutsideSelectionGap() => OnSta(() =>
    {
        var editors = CreateEditors(9);
        var picker = CreateLaidOutPicker(editors);
        var accent = (Brush)picker.Resources["AccentBrush"];
        var grid = Descendants<UniformGrid>(picker).Single();
        Assert.AreEqual(5, picker.Columns);
        Assert.AreEqual(5, grid.Columns);
        Assert.AreEqual(0, grid.Rows);
        var list = (ListBox)picker.FindName("Swatches");
        var first = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
        var sixth = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(5);
        Assert.IsGreaterThan(first.TranslatePoint(new Point(), picker).Y, sixth.TranslatePoint(new Point(), picker).Y);
        var ring = (Border)first.Template.FindName("SelectionBorder", first);
        Assert.AreEqual(new Thickness(2), ring.Padding);
        Assert.AreEqual(new Thickness(2), ring.BorderThickness);
        Assert.AreSame(accent, ring.BorderBrush);
        var fill = Descendants<Border>(first).Single(border => border.Height == 32);
        Assert.AreEqual(4d, fill.TranslatePoint(new Point(), ring).X, 0.01);
        Assert.AreEqual(4d, fill.TranslatePoint(new Point(), ring).Y, 0.01);
        Assert.AreEqual(ring.ActualWidth - 8, fill.ActualWidth, 0.01);
        Assert.AreEqual(ring.ActualHeight - 8, fill.ActualHeight, 0.01);
        Assert.IsFalse(Descendants<Button>(picker).Any());
        var label = Descendants<TextBlock>(fill).Single();
        Assert.AreEqual("LED 1", label.Text);
        Assert.AreEqual((Color)ColorConverter.ConvertFromString(editors[0].ForegroundHex), ((SolidColorBrush)label.Foreground).Color);
    });

    [TestMethod]
    [DataRow(6, 3)]
    [DataRow(7, 4)]
    [DataRow(8, 4)]
    [DataRow(9, 5)]
    [DataRow(10, 5)]
    [DataRow(12, 4)]
    public void MultiRowSwatchesDivideSpaceEvenlyAcrossRows(int itemCount, int expectedColumns) => OnSta(() =>
    {
        var picker = CreateLaidOutPicker(CreateEditors(itemCount));
        var grid = Descendants<UniformGrid>(picker).Single();
        Assert.AreEqual(expectedColumns, picker.Columns);
        Assert.AreEqual(expectedColumns, grid.Columns);
    });

    [TestMethod]
    [DataRow(3, 3)]
    [DataRow(4, 4)]
    public void SmallRowsSizeColumnsToItemCountWithoutTrailingGap(int itemCount, int expectedColumns) => OnSta(() =>
    {
        var editors = CreateEditors(itemCount, "Zone");
        var picker = CreateLaidOutPicker(editors, width: 480);
        var grid = Descendants<UniformGrid>(picker).Single();
        Assert.AreEqual(expectedColumns, picker.Columns);
        Assert.AreEqual(expectedColumns, grid.Columns);
        Assert.AreEqual(0, grid.Rows);
        var list = (ListBox)picker.FindName("Swatches");
        var first = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
        var last = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(itemCount - 1);
        Assert.AreEqual(first.TranslatePoint(new Point(), picker).Y, last.TranslatePoint(new Point(), picker).Y, 0.01);
        Assert.AreEqual(480d / expectedColumns - 4d, first.ActualWidth, 0.01);
        Assert.AreEqual(480d - 2d, last.TranslatePoint(new Point(last.ActualWidth, 0), picker).X, 0.01);
    });

    [TestMethod]
    public void ReplacingItemsSourceUpdatesColumns() => OnSta(() =>
    {
        var picker = CreateLaidOutPicker(CreateEditors(4, "Zone"));
        var grid = Descendants<UniformGrid>(picker).Single();
        Assert.AreEqual(4, picker.Columns);
        Assert.AreEqual(4, grid.Columns);

        picker.ItemsSource = CreateEditors(2);
        MeasureAndArrange(picker);
        Assert.AreEqual(2, picker.Columns);
        Assert.AreEqual(2, grid.Columns);

        picker.ItemsSource = CreateEditors(8);
        MeasureAndArrange(picker);
        Assert.AreEqual(4, picker.Columns);
        Assert.AreEqual(4, grid.Columns);

        picker.ItemsSource = CreateEditors(9);
        MeasureAndArrange(picker);
        Assert.AreEqual(5, picker.Columns);
        Assert.AreEqual(5, grid.Columns);

        picker.ItemsSource = null;
        MeasureAndArrange(picker);
        Assert.AreEqual(1, picker.Columns);
        Assert.AreEqual(1, grid.Columns);
    });

    [TestMethod]
    public void ObservableCollectionMutationsUpdateColumnsAndIgnoreDetachedSources() => OnSta(() =>
    {
        var collection = new ObservableCollection<LightingLedEditor>(CreateEditors(2));
        var picker = CreateLaidOutPicker(collection);
        var grid = Descendants<UniformGrid>(picker).Single();
        Assert.AreEqual(2, picker.Columns);
        Assert.AreEqual(2, grid.Columns);

        collection.Add(new LightingLedEditor(new LightingLedInfo(2, "LED 3"), "#FFFFFF"));
        MeasureAndArrange(picker);
        Assert.AreEqual(3, picker.Columns);
        Assert.AreEqual(3, grid.Columns);

        collection.Add(new LightingLedEditor(new LightingLedInfo(3, "LED 4"), "#FFFFFF"));
        MeasureAndArrange(picker);
        Assert.AreEqual(4, picker.Columns);
        Assert.AreEqual(4, grid.Columns);

        for (int i = 4; i < 8; i++)
            collection.Add(new LightingLedEditor(new LightingLedInfo(i, $"LED {i + 1}"), "#FFFFFF"));
        MeasureAndArrange(picker);
        Assert.AreEqual(4, picker.Columns);
        Assert.AreEqual(4, grid.Columns);

        collection.RemoveAt(0);
        collection.RemoveAt(0);
        collection.RemoveAt(0);
        collection.RemoveAt(0);
        collection.RemoveAt(0);
        MeasureAndArrange(picker);
        Assert.AreEqual(3, picker.Columns);
        Assert.AreEqual(3, grid.Columns);

        collection.Clear();
        MeasureAndArrange(picker);
        Assert.AreEqual(1, picker.Columns);
        Assert.AreEqual(1, grid.Columns);

        var replacement = new ObservableCollection<LightingLedEditor>(CreateEditors(4));
        picker.ItemsSource = replacement;
        MeasureAndArrange(picker);
        Assert.AreEqual(4, picker.Columns);
        Assert.AreEqual(4, grid.Columns);

        // Mutating the previously bound collection must not affect the current column count.
        collection.Add(new LightingLedEditor(new LightingLedInfo(0, "Stale 1"), "#FFFFFF"));
        collection.Add(new LightingLedEditor(new LightingLedInfo(1, "Stale 2"), "#FFFFFF"));
        MeasureAndArrange(picker);
        Assert.AreEqual(4, picker.Columns);
        Assert.AreEqual(4, grid.Columns);
    });

    [TestMethod]
    public void MaxColumnsCapsAndCoercesColumnCount() => OnSta(() =>
    {
        var picker = CreateLaidOutPicker(CreateEditors(9));
        var grid = Descendants<UniformGrid>(picker).Single();
        Assert.AreEqual(5, picker.Columns);

        picker.MaxColumns = 3;
        MeasureAndArrange(picker);
        Assert.AreEqual(3, picker.Columns);
        Assert.AreEqual(3, grid.Columns);

        picker.MaxColumns = 0;
        MeasureAndArrange(picker);
        Assert.AreEqual(1, picker.MaxColumns);
        Assert.AreEqual(1, picker.Columns);
        Assert.AreEqual(1, grid.Columns);
    });
}
