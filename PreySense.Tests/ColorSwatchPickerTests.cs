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
        var editors = Enumerable.Range(0, 8).Select(i => new LightingLedEditor(new LightingLedInfo(i, $"LED {i + 1}"), "#FFFFFF")).ToArray();
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
        var editors = Enumerable.Range(0, 8).Select(i => new LightingLedEditor(new LightingLedInfo(i, $"LED {i + 1}"), "#FFFFFF")).ToArray();
        var picker = new ColorSwatchPicker { ItemsSource = editors, SelectedIndex = 0 };
        var accent = Brushes.Magenta;
        picker.Resources["AccentBrush"] = accent;
        picker.Resources["WindowBgBrush"] = Brushes.Black;
        picker.Resources["BorderMainBrush"] = Brushes.Gray;
        picker.Measure(new Size(500, double.PositiveInfinity));
        picker.Arrange(new Rect(0, 0, 500, picker.DesiredSize.Height));
        picker.UpdateLayout();
        var grid = Descendants<UniformGrid>(picker).Single();
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
}
