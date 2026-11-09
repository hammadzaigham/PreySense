using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using PreySense.Core.Services;
using PreySense.Frontend.ViewModels;
using PreySense.Gpu;

namespace PreySense.Tests;

[TestClass]
[DoNotParallelize]
public class GpuDisplayStateTests
{
    [TestMethod]
    public void ReturningToAutoNotifiesIgpuTileVisibility()
    {
        var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        vm.ActiveGpuMuxMode = 2;
        Assert.IsFalse(vm.ShowIgpuOnlyTile);
        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        vm.ActiveGpuMuxMode = 1;
        Assert.IsTrue(vm.ShowIgpuOnlyTile);
        Assert.AreEqual(5, vm.GpuModeTileColumns);
        CollectionAssert.Contains(changes, nameof(MainViewModel.ShowIgpuOnlyTile));
        CollectionAssert.Contains(changes, nameof(MainViewModel.GpuModeTileColumns));

        vm.ActiveGpuMuxMode = 2; // Advanced Optimus / NVIDIA Only
        Assert.IsFalse(vm.ShowIgpuOnlyTile, "iGPU tile must hide in Advanced Optimus mode.");
        Assert.AreEqual(4, vm.GpuModeTileColumns, "Buttons must expand to 4 columns to fit entire space in Advanced Optimus mode.");
    }

    [TestMethod]
    public void IgpuOnlyActiveHidesAdvancedOptimusAndDgpuOnly()
    {
        var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        vm.ActiveGpuMuxMode = 1; // Auto
        Assert.IsTrue(vm.ShowIgpuOnlyTile);
        Assert.IsTrue(vm.ShowAdvancedOptimusTile);
        Assert.IsTrue(vm.ShowDgpuOnlyTile);
        Assert.IsTrue(vm.ShowDdsGpuTiles);
        Assert.AreEqual(5, vm.GpuModeTileColumns);

        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        vm.IsGpuIgpuOnlyActive = true;
        Assert.IsTrue(vm.ShowIgpuOnlyTile);
        Assert.IsTrue(vm.ShowDdsGpuTiles);
        Assert.IsFalse(vm.ShowAdvancedOptimusTile);
        Assert.IsFalse(vm.ShowDgpuOnlyTile);
        Assert.AreEqual(3, vm.GpuModeTileColumns); // Auto, iGPU Only, Hybrid
        CollectionAssert.Contains(changes, nameof(MainViewModel.ShowAdvancedOptimusTile));
        CollectionAssert.Contains(changes, nameof(MainViewModel.ShowDgpuOnlyTile));
        CollectionAssert.Contains(changes, nameof(MainViewModel.GpuModeTileColumns));

        vm.IsGpuIgpuOnlyActive = false;
        Assert.IsTrue(vm.ShowAdvancedOptimusTile);
        Assert.IsTrue(vm.ShowDgpuOnlyTile);
        Assert.AreEqual(5, vm.GpuModeTileColumns);
    }

    [TestMethod]
    public void AutomaticSelectionAndPhysicalDisplayRoutingAreDifferentStates()
    {
        var autoIgpu = new DdsStatus(true, true, DdsActiveMux.Optimus, 0, 0);
        var autoDgpu = autoIgpu with { ActiveMux = DdsActiveMux.dGPU };
        Assert.IsTrue(autoIgpu.Matches(DdsDisplayMode.Automatic));
        Assert.IsTrue(autoDgpu.Matches(DdsDisplayMode.Automatic));
        Assert.IsFalse(autoDgpu.Matches(DdsDisplayMode.NvidiaGpuOnly));
    }

    [TestMethod]
    public void UnconfirmedOrFailedDriverStatesNeverMatchARequestedMode()
    {
        var status = new DdsStatus(true, false, DdsActiveMux.dGPU, 0, 0);
        Assert.IsTrue(status.Matches(DdsDisplayMode.NvidiaGpuOnly));
        Assert.IsFalse(status.Matches(DdsDisplayMode.Optimus));
        Assert.IsFalse((status with { KmdResponse = 1 }).Matches(DdsDisplayMode.NvidiaGpuOnly));
        Assert.IsFalse((status with { IsSupported = false }).Matches(DdsDisplayMode.NvidiaGpuOnly));
        Assert.IsFalse((status with { IsAutomatic = true, ActiveMux = DdsActiveMux.Unknown }).Matches(DdsDisplayMode.Automatic));
    }

    [TestMethod]
    public async Task DisplaySwitchWaitsForSensorReadsAndPreventsNewOnes()
    {
        var reader = HardwareOperationGate.TryEnterTelemetry();
        Assert.IsNotNull(reader);
        var switching = HardwareOperationGate.EnterDisplaySwitchAsync();
        Assert.IsFalse(switching.IsCompleted);
        reader.Dispose();
        using (await switching)
            Assert.IsNull(HardwareOperationGate.TryEnterTelemetry());
        // Disposing an old read lease twice must not unlock someone else's work.
        reader.Dispose();
        using var resumed = HardwareOperationGate.TryEnterTelemetry();
        Assert.IsNotNull(resumed);
        Assert.IsNull(HardwareOperationGate.TryEnterTelemetry());
    }

