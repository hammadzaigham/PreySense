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
    public async Task FanCurveRendersGridCurveAndEditablePointsWithoutLiveMarkers()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var control = new PreySense.Frontend.Controls.WpfFanCurveControl { Values = FanCurveProfile.DefaultCurve() };
                control.Measure(new System.Windows.Size(400, 240));
                control.Arrange(new System.Windows.Rect(0, 0, 400, 240));
                var visual = new System.Windows.Media.DrawingVisual();
                using (var context = visual.RenderOpen())
                    typeof(PreySense.Frontend.Controls.WpfFanCurveControl).GetMethod("OnRender", BindingFlags.NonPublic | BindingFlags.Instance)!
                        .Invoke(control, new object[] { context });
                var shapes = visual.Drawing.Children.OfType<System.Windows.Media.GeometryDrawing>().ToArray();
                var lines = shapes.Where(s => s.Geometry is System.Windows.Media.LineGeometry).ToArray();
                Assert.AreEqual(11 + FanCurveProfile.PointCount, lines.Count(s => s.Pen.Thickness == 1));
                Assert.AreEqual(FanCurveProfile.PointCount - 1, lines.Count(s => s.Pen.Thickness == 2));
                Assert.HasCount(11 + FanCurveProfile.PointCount * 2 - 1, lines);
                Assert.IsTrue(lines.All(s => s.Pen.DashStyle.Dashes.Count == 0));
                Assert.AreEqual(FanCurveProfile.PointCount, shapes.Count(s => s.Geometry is System.Windows.Media.EllipseGeometry));
                completion.SetResult();
            }
            catch (Exception ex) { completion.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task;
    }
}
