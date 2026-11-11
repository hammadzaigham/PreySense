using PreySense.Frontend.Controls;
using PreySense.Rgb;
using PreySense.Frontend.ViewModels;
using System.Collections.ObjectModel;
using System.Windows;

namespace PreySense.Tests;

[TestClass]
public class KeyboardLayoutTests
{
    [TestMethod]
    public void ClickControlShiftAndRectangleSelectionUsePhysicalOrderWithoutLosingWireIndices()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var editors = new ObservableCollection<LightingLedEditor>
                {
                    new(new(20, "A"), "#00E5FF"), new(new(43, "B"), "#00E5FF"),
                    new(new(31, "C"), "#00E5FF"), new(new(15, "Q"), "#00E5FF")
                };
                var selected = new ObservableCollection<LightingLedEditor>();
                var control = new RgbKeyboardLayout { KeysSource = editors, SelectedKeys = selected };
                control.SelectKeyByIndex(20);
                control.SelectKeyByIndex(43, control: true);
                CollectionAssert.AreEquivalent(new[] { 20, 43 }, selected.Select(k => k.Index).ToArray());
                control.SelectKeyByIndex(20, control: true);
                CollectionAssert.AreEqual(new[] { 43 }, selected.Select(k => k.Index).ToArray());
                control.SelectKeyByIndex(15);
                control.SelectKeyByIndex(31, shift: true);
                CollectionAssert.AreEquivalent(new[] { 15, 20, 31 }, selected.Select(k => k.Index).ToArray());
                var layout = KeyboardLayoutGeometry.Build(editors.Select(k => new LightingLedInfo(k.Index, k.Name)));
                var rectangle = Rect.Union(layout.Single(k => k.Index == 20).Bounds, layout.Single(k => k.Index == 31).Bounds);
                control.SelectKeysInRectangle(rectangle, Array.Empty<LightingLedEditor>());
                CollectionAssert.AreEquivalent(new[] { 20, 31 }, selected.Select(k => k.Index).ToArray());
                control.SelectKeysInRectangle(rectangle, [editors[1]]);
                CollectionAssert.AreEquivalent(new[] { 20, 31, 43 }, selected.Select(k => k.Index).ToArray());
            }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(5)));
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [TestMethod]
    [DataRow(OemUsbKeyboardFamily.Acer, 0x0117)]
    [DataRow(OemUsbKeyboardFamily.Darfon, 0xba51)]
    [DataRow(OemUsbKeyboardFamily.Sunrex, 0x767a)]
    [DataRow(OemUsbKeyboardFamily.Sunrex, 0x666a)]
    public void EverySupportedKeyHasOneNonOverlappingPositionAndKeepsItsWireIndex(OemUsbKeyboardFamily family, int product)
    {
        var keys = OemUsbLightingProtocol.GetKeys(family, (ushort)product);
        var layout = KeyboardLayoutGeometry.Build(keys.Select(k => new LightingLedInfo(k.Index, k.Name)));
        Assert.HasCount(keys.Count, layout);
        CollectionAssert.AreEquivalent(keys.Select(k => k.Index).ToArray(), layout.Select(k => k.Index).ToArray());
        foreach (var key in layout)
        {
            Assert.AreEqual(keys.Single(k => k.Index == key.Index).Name, key.Name);
            Assert.IsTrue(key.Bounds.Width > 0 && key.Bounds.Height > 0);
            foreach (var other in layout.Where(k => k.Index != key.Index))
                Assert.IsFalse(key.Bounds.IntersectsWith(other.Bounds), $"{key.Name} overlaps {other.Name}");
        }
        var a = layout.SingleOrDefault(k => k.Name == "A");
        var q = layout.SingleOrDefault(k => k.Name == "Q");
        if (a != null && q != null) Assert.IsLessThan(a.Bounds.Y, q.Bounds.Y);
    }
}
