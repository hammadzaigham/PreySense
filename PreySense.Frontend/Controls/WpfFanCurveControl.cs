using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PreySense.Mode;

namespace PreySense.Frontend.Controls;

public sealed class WpfFanCurveControl : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(int[]), typeof(WpfFanCurveControl), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TemperatureProperty = DependencyProperty.Register(
        nameof(Temperature), typeof(double), typeof(WpfFanCurveControl), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty CurrentDutyProperty = DependencyProperty.Register(
        nameof(CurrentDuty), typeof(double), typeof(WpfFanCurveControl), new FrameworkPropertyMetadata(-1d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Brush), typeof(WpfFanCurveControl), new FrameworkPropertyMetadata(Brushes.SkyBlue, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register(
        nameof(TextBrush), typeof(Brush), typeof(WpfFanCurveControl), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(
        nameof(GridBrush), typeof(Brush), typeof(WpfFanCurveControl), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SurfaceProperty = DependencyProperty.Register(
        nameof(Surface), typeof(Brush), typeof(WpfFanCurveControl), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));
    public int[] Values { get => (int[]?)GetValue(ValuesProperty) ?? Array.Empty<int>(); set => SetValue(ValuesProperty, value); }
    public double Temperature { get => (double)GetValue(TemperatureProperty); set => SetValue(TemperatureProperty, value); }
    public double CurrentDuty { get => (double)GetValue(CurrentDutyProperty); set => SetValue(CurrentDutyProperty, value); }
    public Brush Accent { get => (Brush)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public Brush TextBrush { get => (Brush)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
    public Brush GridBrush { get => (Brush)GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public Brush Surface { get => (Brush)GetValue(SurfaceProperty); set => SetValue(SurfaceProperty, value); }
    private int _selectedIndex = -1;
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (_selectedIndex == value) return;
            _selectedIndex = value;
            InvalidateVisual();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public event EventHandler? Edited;
    public event EventHandler? SelectionChanged;
    private bool _editingPoint, _dragging;
    private Point _dragOrigin;
    private int _dragStartDuty;
    public WpfFanCurveControl()
    {
        Focusable = true;
        Cursor = Cursors.Arrow;
        SetResourceReference(AccentProperty, "AccentBrush");
        SetResourceReference(TextBrushProperty, "TextSecondaryBrush");
        SetResourceReference(GridBrushProperty, "BorderSecondBrush");
        SetResourceReference(SurfaceProperty, "CardBgBrush");
    }
    private Rect Plot => new(42, 14, Math.Max(1, ActualWidth - 58), Math.Max(1, ActualHeight - 42));
    private Point At(int index, int duty)
    {
        var r = Plot;
        return new(r.Left + r.Width * index / (FanCurveProfile.PointCount - 1), r.Bottom - r.Height * duty / 100);
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(Surface, null, new Rect(RenderSize));
        var plot = Plot;
        var thin = new Pen(GridBrush, 1);
        for (int percent = 0; percent <= 100; percent += 10)
        {
            double y = plot.Bottom - plot.Height * percent / 100;
            dc.DrawLine(thin, new(plot.Left, y), new(plot.Right, y));
            Label(dc, percent + "%", 2, y - 7);
        }
        for (int i = 0; i < FanCurveProfile.PointCount; i++)
        {
            double x = At(i, 0).X;
            dc.DrawLine(thin, new(x, plot.Top), new(x, plot.Bottom));
            if (i % 2 == 0) Label(dc, (FanCurveProfile.MinimumTemperature + i * FanCurveProfile.TemperatureStep) + "°", x - 10, plot.Bottom + 7);
        }
        if (Values.Length != FanCurveProfile.PointCount) return;
        if (CurrentDuty is >= 0 and <= 100)
        {
            double y = plot.Bottom - plot.Height * CurrentDuty / 100;
            dc.DrawLine(new Pen(Accent, 1.5) { DashStyle = DashStyles.Dash }, new(plot.Left, y), new(plot.Right, y));
        }
        var line = new Pen(Accent, 2);
        for (int i = 1; i < Values.Length; i++)
        {
            Point previous = At(i - 1, Values[i - 1]), next = At(i, Values[i]);
            Point corner = new(next.X, previous.Y);
            dc.DrawLine(line, previous, corner);
            dc.DrawLine(line, corner, next);
        }
        if (Temperature > 0)
        {
            double clampedTemp = Math.Clamp(Temperature, FanCurveProfile.MinimumTemperature, FanCurveProfile.MaximumTemperature);
            double x = plot.Left + plot.Width * (clampedTemp - FanCurveProfile.MinimumTemperature) / (FanCurveProfile.MaximumTemperature - FanCurveProfile.MinimumTemperature);
            dc.DrawLine(new Pen(TextBrush, 1) { DashStyle = DashStyles.Dash }, new(x, plot.Top), new(x, plot.Bottom));
        }
        if (CurrentDuty is >= 0 and <= 100 && Temperature > 0)
        {
            double clampedTemp = Math.Clamp(Temperature, FanCurveProfile.MinimumTemperature, FanCurveProfile.MaximumTemperature);
            double x = plot.Left + plot.Width * (clampedTemp - FanCurveProfile.MinimumTemperature) / (FanCurveProfile.MaximumTemperature - FanCurveProfile.MinimumTemperature);
            double y = plot.Bottom - plot.Height * CurrentDuty / 100;
            dc.DrawEllipse(Accent, null, new Point(x, y), 3.5, 3.5);
        }
        for (int i = 0; i < Values.Length; i++)
            dc.DrawEllipse(i == SelectedIndex ? Accent : Surface, line, At(i, Values[i]), i == SelectedIndex ? 5 : 3, i == SelectedIndex ? 5 : 3);
    }
    private void Label(DrawingContext dc, string text, double x, double y)
    {
        var font = new Typeface((FontFamily?)TryFindResource("AppFontFamily") ?? new FontFamily("Segoe UI"),
            FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        dc.DrawText(new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            font, 10, TextBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip), new(x, y));
    }
    private int HitPoint(Point p)
    {
        if (Values.Length != FanCurveProfile.PointCount) return -1;
        int hit = -1;
        double distance = 12 * 12;
        for (int i = 0; i < Values.Length; i++)
        {
            Vector delta = p - At(i, Values[i]);
            if (delta.LengthSquared <= distance) { hit = i; distance = delta.LengthSquared; }
        }
        return hit;
    }
    private bool BeginPointEdit(Point p)
    {
        int hit = HitPoint(p);
        if (hit < 0)
        {
            if (SelectedIndex >= 0) SelectedIndex = -1;
            return false;
        }
        SelectedIndex = hit;
        _dragOrigin = p;
        _dragStartDuty = Values[hit];
        _editingPoint = true;
        _dragging = false;
        return true;
    }
    private void DragPoint(Point p)
    {
        if (!_editingPoint || Values.Length != FanCurveProfile.PointCount || SelectedIndex < 0) return;
        double deltaY = _dragOrigin.Y - p.Y;
        if (!_dragging && Math.Abs(deltaY) < SystemParameters.MinimumVerticalDragDistance) return;
        _dragging = true;
        int previous = Values[SelectedIndex];
        FanCurveProfile.SetPoint(Values, SelectedIndex, _dragStartDuty + (int)Math.Round(deltaY / Plot.Height * 100));
        if (Values[SelectedIndex] == previous) return;
        InvalidateVisual();
        Edited?.Invoke(this, EventArgs.Empty);
    }
    private void EndPointEdit(bool cancel = false)
    {
        if (cancel && _editingPoint && _dragging && Values.Length == FanCurveProfile.PointCount && SelectedIndex >= 0)
        {
            FanCurveProfile.SetPoint(Values, SelectedIndex, _dragStartDuty);
            InvalidateVisual();
            Edited?.Invoke(this, EventArgs.Empty);
        }
        _editingPoint = _dragging = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!IsEnabled || !BeginPointEdit(e.GetPosition(this))) return;
        Focus(); CaptureMouse(); e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!IsEnabled) { Cursor = Cursors.Arrow; return; }
        if (IsMouseCaptured) DragPoint(e.GetPosition(this));
        else Cursor = HitPoint(e.GetPosition(this)) >= 0 ? Cursors.SizeNS : Cursors.Arrow;
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (IsMouseCaptured) { EndPointEdit(); e.Handled = true; }
    }
    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        _editingPoint = _dragging = false;
        base.OnLostMouseCapture(e);
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!IsEnabled) return;
        if (e.Key == Key.Escape)
        {
            if (_editingPoint)
            {
                EndPointEdit(cancel: true); e.Handled = true; return;
            }
            if (SelectedIndex >= 0)
            {
                SelectedIndex = -1; e.Handled = true; return;
            }
        }
        if (Values.Length != FanCurveProfile.PointCount) return;
        if (_editingPoint) return;
        if (e.Key == Key.Left) { SelectedIndex = SelectedIndex < 0 ? 0 : Math.Max(0, SelectedIndex - 1); }
        else if (e.Key == Key.Right) { SelectedIndex = SelectedIndex < 0 ? 0 : Math.Min(FanCurveProfile.PointCount - 1, SelectedIndex + 1); }
        else if (e.Key is Key.Up or Key.Down)
        {
            if (SelectedIndex < 0) return;
            FanCurveProfile.SetPoint(Values, SelectedIndex, Values[SelectedIndex] + (e.Key == Key.Up ? 1 : -1));
            InvalidateVisual();
            Edited?.Invoke(this, EventArgs.Empty);
        }
        else return;
        e.Handled = true;
    }
}
