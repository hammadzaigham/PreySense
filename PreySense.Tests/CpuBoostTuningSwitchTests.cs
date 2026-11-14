using System.Reflection;
using System.Runtime.CompilerServices;
using PreySense.Core.Configuration;
using PreySense.Core.Services;
using PreySense.Frontend.ViewModels;

namespace PreySense.Tests;

[TestClass]
public class CpuBoostTuningSwitchTests
{
    [TestMethod]
    [DataRow(true, true, 0)]
    [DataRow(true, false, 0)]
    [DataRow(false, true, 0)]
    [DataRow(true, true, 4)]
    [DataRow(true, false, 6)]
    public async Task SaveBoostIndependentlyOfEveryOtherApplySwitch(bool enabled, bool active, int boost)
    {
        string folder = Path.Combine(Path.GetTempPath(), "PreySenseBoost-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var store = new ConfigurationService(Path.Combine(folder, "preysense.json"));
            store.Update(config => { config.Tuning.Turbo.CpuBoostMode = 1; config.Tuning.Turbo.CpuBoost = true; });
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            void Set(string field, object value) => typeof(MainViewModel)
                .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, value);
            var hardware = new MockHardwareService();
            hardware.SetPowerMode(active ? (byte)5 : (byte)1);
            Set("_hw", hardware);
            Set("_modeSwitchGate", new SemaphoreSlim(1, 1));
            Set("_modeSwitchLock", new object());
            Set("_isHardwareTuningEnabled", true);
            vm.TuningConfigurationStore = store;
            vm.SetCpuBoostForModeIndex(4, boost);
            int writes = 0;
            vm.CpuBoostWriter = value => { writes++; Assert.AreEqual(boost, value); return true; };
            vm.DriverTuningExecutor = _ => throw new AssertFailedException("Boost-only save must skip CPU/GPU driver writes.");
            Assert.IsTrue(await vm.ApplyTuningProfileAsync(5, 0, 0, 0, null, persist: true,
                applyCpuLimits: false, applyCpuBoost: enabled), vm.DiagnosticMessage);
            var saved = new ConfigurationService(Path.Combine(folder, "preysense.json")).GetCurrent().Tuning.Turbo;
            Assert.AreEqual(enabled, saved.ApplyCpuBoost);
            Assert.AreEqual(enabled ? boost : 1, saved.CpuBoostMode);
            Assert.AreEqual(!enabled || boost > 0, saved.CpuBoost);
            Assert.AreEqual(enabled && active ? 1 : 0, writes);
            Assert.IsFalse(saved.ApplyCpuLimits);
            Assert.IsNull(saved.GpuRuntimeLimitWatts);
            Assert.IsFalse(saved.ApplyGpuClockOffsets);
            Assert.AreEqual(active ? (byte)5 : (byte)1, hardware.CurrentPowerMode);
        }
        finally { Directory.Delete(folder, true); }
    }

    [TestMethod]
    public void DisabledBoostIsSkippedOnModeRestore()
    {
        var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        vm.TuningConfigurationReader = () => new() { Tuning = new() { EnableCpuBoostTuningControl = false } };
        vm.CpuBoostWriter = _ => throw new AssertFailedException("Disabled boost must not write Windows power policy.");
        Assert.IsTrue(vm.ApplyCpuBoostForMode(5));
    }
    [TestMethod]
    [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)] [DataRow(4)] [DataRow(5)] [DataRow(6)]
    public void GlobalBoostSwitchAppliesEveryModePolicyEveryTimeIgnoringLegacyFlags(int boost)
    {
        var cfg = new PreySenseConfig(); cfg.Tuning.EnableCpuBoostTuningControl = true;
        var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        vm.TuningConfigurationReader = () => cfg;
        int writes = 0;
        vm.CpuBoostWriter = value => { writes++; Assert.AreEqual(boost, value); return true; };
        for (int index = 0; index < 5; index++)
        {
            vm.SetCpuBoostForModeIndex(index, boost);
            byte mode = MainViewModel.ModeIndexToByte(index);
            Assert.IsTrue(vm.ApplyCpuBoostForMode(mode));
            Assert.IsTrue(vm.ApplyCpuBoostForMode(mode), "Matching policy values must still be written.");
        }
        Assert.AreEqual(10, writes);
        cfg.Tuning.EnableCpuBoostTuningControl = false;
        cfg.Tuning.Turbo.ApplyCpuBoost = true;
        Assert.IsTrue(vm.ApplyCpuBoostForMode(5));
        Assert.AreEqual(10, writes, "The global off switch overrides legacy per-mode flags.");
    }

    [TestMethod]
    public void BoostToggleLivesInTuningSettingsWhileModesKeepPolicyChoice()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "PreySense.slnx"))) root = root.Parent;
        Assert.IsNotNull(root);
        string settings = File.ReadAllText(Path.Combine(root.FullName, "PreySense.Frontend", "Views", "TuningSettingsWindow.xaml"));
        string modes = File.ReadAllText(Path.Combine(root.FullName, "PreySense.Frontend", "Views", "ModeSettingsWindow.xaml"));
        Assert.Contains("EnableCpuBoostTuningControl", settings);
        Assert.DoesNotContain("SwitchApplyCpuBoost", modes);
        Assert.Contains("DropdownCpuBoost", modes);
    }

    [TestMethod]
    public void CpuBoostDefaultsOnAndPreservesExplicitOff()
    {
        Assert.IsTrue(new TuningConfig().EnableCpuBoostTuningControl);
        Assert.IsTrue(System.Text.Json.JsonSerializer.Deserialize<PreySenseConfig>("{\"Tuning\":{}}")!.Tuning.EnableCpuBoostTuningControl);
        Assert.IsFalse(System.Text.Json.JsonSerializer.Deserialize<PreySenseConfig>("{\"Tuning\":{\"EnableCpuBoostTuningControl\":false}}")!.Tuning.EnableCpuBoostTuningControl);
    }

    [TestMethod]
    public void EnablingBoostImmediatelyReenablesThePolicyDropdown()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            string folder = Path.Combine(Path.GetTempPath(), "PreySenseBoostBinding-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var root = new DirectoryInfo(AppContext.BaseDirectory);
                while (root is not null && !File.Exists(Path.Combine(root.FullName, "PreySense.slnx"))) root = root.Parent;
                var document = System.Xml.Linq.XDocument.Load(Path.Combine(root!.FullName, "PreySense.Frontend", "Views", "ModeSettingsWindow.xaml"));
                var element = document.Descendants().Single(e => (string?)e.Attribute(System.Xml.Linq.XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "DropdownCpuBoost");
                Assert.AreEqual("{Binding EnableCpuBoostTuningControl}", (string?)element.Attribute("IsEnabled"));
                var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
                vm.TuningConfigurationStore = new ConfigurationService(Path.Combine(folder, "preysense.json"));
                vm.EnableCpuBoostTuningControl = false;
                var row = new PreySense.Frontend.Controls.SettingDropdownRow { DataContext = vm };
                System.Windows.Data.BindingOperations.SetBinding(row, System.Windows.UIElement.IsEnabledProperty,
                    new System.Windows.Data.Binding(nameof(MainViewModel.EnableCpuBoostTuningControl)));
                Assert.IsFalse(row.IsEnabled);
                vm.EnableCpuBoostTuningControl = true;
                row.GetBindingExpression(System.Windows.UIElement.IsEnabledProperty)!.UpdateTarget();
                Assert.IsTrue(row.IsEnabled);
                Assert.IsTrue(row.ComboBox.IsEnabled);
                Assert.IsTrue(vm.TuningConfigurationStore.GetCurrent().Tuning.EnableCpuBoostTuningControl);
            }
            catch (Exception ex) { failure = ex; }
            finally { Directory.Delete(folder, true); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(10)));
        if (failure is not null) throw new AssertFailedException("CPU boost dropdown failed to reenable.", failure);
    }

}
