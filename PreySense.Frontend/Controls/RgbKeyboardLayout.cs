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
        return IsPh1671Layout(source) ? BuildPh1671(source) : BuildGeneric(source);
    }

    private static bool IsPh1671Layout(LightingLedInfo[] source)
    {
        if (source.Length < 90) return false;
        var names = new HashSet<string>(source.Select(k => Normalize(k.Name)), StringComparer.OrdinalIgnoreCase);
        return names.Contains("PREDATORSENSE")
            && names.Contains("NUMLOCK")
            && names.Contains("VOLUMEDOWN")
            && names.Contains("RIGHTARROW");
    }

    private static IReadOnlyList<KeyboardKeyPlacement> BuildPh1671(LightingLedInfo[] source)
    {
        // Measured PH16-71 coordinates from RaptorSense PhysicalLayout, scaled so a standard 52x52 keycap is 36x36.
        const double scale = 36.0 / 52.0;
        const double pitch = 64.3;
        const double keyW = 52, smallW = 46, fW = 45, rowH = 52, fRowH = 34;
        const double fRow = 0, row1 = 48, row2 = 114, row3 = 180, row4 = 246, row5 = 312;
        double[] numpadX = [967, 1025, 1082, 1139];

        var positions = new Dictionary<string, (double X, double Y, double W, double H, string Label)>(StringComparer.OrdinalIgnoreCase);
        void Add(string key, double x, double y, double width, double height, string? label = null) =>
            positions[key] = (x * scale, y * scale, width * scale, height * scale, label ?? key);
        void Run(string[] keys, double startX, double y, Func<string, string>? labelFor = null)
        {
            for (int i = 0; i < keys.Length; i++)
                Add(keys[i], startX + i * pitch, y, keyW, rowH, labelFor?.Invoke(keys[i]) ?? keys[i]);
        }

        // F-row: Esc + three 4-key F-groups + PrtSc/Ins/Del + 4 media/power keys above numpad.
        (string Key, string Label)[] fRowKeys =
        [
            ("ESCAPE", "Esc"), ("F1", "F1"), ("F2", "F2"), ("F3", "F3"), ("F4", "F4"),
            ("F5", "F5"), ("F6", "F6"), ("F7", "F7"), ("F8", "F8"),
            ("F9", "F9"), ("F10", "F10"), ("F11", "F11"), ("F12", "F12"),
            ("PRINTSCREEN", "PrtSc"), ("INSERT", "Ins"), ("DELETE", "Del"),
        ];
        double[] fX = [0, 69, 126, 183, 240, 309, 366, 423, 480, 549, 606, 663, 720, 789, 846, 903];
        for (int i = 0; i < fRowKeys.Length; i++)
            Add(fRowKeys[i].Key, fX[i], fRow, i == 0 ? 47 : fW, fRowH, fRowKeys[i].Label);

        (string Key, string Label)[] mediaKeys = [("VOLUMEDOWN", "⏮"), ("VOLUMEUP", "⏯"), ("MUTE", "⏭"), ("POWER", "⏻")];
        for (int i = 0; i < mediaKeys.Length; i++)
            Add(mediaKeys[i].Key, numpadX[i], fRow, fW, fRowH, mediaKeys[i].Label);

        static string CapLabel(string key) => key switch
        {
            "`" => "~\n`", "-" => "_\n-", "=" => "+\n=", "[" => "{\n[", "]" => "}\n]", "\\" => "|\n\\",
            ";" => ":\n;", "'" => "\"\n'", "," => "<\n,", "." => ">\n.", "/" => "?\n/",
            _ => key
        };

        // Number row
        Add("`", 0, row1, 50, rowH, CapLabel("`"));
        Run(["1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "="], 57, row1, CapLabel);
        Add("BACKSPACE", 828, row1, 117, rowH, "Backspace");

        // Top letter row
        Add("TAB", 0, row2, 75, rowH, "Tab");
        Run(["Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P", "[", "]"], 87, row2, CapLabel);
        Add("\\", 859, row2, 86, rowH, CapLabel("\\"));

        // Home row
        Add("CAPSLOCK", 0, row3, 87, rowH, "Caps Lock");
        Run(["A", "S", "D", "F", "G", "H", "J", "K", "L", ";", "'"], 97, row3, CapLabel);
        Add("ENTER", 805, row3, 140, rowH, "Enter");

        // Bottom letter row
        Add("LEFTSHIFT", 0, row4, 117, rowH, "Shift");
        Run(["Z", "X", "C", "V", "B", "N", "M", ",", ".", "/"], 129, row4, CapLabel);
        Add("RIGHTSHIFT", 773, row4, 112, rowH, "Shift");
        Add("UPARROW", 897, row4, 50, rowH, "▲");

        // Space row: right arrow sits under the first numpad column.
        Run(["LEFTCONTROL", "LEFTFN", "LEFTWINDOWS", "LEFTALT"], 0, row5, k => k switch
        {
            "LEFTCONTROL" => "Ctrl", "LEFTFN" => "Fn", "LEFTWINDOWS" => "Win", "LEFTALT" => "Alt", _ => k
        });
        Add("SPACE", 257, row5, 312, rowH, "Space");
        Run(["RIGHTALT", "OPTION"], 580, row5, k => k == "RIGHTALT" ? "Alt" : "Menu");
        Add("RIGHTCONTROL", 709, row5, 110, rowH, "Ctrl");
        Add("LEFTARROW", 832, row5, 50, rowH, "◀");
        Add("DOWNARROW", 897, row5, 50, rowH, "▼");
        Add("RIGHTARROW", 961, row5, 50, rowH, "▶");

        // Numpad
        (string Key, string Label)[][] numpad =
        [
            [("PREDATORSENSE", "P"), ("NUMLOCK", "Num"), ("NUM/", "/"), ("NUM*", "*")],
            [("NUM7", "7"), ("NUM8", "8"), ("NUM9", "9"), ("NUM-", "-")],
            [("NUM4", "4"), ("NUM5", "5"), ("NUM6", "6"), ("NUM+", "+")],
            [("NUM1", "1"), ("NUM2", "2"), ("NUM3", "3")],
        ];
        double[] numpadY = [row1, row2, row3, row4];
        for (int row = 0; row < numpad.Length; row++)
            for (int col = 0; col < numpad[row].Length; col++)
                Add(numpad[row][col].Key, numpadX[col], numpadY[row], smallW, rowH, numpad[row][col].Label);
        Add("NUMENTER", numpadX[3], row4, smallW, row5 + rowH - row4, "Enter");
        Add("NUM0", numpadX[1], row5, smallW, rowH, "0");
        Add("NUM.", numpadX[2], row5, smallW, rowH, ".");

        var result = new List<KeyboardKeyPlacement>(source.Length);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        double extraX = 0, extraY = 6.6;
        foreach (var key in source)
        {
            string name = Normalize(key.Name);
            if (positions.TryGetValue(name, out var position) && used.Add(name))
            {
                result.Add(new(key.Index, key.Name, position.Label, new Rect(position.X, position.Y, position.W, position.H)));
            }
            else
            {
                result.Add(new(key.Index, key.Name, key.Name, new Rect(extraX * 40, extraY * 40, 56, 36)));
                extraX += 1.5;
                if (extraX > 21) { extraX = 0; extraY += 1.1; }
            }
        }
        return result;
    }

    private static IReadOnlyList<KeyboardKeyPlacement> BuildGeneric(LightingLedInfo[] source)
    {
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
    public static readonly DependencyProperty IsPaintModeProperty = DependencyProperty.Register(nameof(IsPaintMode), typeof(bool), typeof(RgbKeyboardLayout),
        new FrameworkPropertyMetadata(false));
    public static readonly DependencyProperty PaintColorHexProperty = DependencyProperty.Register(nameof(PaintColorHex), typeof(string), typeof(RgbKeyboardLayout),
        new FrameworkPropertyMetadata(null));
    public IEnumerable<LightingLedEditor>? KeysSource { get => (IEnumerable<LightingLedEditor>?)GetValue(KeysSourceProperty); set => SetValue(KeysSourceProperty, value); }
    public LightingLedEditor? SelectedKey { get => (LightingLedEditor?)GetValue(SelectedKeyProperty); set => SetValue(SelectedKeyProperty, value); }
    public IList<LightingLedEditor>? SelectedKeys { get => (IList<LightingLedEditor>?)GetValue(SelectedKeysProperty); set => SetValue(SelectedKeysProperty, value); }
    private readonly List<LightingLedEditor> _localSelection = new();
    private IList<LightingLedEditor> Selection => SelectedKeys ?? _localSelection;
    private Point? _dragStart;
    private bool _dragging, _additiveDrag;
    private bool? _paintErase;
    private Rect? _selectionRectangle;
    private LightingLedEditor[] _selectionBeforeDrag = Array.Empty<LightingLedEditor>();
    private int? _anchor;
    public string CommonColorHex { get => (string)GetValue(CommonColorHexProperty); set => SetValue(CommonColorHexProperty, value); }
    public bool UseIndividualColors { get => (bool)GetValue(UseIndividualColorsProperty); set => SetValue(UseIndividualColorsProperty, value); }
    public bool IsPaintMode { get => (bool)GetValue(IsPaintModeProperty); set => SetValue(IsPaintModeProperty, value); }
    public string? PaintColorHex { get => (string?)GetValue(PaintColorHexProperty); set => SetValue(PaintColorHexProperty, value); }
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

    private static readonly Typeface CachedTypeface = new("Segoe UI");
    private static readonly SolidColorBrush SelectionOverlayBrush = CreateFrozenBrush(Color.FromArgb(32, 128, 128, 128));
    private readonly Dictionary<Color, SolidColorBrush> _brushCache = new();
    private Pen? _cachedAccentPen2;
    private Pen? _cachedAccentPen1;
    private Brush? _lastAccentBrush;

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private SolidColorBrush GetBrush(Color color)
    {
        if (!_brushCache.TryGetValue(color, out var brush))
        {
            if (_brushCache.Count > 512) _brushCache.Clear();
            brush = CreateFrozenBrush(color);
            _brushCache[color] = brush;
        }
        return brush;
    }

    private void ChangeSource(object? oldSource, object? newSource)
    {
        if (oldSource is INotifyCollectionChanged oldList) oldList.CollectionChanged -= OnCollectionChanged;
        if (newSource is INotifyCollectionChanged newList)
        {
            newList.CollectionChanged -= OnCollectionChanged;
            newList.CollectionChanged += OnCollectionChanged;
        }
        _listening = true;
        Rebuild();
    }
    private void ChangeSelection(object? oldSelection, object? newSelection)
    {
        if (oldSelection is INotifyCollectionChanged oldList) oldList.CollectionChanged -= OnSelectionChanged;
        if (newSelection is INotifyCollectionChanged newList)
        {
            newList.CollectionChanged -= OnSelectionChanged;
            newList.CollectionChanged += OnSelectionChanged;
        }
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
        foreach (var key in _keys)
        {
            key.PropertyChanged -= OnKeyChanged;
            key.PropertyChanged += OnKeyChanged;
        }
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
        if (!ReferenceEquals(accent, _lastAccentBrush) || _cachedAccentPen2 == null || _cachedAccentPen1 == null)
        {
            _lastAccentBrush = accent;
            _cachedAccentPen2 = new Pen(accent, 2);
            _cachedAccentPen1 = new Pen(accent, 1);
            if (accent.IsFrozen) { _cachedAccentPen2.Freeze(); _cachedAccentPen1.Freeze(); }
        }
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach (var placement in _placements)
        {
            var key = _keys.First(k => k.Index == placement.Index);
            Color rgb;
            try { rgb = (Color)ColorConverter.ConvertFromString(UseIndividualColors ? key.ColorHex : CommonColorHex); } catch { rgb = background; }
            Color tint = Color.FromRgb((byte)((background.R * 3 + rgb.R) / 4), (byte)((background.G * 3 + rgb.G) / 4), (byte)((background.B * 3 + rgb.B) / 4));
            bool selected = Selection.Contains(key) || (Selection.Count == 0 && ReferenceEquals(key, SelectedKey));
            dc.DrawRoundedRectangle(GetBrush(tint), selected ? _cachedAccentPen2 : null, placement.Bounds, 4, 4);
            var strip = new Rect(placement.Bounds.X + 4, placement.Bounds.Bottom - 5, Math.Max(1, placement.Bounds.Width - 8), 2);
            dc.DrawRoundedRectangle(GetBrush(rgb), null, strip, 1, 1);
            double fontSize = placement.Bounds.Height < 26 || placement.Label.Contains('\n') || placement.Label.Length > 5 ? 9 : 11;
            var text = new FormattedText(placement.Label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, CachedTypeface,
                fontSize, foreground, dpi)
                { MaxTextWidth = Math.Max(1, placement.Bounds.Width - 6), Trimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center };
            dc.DrawText(text, new Point(placement.Bounds.X + 3, placement.Bounds.Y + (placement.Bounds.Height - text.Height) / 2 - 1));
        }
        if (_selectionRectangle is Rect rectangle)
            dc.DrawRectangle(SelectionOverlayBrush, _cachedAccentPen1, rectangle);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var point = e.GetPosition(this);
        Focus();
        if (IsPaintMode)
        {
            _paintErase = false;
            CaptureMouse();
            PaintKeyAt(point, erase: false);
            e.Handled = true;
            return;
        }
        var hit = _placements.FirstOrDefault(p => p.Bounds.Contains(point));
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

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        if (!IsPaintMode) return;
        Focus();
        _paintErase = true;
        CaptureMouse();
        PaintKeyAt(e.GetPosition(this), erase: true);
        e.Handled = true;
    }

    internal LightingLedEditor? PaintKeyAt(Point point, bool erase)
    {
        var hit = _placements.FirstOrDefault(p => p.Bounds.Contains(point));
        if (hit == null) return null;
        var editor = _keys.FirstOrDefault(k => k.Index == hit.Index);
        if (editor == null) return null;

        string targetColor = erase ? "#000000" : ResolvePaintColorHex();
        if (!UseIndividualColors)
        {
            foreach (var item in _keys) item.ColorHex = CommonColorHex;
            SetCurrentValue(UseIndividualColorsProperty, true);
        }
        if (!string.Equals(editor.ColorHex, targetColor, StringComparison.OrdinalIgnoreCase))
            editor.ColorHex = targetColor;
        SetCurrentValue(SelectedKeyProperty, editor);
        AutomationProperties.SetName(this, "Keyboard: " + hit.Name);
        InvalidateVisual();
        return editor;
    }

    private string ResolvePaintColorHex()
    {
        if (!string.IsNullOrWhiteSpace(PaintColorHex)) return PaintColorHex;
        if (!string.IsNullOrWhiteSpace(SelectedKey?.ColorHex)) return SelectedKey.ColorHex;
        return string.IsNullOrWhiteSpace(CommonColorHex) ? "#00E5FF" : CommonColorHex;
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
        if (IsPaintMode && _paintErase is bool erase)
        {
            bool pressed = erase ? e.RightButton == MouseButtonState.Pressed : e.LeftButton == MouseButtonState.Pressed;
            if (pressed)
            {
                PaintKeyAt(point, erase);
                return;
            }
            _paintErase = null;
            ReleaseMouseCapture();
            return;
        }
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
        if (_paintErase == false)
        {
            _paintErase = null;
            ReleaseMouseCapture();
            e.Handled = true;
            return;
        }
        if (_dragStart == null) return;
        _dragStart = null; _dragging = false; _selectionRectangle = null;
        ReleaseMouseCapture(); InvalidateVisual(); e.Handled = true;
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        if (_paintErase != true) return;
        _paintErase = null;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _paintErase = null;
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
