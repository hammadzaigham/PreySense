using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using PreySense.Core.Services;
using PreySense.Frontend.ViewModels;

namespace PreySense.Tests;

[TestClass]
[DoNotParallelize]
public class RefreshRateReadbackTests
{
    private static MainViewModel Create(DisplayHardware hardware)
    {
        var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        void Set(string field, object value) => typeof(MainViewModel).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm, value);
        Set("_hw", hardware);
        Set("_currentRefreshRate", 240);
        Set("<AvailableRefreshRates>k__BackingField", new ObservableCollection<string> { "240 Hz", "120 Hz", "60 Hz" });
        return vm;
    }

    [TestMethod]
    public async Task RejectedRequestRestoresTheActualSelection()
    {
        var vm = Create(new DisplayHardware { Accept = false });
        await vm.SetRefreshRateAsync(120);
        Assert.AreEqual(240, vm.CurrentRefreshRate);
        Assert.AreEqual("240 Hz", vm.SelectedRefreshRateOption);
        Assert.Contains("rejected", vm.DiagnosticMessage);
    }

    [TestMethod]
    public async Task AcceptedButUnchangedHardwareIsReportedAsUnconfirmed()
    {
        var vm = Create(new DisplayHardware { Change = false });
        await vm.SetRefreshRateAsync(120);
        Assert.AreEqual(240, vm.CurrentRefreshRate);
        Assert.Contains("not confirmed", vm.DiagnosticMessage);
    }

    [TestMethod]
    public async Task ConfirmedRequestsUpdateTheLabelAndRunInOrder()
    {
        var hardware = new DisplayHardware();
        var vm = Create(hardware);
        await Task.WhenAll(vm.SetRefreshRateAsync(120), vm.SetRefreshRateAsync(60));
        CollectionAssert.AreEqual(new[] { 120, 60 }, hardware.Requests);
        Assert.AreEqual(60, vm.CurrentRefreshRate);
        Assert.AreEqual("60 Hz", vm.SelectedRefreshRateOption);
        Assert.AreEqual(60, vm.CurrentRefreshRate);
    }

    [TestMethod]
    public async Task ExceptionsDoNotLeaveAnOptimisticRefreshRate()
    {
        var vm = Create(new DisplayHardware { Throw = true });
        await vm.SetRefreshRateAsync(120);
        Assert.AreEqual(240, vm.CurrentRefreshRate);
        Assert.AreEqual("240 Hz", vm.SelectedRefreshRateOption);
        Assert.Contains("Could not change refresh rate", vm.DiagnosticMessage);
    }

    [TestMethod]
    [DataRow(60, 1)]
    [DataRow(240, 0)]
    public async Task CachedSelectionChecksTheLiveRateBeforeSkippingAWrite(int liveRate, int expectedWrites)
    {
        var hardware = new DisplayHardware { Rate = liveRate };
        var vm = Create(hardware);
        await vm.SetRefreshRateAsync(240);
        Assert.HasCount(expectedWrites, hardware.Requests);
        Assert.AreEqual(240, hardware.Rate);
        Assert.AreEqual("240 Hz", vm.SelectedRefreshRateOption);
    }

    private sealed class DisplayHardware : MockHardwareService, IHardwareService
    {
        public bool Accept = true, Change = true, Throw;
        public int Rate = 240;
        public List<int> Requests = new();
        public new int GetDisplayRefreshRate() => Rate;
        public new bool SetDisplayRefreshRate(int hz)
        {
            Requests.Add(hz);
            if (Throw) throw new InvalidOperationException("test rejection");
            if (Accept && Change) Rate = hz;
            return Accept;
        }
    }
}
