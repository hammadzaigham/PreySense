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

    [TestMethod]
    public void Ph1671PhysicalLayoutMatchesMeasuredGeometry()
    {
        var keys = OemUsbLightingProtocol.GetKeys(OemUsbKeyboardFamily.Acer, 0x0117);
        var layout = KeyboardLayoutGeometry.Build(keys.Select(k => new LightingLedInfo(k.Index, k.Name)));
        var byName = layout.ToDictionary(k => k.Name, StringComparer.OrdinalIgnoreCase);

        // Half-height F-row with 4-key group gaps.
        var esc = byName["Escape"];
        var f1 = byName["F1"];
        var f4 = byName["F4"];
        var f5 = byName["F5"];
        var keyA = byName["A"];
        Assert.IsLessThan(keyA.Bounds.Height, f1.Bounds.Height);
        Assert.AreEqual(esc.Bounds.Y, f1.Bounds.Y, 0.01);
        double intraGroupGap = byName["F2"].Bounds.Left - f1.Bounds.Right;
        double interGroupGap = f5.Bounds.Left - f4.Bounds.Right;
        Assert.IsGreaterThan(intraGroupGap, interGroupGap);

        // Staggered main rows and wide modifiers.
        Assert.IsGreaterThan(byName["Q"].Bounds.Left, byName["A"].Bounds.Left);
        Assert.IsGreaterThan(byName["A"].Bounds.Left, byName["Z"].Bounds.Left);
        Assert.IsGreaterThan(keyA.Bounds.Width, byName["Space"].Bounds.Width);
        Assert.IsGreaterThan(keyA.Bounds.Width, byName["Left Shift"].Bounds.Width);
        Assert.IsGreaterThan(keyA.Bounds.Width, byName["Left Enter"].Bounds.Width);

        // Media keys above numpad in F-row.
        var prev = byName["Volume Down"];
        var play = byName["Volume Up"];
        var next = byName["Mute"];
        var power = byName["Power"];
        Assert.AreEqual(f1.Bounds.Y, prev.Bounds.Y, 0.01);
        Assert.IsLessThan(play.Bounds.Left, prev.Bounds.Left);
        Assert.IsLessThan(next.Bounds.Left, play.Bounds.Left);
        Assert.IsLessThan(power.Bounds.Left, next.Bounds.Left);

        // Right arrow sits under numpad's first column; NumEnter spans two rows.
        var rightArrow = byName["Right Arrow"];
        var num7 = byName["Number Pad 7"];
        var num0 = byName["Number Pad 0"];
        var numEnter = byName["Number Pad Enter"];
        Assert.IsLessThan(num0.Bounds.Left, rightArrow.Bounds.Left);
        Assert.IsLessThan(num7.Bounds.Right, rightArrow.Bounds.Left);
        Assert.IsGreaterThan(keyA.Bounds.Height, numEnter.Bounds.Height);
    }

    [TestMethod]
    public void PaintModeLeftDragPaintsRightDragErasesAndOffUsesMarqueeSelection()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var editors = new ObservableCollection<LightingLedEditor>
                {
                    new(new(20, "A"), "#112233"), new(new(43, "B"), "#112233"),
                    new(new(31, "C"), "#112233"), new(new(15, "Q"), "#112233")
                };
                var selected = new ObservableCollection<LightingLedEditor>();
                var control = new RgbKeyboardLayout
                {
                    KeysSource = editors,
                    SelectedKeys = selected,
                    CommonColorHex = "#445566",
                    UseIndividualColors = false,
                    IsPaintMode = true,
                    PaintColorHex = "#FF00AA"
                };
                var layout = KeyboardLayoutGeometry.Build(editors.Select(k => new LightingLedInfo(k.Index, k.Name)));
                static Point Center(Rect r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);

                // Left-drag painting enables individual colors, copies CommonColorHex to untouched keys, and paints crossed keys.
                var paintedA = control.PaintKeyAt(Center(layout.Single(k => k.Index == 20).Bounds), erase: false);
                var paintedC = control.PaintKeyAt(Center(layout.Single(k => k.Index == 31).Bounds), erase: false);
                Assert.IsTrue(control.UseIndividualColors);
                Assert.AreEqual("#FF00AA", paintedA?.ColorHex);
                Assert.AreEqual("#FF00AA", paintedC?.ColorHex);
                Assert.AreEqual("#445566", editors[1].ColorHex);
                Assert.AreEqual("#445566", editors[3].ColorHex);
                Assert.AreSame(editors[2], control.SelectedKey);

                // Right-drag erasing sets crossed keys to #000000.
                var erasedA = control.PaintKeyAt(Center(layout.Single(k => k.Index == 20).Bounds), erase: true);
                Assert.AreEqual("#000000", erasedA?.ColorHex);
                Assert.AreEqual("#FF00AA", editors[2].ColorHex);

                // With IsPaintMode == false, marquee rectangle selection still works.
                control.IsPaintMode = false;
                var rectangle = Rect.Union(layout.Single(k => k.Index == 20).Bounds, layout.Single(k => k.Index == 31).Bounds);
                control.SelectKeysInRectangle(rectangle, Array.Empty<LightingLedEditor>());
                CollectionAssert.AreEquivalent(new[] { 20, 31 }, selected.Select(k => k.Index).ToArray());
            }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(5)));
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
