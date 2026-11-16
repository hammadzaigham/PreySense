using System.Reflection;
using System.Runtime.CompilerServices;
using PreySense.Frontend.ViewModels;

namespace PreySense.Tests;

[TestClass]
public class ModeSwitchShutdownTests
{
    [TestMethod]
    public async Task ShutdownYieldsUntilActiveApplyFinishesAndRejectsNewEntries()
    {
        var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        using var gate = new SemaphoreSlim(0, 1);
        typeof(MainViewModel).GetField("_modeSwitchGate", flags)!.SetValue(vm, gate);
        typeof(MainViewModel).GetField("_modeSwitchLock", flags)!.SetValue(vm, new object());
        Task shutdown = vm.PrepareModeSwitchShutdownAsync();
        Assert.IsFalse(shutdown.IsCompleted, "Shutdown must await the in-flight holder without blocking its caller.");
        var enter = (Task<bool>)typeof(MainViewModel).GetMethod("TryEnterModeSwitchGateAsync", flags)!.Invoke(vm, null)!;
        Assert.IsFalse(await enter);
        gate.Release();
        await shutdown.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.AreEqual(1, gate.CurrentCount);
        Assert.IsTrue((bool)typeof(MainViewModel).GetField("_modeSwitchShutdownPrepared", flags)!.GetValue(vm)!);
        enter = (Task<bool>)typeof(MainViewModel).GetMethod("TryEnterModeSwitchGateAsync", flags)!.Invoke(vm, null)!;
        Assert.IsFalse(await enter);
    }
}
