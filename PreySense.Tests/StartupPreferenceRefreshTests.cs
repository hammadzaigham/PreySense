using System.Runtime.CompilerServices;
using PreySense.Frontend.ViewModels;

namespace PreySense.Tests;

[TestClass]
public class StartupPreferenceRefreshTests
{
    [TestMethod]
    public async Task OverlappingWindowRefreshesShareTheSameStartupTaskQuery()
    {
        var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int reads = 0;
        vm.StartupPreferenceReader = () =>
        {
            Interlocked.Increment(ref reads);
            started.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            return true;
        };
        Task first = vm.RefreshStartupPreferenceAsync();
        try
        {
            Assert.IsTrue(started.Wait(TimeSpan.FromSeconds(5)));
            Task second = vm.RefreshStartupPreferenceAsync();
            Assert.AreSame(first, second);
            Assert.IsFalse(vm.CanChangeRunOnStartup);
            release.Set();
            await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(1, reads);
            Assert.IsTrue(vm.IsRunOnStartupEnabled);
            Assert.IsTrue(vm.CanChangeRunOnStartup);
            await vm.RefreshStartupPreferenceAsync();
            Assert.AreEqual(2, reads, "A later reopening must query actual state again rather than cache it forever.");
        }
        finally { release.Set(); await first; }
    }
}
