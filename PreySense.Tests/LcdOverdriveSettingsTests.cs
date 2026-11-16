using System.Reflection;
using System.Runtime.CompilerServices;
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
        await vm.RefreshLcdOverdriveAsync();
        await vm.ToggleLcdOverdriveCommand.ExecuteAsync(null);
        Assert.IsFalse(vm.CanChangeLcdOverdrive);
        Assert.IsFalse(vm.IsLcdOverdriveEnabled);
        Assert.AreEqual(0, panel.Writes);
    }

    [TestMethod]
    public async Task SupportedPanelReflectsReadbackAndDisablesAfterFailedVerification()
    {
        var panel = new Panel { Available = true };
        var vm = Create(panel);
        await vm.RefreshLcdOverdriveAsync();
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

    private sealed class Panel : MockHardwareService, IHardwareService
    {
        public bool Available, IgnoreWrites, Enabled;
        public int Writes;
        public new bool TryGetLcdOverdrive(out bool enabled) { enabled = Enabled; return Available; }
        public new bool SetLcdOverdrive(bool enabled)
        {
            Writes++;
            if (!IgnoreWrites) Enabled = enabled;
            return true;
        }
    }
}