    [TestMethod]
    public async Task RejectedGpuSwitchKeepsTheConfirmedSelection()
    {
        var hardware = new RejectingDds();
        var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        typeof(MainViewModel).GetField("_hw", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm, hardware);
        typeof(MainViewModel).GetField("_activeGpuMuxMode", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm, 1);
        await vm.SetGpuMuxModeAsync(2);
        Assert.AreEqual(1, hardware.Requests);
        Assert.AreEqual(1, vm.ActiveGpuMuxMode);
        Assert.IsTrue(vm.IsGpuAutoActive);
        Assert.IsFalse(vm.IsGpuNvidiaOnlyActive);
        Assert.Contains("blocked by test driver", vm.DiagnosticMessage);
    }

    [TestMethod]
    [DoNotParallelize]
    [DataRow(DdsActiveMux.Optimus)]
    [DataRow(DdsActiveMux.dGPU)]
    public async Task AlreadyAutomaticLiveDriverState_DoesNotCommitOrRefreshAdapters(DdsActiveMux route)
    {
        var hardware = new RecordingDds(new(true, true, route, 0, 0));
        var store = PreySense.Core.Configuration.ConfigurationService.Instance;
        var original = store.GetCurrent();
        try
        {
            var vm = CreateDdsViewModel(hardware);
            await vm.SetGpuMuxModeAsync(1);
            Assert.AreEqual(0, hardware.Requests);
            Assert.AreEqual(1, hardware.StatusReads);
            Assert.AreEqual(0, hardware.SupportReads);
            Assert.AreEqual(0, hardware.AdapterRefreshes);
            Assert.IsTrue(vm.IsGpuAutoActive);
            Assert.IsFalse(vm.IsBusy);
        }
        finally { store.Save(original); }
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task CachedAutomaticSelection_DoesNotSkipARequiredLiveDriverChange()
    {
        var hardware = new RecordingDds(new(true, false, DdsActiveMux.Optimus, 0, 0));
        var store = PreySense.Core.Configuration.ConfigurationService.Instance;
        var original = store.GetCurrent();
        try
        {
            var vm = CreateDdsViewModel(hardware);
            await vm.SetGpuMuxModeAsync(1);
            Assert.AreEqual(1, hardware.Requests);
            Assert.AreEqual(2, hardware.StatusReads, "One live preflight and one required post-commit confirmation.");
            Assert.AreEqual(0, hardware.SupportReads);
            Assert.AreEqual(1, hardware.AdapterRefreshes);
            Assert.IsTrue(vm.IsGpuAutoActive);
        }
        finally { store.Save(original); }
    }

    private static MainViewModel CreateDdsViewModel(IHardwareService hardware)
    {
        var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        typeof(MainViewModel).GetField("_hw", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm, hardware);
        typeof(MainViewModel).GetField("_activeGpuMuxMode", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm, 1);
        typeof(MainViewModel).GetField("<AvailableRefreshRates>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(vm, new System.Collections.ObjectModel.ObservableCollection<string>());
        typeof(MainViewModel).GetField("_dispatcher", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(vm, Dispatcher.CurrentDispatcher);
        return vm;
    }

    private sealed class RecordingDds(DdsStatus status) : MockHardwareService, IHardwareService
    {
        public int Requests, StatusReads, SupportReads, AdapterRefreshes;
        public new DdsStatus? GetDdsStatus() { StatusReads++; return status; }
        public new bool IsDdsSupported() { SupportReads++; return status.IsSupported; }
        public void RefreshGpuAdapter() => AdapterRefreshes++;
        public new (bool Success, string Message) SetDdsMode(DdsDisplayMode mode)
        {
            Requests++;
            status = new(true, mode == DdsDisplayMode.Automatic,
                mode == DdsDisplayMode.NvidiaGpuOnly ? DdsActiveMux.dGPU : DdsActiveMux.Optimus, 0, 0);
            return (true, "applied");
        }
    }

    private sealed class RejectingDds : MockHardwareService, IHardwareService
    {
        public int Requests;
        public new (bool Success, string Message) SetDdsMode(DdsDisplayMode mode)
        {
            Requests++;
            return (false, "blocked by test driver");
        }
    }

    [TestMethod]
    [DataRow(false, false, false, 1)]
    [DataRow(true, true, true, 1)]
    [DataRow(true, false, true, 2)]
    [DataRow(false, true, false, 2)]
    [DataRow(true, false, false, 3)]
    public async Task DeviceChecksStopWhenWindowsConfirmsTheExpectedState(bool expected, bool first, bool next, int expectedReads)
    {
        var hardware = new DeviceStateSequence(first, next);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
                    typeof(MainViewModel).GetField("_hw", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm, hardware);
                    typeof(MainViewModel).GetField("_dispatcher", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm, dispatcher);
                    await (Task)typeof(MainViewModel).GetMethod("RefreshGpuDeviceModeStateAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
                        .Invoke(vm, new object?[] { 3, false, expected })!;
                    completed.SetResult();
                }
                catch (Exception ex) { completed.SetException(ex); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(expectedReads, hardware.Reads);
    }

    private sealed class DeviceStateSequence(bool first, bool next) : MockHardwareService, IHardwareService
    {
        public int Reads;
        public new bool? IsDiscreteGpuDisabled => Interlocked.Increment(ref Reads) == 1 ? first : next;
    }
}
