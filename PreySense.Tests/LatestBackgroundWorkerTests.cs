using System.Collections.Concurrent;
using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using PreySense.Core.Configuration;
using PreySense.Core.Services;
using PreySense.Frontend.Services;
using PreySense.Frontend.ViewModels;

namespace PreySense.Tests;

[TestClass]
public class LatestBackgroundWorkerTests
{
    [TestMethod]
    public async Task SlowWork_DoesNotBlockCallerAndOnlyLatestPendingStateRuns()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var values = new ConcurrentQueue<int>();
        bool released = false;
        var worker = new LatestBackgroundWorker<int>(value =>
        {
            values.Enqueue(value);
            if (value == 1)
            {
                started.Set();
                released = release.Wait(TimeSpan.FromSeconds(5));
            }
        }, (_, latest) => latest);
        Task first = worker.Submit(1);
        try
        {
            Assert.IsTrue(started.Wait(TimeSpan.FromSeconds(5)));
            _ = worker.Submit(2);
            Task last = worker.Submit(3);
            Assert.IsFalse(first.IsCompleted, "The slow write should remain active while new requests return.");
            release.Set();
            await last.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsTrue(released, "The write timed out rather than being released by the caller.");
            CollectionAssert.AreEqual(new[] { 1, 3 }, values.ToArray());
        }
        finally { release.Set(); await worker.StopAsync(); }
    }

    [TestMethod]
    public async Task StopDropsPendingWritesAndWaitsForTheActiveWrite()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var values = new ConcurrentQueue<int>();
        bool released = false;
        var worker = new LatestBackgroundWorker<int>(value =>
        {
            values.Enqueue(value);
            started.Set();
            released = release.Wait(TimeSpan.FromSeconds(5));
        }, (_, latest) => latest);
        _ = worker.Submit(1);
        try
        {
            Assert.IsTrue(started.Wait(TimeSpan.FromSeconds(5)));
            _ = worker.Submit(2);
            Task stopped = worker.StopAsync();
            Assert.IsFalse(stopped.IsCompleted);
            release.Set();
            await stopped.WaitAsync(TimeSpan.FromSeconds(5));
            await worker.Submit(3);
            Assert.IsTrue(released);
            CollectionAssert.AreEqual(new[] { 1 }, values.ToArray());
        }
        finally { release.Set(); await worker.StopAsync(); }
    }

    [TestMethod]
    public async Task StopWithDrain_CompletesTheNewestPendingWriteBeforeReturning()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var values = new ConcurrentQueue<int>();
        var worker = new LatestBackgroundWorker<int>(value =>
        {
            values.Enqueue(value);
            if (value != 1) return;
            started.Set();
            release.Wait(TimeSpan.FromSeconds(5));
        }, (_, latest) => latest);
        _ = worker.Submit(1);
        try
        {
            Assert.IsTrue(started.Wait(TimeSpan.FromSeconds(5)));
            _ = worker.Submit(2);
            _ = worker.Submit(3);
            Task stopped = worker.StopAsync(drainPending: true);
            Assert.IsFalse(stopped.IsCompleted);
            _ = worker.Submit(4);
            release.Set();
            await stopped.WaitAsync(TimeSpan.FromSeconds(5));
            CollectionAssert.AreEqual(new[] { 1, 3 }, values.ToArray());
        }
        finally { release.Set(); await worker.StopAsync(); }
    }

    [TestMethod]
    public async Task FailedWrite_DoesNotDropTheNewestPendingUpdateOrRetryTheFailure()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var values = new ConcurrentQueue<int>();
        var worker = new LatestBackgroundWorker<int>(value =>
        {
            values.Enqueue(value);
            if (value != 1) return;
            started.Set();
            release.Wait(TimeSpan.FromSeconds(5));
            throw new InvalidOperationException("Simulated lighting write failure.");
        }, (_, latest) => latest);
        _ = worker.Submit(1);
        try
        {
            Assert.IsTrue(started.Wait(TimeSpan.FromSeconds(5)));
            _ = worker.Submit(2);
            Task latest = worker.Submit(3);
            release.Set();
            await latest.WaitAsync(TimeSpan.FromSeconds(5));
            CollectionAssert.AreEqual(new[] { 1, 3 }, values.ToArray());
            await worker.Submit(4).WaitAsync(TimeSpan.FromSeconds(5));
            CollectionAssert.AreEqual(new[] { 1, 3, 4 }, values.ToArray());
        }
        finally { release.Set(); await worker.StopAsync(); }
    }

    [TestMethod]
    public void LightingWrites_RunOffCallerWithTheCapturedColorsBrightnessAndTimeout()
    {
        var hardware = DispatchProxy.Create<IHardwareService, LightingHardwareProxy>();
        var recorder = (LightingHardwareProxy)hardware;
        int caller = Environment.CurrentManagedThreadId;
        var vm = CreateLightingViewModel(hardware);
        var worker = CreateLightingWorker(vm.ApplyLightingRequest);
        var keyboard = new KeyboardLightingConfig
        {
            Brightness = 50, BacklightTimeoutSeconds = 60,
            Zone1ColorHex = "#112233", Zone2ColorHex = "#224466", Zone3ColorHex = "#336699", Zone4ColorHex = "#4488CC"
        };
        var cover = new CoverLightingConfig { ColorHex = "#445566", Brightness = 25 };
        try
        {
            // Keep this caller occupied, so a reused thread-pool continuation cannot
            // be mistaken for a UI-thread hardware call.
            worker.Submit(new(keyboard, cover, MainViewModel.LightingWork.Keyboard | MainViewModel.LightingWork.Cover))
                .WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            var calls = recorder.Calls.ToArray();
            CollectionAssert.AreEqual(new[] { "SetRgbAllZones", "SetRgbBrightness", "SetKbdBacklightTimeoutSeconds", "SetCoverLogoLighting" }, calls.Select(c => c.Name).ToArray());
            Assert.IsTrue(calls.All(call => call.Thread != caller));
            Assert.AreEqual(ColorTranslator.FromHtml("#112233"), (Color)calls[0].Arguments[0]!);
            Assert.AreEqual(ColorTranslator.FromHtml("#4488CC"), (Color)calls[0].Arguments[3]!);
            Assert.AreEqual((byte)50, calls[1].Arguments[0]);
            Assert.AreEqual(60, calls[2].Arguments[0]);
            Assert.AreEqual(ColorTranslator.FromHtml("#445566"), (Color)calls[3].Arguments[1]!);
            Assert.AreEqual((byte)25, calls[3].Arguments[2]);
        }
        finally { worker.StopAsync().GetAwaiter().GetResult(); }
    }

    [TestMethod]
    public async Task FnBrightness_UpdatesTheSyncedCoverWithoutReapplyingKeyboardColors()
    {
        var hardware = DispatchProxy.Create<IHardwareService, LightingHardwareProxy>();
        var recorder = (LightingHardwareProxy)hardware;
        var worker = CreateLightingWorker(CreateLightingViewModel(hardware).ApplyLightingRequest);
        try
        {
            await worker.Submit(new(new() { Brightness = 75 },
                new() { SyncWithKeyboard = true, ColorHex = "#445566", Brightness = 25 }, MainViewModel.LightingWork.Brightness))
                .WaitAsync(TimeSpan.FromSeconds(5));
            var calls = recorder.Calls.ToArray();
            Assert.HasCount(1, calls);
            Assert.AreEqual("SetCoverLogoLighting", calls[0].Name);
            Assert.AreEqual((byte)75, calls[0].Arguments[2]);
        }
        finally { await worker.StopAsync(); }
    }

    [TestMethod]
    public async Task PendingLightingUpdates_KeepDirtyComponentsAndUseTheLatestSnapshot()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var requests = new ConcurrentQueue<MainViewModel.LightingRequest>();
        var worker = CreateLightingWorker(request =>
        {
            requests.Enqueue(request);
            if (request.Keyboard.Brightness != 10) return;
            started.Set();
            release.Wait(TimeSpan.FromSeconds(5));
        });
        _ = worker.Submit(new(new() { Brightness = 10 }, new(), MainViewModel.LightingWork.Keyboard));
        try
        {
            Assert.IsTrue(started.Wait(TimeSpan.FromSeconds(5)));
            _ = worker.Submit(new(new() { Brightness = 50 }, new(), MainViewModel.LightingWork.Keyboard));
            Task latest = worker.Submit(new(new() { Brightness = 50 }, new() { ColorHex = "#123456" }, MainViewModel.LightingWork.Cover));
            release.Set();
            await latest.WaitAsync(TimeSpan.FromSeconds(5));
            var applied = requests.ToArray();
            Assert.HasCount(2, applied);
            Assert.AreEqual(MainViewModel.LightingWork.Keyboard | MainViewModel.LightingWork.Cover, applied[1].Work);
            Assert.AreEqual(50, applied[1].Keyboard.Brightness);
            Assert.AreEqual("#123456", applied[1].Cover.ColorHex);
        }
        finally { release.Set(); await worker.StopAsync(); }
    }

    private static MainViewModel CreateLightingViewModel(IHardwareService hardware)
    {
        var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        typeof(MainViewModel).GetField("_hw", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm, hardware);
        return vm;
    }

    private static LatestBackgroundWorker<MainViewModel.LightingRequest> CreateLightingWorker(Action<MainViewModel.LightingRequest> execute) =>
        new(execute, (pending, latest) => latest with { Work = pending.Work | latest.Work });

    public class LightingHardwareProxy : DispatchProxy
    {
        public readonly ConcurrentQueue<(string Name, int Thread, object?[] Arguments)> Calls = new();
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Calls.Enqueue((method!.Name, Environment.CurrentManagedThreadId, args?.ToArray() ?? []));
            return method.ReturnType == typeof(void) ? null : method.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null;
        }
    }
}
