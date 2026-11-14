using System.Reflection;
using System.Runtime.CompilerServices;
using PreySense.Core.Services;
using PreySense.Frontend.ViewModels;

namespace PreySense.Tests;

[TestClass]
public class PowerModeEventEfficiencyTests
{
    private static MainViewModel Create(RecordingHardware hardware)
    {
        var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        Set(vm, "_hw", hardware);
        Set(vm, "_modeSwitchGate", new SemaphoreSlim(1, 1));
        Set(vm, "_modeSwitchLock", new object());
        Set(vm, "_pendingHardwareMode", (byte)0xFF);
        Set(vm, "_activePowerMode", (byte)1);
        return vm;
    }

    private static void Set(MainViewModel vm, string name, object value) => typeof(MainViewModel)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, value);

    [TestMethod]
    [DataRow("same")]
    [DataRow("pending")]
    [DataRow("physical-key")]
    [DataRow("busy")]
    [DataRow("restoring")]
    [DataRow("disposed")]
    public async Task LocalEchoesAvoidNativeModeRead(string reason)
    {
        var hardware = new RecordingHardware();
        var vm = Create(hardware);
        byte mode = 4;
        switch (reason)
        {
            case "same": mode = 1; break;
            case "pending": Set(vm, "_pendingHardwareMode", (byte)5); break;
            case "physical-key": Set(vm, "_lastPhysicalModeKeyUtc", DateTime.UtcNow); break;
            case "busy": Set(vm, "_isBusy", true); break;
            case "restoring": Set(vm, "_isRestoringSession", true); break;
            case "disposed": Set(vm, "_disposed", true); break;
        }
        await vm.ReconcilePowerModeChangeAsync(mode);
        Assert.AreEqual(0, hardware.Reads);
    }

    [TestMethod]
    public async Task ExternalEventStillReadsNativeModeBeforeReconciliation()
    {
        var hardware = new RecordingHardware();
        await Create(hardware).ReconcilePowerModeChangeAsync(4);
        Assert.AreEqual(1, hardware.Reads);
    }

    [TestMethod]
    public async Task LocalModeChangeDuringReadStillSuppressesStaleEvent()
    {
        var hardware = new RecordingHardware { BlockRead = true };
        var vm = Create(hardware);
        Task reconcile = vm.ReconcilePowerModeChangeAsync(4);
        try
        {
            Assert.IsTrue(hardware.Started.Wait(TimeSpan.FromSeconds(5)));
            Set(vm, "_activePowerMode", (byte)4);
        }
        finally { hardware.Release.Set(); }
        await reconcile;
        Assert.AreEqual(1, hardware.Reads);
        Assert.AreEqual(0, hardware.Writes);
    }

    private sealed class RecordingHardware : MockHardwareService, IHardwareService
    {
        public int Reads, Writes;
        public bool BlockRead;
        public ManualResetEventSlim Started { get; } = new(false);
        public ManualResetEventSlim Release { get; } = new(false);
        public bool TryGetPowerMode(out byte mode)
        {
            Interlocked.Increment(ref Reads);
            mode = 4;
            Started.Set();
            if (BlockRead && !Release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            return BlockRead;
        }
        public new bool SetPowerMode(byte mode) { Interlocked.Increment(ref Writes); return true; }
    }
}
