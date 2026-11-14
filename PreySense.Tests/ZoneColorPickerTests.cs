using System.Reflection;
using System.Runtime.CompilerServices;
using PreySense.Frontend.ViewModels;

namespace PreySense.Tests;

[TestClass]
public class ZoneColorPickerTests
{
    [TestMethod]
    public void SelectionUsesTheExistingZoneAndColorEditsKeepSwatchInstances()
    {
        var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        var fields = BindingFlags.Instance | BindingFlags.NonPublic;
        var colors = new[] { "#FFFFFF", "#000000", "#00AEC7", "#C700FF" };
        typeof(MainViewModel).GetField("_zoneHexColors", fields)!.SetValue(vm, colors);
        typeof(MainViewModel).GetField("_selectedRgbZone", fields)!.SetValue(vm, 1);
        var choices = vm.ZoneColorChoices;
        Assert.HasCount(4, choices);
        vm.SelectedZoneColorIndex = 2;
        Assert.AreEqual(3, vm.SelectedRgbZone);
        Assert.AreEqual("#00AEC7", vm.SelectedHexColor);
        vm.SelectedZoneColorIndex = -1;
        Assert.AreEqual(3, vm.SelectedRgbZone, "Picker initialization must not clear the selected zone.");
        colors[2] = "#FFFFFF";
        typeof(MainViewModel).GetMethod("NotifyZoneColorProperties", fields)!.Invoke(vm, [2]);
        Assert.AreSame(choices, vm.ZoneColorChoices);
        Assert.AreEqual("#FFFFFF", choices[2].ColorHex);
        Assert.AreEqual("#111111", choices[2].ForegroundHex);
    }

    [TestMethod]
    [DataRow("#FFFFFF", "#111111")]
    [DataRow("#10FF10", "#111111")]
    [DataRow("#00AEC7", "#111111")]
    [DataRow("#808080", "#111111")]
    [DataRow("#000000", "#F5F5F5")]
    [DataRow("#2E09D0", "#F5F5F5")]
    public void PickerTextContrastsWithItsColor(string background, string foreground) =>
        Assert.AreEqual(foreground, MainViewModel.ContrastForegroundHex(background));
}
