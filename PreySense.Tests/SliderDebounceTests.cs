using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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

    [TestMethod]
    public void RepeatButtonInsideSliderDoesNotFlushPrematurely() => OnSta(() =>
    {
        var slider = new Slider { Maximum = 100 };
        var template = new ControlTemplate(typeof(Slider));
        template.VisualTree = new FrameworkElementFactory(typeof(RepeatButton), "TrackButton");
        slider.Template = template;
        slider.ApplyTemplate();
        var repeatButton = (RepeatButton)slider.Template.FindName("TrackButton", slider);
        SliderDebounce.SetEnabled(slider, true);
        int events = 0;
        slider.ValueChanged += (_, _) => events++;
        SliderDebounce.QueueChange(slider, 0, 50);
        repeatButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, repeatButton));
        Assert.AreEqual(0, events);
    });

    [TestMethod]
    public void LostMouseCaptureFlushesPendingChange() => OnSta(() =>
    {
        var slider = new Slider { Maximum = 100 };
        SliderDebounce.SetEnabled(slider, true);
        int events = 0; double latest = -1;
        slider.ValueChanged += (_, e) => { events++; latest = e.NewValue; };
        SliderDebounce.QueueChange(slider, 0, 60);
        slider.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.LostMouseCaptureEvent });
        Assert.AreEqual(1, events);
        Assert.AreEqual(60d, latest);
    });

    [TestMethod]
    public void MouseCaptureDefersUntilLostMouseCaptureOrMouseUp() => OnSta(() =>
    {
        var slider = new Slider { Maximum = 100 };
        SliderDebounce.SetEnabled(slider, true);
        int events = 0; double oldValue = -1, latest = -1;
        slider.ValueChanged += (_, e) => { events++; oldValue = e.OldValue; latest = e.NewValue; };
        var previous = SliderDebounce.IsDragCaptureActive;
        bool captured = true;
        SliderDebounce.IsDragCaptureActive = s => ReferenceEquals(s, slider) && captured;
        try
        {
            SliderDebounce.QueueChange(slider, 0, 35);
            SliderDebounce.QueueChange(slider, 35, 75);

            var frame = new DispatcherFrame();
            var stop = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            stop.Tick += (_, _) => { stop.Stop(); frame.Continue = false; };
            stop.Start(); Dispatcher.PushFrame(frame);
            Assert.AreEqual(0, events);

            captured = false;
            slider.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
            Assert.AreEqual(1, events);
            Assert.AreEqual(0d, oldValue);
            Assert.AreEqual(75d, latest);
        }
        finally
        {
            SliderDebounce.IsDragCaptureActive = previous;
        }
    });

    [TestMethod]
    public void PreviewKeyUpFlushesOnNavigationKeyOnly() => OnSta(() =>
    {
        var slider = new Slider { Maximum = 100 };
        SliderDebounce.SetEnabled(slider, true);
        int events = 0; double latest = -1;
        slider.ValueChanged += (_, e) => { events++; latest = e.NewValue; };
        SliderDebounce.QueueChange(slider, 0, 70);
        var source = new TestPresentationSource();
        slider.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.A) { RoutedEvent = UIElement.PreviewKeyUpEvent });
        Assert.AreEqual(0, events);
        slider.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Right) { RoutedEvent = UIElement.PreviewKeyUpEvent });
        Assert.AreEqual(1, events);
        Assert.AreEqual(70d, latest);
    });

    [TestMethod]
    public void FlushWithNoPendingChangeDoesNotUpdateSourceOrFireValueChanged() => OnSta(() =>
    {
        var vm = new TestSliderSource();
        var slider = new Slider { Maximum = 100 };
        slider.SetBinding(RangeBase.ValueProperty, new Binding(nameof(TestSliderSource.Value))
        {
            Source = vm,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.Explicit
        });
        SliderDebounce.SetEnabled(slider, true);
        int events = 0;
        slider.ValueChanged += (_, _) => events++;
        vm.SetCount = 0;
        slider.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, slider, new Button())
            { RoutedEvent = UIElement.LostKeyboardFocusEvent });
        slider.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.LostMouseCaptureEvent });
        Assert.AreEqual(0, events);
        Assert.AreEqual(0, vm.SetCount);
    });

    private sealed class TestSliderSource
    {
        private double _value = 25;
        public int SetCount { get; set; }
        public double Value
        {
            get => _value;
            set { _value = value; SetCount++; }
        }
    }

    private sealed class TestPresentationSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = null!;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
        public override bool IsDisposed => false;
    }
}
