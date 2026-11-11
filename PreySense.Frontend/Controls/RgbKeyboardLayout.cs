using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using PreySense.Frontend.ViewModels;
using PreySense.Rgb;

namespace PreySense.Frontend.Controls;

public sealed record KeyboardKeyPlacement(int Index, string Name, string Label, Rect Bounds);

/// <summary>Readable PC key geometry. Wire indices always come from the detected OEM layout.</summary>
public static class KeyboardLayoutGeometry
{
    public static IReadOnlyList<KeyboardKeyPlacement> Build(IEnumerable<LightingLedInfo> keys)
    {
        var source = keys.ToArray();
        bool numberPad = source.Any(k => Normalize(k.Name).StartsWith("NUM", StringComparison.Ordinal));
        var positions = new Dictionary<string, (double X, double Y, double W, double H, string Label)>(StringComparer.OrdinalIgnoreCase);
        void Add(string key, double x, double y, double width = 1, double height = 1, string? label = null) =>
            positions[key] = (x, y, width, height, label ?? key);
        void Row(string[] names, double x, double y) { foreach (string name in names) { Add(name, x, y); x++; } }
        Add("ESCAPE", 0, 0, label: "Esc");
        for (int i = 1; i <= 12; i++) Add($"F{i}", 1 + i + (i > 4 ? 0.5 : 0) + (i > 8 ? 0.5 : 0), 0);
        Row(["`", "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "="], 0, 1.3);
        Add("BACKSPACE", 13, 1.3, 2, label: "Backspace");
        Add("TAB", 0, 2.3, 1.5, label: "Tab");
        Row(["Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P", "[", "]"], 1.5, 2.3);
        Add("\\", 13.5, 2.3, 1.5, label: "\\");
        Add("CAPSLOCK", 0, 3.3, 1.75, label: "Caps");
        Row(["A", "S", "D", "F", "G", "H", "J", "K", "L", ";", "'"], 1.75, 3.3);
        Add("ENTER", 12.75, 3.3, 2.25, label: "Enter");
        Add("LEFTSHIFT", 0, 4.3, 2.25, label: "Shift");
        Row(["Z", "X", "C", "V", "B", "N", "M", ",", ".", "/"], 2.25, 4.3);
        Add("RIGHTSHIFT", 12.25, 4.3, 2.75, label: "Shift");
        Add("LEFTCONTROL", 0, 5.3, 1.25, label: "Ctrl");
        Add("LEFTFN", 1.25, 5.3, label: "Fn");
        Add("LEFTWINDOWS", 2.25, 5.3, 1.25, label: "Win");
        Add("LEFTALT", 3.5, 5.3, 1.25, label: "Alt");
        Add("SPACE", 4.75, 5.3, 6, label: "Space");
        if (source.Any(k => Normalize(k.Name) is "LEFTSPACE" or "RIGHTSPACE"))
        {
            Add("LEFTSPACE", 4.75, 5.3, 2, label: "Space L");
            Add("SPACE", 6.75, 5.3, 2, label: "Space");
            Add("RIGHTSPACE", 8.75, 5.3, 2, label: "Space R");
        }
        Add("RIGHTALT", 10.75, 5.3, 1.25, label: "Alt");
        Add("OPTION", 12, 5.3, 1.5, label: "Menu");
        Add("RIGHTCONTROL", 13.5, 5.3, 1.5, label: "Ctrl");
        Add("PRINTSCREEN", 15.5, 0, label: "PrtSc");
        Add("PREDATORSENSE", 16.5, 0, label: "P");
        Add("PAUSE/BREAK", 17.5, 0, label: "Pause");
        Row(["INSERT", "HOME", "PAGEUP"], 15.5, 1.3);
        Row(["DELETE", "END", "PAGEDOWN"], 15.5, 2.3);
        Add("UPARROW", 16.5, 4.3, label: "↑");
        Add("LEFTARROW", 15.5, 5.3, label: "←");
        Add("DOWNARROW", 16.5, 5.3, label: "↓");
        Add("RIGHTARROW", 17.5, 5.3, label: "→");
        if (numberPad)
        {
            Row(["NUMLOCK", "NUM/", "NUM*", "NUM-"], 19, 1.3);
            Row(["NUM7", "NUM8", "NUM9"], 19, 2.3);
            Row(["NUM4", "NUM5", "NUM6"], 19, 3.3);
            Row(["NUM1", "NUM2", "NUM3"], 19, 4.3);
            Add("NUM+", 22, 2.3, height: 2, label: "+");
            Add("NUMENTER", 22, 4.3, height: 2, label: "Enter");
            Add("NUM0", 19, 5.3, 2, label: "0");
            Add("NUM.", 21, 5.3, label: ".");
            Row(["MUTE", "VOLUMEDOWN", "VOLUMEUP", "POWER"], 19, 0);
        }
        var result = new List<KeyboardKeyPlacement>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        double extraX = 0, extraY = 6.6;
        foreach (var key in source)
        {
            string name = Normalize(key.Name);
            if (positions.TryGetValue(name, out var position) && used.Add(name))
            {
                string label = position.Label switch { "INSERT" => "Ins", "HOME" => "Home", "PAGEUP" => "PgUp", "DELETE" => "Del", "END" => "End", "PAGEDOWN" => "PgDn",
                    "NUMLOCK" => "Num", "MUTE" => "Mute", "VOLUMEDOWN" => "Vol−", "VOLUMEUP" => "Vol+", "POWER" => "Power", _ => position.Label.StartsWith("NUM", StringComparison.Ordinal) ? position.Label[3..] : position.Label };
                result.Add(new(key.Index, key.Name, label, new Rect(position.X * 40, position.Y * 40, position.W * 40 - 4, position.H * 40 - 4)));
            }
            else
            {
                // Preserve vendor-specific keys without inventing their physical location.
                result.Add(new(key.Index, key.Name, key.Name, new Rect(extraX * 40, extraY * 40, 56, 36)));
                extraX += 1.5;
                if (extraX > (numberPad ? 21 : 17)) { extraX = 0; extraY += 1.1; }
            }
        }
        return result;
    }

    private static string Normalize(string name)
    {
        string key = name.Replace(" (ANSI)", "", StringComparison.OrdinalIgnoreCase).Replace(" ", "").ToUpperInvariant();
        if (key.StartsWith("NUMBERPAD", StringComparison.Ordinal)) key = "NUM" + key[9..];
        if (key is "LEFTENTER" or "RETURN") return "ENTER";
        if (key.Length > 0 && key.All(c => c == '\\')) return "\\";
        return key;
    }
}

public sealed class RgbKeyboardLayout : FrameworkElement
{
    private static DependencyProperty ThemeBrush(string name) => DependencyProperty.Register(name, typeof(Brush), typeof(RgbKeyboardLayout),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty KeyBackgroundProperty = ThemeBrush("KeyBackground");
    public static readonly DependencyProperty KeyForegroundProperty = ThemeBrush("KeyForeground");
    public static readonly DependencyProperty KeyAccentProperty = ThemeBrush("KeyAccent");
    public static readonly DependencyProperty KeyBorderProperty = ThemeBrush("KeyBorder");
    public static readonly DependencyProperty KeysSourceProperty = DependencyProperty.Register(nameof(KeysSource), typeof(IEnumerable<LightingLedEditor>), typeof(RgbKeyboardLayout),
        new FrameworkPropertyMetadata(null, (d, e) => ((RgbKeyboardLayout)d).ChangeSource(e.OldValue, e.NewValue)));
    public static readonly DependencyProperty SelectedKeyProperty = DependencyProperty.Register(nameof(SelectedKey), typeof(LightingLedEditor), typeof(RgbKeyboardLayout),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SelectedKeysProperty = DependencyProperty.Register(nameof(SelectedKeys), typeof(IList<LightingLedEditor>), typeof(RgbKeyboardLayout),
        new FrameworkPropertyMetadata(null, (d, e) => ((RgbKeyboardLayout)d).ChangeSelection(e.OldValue, e.NewValue)));
    public static readonly DependencyProperty CommonColorHexProperty = DependencyProperty.Register(nameof(CommonColorHex), typeof(string), typeof(RgbKeyboardLayout),
        new FrameworkPropertyMetadata("#00E5FF", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty UseIndividualColorsProperty = DependencyProperty.Register(nameof(UseIndividualColors), typeof(bool), typeof(RgbKeyboardLayout),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender));
    public IEnumerable<LightingLedEditor>? KeysSource { get => (IEnumerable<LightingLedEditor>?)GetValue(KeysSourceProperty); set => SetValue(KeysSourceProperty, value); }
    public LightingLedEditor? SelectedKey { get => (LightingLedEditor?)GetValue(SelectedKeyProperty); set => SetValue(SelectedKeyProperty, value); }
    public IList<LightingLedEditor>? SelectedKeys { get => (IList<LightingLedEditor>?)GetValue(SelectedKeysProperty); set => SetValue(SelectedKeysProperty, value); }
    private readonly List<LightingLedEditor> _localSelection = new();
    private IList<LightingLedEditor> Selection => SelectedKeys ?? _localSelection;
    private Point? _dragStart;
    private bool _dragging, _additiveDrag;
    private Rect? _selectionRectangle;
    private LightingLedEditor[] _selectionBeforeDrag = Array.Empty<LightingLedEditor>();
    private int? _anchor;
    public string CommonColorHex { get => (string)GetValue(CommonColorHexProperty); set => SetValue(CommonColorHexProperty, value); }
    public bool UseIndividualColors { get => (bool)GetValue(UseIndividualColorsProperty); set => SetValue(UseIndividualColorsProperty, value); }
    private readonly List<LightingLedEditor> _keys = new();
    private IReadOnlyList<KeyboardKeyPlacement> _placements = Array.Empty<KeyboardKeyPlacement>();
    private bool _listening;
    public RgbKeyboardLayout()
    {
        Focusable = true; Cursor = Cursors.Hand; ClipToBounds = true;
        SetResourceReference(KeyBackgroundProperty, "ButtonMainBrush");
        SetResourceReference(KeyForegroundProperty, "TextPrimaryBrush");
        SetResourceReference(KeyAccentProperty, "AccentBrush");
        SetResourceReference(KeyBorderProperty, "BorderMainBrush");
        Loaded += (_, _) => { if (!_listening) { ChangeSource(null, KeysSource); ChangeSelection(null, SelectedKeys); } };
        Unloaded += (_, _) =>
        {
            if (KeysSource is INotifyCollectionChanged source) source.CollectionChanged -= OnCollectionChanged;
            foreach (var key in _keys) key.PropertyChanged -= OnKeyChanged;
            if (SelectedKeys is INotifyCollectionChanged selection) selection.CollectionChanged -= OnSelectionChanged;
            _listening = false;
        };
    }

    private void ChangeSource(object? oldSource, object? newSource)
    {
        if (oldSource is INotifyCollectionChanged oldList) oldList.CollectionChanged -= OnCollectionChanged;
        if (newSource is INotifyCollectionChanged newList) newList.CollectionChanged += OnCollectionChanged;
        _listening = true;
        Rebuild();
    }
    private void ChangeSelection(object? oldSelection, object? newSelection)
    {
        if (oldSelection is INotifyCollectionChanged oldList) oldList.CollectionChanged -= OnSelectionChanged;
        if (newSelection is INotifyCollectionChanged newList) newList.CollectionChanged += OnSelectionChanged;
        InvalidateVisual();
    }
    private void OnSelectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();
    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();
    private void OnKeyChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();
    private void Rebuild()
    {
        foreach (var key in _keys) key.PropertyChanged -= OnKeyChanged;
        _keys.Clear();
        if (KeysSource != null) _keys.AddRange(KeysSource);
        foreach (var key in _keys) key.PropertyChanged += OnKeyChanged;
        _placements = KeyboardLayoutGeometry.Build(_keys.Select(k => new LightingLedInfo(k.Index, k.Name)));
        InvalidateMeasure(); InvalidateVisual();
    }
    protected override Size MeasureOverride(Size availableSize) => new(_placements.Count == 0 ? 740 : _placements.Max(p => p.Bounds.Right) + 4,
        _placements.Count == 0 ? 256 : _placements.Max(p => p.Bounds.Bottom) + 4);

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        Color background = (GetValue(KeyBackgroundProperty) as SolidColorBrush)?.Color ?? Color.FromRgb(35, 35, 35);
        Brush foreground = GetValue(KeyForegroundProperty) as Brush ?? Brushes.White;
        Brush accent = GetValue(KeyAccentProperty) as Brush ?? Brushes.DeepSkyBlue;
        Brush border = GetValue(KeyBorderProperty) as Brush ?? Brushes.DimGray;
        var typeface = new Typeface("Segoe UI");
        foreach (var placement in _placements)
        {
            var key = _keys.First(k => k.Index == placement.Index);
            Color rgb;
            try { rgb = (Color)ColorConverter.ConvertFromString(UseIndividualColors ? key.ColorHex : CommonColorHex); } catch { rgb = background; }
            Color tint = Color.FromRgb((byte)((background.R * 3 + rgb.R) / 4), (byte)((background.G * 3 + rgb.G) / 4), (byte)((background.B * 3 + rgb.B) / 4));
            bool selected = Selection.Contains(key) || (Selection.Count == 0 && ReferenceEquals(key, SelectedKey));
            dc.DrawRoundedRectangle(new SolidColorBrush(tint), selected ? new Pen(accent, 2) : null, placement.Bounds, 4, 4);
            var strip = new Rect(placement.Bounds.X + 4, placement.Bounds.Bottom - 5, Math.Max(1, placement.Bounds.Width - 8), 2);
            dc.DrawRoundedRectangle(new SolidColorBrush(rgb), null, strip, 1, 1);
            var text = new FormattedText(placement.Label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface,
                placement.Label.Length > 5 ? 9 : 11, foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip)
                { MaxTextWidth = Math.Max(1, placement.Bounds.Width - 6), Trimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center };
            dc.DrawText(text, new Point(placement.Bounds.X + 3, placement.Bounds.Y + (placement.Bounds.Height - text.Height) / 2 - 1));
        }
        if (_selectionRectangle is Rect rectangle)
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(32, 128, 128, 128)), new Pen(accent, 1), rectangle);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var point = e.GetPosition(this);
        var hit = _placements.FirstOrDefault(p => p.Bounds.Contains(point));
        Focus();
        bool control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        _selectionBeforeDrag = Selection.ToArray();
        _additiveDrag = control || shift;
        _dragStart = point;
        CaptureMouse();
        if (hit != null) SelectKeyByIndex(hit.Index, control, shift);
        else if (!_additiveDrag) { Selection.Clear(); SetCurrentValue(SelectedKeyProperty, null); }
        InvalidateVisual(); e.Handled = true;
    }
    internal void SelectKeyByIndex(int index, bool control = false, bool shift = false)
    {
        var hit = _placements.FirstOrDefault(p => p.Index == index);
        if (hit == null) return;

        if (shift && _anchor.HasValue)
        {
            var ordered = _placements.OrderBy(p => p.Bounds.Y).ThenBy(p => p.Bounds.X).ToArray();
            int start = Array.FindIndex(ordered, p => p.Index == _anchor);
            int end = Array.FindIndex(ordered, p => p.Index == hit.Index);
            if (start < 0) start = end;
            if (!control) Selection.Clear();
            foreach (var key in ordered.Skip(Math.Min(start, end)).Take(Math.Abs(end - start) + 1)) AddSelection(key);
            SetPrimary(hit);
        }
        else if (control)
        {
            var editor = _keys.First(k => k.Index == hit.Index);
            if (Selection.Contains(editor)) Selection.Remove(editor); else Selection.Add(editor);
            SetCurrentValue(SelectedKeyProperty, Selection.LastOrDefault());
            _anchor = hit.Index;
        }
        else { Select(hit); _anchor = hit.Index; }
        InvalidateVisual();
    }

    private void Select(KeyboardKeyPlacement key)
    {
        Selection.Clear();
        AddSelection(key);
        SetPrimary(key);
    }
    private void AddSelection(KeyboardKeyPlacement key)
    {
        var editor = _keys.First(k => k.Index == key.Index);
        if (!Selection.Contains(editor)) Selection.Add(editor);
    }
    private void SetPrimary(KeyboardKeyPlacement key)
    {
        if (!UseIndividualColors)
        {
            foreach (var editor in _keys) editor.ColorHex = CommonColorHex;
            SetCurrentValue(UseIndividualColorsProperty, true);
        }
        SetCurrentValue(SelectedKeyProperty, _keys.First(k => k.Index == key.Index));
        AutomationProperties.SetName(this, "Keyboard: " + key.Name);
        InvalidateVisual();
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down) || _placements.Count == 0) return;
        var ordered = _placements.OrderBy(p => p.Bounds.Y).ThenBy(p => p.Bounds.X).ToArray();
        var current = ordered.FirstOrDefault(p => p.Index == SelectedKey?.Index);
        if (current == null) { Select(ordered[0]); e.Handled = true; return; }
        static Point Center(Rect r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);
        bool vertical = e.Key is Key.Up or Key.Down;
        int sign = e.Key is Key.Left or Key.Up ? -1 : 1;
        var next = ordered.Select(p => (Key: p, Delta: Center(p.Bounds) - Center(current.Bounds)))
            .Where(p => (vertical ? p.Delta.Y : p.Delta.X) * sign > 1)
            .OrderBy(p => (vertical ? Math.Abs(p.Delta.X) * 4 + Math.Abs(p.Delta.Y) : Math.Abs(p.Delta.Y) * 4 + Math.Abs(p.Delta.X)))
            .FirstOrDefault();
        if (next.Key != null)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) { AddSelection(current); AddSelection(next.Key); SetPrimary(next.Key); }
            else Select(next.Key);
        }
        e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var point = e.GetPosition(this);
        if (_dragStart is Point start && e.LeftButton == MouseButtonState.Pressed)
        {
            if (!_dragging && (point - start).Length < 5) return;
            _dragging = true;
            _selectionRectangle = new Rect(start, point);
            SelectKeysInRectangle(_selectionRectangle.Value, _additiveDrag ? _selectionBeforeDrag : Array.Empty<LightingLedEditor>());
            InvalidateVisual(); return;
        }
    }
    internal void SelectKeysInRectangle(Rect rectangle, IReadOnlyList<LightingLedEditor> previous)
    {
        Selection.Clear();
        foreach (var key in previous) if (_keys.Contains(key)) Selection.Add(key);
        KeyboardKeyPlacement? last = null;
        foreach (var key in _placements.Where(p => rectangle.IntersectsWith(p.Bounds)))
        { AddSelection(key); last = key; }
        if (last != null) SetPrimary(last); else SetCurrentValue(SelectedKeyProperty, Selection.LastOrDefault());
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragStart == null) return;
        _dragStart = null; _dragging = false; _selectionRectangle = null;
        ReleaseMouseCapture(); InvalidateVisual(); e.Handled = true;
    }
    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _dragStart = null; _dragging = false; _selectionRectangle = null; InvalidateVisual();
    }
}

public sealed class HexRgbColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try { return (Color)ColorConverter.ConvertFromString(value as string ?? "#000000"); }
        catch { return Colors.Black; }
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Color c ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : Binding.DoNothing;
}
