using System.Collections.Concurrent;
using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using PreySense.Core.Configuration;
using PreySense.Core.Services;
using PreySense.Frontend.Services;
using PreySense.Frontend.ViewModels;
using PreySense.Rgb;

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
            EffectTag = "HW:0", Brightness = 50, BacklightTimeoutSeconds = 60,
            Zone1ColorHex = "#112233", Zone2ColorHex = "#224466", Zone3ColorHex = "#336699", Zone4ColorHex = "#4488CC"
        };
        var cover = new CoverLightingConfig { Mode = 1, ColorHex = "#445566", Brightness = 25 };
        try
        {
            // Keep this caller occupied, so a reused thread-pool continuation cannot
            // be mistaken for a UI-thread hardware call.
            worker.Submit(new(keyboard, cover, MainViewModel.LightingWork.Keyboard | MainViewModel.LightingWork.Cover))
                .WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            var calls = recorder.Calls.ToArray();
            CollectionAssert.AreEqual(new[] { "SetRgbAllZones", "SetKbdBacklightTimeoutSeconds", "SetCoverLogoLighting" }, calls.Select(c => c.Name).ToArray());
            Assert.IsTrue(calls.All(call => call.Thread != caller));
            Assert.AreEqual(ColorTranslator.FromHtml("#112233"), (Color)calls[0].Arguments[0]!);
            Assert.AreEqual(ColorTranslator.FromHtml("#4488CC"), (Color)calls[0].Arguments[3]!);
            Assert.AreEqual((byte)50, calls[0].Arguments[4], "Zones and their final brightness must use one hardware operation.");
            Assert.AreEqual(60, calls[1].Arguments[0]);
            Assert.AreEqual(ColorTranslator.FromHtml("#445566"), (Color)calls[2].Arguments[1]!);
            Assert.AreEqual((byte)25, calls[2].Arguments[2]);
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
            await worker.Submit(new(new() { EffectTag = "HW:0", Brightness = 75 },
                new() { Mode = 1, SyncWithKeyboard = true, ColorHex = "#445566", Brightness = 25 }, MainViewModel.LightingWork.Brightness))
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

    [TestMethod]
    public void AnimationFrames_CannotReplaceSettingsOrInheritSaveWork()
    {
        var settings = new MainViewModel.LightingRequest(new() { Zone1ColorHex = "#123456", Brightness = 75 },
            new(), MainViewModel.LightingWork.Keyboard | MainViewModel.LightingWork.Save);
        var frame = new MainViewModel.LightingRequest(new() { Zone1ColorHex = "#ABCDEF", Brightness = 0 },
            new(), MainViewModel.LightingWork.Frame);
        Assert.AreSame(settings, MainViewModel.MergeLightingRequests(settings, frame));
        Assert.AreSame(settings, MainViewModel.MergeLightingRequests(frame, settings));
    }

    [TestMethod]
    [DoNotParallelize]
    public void IdleDimmedLighting_DoesNotPersistZeroBrightness()
    {
        var hardware = DispatchProxy.Create<IHardwareService, LightingHardwareProxy>();
        var store = ConfigurationService.Instance;
        var previous = store.GetCurrent().Lighting;
        try
        {
            CreateLightingViewModel(hardware).ApplyLightingRequest(new(new() { EffectTag = "HW:0", Brightness = 0 },
                new(), MainViewModel.LightingWork.Keyboard | MainViewModel.LightingWork.Save, SavedKeyboardBrightness: 75));
            var write = ((LightingHardwareProxy)hardware).Calls.Single(call => call.Name == "SetRgbAllZones");
            Assert.AreEqual((byte)0, write.Arguments[4], "Idle dimming must still reach the hardware.");
            Assert.AreEqual(75, store.GetCurrent().Lighting.Keyboard.Brightness);
        }
        finally { store.Update(config => config.Lighting = previous); }
    }

    [TestMethod]
    [DoNotParallelize]
    public void ModeLightingRecomputesColorsFromActiveModeInsteadOfSavedStaticZones()
    {
        var store = ConfigurationService.Instance;
        var original = store.GetCurrent();
        try
        {
        store.Update(cfg => cfg.Lighting.PowerModeColors.Clear());
        var hardware = DispatchProxy.Create<IHardwareService, LightingHardwareProxy>();
        var recorder = (LightingHardwareProxy)hardware;
        var vm = CreateLightingViewModel(hardware);
        var keyboard = new KeyboardLightingConfig
        {
            EffectTag = "MODE", Zone1ColorHex = "#112233", Zone4ColorHex = "#445566",
            PowerModeColors = new() { ["05"] = "#ABCDEF" }
        };
        foreach (var (mode, expected) in new[] { ((byte)5, "#C700FF"), ((byte)0, "#A0FF90"), ((byte)1, "#00AEC7") })
        {
            typeof(MainViewModel).GetField("_activePowerMode", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm, mode);
            vm.ApplyLightingRequest(new(keyboard, new() { SyncWithKeyboard = true }, MainViewModel.LightingWork.Keyboard));
            var call = recorder.Calls.Last(c => c.Name == "SetRgbAllZones");
            Assert.IsTrue(call.Arguments.Take(4).All(c => (Color)c! == ColorTranslator.FromHtml(expected)));
            var cover = recorder.Calls.Last(c => c.Name == "SetCoverLogoLighting");
            Assert.AreEqual(ColorTranslator.FromHtml(expected), (Color)cover.Arguments[1]!);
        }
        Assert.AreEqual("#112233", keyboard.Zone1ColorHex, "Applying Mode must preserve the saved static palette.");
        Assert.AreEqual("#ABCDEF", keyboard.PowerModeColors["05"], "Legacy individual overrides are ignored without rewriting them.");
        }
        finally { store.Save(original); }
    }

    [TestMethod]
    [DoNotParallelize]
    public void ModeBrightnessOverridesLegacyBrightnessAndCoverEnableWithoutChangingSavedStaticValues()
    {
        var store = ConfigurationService.Instance;
        var original = store.GetCurrent();
        var hardware = DispatchProxy.Create<IHardwareService, LightingHardwareProxy>();
        var recorder = (LightingHardwareProxy)hardware;
        var vm = CreateLightingViewModel(hardware);
        var keyboard = new KeyboardLightingConfig { EffectTag = "MODE", Brightness = 17 };
        var cover = new CoverLightingConfig { Mode = 4, Enabled = false, Brightness = 0 };
        try
        {
            store.Update(cfg => cfg.Lighting.ModeBrightness = 60);
            vm.ApplyLightingRequest(new(keyboard, cover, MainViewModel.LightingWork.Keyboard | MainViewModel.LightingWork.Cover));
            Assert.AreEqual((byte)60, recorder.Calls.Last(c => c.Name == "SetRgbAllZones").Arguments[4]);
            var coverWrite = recorder.Calls.Last(c => c.Name == "SetCoverLogoLighting");
            Assert.AreEqual((byte)60, coverWrite.Arguments[2]);
            Assert.IsTrue((bool)coverWrite.Arguments[4]!);
            store.Update(cfg => cfg.Lighting.ModeBrightness = 0);
            vm.ApplyLightingRequest(new(keyboard, cover, MainViewModel.LightingWork.Keyboard | MainViewModel.LightingWork.Cover));
            Assert.AreEqual((byte)0, recorder.Calls.Last(c => c.Name == "SetRgbAllZones").Arguments[4]);
            Assert.IsFalse((bool)recorder.Calls.Last(c => c.Name == "SetCoverLogoLighting").Arguments[4]!);
            Assert.AreEqual(17, keyboard.Brightness);
            Assert.AreEqual(0, cover.Brightness);
            Assert.IsFalse(cover.Enabled);
        }
        finally { store.Save(original); }
    }

    private static MainViewModel CreateLightingViewModel(IHardwareService hardware)
    {
        var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        typeof(MainViewModel).GetField("_hw", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm, hardware);
        return vm;
    }

    private static LatestBackgroundWorker<MainViewModel.LightingRequest> CreateLightingWorker(Action<MainViewModel.LightingRequest> execute) =>
        new(execute, MainViewModel.MergeLightingRequests);

    public class LightingHardwareProxy : DispatchProxy
    {
        public readonly ConcurrentQueue<(string Name, int Thread, object?[] Arguments)> Calls = new();
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "get_IsCoverLightingAvailable") return true;
            Calls.Enqueue((method!.Name, Environment.CurrentManagedThreadId, args?.ToArray() ?? []));
            return method.ReturnType == typeof(void) ? null : method.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null;
        }
    }
}
