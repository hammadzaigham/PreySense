using PreySense.Frontend.ViewModels;
using PreySense.Frontend.Services;

namespace PreySense.Tests;

[TestClass]
public class FnBrightnessStepTests
{
    [TestMethod]
    public void DuplicateChannelsDoNotDoubleStepButRepeatedPressesAndReversalsDo()
    {
        var filter = new FnBrightnessEventFilter();
        Assert.IsTrue(filter.Accept("HID", -1, 1000));
        Assert.IsFalse(filter.Accept("Keyboard", -1, 1020));
        Assert.IsTrue(filter.Accept("HID", -1, 1040));
        Assert.IsTrue(filter.Accept("Keyboard", 1, 1060));
        Assert.IsTrue(filter.Accept("HID", -1, 1200));
    }

    [TestMethod]
    public void PerKeyFnBrightnessUsesSavedKeyboardProfileWhenItsEditorIsNotSelected()
    {
        var vm = (MainViewModel)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        typeof(MainViewModel).GetField("_rgbBrightness", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(vm, 100);
        var keyboard = new PreySense.Rgb.LightingDeviceInfo("usb:keyboard", "Keyboard", "Keyboard",
            Enumerable.Range('A', 26).Select((letter, i) => new PreySense.Rgb.LightingLedInfo(i, ((char)letter).ToString())).ToArray(), [], "");
        var lighting = new PreySense.Core.Configuration.LightingConfig();
        lighting.Devices[keyboard.Id] = new() { Mode = -1, Brightness = 60 };
        int current = vm.KeyboardBrightnessForFn(lighting, keyboard);
        Assert.AreEqual(60, current);
        Assert.AreEqual(50, MainViewModel.StepFnBrightness(current, -1));
        var pendingSave = new TaskCompletionSource();
        typeof(MainViewModel).GetField("_fnKeyboardBrightnessSave", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(vm, pendingSave.Task);
        typeof(MainViewModel).GetField("_rgbBrightness", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(vm, 50);
        Assert.AreEqual(25, MainViewModel.StepFnBrightness(vm.KeyboardBrightnessForFn(lighting, keyboard), -1),
            "A second press must use the previous press even before its background save completes.");
    }

    [TestMethod]
    public void OrdinaryF11AndF12AreNotConsumedAsFnBrightness()
    {
        Assert.IsFalse(KeyboardHook.TryGetBrightnessDelta(0x7A, 0x57, out _));
        Assert.IsFalse(KeyboardHook.TryGetBrightnessDelta(0x7B, 0x58, out _));
        Assert.IsTrue(KeyboardHook.TryGetBrightnessDelta(0xFF, 0x57, out int down));
        Assert.AreEqual(-1, down);
    }

    [TestMethod]
    [DataRow(60, -1, 50)]
    [DataRow(50, -1, 25)]
    [DataRow(60, -2, 25)]
    [DataRow(60, 1, 75)]
    [DataRow(75, 1, 100)]
    [DataRow(50, 1, 75)]
    [DataRow(1, -1, 0)]
    [DataRow(99, 1, 100)]
    [DataRow(0, -1, 0)]
    [DataRow(100, 1, 100)]
    [DataRow(60, 0, 60)]
    [DataRow(60, int.MinValue, 0)]
    [DataRow(60, int.MaxValue, 100)]
    public void FnStepsToNextFirmwareRung(int current, int steps, int expected) =>
        Assert.AreEqual(expected, MainViewModel.StepFnBrightness(current, steps));
}
