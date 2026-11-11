using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using PreySense.Core.Configuration;
using PreySense.Core.Services;
using PreySense.Frontend.ViewModels;
using PreySense.Rgb;

namespace PreySense.Tests;

[TestClass]
[DoNotParallelize]
public class ExtendedLightingEditorTests
{
    [TestMethod]
    public void GroupColorEditsApplyAutomaticallyAndKeepUnselectedKeysUnchanged()
    {
        string id = "fake-group-" + Guid.NewGuid().ToString("N");
        var hardware = new FakeLightingHardware();
        var vm = Create(hardware);
        try
        {
            typeof(MainViewModel).GetField("_autoDeviceLightingEnabled", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, true);
            vm.SelectedLightingDevice = new(id, "Test keyboard", "Keyboard", [new(20, "A"), new(43, "B"), new(31, "C")], [new(-1, "Direct")], "Test only");
            Assert.IsFalse(hardware.Entered.IsSet, "Loading defaults must never write lighting.");
            vm.DeviceLightingIndividualColors = true;
            vm.SelectedLightingKeys.Add(vm.LightingLedEditors[0]);
            vm.SelectedLightingKeys.Add(vm.LightingLedEditors[2]);
            vm.SelectedLightingKey = vm.LightingLedEditors[0];
            string untouched = vm.LightingLedEditors[1].ColorHex;
            vm.LightingLedEditors[2].ColorHex = "#FF0000";
            vm.SetSelectedLightingKeysColor(vm.SelectedLightingKey.ColorHex);
            Assert.AreEqual(vm.SelectedLightingKey.ColorHex, vm.LightingLedEditors[2].ColorHex, "Picking the primary key's existing color still recolors the other selected keys.");
            vm.SelectedLightingKey.ColorHex = "#123456";
            vm.SelectedLightingKey.ColorHex = "#654321";
            Assert.AreEqual("#654321", vm.LightingLedEditors[2].ColorHex);
            Assert.AreEqual(untouched, vm.LightingLedEditors[1].ColorHex);
            hardware.Release.Set();
            vm.FlushExtendedLightingEditsAsync().WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            Assert.AreEqual("#654321", hardware.Applied!.LedColors[20]);
            Assert.AreEqual("#654321", hardware.Applied.LedColors[31]);
            Assert.AreEqual(untouched, hardware.Applied.LedColors[43]);
        }
        finally
        {
            hardware.Release.Set();
            vm.StopExtendedLightingAsync().GetAwaiter().GetResult();
            ConfigurationService.Instance.Update(config => config.Lighting.Devices.Remove(id));
        }
    }

    [TestMethod]
    public void ApplyingCapturesImmutableEditorStateAndSavesOnlyAfterHardwareSucceeds()
    {
        string id = "fake-lighting-" + Guid.NewGuid().ToString("N");
        var hardware = new FakeLightingHardware();
        var vm = Create(hardware);
        try
        {
            vm.SelectedLightingDevice = new(id, "Fake keyboard", "Keyboard", [new(4, "Key 4")], [new(-1, "Direct"), new(7, "Wave")], "Test only");
            vm.DeviceLightingColorHex = "#123456";
            vm.DeviceLightingIndividualColors = true;
            vm.LightingLedEditors[0].ColorHex = "#654321";
            Task apply = vm.ApplySelectedLightingDeviceAsync();
            Assert.IsTrue(hardware.Entered.Wait(TimeSpan.FromSeconds(3)));
            Assert.IsFalse(ConfigurationService.Instance.GetCurrent().Lighting.Devices.ContainsKey(id));
            vm.DeviceLightingColorHex = "#FFFFFF";
            vm.LightingLedEditors[0].ColorHex = "#000000";
            hardware.Release.Set();
            Assert.IsTrue(apply.Wait(TimeSpan.FromSeconds(3)));
            Assert.AreEqual("#123456", hardware.Applied!.ColorHex);
            Assert.AreEqual("#654321", hardware.Applied.LedColors[4]);
            Assert.AreEqual("#123456", ConfigurationService.Instance.GetCurrent().Lighting.Devices[id].ColorHex);
            vm.SelectedLightingDeviceMode = vm.LightingDeviceModes[1];
            Assert.IsFalse(vm.IsDirectLightingDeviceMode);
            Assert.IsFalse(vm.ShowIndividualLightingEditors);
        }
        finally
        {
            hardware.Release.Set();
            vm.StopExtendedLightingAsync().GetAwaiter().GetResult();
            ConfigurationService.Instance.Update(config => config.Lighting.Devices.Remove(id));
        }
    }

