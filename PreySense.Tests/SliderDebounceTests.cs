using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using PreySense.Frontend.Services;

namespace PreySense.Tests;

[TestClass]
public class SliderDebounceTests
{
    private static void OnSta(Action test)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { test(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(5)));
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [TestMethod]
    public void BurstDeliversOnlyLatestValueAfterPause() => OnSta(() =>
    {
        var slider = new Slider();
        SliderDebounce.SetEnabled(slider, true);
        int events = 0; double oldValue = -1, latest = -1;
        slider.ValueChanged += (_, e) => { events++; oldValue = e.OldValue; latest = e.NewValue; };
        SliderDebounce.QueueChange(slider, 0, 10);
        SliderDebounce.QueueChange(slider, 10, 20);
        SliderDebounce.QueueChange(slider, 20, 30);
        Assert.AreEqual(0, events);
        var frame = new DispatcherFrame();
        var stop = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        stop.Tick += (_, _) => { stop.Stop(); frame.Continue = false; };
        stop.Start(); Dispatcher.PushFrame(frame);
        Assert.AreEqual(1, events);
        Assert.AreEqual(0d, oldValue);
        Assert.AreEqual(30d, latest);
    });

    [TestMethod]
    public void ActionButtonFlushesPendingSliderBeforeClickHandler() => OnSta(() =>
    {
        var slider = new Slider(); SliderDebounce.SetEnabled(slider, true);
        int events = 0;
        slider.ValueChanged += (_, _) => events++;
        SliderDebounce.QueueChange(slider, 0, 80);
        var save = new Button();
        save.Click += (_, _) => Assert.AreEqual(1, events);
        save.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.AreEqual(1, events);
    });

    [TestMethod]
    public void ProgrammaticInitializationRemainsSynchronous() => OnSta(() =>
    {
        var slider = new Slider { Maximum = 100 }; SliderDebounce.SetEnabled(slider, true);
        int events = 0;
        slider.ValueChanged += (_, _) => events++;
        slider.Value = 45;
        Assert.AreEqual(1, events);
    });
}
