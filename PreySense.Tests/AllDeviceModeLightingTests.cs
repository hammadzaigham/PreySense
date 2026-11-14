using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using PreySense.Core.Configuration;
using PreySense.Core.Services;
using PreySense.Frontend.Controls;
using PreySense.Frontend.ViewModels;
using PreySense.Rgb;

namespace PreySense.Tests;

[TestClass]
[DoNotParallelize]
public class AllDeviceModeLightingTests
{
    [TestMethod]
    public void ConfirmedModeWaitsForConfiguredLightingDelayBeforePainting()
    {
        var store = ConfigurationService.Instance;
        var original = store.GetCurrent();
        Exception? failure = null;
        try
        {
            store.Update(cfg => { cfg.Lighting.Devices.Clear(); cfg.Lighting.PowerModeColors.Clear(); });
            var thread = new Thread(() =>
            {
                MainViewModel? vm = null;
                try
                {
                    var hardware = new LightingHardware(); // CurrentPowerMode stays Balanced.
                    vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
                    var fields = BindingFlags.NonPublic | BindingFlags.Instance;
                    typeof(MainViewModel).GetField("_hw", fields)!.SetValue(vm, hardware);
                    var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                    typeof(MainViewModel).GetField("_dispatcher", fields)!.SetValue(vm, dispatcher);
                    vm.ModeToastPresenter = (_, _) => { };
                    var frame = new System.Windows.Threading.DispatcherFrame();
                    var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
                    var elapsed = System.Diagnostics.Stopwatch.StartNew();
                    timer.Tick += (_, _) =>
                    {
                        lock (hardware.Writes)
                            if (hardware.Writes.Count > 0 || elapsed.Elapsed > ModeKeyLighting.EcModeTransitionDelay + TimeSpan.FromSeconds(2)) frame.Continue = false;
                    };
                    timer.Start();
                    vm.NotifyConfirmedPowerMode(4);
                    System.Windows.Threading.Dispatcher.PushFrame(frame);
                    timer.Stop();
                    lock (hardware.Writes)
                    {
                        Assert.IsNotEmpty(hardware.Writes);
                        Assert.IsTrue(elapsed.Elapsed >= ModeKeyLighting.EcModeTransitionDelay, "Lighting must wait for the configured transition delay.");
                        Assert.IsTrue(hardware.Writes.All(w => w.Settings.ColorHex == "#2E09D0"));
                    }
                    Assert.AreEqual((byte)1, hardware.CurrentPowerMode);
                }
                catch (Exception ex) { failure = ex; }
                finally
                {
                    if (vm is not null) typeof(MainViewModel).GetField("_disposed", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm, true);
                }
            });
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(5)));
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
        finally { store.Save(original); }
    }

    [TestMethod]
    [DataRow("Keyboard")]
    [DataRow("Hotkey")]
    [DataRow("Logo")]
    [DataRow("Light bar")]
    [DataRow("Lighting device")]
    public void EveryDeviceEditorOffersExactlyOneMode(string kind)
    {
        var device = new LightingDeviceInfo("fake", "Fake", kind, [new(0, "LED")], [new(0, "Static")], "");
        Assert.HasCount(1, device.GetEditorModes().Where(m => ModeKeyLighting.IsFollowMode(m.Value)).ToArray());
        var advertised = device with { Modes = [new(ModeKeyLighting.FollowOperatingMode, "Mode"), new(0, "Static")] };
        Assert.HasCount(1, advertised.GetEditorModes().Where(m => ModeKeyLighting.IsFollowMode(m.Value)).ToArray());
    }

    [TestMethod]
    public void NewAndMissingLightingPropertiesDefaultToModeWhileExplicitChoicesSurvive()
    {
        var lighting = System.Text.Json.JsonSerializer.Deserialize<LightingConfig>("{}")!;
        var device = System.Text.Json.JsonSerializer.Deserialize<DeviceLightingConfig>("{}")!;
        Assert.AreEqual("MODE", lighting.Keyboard.EffectTag);
        Assert.AreEqual("Mode", lighting.Keyboard.PresetTheme);
        Assert.AreEqual(4, lighting.Cover.Mode);
        Assert.AreEqual(ModeKeyLighting.FollowOperatingMode, device.Mode);
        var explicitDevice = System.Text.Json.JsonSerializer.Deserialize<DeviceLightingConfig>("{\"Mode\":-1}")!;
        var explicitCover = System.Text.Json.JsonSerializer.Deserialize<CoverLightingConfig>("{\"Mode\":2}")!;
        var explicitKeyboard = System.Text.Json.JsonSerializer.Deserialize<KeyboardLightingConfig>("{\"EffectTag\":\"HW:0\"}")!;
        Assert.AreEqual(-1, explicitDevice.Mode);
        Assert.AreEqual(2, explicitCover.Mode);
        Assert.AreEqual("HW:0", explicitKeyboard.EffectTag);
    }

    [TestMethod]
    public void MissingDeviceProfilesApplyModeWithoutCreatingConfigurationEntries()
    {
        var store = ConfigurationService.Instance;
        var original = store.GetCurrent();
        var hardware = new LightingHardware();
        try
        {
            store.Update(cfg => { cfg.Lighting.Devices.Clear(); cfg.Lighting.PowerModeColors.Clear(); });
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            typeof(MainViewModel).GetField("_hw", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm, hardware);
            vm.ActivePowerMode = 5;
            vm.ReapplyExtendedModeLighting();
            Assert.HasCount(5, hardware.Writes);
            Assert.IsTrue(hardware.Writes.All(w => w.Settings.Mode == ModeKeyLighting.FollowOperatingMode && w.Settings.ColorHex == "#C700FF"));
            Assert.IsEmpty(store.GetCurrent().Lighting.Devices);
        }
        finally { store.Save(original); }
    }

    [TestMethod]
    public void ReapplyWritesEveryModeDeviceEvenWhenModeAndColorAreUnchanged()
    {
        var store = ConfigurationService.Instance;
        var original = store.GetCurrent();
        var hardware = new LightingHardware();
        try
        {
            store.Update(cfg =>
            {
                cfg.Lighting.ModeBrightness = 40;
                cfg.Lighting.PowerModeColors = new() { ["01"] = "#123456", ["04"] = "#123456" };
                foreach (var device in hardware.Devices)
                    cfg.Lighting.Devices[device.Id] = new DeviceLightingConfig
                    {
                        Mode = device.Id == "static-test" ? 0 : ModeKeyLighting.FollowOperatingMode,
                        Brightness = 40,
                        PowerModeColors = new() { ["01"] = "#987654", ["04"] = "#987654" }
                    };
            });
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            typeof(MainViewModel).GetField("_hw", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm, hardware);
            vm.ActivePowerMode = 1;
            vm.ReapplyExtendedModeLighting();
            vm.ReapplyExtendedModeLighting();
            vm.ActivePowerMode = 4;
            vm.ReapplyExtendedModeLighting();
            Assert.HasCount(12, hardware.Writes);
            foreach (var kind in new[] { "Keyboard", "Logo", "Hotkey", "Light bar" })
                Assert.AreEqual(3, hardware.Writes.Count(w => w.DeviceId == kind));
            Assert.IsTrue(hardware.Writes.All(w => w.Settings.ColorHex == "#123456" && w.Settings.Brightness == 40));
            Assert.IsFalse(hardware.Writes.Any(w => w.DeviceId == "static-test"));
        }
        finally { store.Save(original); }
    }

    [TestMethod]
    public async Task SharedPaletteEditPersistsAndReappliesEveryModeDeviceButLeavesStaticAlone()
    {
        var store = ConfigurationService.Instance;
        var original = store.GetCurrent();
        var hardware = new LightingHardware();
        try
        {
            store.Update(cfg =>
            {
                cfg.Lighting.PowerModeColors.Clear();
                cfg.Lighting.Keyboard.PowerModeColors = new() { ["05"] = "#654321" };
                foreach (var device in hardware.Devices)
                    cfg.Lighting.Devices[device.Id] = new()
                    {
                        Mode = device.Id == "static-test" ? 0 : ModeKeyLighting.FollowOperatingMode,
                        ColorHex = "#111111", PowerModeColors = new() { ["05"] = "#987654" }
                    };
            });
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            typeof(MainViewModel).GetField("_hw", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm, hardware);
            typeof(MainViewModel).GetField("<KeyboardModeColorEditors>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(vm, new System.Collections.ObjectModel.ObservableCollection<ModePowerModeColorEditor>());
            vm.ActivePowerMode = 5;
            typeof(MainViewModel).GetMethod("LoadKeyboardModeColors", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(vm, [store.GetCurrent().Lighting.PowerModeColors]);
            vm.SelectedKeyboardModeColor!.ColorHex = "#123456";
            await vm.ApplySharedModePaletteAsync();
            Assert.AreEqual("#123456", store.GetCurrent().Lighting.PowerModeColors["05"]);
            Assert.HasCount(4, hardware.Writes);
            Assert.IsTrue(hardware.Writes.All(w => w.Settings.ColorHex == "#123456"));
            Assert.IsFalse(hardware.Writes.Any(w => w.DeviceId == "static-test"));
            Assert.AreEqual("#111111", store.GetCurrent().Lighting.Devices["static-test"].ColorHex);
            Assert.AreEqual("#987654", store.GetCurrent().Lighting.Devices["Keyboard"].PowerModeColors["05"], "Old individual overrides remain in the file but do not control shared Mode lighting.");
        }
        finally { store.Save(original); }
    }

    [TestMethod]
    public void KeyboardModeSyncUsesSharedPaletteAndBrightnessEvenWithLegacyStaticProfile()
    {
        var store = ConfigurationService.Instance;
        var original = store.GetCurrent();
        var hardware = new LightingHardware();
        try
        {
            store.Update(cfg =>
            {
                cfg.Lighting.Keyboard.EffectTag = "MODE";
                cfg.Lighting.Keyboard.Brightness = 90;
                cfg.Lighting.ModeBrightness = 35;
                cfg.Lighting.PowerModeColors = new() { ["05"] = "#123456" };
                foreach (var device in hardware.Devices)
                    cfg.Lighting.Devices[device.Id] = new() { Mode = 0 };
                cfg.Lighting.Devices["Hotkey"] = new() { Mode = 2, SyncWithKeyboard = true, Brightness = 80 };
            });
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            typeof(MainViewModel).GetField("_hw", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm, hardware);
            vm.ActivePowerMode = 5;
            vm.ReapplyExtendedModeLighting(includeOtherHotkeyProfiles: false);
            var write = hardware.Writes.Single();
            Assert.AreEqual("Hotkey", write.DeviceId);
            Assert.AreEqual(ModeKeyLighting.FollowOperatingMode, write.Settings.Mode);
            Assert.AreEqual("#123456", write.Settings.ColorHex);
            Assert.AreEqual(35, write.Settings.Brightness);
            Assert.AreEqual(2, store.GetCurrent().Lighting.Devices["Hotkey"].Mode);
        }
        finally { store.Save(original); }
    }

    [TestMethod]
    public async Task SharedBrightnessPersistsReappliesOnlyModeAndUsesFnRungs()
    {
        var store = ConfigurationService.Instance;
        var original = store.GetCurrent();
        var hardware = new LightingHardware();
        try
        {
            store.Update(cfg =>
            {
                cfg.Lighting.ModeBrightness = 100;
                foreach (var device in hardware.Devices)
                    cfg.Lighting.Devices[device.Id] = new()
                    {
                        Mode = device.Id == "static-test" ? 0 : ModeKeyLighting.FollowOperatingMode,
                        Brightness = 22
                    };
            });
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            typeof(MainViewModel).GetField("_hw", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm, hardware);
            vm.ModeLightingBrightness = 60;
            Assert.AreEqual(60, store.GetCurrent().Lighting.ModeBrightness);
            vm.StepSharedModeBrightness(-1);
            Assert.AreEqual(50, vm.ModeLightingBrightness);
            vm.StepSharedModeBrightness(-1);
            Assert.AreEqual(25, vm.ModeLightingBrightness);
            await vm.ApplySharedModePaletteAsync();
            Assert.HasCount(4, hardware.Writes);
            Assert.IsTrue(hardware.Writes.All(w => w.Settings.Brightness == 25));
            Assert.AreEqual(22, store.GetCurrent().Lighting.Devices["static-test"].Brightness);
            Assert.AreEqual(22, store.GetCurrent().Lighting.Devices["Keyboard"].Brightness, "Shared brightness must not overwrite the saved per-effect brightness.");
            Assert.AreEqual(25, store.GetCurrent().Lighting.ModeBrightness);
            var config = System.Text.Json.JsonSerializer.Deserialize<LightingConfig>("{}")!;
            Assert.AreEqual(100, config.ModeBrightness);
        }
        finally { store.Save(original); }
    }

    [TestMethod]
    public void SaturatedColorWheelThumbRemainsInsideControlAtEveryHue()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var wheel = new ColorWheelControl();
                wheel.Measure(new Size(180, 180));
                wheel.Arrange(new Rect(0, 0, 180, 180));
                // SizeChanged normally runs during WPF layout; invoke its handler explicitly
                // without starting an application or any native hardware services.
                var center = typeof(ColorWheelControl).GetField("_center", BindingFlags.NonPublic | BindingFlags.Instance)!;
                var radius = typeof(ColorWheelControl).GetField("_radius", BindingFlags.NonPublic | BindingFlags.Instance)!;
                var inset = (double)typeof(ColorWheelControl).GetField("WheelInset", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;
                center.SetValue(wheel, new Point(90, 90));
                radius.SetValue(wheel, 90 - inset);
                var position = typeof(ColorWheelControl).GetMethod("GetPositionFromColor", BindingFlags.NonPublic | BindingFlags.Instance)!;
                for (int hue = 0; hue < 360; hue++)
                {
                    var point = (Point)position.Invoke(wheel, [ColorWheelControl.ColorFromHsv(hue, 1, 1)])!;
                    Assert.IsTrue(point.X - 8.25 >= 0 && point.X + 8.25 <= 180);
                    Assert.IsTrue(point.Y - 8.25 >= 0 && point.Y + 8.25 <= 180);
                }
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class LightingHardware : MockHardwareService, IExtendedLightingHardware, IHardwareService
    {
        public IReadOnlyList<LightingDeviceInfo> Devices { get; } = new[] { "Keyboard", "Logo", "Hotkey", "Light bar", "static-test" }
            .Select(kind => new LightingDeviceInfo(kind, kind, kind, [new(0, "LED")], [new(ModeKeyLighting.FollowOperatingMode, "Mode"), new(0, "Static")], "")).ToArray();
        public new bool IsCoverLightingAvailable => false;
        public List<(string DeviceId, DeviceLightingConfig Settings)> Writes { get; } = new();
        public IReadOnlyList<LightingDeviceInfo> DiscoverLightingDevices(bool refresh = false) => Devices;
        public void ApplyLightingDevice(string deviceId, DeviceLightingConfig settings) { lock (Writes) Writes.Add((deviceId, settings)); }
    }
}