    [TestMethod]
    public void RejectedHardwareUpdateDoesNotCreateSavedDeviceProfile()
    {
        string id = "fake-lighting-" + Guid.NewGuid().ToString("N");
        var hardware = new FakeLightingHardware { Fail = true };
        hardware.Release.Set();
        var vm = Create(hardware);
        try
        {
            vm.SelectedLightingDevice = new(id, "Fake bar", "Lightbar", [], [new(-1, "Direct")], "Test only");
            Assert.IsTrue(vm.ApplySelectedLightingDeviceAsync().Wait(TimeSpan.FromSeconds(3)));
            Assert.IsFalse(ConfigurationService.Instance.GetCurrent().Lighting.Devices.ContainsKey(id));
        }
        finally { vm.StopExtendedLightingAsync().GetAwaiter().GetResult(); }
    }

    [TestMethod]
    public void LightBarStatic_SelectsFirstLedWithoutWritingAndEditsOnlyTheSelectedLed()
    {
        var hardware = new FakeLightingHardware();
        var vm = Create(hardware);
        try
        {
            vm.SelectedLightingDevice = new("sample-bar-" + Guid.NewGuid().ToString("N"), "Dynamic bar", "Light bar",
                Enumerable.Range(0, 13).Select(i => new LightingLedInfo(i, $"LED {i + 1}")).ToArray(),
                [new(-1, "Custom colors"), new(0, "Static")], "Sample");
            Assert.AreEqual("Static", vm.SelectedLightingDeviceMode!.Name);
            Assert.AreEqual(-1, vm.SelectedLightingDeviceMode.Value);
            Assert.HasCount(1, vm.LightingDeviceModes);
            Assert.AreSame(vm.LightingLedEditors[0], vm.SelectedLightingKey);
            Assert.IsFalse(hardware.Entered.IsSet);
            string original = vm.LightingLedEditors[0].ColorHex;
            vm.SelectedLightingKey = vm.LightingLedEditors[7];
            vm.SetSelectedLightingKeysColor("#FFFFFF");
            Assert.AreEqual("#FFFFFF", vm.LightingLedEditors[7].ColorHex);
            Assert.AreEqual(original, vm.LightingLedEditors[0].ColorHex);
            Assert.AreEqual("#111111", vm.LightingLedEditors[7].ForegroundHex);
        }
        finally { vm.StopExtendedLightingAsync().GetAwaiter().GetResult(); }
    }

    [TestMethod]
    [DataRow(4)] [DataRow(5)]
    public void ModeKeyIndependentEffects_ExposeAndSaveAnimationSpeed(int mode)
    {
        string id = "sample-mode-key-" + Guid.NewGuid().ToString("N");
        var hardware = new FakeLightingHardware();
        hardware.Release.Set();
        var vm = Create(hardware);
        try
        {
            vm.SelectedLightingDevice = new(id, "Mode Key", "Hotkey", [new(0, "LED 1")],
                [new(2, "Static"), new(4, "Breathing", AcerHidLightingProtocol.SupportsAnimationSpeed(0x65, 1, 5, 4), SpeedMaximum: 5),
                    new(5, "Neon", AcerHidLightingProtocol.SupportsAnimationSpeed(0x65, 1, 5, 5), SpeedMaximum: 5)], "Sample");
            vm.SelectedLightingDeviceMode = vm.LightingDeviceModes.Single(item => item.Value == mode);
            Assert.IsFalse(vm.IsModeKeySyncWithKeyboard);
            Assert.IsTrue(vm.HasDeviceLightingSpeed);
            vm.DeviceLightingSpeedLevel = 10; // Out-of-range requests must stop at the advertised maximum.
            Assert.AreEqual(1, vm.DeviceLightingSpeedMinimum);
            Assert.AreEqual(5, vm.DeviceLightingSpeedMaximum);
            Assert.AreEqual(5, vm.DeviceLightingSpeedLevel);
            Assert.IsTrue(vm.ApplySelectedLightingDeviceAsync().Wait(TimeSpan.FromSeconds(3)));
            Assert.AreEqual(mode, hardware.Applied!.Mode);
            Assert.AreEqual(100, hardware.Applied.Speed);
            Assert.AreEqual(5, hardware.Applied.AnimationSpeedLevel);
            Assert.AreEqual(5, ConfigurationService.Instance.GetCurrent().Lighting.Devices[id].AnimationSpeedLevel);
            Assert.IsFalse(hardware.Applied.SyncWithKeyboard);
        }
        finally
        {
            vm.StopExtendedLightingAsync().GetAwaiter().GetResult();
            ConfigurationService.Instance.Update(config => config.Lighting.Devices.Remove(id));
        }
    }

