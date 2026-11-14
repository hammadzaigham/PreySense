using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using PreySense.Core.Configuration;
using PreySense.Frontend.ViewModels;
using PreySense.Rgb;

namespace PreySense.Tests;

[TestClass]
[DoNotParallelize]
public class KeyboardModeColorEditorTests
{
    [TestMethod]
    public void EditingTurboWhileBalancedIsActiveKeepsTurboSelectedAndBalancedUnchanged()
    {
        var store = ConfigurationService.Instance;
        var original = store.GetCurrent();
        try
        {
            store.Update(cfg => cfg.Lighting.PowerModeColors.Clear());
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            void Set(string name, object value) => typeof(MainViewModel).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, value);
            Set("<KeyboardModeColorEditors>k__BackingField", new ObservableCollection<ModePowerModeColorEditor>());
            Set("_activePowerMode", (byte)1);
            Set("_zoneHexColors", new[] { "#00AEC7", "#00AEC7", "#00AEC7", "#00AEC7" });
            Set("_lightingEffectTag", "MODE");
            typeof(MainViewModel).GetMethod("LoadKeyboardModeColors", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, [new Dictionary<string, string>()]);
            vm.SelectedKeyboardModeColor = vm.KeyboardModeColorEditors.Single(e => e.PowerMode == 5);
            vm.SelectedKeyboardModeColor.ColorHex = "#112233";
            Assert.AreEqual((byte)5, vm.SelectedKeyboardModeColor.PowerMode);
            vm.SelectedKeyboardModeColor.ColorHex = "#445566";
            Assert.AreEqual((byte)5, vm.SelectedKeyboardModeColor.PowerMode);
            Assert.AreEqual("#00AEC7", vm.KeyboardModeColorEditors.Single(e => e.PowerMode == 1).ColorHex);
            Assert.AreEqual("#445566", store.GetCurrent().Lighting.PowerModeColors["05"]);
            Assert.IsFalse(store.GetCurrent().Lighting.PowerModeColors.ContainsKey("01"));
        }
        finally { store.Save(original); }
    }

    [TestMethod]
    public void SharedModeEditorPersistsOnlyGlobalCustomColorsWithoutChangingOtherSlots()
    {
        var store = ConfigurationService.Instance;
        var original = store.GetCurrent();
        try
        {
        // Exercise the editor without starting native watchers or writing to hardware.
        var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        void Set(string name, object value) => typeof(MainViewModel)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, value);
        Set("<KeyboardModeColorEditors>k__BackingField", new ObservableCollection<ModePowerModeColorEditor>());
        Set("_isRestoringSession", true);
        Set("_activePowerMode", (byte)5);
        typeof(MainViewModel).GetMethod("LoadKeyboardModeColors", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(vm, [new Dictionary<string, string> { ["05"] = "#112233", ["01"] = "#00AEC7" }]);
        Assert.HasCount(5, vm.KeyboardModeColorEditors);
        Assert.AreEqual((byte)5, vm.SelectedKeyboardModeColor!.PowerMode);
        Assert.AreEqual("#112233", vm.SelectedKeyboardModeColor.ColorHex);
        vm.SelectedKeyboardModeColor.ColorHex = "#445566";
        Assert.AreEqual("#00AEC7", vm.KeyboardModeColorEditors.Single(e => e.PowerMode == 1).ColorHex);
        Dictionary<string, string> Colors() => (Dictionary<string, string>)typeof(MainViewModel)
            .GetField("_keyboardPowerModeColors", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
        var persisted = JsonSerializer.Deserialize<LightingConfig>(JsonSerializer.Serialize(store.GetCurrent().Lighting))!;
        Assert.HasCount(1, persisted.PowerModeColors);
        Assert.AreEqual("#445566", persisted.PowerModeColors["05"]);
        vm.SelectedKeyboardModeColor.ColorHex = "#C700FF";
        Assert.IsEmpty(Colors(), "Returning to the factory default removes the persisted override.");
        Assert.IsEmpty(store.GetCurrent().Lighting.PowerModeColors);
        }
        finally { store.Save(original); }
    }
}
