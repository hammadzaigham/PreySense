namespace PreySense.UI;

public class LabeledSliderControl : Panel
{
    private readonly TableLayoutPanel _layout = new();
    private readonly Label _title = new();
    private readonly Label _value = new();
    private readonly Slider _slider = new();
    private bool _showLabels = true;

    public LabeledSliderControl()
    {
        AutoSize = false;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(0);
        Width = 280;
        Height = 74;
        ApplyThemeColors();

        _layout.Dock = DockStyle.Fill;
        _layout.ColumnCount = 1;
        _layout.RowCount = 3;
        _layout.Margin = Padding.Empty;
        _layout.Padding = Padding.Empty;
        _layout.BackColor = Color.Transparent;
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 18F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        _title.AutoSize = false;
        _title.Dock = DockStyle.Fill;
        _title.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _title.ForeColor = RForm.foreMain;
        _title.TextAlign = ContentAlignment.BottomLeft;
        _title.Margin = Padding.Empty;

        _value.AutoSize = false;
        _value.Dock = DockStyle.Fill;
        _value.Font = new Font("Segoe UI", 8F, FontStyle.Regular);
        _value.ForeColor = RForm.foreMain;
        _value.TextAlign = ContentAlignment.TopRight;
        _value.Margin = Padding.Empty;

        _slider.Dock = DockStyle.Fill;
        _slider.Margin = new Padding(0);
        _slider.ValueChanged += (_, _) => UpdateValueText();

        _layout.Controls.Add(_title, 0, 0);
        _layout.Controls.Add(_value, 0, 1);
        _layout.Controls.Add(_slider, 0, 2);
        Controls.Add(_layout);
    }

    public bool ShowLabels
    {
        get => _showLabels;
        set
        {
            _showLabels = value;
            _title.Visible = value;
            _value.Visible = value;
            Height = value ? 74 : 40;
            LayoutChildren();
        }
    }

    public string Title
    {
        get => _title.Text;
        set
        {
            _title.Text = value;
            _title.Visible = ShowLabels;
        }
    }

    public string ValueFormat
    {
        get => _value.Text;
        set
        {
            _value.Text = value;
            _value.Visible = ShowLabels;
        }
    }

    public string ValueText
    {
        get => _value.Text;
        set => _value.Text = value;
    }

    public Slider Slider => _slider;

    public float ThumbScale
    {
        get => _slider.ThumbScale;
        set => _slider.ThumbScale = value;
    }

    public int RightContentWidth
    {
        get => _slider.Width;
        set
        {
            Width = Math.Max(0, value);
            LayoutChildren();
        }
    }

    public int Min
    {
        get => _slider.Min;
        set => _slider.Min = value;
    }

    public int Max
    {
        get => _slider.Max;
        set => _slider.Max = value;
    }

    public int Step
    {
        get => _slider.Step;
        set => _slider.Step = value;
    }

    public int Value
    {
        get => _slider.Value;
        set
        {
            _slider.Value = value;
            UpdateValueText();
        }
    }

    public event EventHandler? ValueChanged
    {
        add => _slider.ValueChanged += value;
        remove => _slider.ValueChanged -= value;
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        LayoutChildren();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyThemeColors();
    }

    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);
        ApplyThemeColors();
    }

    protected override void OnBackColorChanged(EventArgs e)
    {
        base.OnBackColorChanged(e);
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        using var brush = new SolidBrush(BackColor);
        pevent.Graphics.FillRectangle(brush, ClientRectangle);
    }

    private void UpdateValueText()
    {
        if (!ShowLabels) return;
        if (string.IsNullOrWhiteSpace(_value.Text) || _value.Text.Contains("{0}"))
            _value.Text = $"{_slider.Value}";
    }

    private void LayoutChildren()
    {
        if (_layout.RowStyles.Count < 3)
            return;

        _layout.RowStyles[0].SizeType = SizeType.Absolute;
        _layout.RowStyles[0].Height = ShowLabels ? 22F : 0F;
        _layout.RowStyles[1].SizeType = SizeType.Absolute;
        _layout.RowStyles[1].Height = ShowLabels ? 18F : 0F;
        _layout.RowStyles[2].SizeType = SizeType.Percent;
        _layout.RowStyles[2].Height = 100F;
        _layout.PerformLayout();
    }

    public void ApplyThemeColors()
    {
        BackColor = UiTheme.CardBackground;
        _layout.BackColor = BackColor;
        _title.ForeColor = UiTheme.TextPrimary;
        _value.ForeColor = UiTheme.TextPrimary;
        Invalidate();
    }
}