    [TestMethod]
    [DataRow("Keyboard", 4, true, false)]
    [DataRow("Keyboard", 4, false, true)]
    [DataRow("Keyboard", 8, true, true)]
    [DataRow("Lightbar", 4, true, true)]
    [DataRow("Logo", 1, true, false)]
    public async Task Restore_UsesOneAuthoritativeProfilePerLightingDevice(string kind, int leds, bool legacyAvailable, bool expectedWrite)
    {
        string id = "restore-" + Guid.NewGuid().ToString("N");
        var device = new LightingDeviceInfo(id, "Test", kind,
            Enumerable.Range(0, leds).Select(i => new LightingLedInfo(i, $"LED {i + 1}")).ToArray(), [new(-1, "Static")], "Test");
        var hardware = new FakeLightingHardware { IsRgbAvailable = legacyAvailable, Devices = [device] };
        hardware.Release.Set();
        var vm = Create(hardware);
        var store = ConfigurationService.Instance;
        try
        {
            store.Update(config => config.Lighting.Devices[id] = new() { ColorHex = "#123456" });
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var restore = (Task)typeof(MainViewModel).GetMethod("RestoreExtendedLightingAfterAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(vm, [ready.Task])!;
            Assert.IsFalse(hardware.Entered.IsSet, "Device restoration must wait for the primary keyboard operation.");
            ready.SetResult();
            await restore.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.AreEqual(expectedWrite, hardware.Entered.IsSet);
            if (expectedWrite) Assert.AreEqual("#123456", hardware.Applied!.ColorHex);
            Assert.AreEqual("#123456", store.GetCurrent().Lighting.Devices[id].ColorHex, "Loading must not replace a stored profile.");
        }
        finally { store.Update(config => config.Lighting.Devices.Remove(id)); }
    }

    [TestMethod]
    public void EditDuringRestoreDiscoveryWinsOnHardwareAndDisk()
    {
        string id = Guid.NewGuid().ToString("N");
        var device = new LightingDeviceInfo(id, "Bar", "Lightbar", [new(0, "LED")], [new(0, "Static")], "Test");
        using var discovering = new ManualResetEventSlim();
        using var finishDiscovery = new ManualResetEventSlim();
        var hardware = new FakeLightingHardware
        {
            Devices = [device],
            OnDiscover = () =>
            {
                discovering.Set();
                if (!finishDiscovery.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            }
        };
        hardware.Release.Set();
        var vm = Create(hardware);
        var store = ConfigurationService.Instance;
        Task restore = Task.CompletedTask;
        try
        {
            store.Update(config => config.Lighting.Devices[id] = new() { ColorHex = "#112233" });
            vm.SelectedLightingDevice = device;
            restore = (Task)typeof(MainViewModel).GetMethod("RestoreExtendedLightingAfterAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(vm, [Task.CompletedTask])!;
            Assert.IsTrue(discovering.Wait(TimeSpan.FromSeconds(3)));
            vm.DeviceLightingColorHex = "#ABCDEF";
            Assert.IsTrue(vm.ApplySelectedLightingDeviceAsync().Wait(TimeSpan.FromSeconds(3)));
            finishDiscovery.Set();
            Assert.IsTrue(restore.Wait(TimeSpan.FromSeconds(3)));
            Assert.AreEqual("#ABCDEF", hardware.Applied!.ColorHex);
            Assert.AreEqual("#ABCDEF", store.GetCurrent().Lighting.Devices[id].ColorHex);
        }
        finally
        {
            finishDiscovery.Set();
            restore.GetAwaiter().GetResult();
            vm.StopExtendedLightingAsync().GetAwaiter().GetResult();
            store.Update(config => config.Lighting.Devices.Remove(id));
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RestoreFailure_DoesNotEscapeTheBackgroundTaskOrChangeSavedSettings(bool keyboardStartupFails)
    {
        string id = "restore-failure-" + Guid.NewGuid().ToString("N");
        var hardware = new FakeLightingHardware { DiscoveryFails = !keyboardStartupFails };
        var vm = Create(hardware);
        var store = ConfigurationService.Instance;
        try
        {
            store.Update(config => config.Lighting.Devices[id] = new() { ColorHex = "#123456" });
            Task keyboard = keyboardStartupFails ? Task.FromException(new IOException("Simulated keyboard startup failure")) : Task.CompletedTask;
            var restore = (Task)typeof(MainViewModel).GetMethod("RestoreExtendedLightingAfterAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(vm, [keyboard])!;
            await restore.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.IsFalse(hardware.Entered.IsSet);
            Assert.AreEqual("#123456", store.GetCurrent().Lighting.Devices[id].ColorHex);
        }
        finally { store.Update(config => config.Lighting.Devices.Remove(id)); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PreviousDeviceCompletion_DoesNotReplaceTheCurrentDevicesStatus(bool fail)
    {
        string id = "status-" + Guid.NewGuid().ToString("N");
        var hardware = new FakeLightingHardware { Fail = fail };
        var vm = Create(hardware);
        try
        {
            vm.SelectedLightingDevice = new(id, "First bar", "Lightbar", [new(0, "LED 1")], [new(-1, "Static")], "First device details");
            Task apply = vm.ApplySelectedLightingDeviceAsync();
            Assert.IsTrue(hardware.Entered.Wait(TimeSpan.FromSeconds(3)));
            vm.SelectedLightingDevice = new(id + "-second", "Second bar", "Lightbar", [new(0, "LED 1")], [new(-1, "Static")], "Second device details");
            hardware.Release.Set();
            Assert.IsTrue(apply.Wait(TimeSpan.FromSeconds(3)));
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Assert.AreEqual("Second device details", vm.LightingDeviceStatus);
        }
        finally
        {
            hardware.Release.Set();
            vm.StopExtendedLightingAsync().GetAwaiter().GetResult();
            ConfigurationService.Instance.Update(config => config.Lighting.Devices.Remove(id));
        }
    }

    private static MainViewModel Create(FakeLightingHardware hardware)
    {
        // Bypass application startup, watchers and native hardware initialization entirely.
        var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        void Set(string field, object value) => typeof(MainViewModel).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, value);
        Set("_hw", hardware);
        Set("_dispatcher", Dispatcher.CurrentDispatcher);
        Set("<LightingDevices>k__BackingField", new ObservableCollection<LightingDeviceInfo>());
        Set("<LightingDeviceModes>k__BackingField", new ObservableCollection<LightingModeInfo>());
        Set("<LightingLedEditors>k__BackingField", new ObservableCollection<LightingLedEditor>());
        Set("<SelectedLightingKeys>k__BackingField", new ObservableCollection<LightingLedEditor>());
        Set("<ModePowerModeColorEditors>k__BackingField", new ObservableCollection<ModePowerModeColorEditor>());
        return vm;
    }

    private sealed class FakeLightingHardware : MockHardwareService, IExtendedLightingHardware
    {
        public ManualResetEventSlim Entered { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public bool Fail { get; init; }
        public bool DiscoveryFails { get; init; }
        public DeviceLightingConfig? Applied { get; private set; }
        public IReadOnlyList<LightingDeviceInfo> Devices { get; init; } = [];
        public Action? OnDiscover { get; init; }
        public IReadOnlyList<LightingDeviceInfo> DiscoverLightingDevices(bool refresh = false)
        {
            OnDiscover?.Invoke();
            return DiscoveryFails ? throw new IOException("Simulated lighting discovery failure") : Devices;
        }
        public void ApplyLightingDevice(string deviceId, DeviceLightingConfig settings)
        {
            Entered.Set();
            if (!Release.Wait(TimeSpan.FromSeconds(3))) throw new TimeoutException();
            if (Fail) throw new IOException("Simulated failure");
            Applied = settings;
        }
    }
}
