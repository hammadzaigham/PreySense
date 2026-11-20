using System.Reflection;
using System.Runtime.CompilerServices;
using PreySense.Core.Configuration;
using PreySense.Core.Services;
using PreySense.Frontend.ViewModels;

namespace PreySense.Tests;

[TestClass]
public class LcdOverdriveSettingsTests
{
    private static MainViewModel Create(Panel panel)
    {
        var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        typeof(MainViewModel).GetField("_hw", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm, panel);
        return vm;
    }

    [TestMethod]
    public async Task UnavailablePanelStaysDisabledWithoutWrites()
    {
        var panel = new Panel { Available = false };
        var vm = Create(panel);
        Assert.IsFalse(vm.CanChangeLcdOverdrive);
        await vm.InitializeLcdOverdriveAsync();
        await vm.ToggleLcdOverdriveCommand.ExecuteAsync(null);
        Assert.IsFalse(vm.CanChangeLcdOverdrive);
        Assert.IsFalse(vm.IsLcdOverdriveAvailable);
        Assert.IsFalse(vm.IsLcdOverdriveEnabled);
        Assert.AreEqual(0, panel.Writes);
    }

    [TestMethod]
    public async Task SupportedPanelReflectsReadbackAndDisablesAfterFailedVerification()
    {
        var panel = new Panel { Available = true };
        var vm = Create(panel);
        await vm.InitializeLcdOverdriveAsync();
        Assert.IsTrue(vm.CanChangeLcdOverdrive);
        Assert.AreEqual(0, panel.Writes);
        await vm.ToggleLcdOverdriveCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.IsLcdOverdriveEnabled);
        Assert.AreEqual(1, panel.Writes);
        panel.IgnoreWrites = true;
        await vm.ToggleLcdOverdriveCommand.ExecuteAsync(null);
        Assert.IsFalse(vm.CanChangeLcdOverdrive);
        await vm.ToggleLcdOverdriveCommand.ExecuteAsync(null);
        Assert.AreEqual(2, panel.Writes);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task StartupReusesSavedCapabilityAndLastVerifiedState(bool supported)
    {
        string path = Path.Combine(Path.GetTempPath(), $"PreySense-Lcd-{Guid.NewGuid():N}.json");
        try
        {
            var panel = new Panel { Available = supported };
            var vm = Create(panel);
            await vm.InitializeLcdOverdriveAsync(new ConfigurationService(path));
            Assert.AreEqual(1, panel.Reads);
            if (supported)
                await vm.ToggleLcdOverdriveCommand.ExecuteAsync(null);

            var store = new ConfigurationService(path);
            Assert.AreEqual(supported, store.GetCurrent().Display.LcdOverdriveSupported);
            var nextPanel = new Panel();
            var nextVm = Create(nextPanel);
            await nextVm.InitializeLcdOverdriveAsync(store);
            Assert.AreEqual(0, nextPanel.Reads);
            Assert.AreEqual(supported, nextVm.IsLcdOverdriveAvailable);
            Assert.AreEqual(supported, nextVm.IsLcdOverdriveEnabled);
        }
        finally { File.Delete(path); }
    }

    private sealed class Panel : MockHardwareService, IHardwareService
    {
        public bool Available, IgnoreWrites, Enabled;
        public int Reads, Writes;
        public new bool TryGetLcdOverdrive(out bool enabled)
        {
            Reads++;
            enabled = Enabled;
            return Available;
        }
        public new bool SetLcdOverdrive(bool enabled)
        {
            Writes++;
            if (!IgnoreWrites) Enabled = enabled;
            return true;
        }
    }
}
