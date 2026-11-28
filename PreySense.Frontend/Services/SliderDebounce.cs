using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace PreySense.Frontend.Services;

/// <summary>Coalesces user slider events; programmatic initialization remains synchronous.</summary>
public static class SliderDebounce
{
    public const int DelayMilliseconds = 150;
    public static readonly RoutedEvent DraftValueChangedEvent = EventManager.RegisterRoutedEvent(
        "DraftValueChanged", RoutingStrategy.Bubble, typeof(RoutedPropertyChangedEventHandler<double>), typeof(SliderDebounce));
    public static void AddDraftValueChangedHandler(DependencyObject target, RoutedPropertyChangedEventHandler<double> handler)
        => (target as UIElement)?.AddHandler(DraftValueChangedEvent, handler);
    public static void RemoveDraftValueChangedHandler(DependencyObject target, RoutedPropertyChangedEventHandler<double> handler)
        => (target as UIElement)?.RemoveHandler(DraftValueChangedEvent, handler);
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(SliderDebounce), new PropertyMetadata(false, EnabledChanged));
    public static void SetEnabled(DependencyObject target, bool value) => target.SetValue(EnabledProperty, value);
    public static bool GetEnabled(DependencyObject target) => (bool)target.GetValue(EnabledProperty);
    private static readonly ConditionalWeakTable<Slider, Pending> States = new();
    [ThreadStatic] private static HashSet<Pending>? _pending;
    internal static Func<Slider, bool> IsDragCaptureActive { get; set; } = static s => s.IsMouseCaptureWithin;

    static SliderDebounce()
    {
        EventManager.RegisterClassHandler(typeof(Slider), RangeBase.ValueChangedEvent,
            new RoutedPropertyChangedEventHandler<double>(Changed), true);
        EventManager.RegisterClassHandler(typeof(ButtonBase), ButtonBase.ClickEvent,
            new RoutedEventHandler(OnButtonClick), true);
    }

    private static void EnabledChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not Slider slider) return;
        if ((bool)e.NewValue)
        {
            slider.PreviewMouseLeftButtonUp += Released;
            slider.LostMouseCapture += Released;
            slider.PreviewKeyUp += KeyReleased;
            slider.LostKeyboardFocus += Released;
            slider.Unloaded += Released;
        }
        else
        {
            Flush(slider);
            slider.PreviewMouseLeftButtonUp -= Released;
            slider.LostMouseCapture -= Released;
            slider.PreviewKeyUp -= KeyReleased;
            slider.LostKeyboardFocus -= Released;
            slider.Unloaded -= Released;
        }
    }

    private static void Released(object sender, RoutedEventArgs e) => Flush((Slider)sender);

    private static void KeyReleased(object sender, KeyEventArgs e)
    {
        if (IsSliderNavigationKey(e.Key))
            Flush((Slider)sender);
    }

    private static bool IsSliderNavigationKey(Key key) => key is
        Key.Left or Key.Right or Key.Up or Key.Down or
        Key.PageUp or Key.PageDown or Key.Home or Key.End;

    private static void OnButtonClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && IsInsideSlider(source)) return;
        FlushPending();
    }

    private static bool IsInsideSlider(DependencyObject? source)
    {
        for (var current = source; current != null;)
        {
            if (current is Slider) return true;
            current = (current is Visual or Visual3D ? VisualTreeHelper.GetParent(current) : null)
                ?? LogicalTreeHelper.GetParent(current);
        }
        return false;
    }
    private static void Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var slider = (Slider)sender;
        if (!GetEnabled(slider)) return;
        var state = States.GetValue(slider, s => new Pending(s));
        if (state.Delivering) return;
        if (!slider.IsLoaded || !(slider.IsMouseOver || slider.IsMouseCaptureWithin || slider.IsKeyboardFocusWithin))
        {
            state.Cancel(); // A new profile or binding refresh supersedes an old user draft.
            return;
        }
        e.Handled = true;
        slider.RaiseEvent(new RoutedPropertyChangedEventArgs<double>(e.OldValue, e.NewValue, DraftValueChangedEvent));
        state.Queue(e.OldValue, e.NewValue);
    }

    internal static void QueueChange(Slider slider, double oldValue, double newValue) =>
        States.GetValue(slider, s => new Pending(s)).Queue(oldValue, newValue);
    internal static void Flush(Slider slider)
    {
        if (States.TryGetValue(slider, out var state)) state.Flush();
    }
    private static void FlushPending()
    {
        foreach (var state in _pending?.ToArray() ?? []) state.Flush();
    }

    private sealed class Pending
    {
        private readonly Slider _slider;
        private readonly DispatcherTimer _timer;
        private double _oldValue, _newValue;
        private bool _hasValue;
        public bool Delivering { get; private set; }
        public Pending(Slider slider)
        {
            _slider = slider;
            _timer = new DispatcherTimer(DispatcherPriority.Input, slider.Dispatcher)
                { Interval = TimeSpan.FromMilliseconds(DelayMilliseconds) };
            _timer.Tick += (_, _) => Flush();
        }
        public void Queue(double oldValue, double newValue)
        {
            if (!_hasValue) _oldValue = oldValue;
            _newValue = newValue;
            _hasValue = true;
            (_pending ??= []).Add(this);
            _timer.Stop();
            if (!IsDragCaptureActive(_slider))
                _timer.Start();
        }
        public void Cancel()
        {
            _timer.Stop(); _hasValue = false; _pending?.Remove(this);
        }
        public void Flush()
        {
            if (!_hasValue) return;
            Cancel();
            Delivering = true;
            try
            {
                _slider.GetBindingExpression(RangeBase.ValueProperty)?.UpdateSource();
                _slider.RaiseEvent(new RoutedPropertyChangedEventArgs<double>(_oldValue, _newValue, RangeBase.ValueChangedEvent));
            }
            finally { Delivering = false; }
        }
    }
}
