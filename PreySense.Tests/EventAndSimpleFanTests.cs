using System.Reflection;
using System.Runtime.CompilerServices;
using PreySense.Core.Services;
using PreySense.Helpers;
using PreySense.Mode;

namespace PreySense.Tests;

[TestClass]
[DoNotParallelize]
public class EventAndSimpleFanTests
{
    [TestMethod]
    public async Task ChangeSignalWakesOnChangeAndCancelsWithoutPolling()
    {
        var signal = new AsyncChangeSignal();
        var first = signal.WaitAsync(CancellationToken.None);
        var second = signal.WaitAsync(CancellationToken.None);
        Assert.IsFalse(first.IsCompleted);
        signal.Pulse();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(1));
        using var cancellation = new CancellationTokenSource();
        var canceled = signal.WaitAsync(cancellation.Token);
        cancellation.Cancel();
        try { await canceled; Assert.Fail("A canceled waiter must exit."); }
        catch (OperationCanceledException) { }
        var next = signal.WaitAsync(CancellationToken.None);
        Assert.IsFalse(next.IsCompleted);
        signal.Pulse();
        await next.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [TestMethod]
    public void KeyboardActivityPublishesAnImmediateNotification()
    {
        int notifications = 0;
        void OnKey() => notifications++;
        KeyboardActivity.KeyDown += OnKey;
        try { KeyboardActivity.NoteKeyDown(); Assert.AreEqual(1, notifications); }
        finally { KeyboardActivity.KeyDown -= OnKey; }
        KeyboardActivity.NoteKeyDown();
        Assert.AreEqual(1, notifications);
    }

    [TestMethod]
    public void ForegroundCallbackTracksChangesAndStopsDeliveringAfterDisposal()
    {
        var events = (ForegroundWindowEvents)RuntimeHelpers.GetUninitializedObject(typeof(ForegroundWindowEvents));
        var received = new List<nint>();
        typeof(ForegroundWindowEvents).GetField("_changed", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(events, new Action<nint>(received.Add));
        var callback = typeof(ForegroundWindowEvents).GetMethod("OnEvent", BindingFlags.Instance | BindingFlags.NonPublic)!;
        void Send(nint window) => callback.Invoke(events, new object[] { (nint)0, (uint)3, window, 0, 0, (uint)0, (uint)0 });
        Send(12); Send(24);
        Assert.AreEqual((nint)24, events.CurrentWindow);
        CollectionAssert.AreEqual(new nint[] { 12, 24 }, received);
        events.Dispose();
        Send(36);
        Assert.AreEqual((nint)24, events.CurrentWindow);
        Assert.HasCount(2, received);
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(0)]
    public void SleepingGpuDoesNotPreventCpuDrivenFanControl(int gpuTemperature)
    {
        Assert.IsTrue(FanCurveProfile.ValidTemperatures(78, gpuTemperature));
        Assert.AreEqual(78, FanCurveProfile.CoolingTemperature(78, gpuTemperature));
        Assert.AreEqual(88, FanCurveProfile.CoolingTemperature(78, 88));
        Assert.IsFalse(FanCurveProfile.ValidTemperatures(-1, 88));
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    public void WindowsCachedDevicePowerDataIsDecodedWithoutGpuApiCalls(int state)
    {
        var data = new byte[56];
        BitConverter.GetBytes(data.Length).CopyTo(data, 0);
        BitConverter.GetBytes(state).CopyTo(data, 4);
        Assert.AreEqual(state, DeviceHelper.DecodeDisplayDevicePowerState(data));
        Assert.IsNull(DeviceHelper.DecodeDisplayDevicePowerState(data.AsSpan(0, 4)));
    }

    [TestMethod]
    public void FanStagePresetsExposeEcRiseAndRpmFields()
    {
        var balanced = FanCurveProfile.DefaultStages(1);
        Assert.AreEqual(56, balanced[0].CpuRiseC);
        Assert.AreEqual(50, balanced[0].GpuRiseC);
        Assert.AreEqual(0, balanced[0].CpuTargetRpm);
        Assert.AreEqual(1800, balanced[1].CpuTargetRpm);
        Assert.AreEqual(FanStage.RpmToDuty(1800, FanStage.CpuMaxRpm), balanced[1].CpuDutyPercent);
    }

    [TestMethod]
    public void FanStageEntryTemperaturesStayStrictlyIncreasing()
    {
        Assert.AreEqual(61, FanStage.ClampRiseBetweenNeighbors(55, 60, 70));
        Assert.AreEqual(69, FanStage.ClampRiseBetweenNeighbors(80, 60, 70));
        Assert.AreEqual(65, FanStage.ClampRiseBetweenNeighbors(65, 60, 70));
        Assert.AreEqual(FanStage.DisabledTripCelsius, FanStage.ClampRiseBetweenNeighbors(127, 60, 70));

        int[] temps = [65, 65, 70];
        FanStage.EnforceStrictlyIncreasing(temps);
        Assert.AreEqual(65, temps[0]);
        Assert.AreEqual(66, temps[1]);
        Assert.AreEqual(70, temps[2]);

        var stages = FanCurveProfile.DefaultStages(1);
        FanStage.ApplyEntryTemperatures(stages, [1, 2, 3], [56, 65, 72], gpu: false);
        Assert.AreEqual(56, stages[0].CpuRiseC);
        Assert.AreEqual(65, stages[1].CpuRiseC);
        Assert.AreEqual(72, stages[2].CpuRiseC);
        Assert.AreEqual(FanStage.DisabledTripCelsius, stages[3].CpuRiseC);
    }

    [TestMethod]
    public void FanStageConfigKeepsCpuAndGpuTempsAndDutiesSeparate()
    {
        var runtime = FanCurveProfile.DefaultStages(1)[1];
        var stored = new PreySense.Core.Configuration.FanStageConfig(runtime);
        Assert.AreEqual(runtime.CpuRiseC, stored.CpuTempC);
        Assert.AreEqual(runtime.GpuRiseC, stored.GpuTempC);
        Assert.AreEqual(runtime.CpuDutyPercent, stored.CpuDutyPercent);
        Assert.AreEqual(runtime.GpuDutyPercent, stored.GpuDutyPercent);
        Assert.AreEqual(0, stored.CpuRiseC);
        Assert.AreEqual(0, stored.CpuFallC);
        Assert.AreEqual(0, stored.CpuTargetRpm);

        var restored = stored.ToFanStage();
        Assert.AreEqual(runtime.CpuRiseC, restored.CpuRiseC);
        Assert.AreEqual(runtime.GpuRiseC, restored.GpuRiseC);
        Assert.AreEqual(runtime.CpuDutyPercent, restored.CpuDutyPercent);
        Assert.AreEqual(runtime.GpuDutyPercent, restored.GpuDutyPercent);

        var legacy = new PreySense.Core.Configuration.FanStageConfig
        {
            CpuRiseC = 70, GpuRiseC = 65, CpuFallC = 60, GpuFallC = 55,
            CpuTargetRpm = 2000, GpuTargetRpm = 2200
        };
        var migrated = legacy.ToFanStage();
        Assert.AreEqual(70, migrated.CpuRiseC);
        Assert.AreEqual(65, migrated.GpuRiseC);
        Assert.AreEqual(FanStage.RpmToDuty(2000, FanStage.CpuMaxRpm), migrated.CpuDutyPercent);
        Assert.AreEqual(FanStage.RpmToDuty(2200, FanStage.GpuMaxRpm), migrated.GpuDutyPercent);
    }
}
