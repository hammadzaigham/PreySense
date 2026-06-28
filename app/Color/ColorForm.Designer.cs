using PreySense.UI;

namespace PreySense.Display;

public partial class ColorForm
{
    private UiBuilder _ui = null!;
    private FlowLayoutPanel _root = null!;
    private RoundedPanel _card = null!;
    private TableLayoutPanel _body = null!;
    private Label _title = null!;
    private RButton _resetButton = null!;
    private RButton _btnAll = null!;
    private RButton _btnRed = null!;
    private RButton _btnGreen = null!;
    private RButton _btnBlue = null!;
    private LabeledSliderControl _brightnessSlider = null!;
    private LabeledSliderControl _contrastSlider = null!;
    private LabeledSliderControl _gammaSlider = null!;
    private LabeledSliderControl _saturationSlider = null!;
    private LabeledSliderControl _hueSlider = null!;
    private RNumericUpDown _brightnessValue = null!;
    private RNumericUpDown _contrastValue = null!;
    private RNumericUpDown _gammaValue = null!;
    private RNumericUpDown _saturationValue = null!;
    private RNumericUpDown _hueValue = null!;
    private PredatorDropDown _blueLightCombo = null!;
    private System.Windows.Forms.Timer _applyTimer = null!;
    private bool _pendingApply;

    private void InitializeComponent()
    {
        float scale = DeviceDpi / 96f;
        _ui = new UiBuilder(scale, (int)Math.Round(430 * scale));

        SuspendLayout();
        Controls.Clear();

        UiTheme.ApplyFixedDialog(this, "Display Profile");
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        ShowIcon = false;

        _root = _ui.RootStack(this, _ui.FormWidth, new Padding(_ui.S(UiTheme.DialogPadding), _ui.S(2), _ui.S(UiTheme.DialogPadding), _ui.S(2)));
        _card = _ui.StackCard(_root, string.Empty, UiTheme.Font(scale, 9f, FontStyle.Bold), _ui.FormWidth, out _body);
        _card.BackColor = UiTheme.CardBackground;

        var header = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.Transparent
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _title = _ui.Text("Display Profile", UiTheme.Font(scale, 9f, FontStyle.Bold), UiTheme.TextPrimary);
        _title.Margin = Padding.Empty;
        header.Controls.Add(_title, 0, 0);

        _resetButton = _ui.Button("Reset", _ui.S(72), _ui.S(24), UiTheme.Font(scale, 8f, FontStyle.Bold), UiTheme.Separator, secondary: true);
        _resetButton.BorderRadius = 2;
        _resetButton.Borderless = false;
        _resetButton.FlatStyle = FlatStyle.Flat;
        _resetButton.FlatAppearance.BorderSize = 0;
        _resetButton.BorderColor = UiTheme.Separator;
        _resetButton.HoverBorderColor = UiTheme.Accent;
        _resetButton.BackColor = UiTheme.ElevatedCardBackground;
        _resetButton.ForeColor = UiTheme.TextPrimary;
        _resetButton.Anchor = AnchorStyles.Right;
        _resetButton.Margin = Padding.Empty;
        _resetButton.Click += ResetClicked;
        header.Controls.Add(_resetButton, 1, 0);

        _body.Controls.Add(header, 0, _body.RowCount++);
        _body.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _body.Controls.Add(new Panel { Height = _ui.S(10), Dock = DockStyle.Top, Margin = Padding.Empty, BackColor = Color.Transparent }, 0, _body.RowCount++);
        _body.RowStyles.Add(new RowStyle(SizeType.Absolute, _ui.S(10)));

        AddChannelButtonsRow(out _btnAll, out _btnRed, out _btnGreen, out _btnBlue);

        AddRangeRow("Brightness", 0, 100, 50, 0, out _brightnessSlider, out _brightnessValue);
        AddRangeRow("Contrast", 0, 100, 50, 0, out _contrastSlider, out _contrastValue);
        AddRangeRow("Gamma", 0, 500, 100, 2, out _gammaSlider, out _gammaValue);
        AddRangeRow("Saturation", 0, 100, 50, 0, out _saturationSlider, out _saturationValue);
        AddRangeRow("Hue", -180, 180, 0, 0, out _hueSlider, out _hueValue);
        AddDropDownRow("Bluelight Shield", out _blueLightCombo);

        _brightnessSlider.ValueChanged += SliderChanged;
        _contrastSlider.ValueChanged += SliderChanged;
        _gammaSlider.ValueChanged += SliderChanged;
        _saturationSlider.ValueChanged += SliderChanged;
        _hueSlider.ValueChanged += SliderChanged;

        _brightnessValue.ValueChanged += NumericChanged;
        _contrastValue.ValueChanged += NumericChanged;
        _gammaValue.ValueChanged += NumericChanged;
        _saturationValue.ValueChanged += NumericChanged;
        _hueValue.ValueChanged += NumericChanged;

        _applyTimer = new System.Windows.Forms.Timer { Interval = 33 };
        _applyTimer.Tick += ApplyTimerTick;
        _applyTimer.Start();

        ResumeLayout(false);
        PerformLayout();
    }

    private void AddRangeRow(string labelText, int min, int max, decimal defaultValue, int decimalPlaces, out LabeledSliderControl slider, out RNumericUpDown numeric)
    {
        int width = _ui.FormWidth - _ui.S(UiTheme.DialogPadding) * 2 - _ui.S(UiTheme.CardPadding) * 2;
        int labelWidth = _ui.S(92);
        int numericWidth = _ui.S(66);
        int sliderWidth = width - labelWidth - numericWidth - _ui.S(10);

        var row = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, _ui.S(UiTheme.RowGap)),
            Padding = Padding.Empty,
            BackColor = Color.Transparent
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, labelWidth));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, sliderWidth));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, numericWidth));
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var label = _ui.Text(labelText, UiTheme.Font(_ui.Scale, 9f), UiTheme.TextPrimary);
        label.AutoSize = false;
        label.Width = labelWidth;
        label.Height = _ui.S(18);
        label.TextAlign = ContentAlignment.MiddleLeft;
        label.Margin = new Padding(0, 0, _ui.S(2), 0);
        row.Controls.Add(label, 0, 0);

        slider = new LabeledSliderControl
        {
            ShowLabels = false,
            Min = min,
            Max = max,
            Value = (int)defaultValue,
            Step = 1,
            Height = _ui.S(28),
            Width = sliderWidth,
            Margin = new Padding(0)
        };
        row.Controls.Add(slider, 1, 0);

        numeric = new RNumericUpDown
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(defaultValue, min, max),
            Width = numericWidth,
            Height = _ui.S(26),
            Margin = new Padding(0),
            TextAlign = HorizontalAlignment.Center,
            BorderStyle = BorderStyle.None
        };
        if (decimalPlaces > 0)
        {
            numeric.DecimalPlaces = decimalPlaces;
            numeric.Increment = decimalPlaces == 2 ? 0.01m : 0.1m;
        }
        else
        {
            numeric.Increment = 1;
        }
        numeric.ApplyTheme(true);
        row.Controls.Add(numeric, 2, 0);

        _body.Controls.Add(row, 0, _body.RowCount++);
        _body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    }

    private void AddDropDownRow(string labelText, out PredatorDropDown dropDown)
    {
        int width = _ui.FormWidth - _ui.S(UiTheme.DialogPadding) * 2 - _ui.S(UiTheme.CardPadding) * 2;
        int labelWidth = _ui.S(92);
        int comboWidth = width - labelWidth - _ui.S(2);

        var row = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, _ui.S(UiTheme.RowGap)),
            Padding = Padding.Empty,
            BackColor = Color.Transparent
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, labelWidth));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, comboWidth));
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var label = _ui.Text(labelText, UiTheme.Font(_ui.Scale, 9f), UiTheme.TextPrimary);
        label.AutoSize = false;
        label.Width = labelWidth;
        label.Height = _ui.S(28);
        label.TextAlign = ContentAlignment.MiddleLeft;
        label.Margin = Padding.Empty;
        row.Controls.Add(label, 0, 0);

        dropDown = new PredatorDropDown
        {
            Width = comboWidth,
            Height = _ui.S(28),
            Margin = Padding.Empty,
            Cursor = Cursors.Hand
        };
        row.Controls.Add(dropDown, 1, 0);

        _body.Controls.Add(row, 0, _body.RowCount++);
        _body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    }

    private void AddChannelButtonsRow(out RButton btnAll, out RButton btnRed, out RButton btnGreen, out RButton btnBlue)
    {
        int width = _ui.FormWidth - _ui.S(UiTheme.DialogPadding) * 2 - _ui.S(UiTheme.CardPadding) * 2;
        int btnWidth = (width - _ui.S(12)) / 4;

        var row = new TableLayoutPanel
        {
            // Flow/Table row containing All, Red, Green, Blue buttons
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            ColumnCount = 4,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, _ui.S(10)),
            Padding = Padding.Empty,
            BackColor = Color.Transparent
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, btnWidth));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, btnWidth));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, btnWidth));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, btnWidth));
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        btnAll = _ui.Button("All", btnWidth, _ui.S(26), UiTheme.Font(_ui.Scale, 8.5f, FontStyle.Bold), UiTheme.Separator, secondary: true);
        btnRed = _ui.Button("Red", btnWidth, _ui.S(26), UiTheme.Font(_ui.Scale, 8.5f, FontStyle.Bold), UiTheme.Separator, secondary: true);
        btnGreen = _ui.Button("Green", btnWidth, _ui.S(26), UiTheme.Font(_ui.Scale, 8.5f, FontStyle.Bold), UiTheme.Separator, secondary: true);
        btnBlue = _ui.Button("Blue", btnWidth, _ui.S(26), UiTheme.Font(_ui.Scale, 8.5f, FontStyle.Bold), UiTheme.Separator, secondary: true);

        foreach (var btn in new[] { btnAll, btnRed, btnGreen, btnBlue })
        {
            btn.BorderRadius = 2;
            btn.Borderless = false;
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 0;
            btn.BorderColor = Color.Transparent;
            btn.HoverBorderColor = Color.FromArgb(160, 160, 160);
            btn.BackColor = UiTheme.ElevatedCardBackground;
            btn.ForeColor = UiTheme.TextPrimary;
            btn.Margin = new Padding(0, 0, _ui.S(3), 0);
        }
        btnBlue.Margin = Padding.Empty;

        row.Controls.Add(btnAll, 0, 0);
        row.Controls.Add(btnRed, 1, 0);
        row.Controls.Add(btnGreen, 2, 0);
        row.Controls.Add(btnBlue, 3, 0);

        _body.Controls.Add(row, 0, _body.RowCount++);
        _body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    }
}





