using System;
using System.Drawing;
using System.Windows.Forms;
using PreySense.UI;

namespace PreySense.Rgb
{
    public partial class RgbForm
    {
        private float _dpiScale = 1f;
        private int _formW;
        private UiBuilder _ui = null!;
        private bool _isUpdatingUI;

        private Font _fontSection = null!;
        private Font _fontLabel = null!;
        private Font _fontBody = null!;
        private Font _fontBold = null!;

        private Label _titleLabel = null!;
        private RButton _ledTimeoutButton = null!;
        private PredatorDropDown _effectDropdown = null!;
        private PredatorDropDown _presetDropdown = null!;
        private TableLayoutPanel _presetRow = null!;
        private LabeledSliderControl _speedSettingRow = null!;
        private RNumericUpDown _speedValueBox = null!;
        private LabeledSliderControl _brightnessSettingRow = null!;
        private RNumericUpDown _brightnessValueBox = null!;
        private PredatorDropDown _directionDropdown = null!;
        private TableLayoutPanel _directionRow = null!;
        private RButton[] _zoneColorButtons = Array.Empty<RButton>();
        private TableLayoutPanel _zoneRow = null!;
        private const int LabelColPx = 84;

        private int S(int px) => (int)(px * _dpiScale);

        private void InitializeComponent()
        {
            _fontSection = UiTheme.Font(_dpiScale, 9f, FontStyle.Bold);
            _fontLabel = UiTheme.Font(_dpiScale, 9f);
            _fontBody = UiTheme.Font(_dpiScale, 9f);
            _fontBold = UiTheme.Font(_dpiScale, 9f, FontStyle.Bold);

            SuspendLayout();
            Controls.Clear();

            _formW = S(420);
            _ui = new UiBuilder(_dpiScale, _formW);

            UiTheme.ApplyFixedDialog(this, "Keyboard Lighting");
            AutoScaleMode = AutoScaleMode.None;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            ShowIcon = false;

            int contentWidth = _formW - S(UiTheme.DialogPadding * 2);
            int cardBodyWidth = contentWidth - S(UiTheme.CardPadding) * 4;
            int labelColWidth = S(LabelColPx);
            int valueWidth = S(52);
            int controlWidth = Math.Max(S(220), cardBodyWidth - labelColWidth - S(8));
            int sliderWidth = Math.Max(S(140), controlWidth - valueWidth);

            var root = _ui.RootStack(this, _formW,
                new Padding(S(UiTheme.DialogPadding), S(2), S(UiTheme.DialogPadding), S(2)));

            _ui.StackCard(root, string.Empty, _fontSection, contentWidth, out var body);

            // Header row: title on the left, idle-timeout toggle pinned to the right.
            var headerRow = new TableLayoutPanel
            {
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = S(26),
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, S(4)),
                Padding = Padding.Empty,
                BackColor = Color.Transparent
            };
            headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            headerRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _titleLabel = _ui.Text("Keyboard", UiTheme.Font(_dpiScale, 8f, FontStyle.Bold), UiTheme.TextPrimary);
            _titleLabel.Anchor = AnchorStyles.Left;
            _titleLabel.Margin = Padding.Empty;
            headerRow.Controls.Add(_titleLabel, 0, 0);

            _ledTimeoutButton = _ui.Button("30s Timeout", S(120), S(22), _fontBody, colorGray);
            _ledTimeoutButton.BorderRadius = 2;
            _ledTimeoutButton.Secondary = true;
            _ledTimeoutButton.FlatStyle = FlatStyle.Flat;
            _ledTimeoutButton.FlatAppearance.BorderSize = 0;
            _ledTimeoutButton.Borderless = true;
            _ledTimeoutButton.BackColor = buttonSecond;
            _ledTimeoutButton.ForeColor = foreMain;
            _ledTimeoutButton.FlatAppearance.BorderColor = borderSecond;
            _ledTimeoutButton.HoverBorderColor = borderSecond;
            _ledTimeoutButton.UseVisualStyleBackColor = false;
            _ledTimeoutButton.Anchor = AnchorStyles.Right;
            _ledTimeoutButton.Margin = Padding.Empty;
            _ledTimeoutButton.Click += (_, _) => ToggleLedTimeout();
            headerRow.Controls.Add(_ledTimeoutButton, 1, 0);

            body.Controls.Add(headerRow, 0, body.RowCount);
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.RowCount++;

            // Effect dropdown.
            AddComboRow(body, "Effect", controlWidth, out _effectDropdown);
            foreach (var name in RgbModeNames) _effectDropdown.Items.Add(name);
            _effectDropdown.SelectedIndex = 0;

            // Speed slider (1-5).
            _speedSettingRow = MakeSliderRow(body, "Speed", sliderWidth, valueWidth, 1, 5, 3, out _speedValueBox, val =>
            {
                if (!CanApplyHardware()) return;
                _wmi.SetSpeed((byte)val);
                SaveRgbState();
            });
            ArmSliderInputs(_speedSettingRow, _speedValueBox);

            // Direction dropdown (only visible for wave mode).
            _directionRow = AddComboRow(body, "Direction", controlWidth, out _directionDropdown);
            _directionDropdown.Items.Add("Left");
            _directionDropdown.Items.Add("Right");
            _directionDropdown.SelectedIndex = 0;
            ArmDropdown(_directionDropdown);
            _directionRow.Visible = false;

            // Preset dropdown.
            _presetRow = AddComboRow(body, "Preset", controlWidth, out _presetDropdown);
            foreach (var name in CustomPresetNames) _presetDropdown.Items.Add(name);
            _presetDropdown.SelectedIndex = 0;
            ArmDropdown(_presetDropdown);
            _presetRow.Visible = false;

            // Zone color buttons (Z1-Z4 + Sync).
            var zonesRow = _ui.Stack(controlWidth, FlowDirection.LeftToRight);
            zonesRow.AutoSize = false;
            zonesRow.Height = S(32);
            zonesRow.Width = controlWidth;
            zonesRow.Margin = Padding.Empty;
            int zoneBtnW = (controlWidth - S(UiTheme.ColumnGap) * 4) / 5;
            _zoneColorButtons = new RButton[5];
            string[] zoneLabels = { "Z1", "Z2", "Z3", "Z4", "Sync" };
            for (int i = 0; i < 5; i++)
            {
                bool isSync = i == 4;
                _zoneColorButtons[i] = _ui.Button(zoneLabels[i], zoneBtnW, S(30), _fontBody, UiTheme.Separator, secondary: !isSync);
                _zoneColorButtons[i].BorderRadius = 2;
                _zoneColorButtons[i].Borderless = true;
                _zoneColorButtons[i].FlatAppearance.BorderSize = 0;
                _zoneColorButtons[i].BorderColor = UiTheme.Separator;
                _zoneColorButtons[i].HoverBorderColor = UiTheme.Separator;
                _zoneColorButtons[i].Margin = new Padding(0, 0, isSync ? 0 : S(UiTheme.ColumnGap), 0);
                zonesRow.Controls.Add(_zoneColorButtons[i]);
            }
            _zoneRow = AddControlRow(body, "Zones", zonesRow);
            _zoneRow.Visible = false;

            // Brightness slider (1-5).
            _brightnessSettingRow = MakeSliderRow(body, "Brightness", sliderWidth, valueWidth, 1, 5, 5, out _brightnessValueBox, val =>
            {
                if (!CanApplyHardware()) return;
                _wmi.SetBrightness((byte)val);
                if (_effectDropdown.SelectedIndex == 0)
                {
                    _wmi.SetZoneColors(_wmi.ZoneColors, (byte)val);
                }
                SaveRgbState();
            });
            ArmSliderInputs(_brightnessSettingRow, _brightnessValueBox);

            _effectDropdown.SelectedIndexChanged += (_, _) => ApplyMode();
            ArmDropdown(_effectDropdown);
            _directionDropdown.SelectedIndexChanged += (_, _) => ApplyDirection();
            _presetDropdown.SelectedIndexChanged += (_, _) => ApplyPreset();
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                _zoneColorButtons[i].Click += (_, _) => PickZone(idx);
            }
            _zoneColorButtons[4].Click += (_, _) => SyncZones();

            ResumeLayout(false);
            PerformLayout();
        }

        /// <summary>
        /// Builds a "label | slider | numeric" row matching the Color Profile dialog:
        /// the slider and the editable numeric box stay in sync and both report changes
        /// through <paramref name="onValueChanged"/>.
        /// </summary>
        private LabeledSliderControl MakeSliderRow(
            TableLayoutPanel parent,
            string labelText,
            int sliderWidth,
            int valueWidth,
            int min,
            int max,
            int defaultValue,
            out RNumericUpDown nudVal,
            Action<int> onValueChanged)
        {
            var row = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Width = parent.Width,
                ColumnCount = 3,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, S(1)),
                Padding = Padding.Empty,
                BackColor = Color.Transparent
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(LabelColPx)));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, sliderWidth));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, valueWidth));
            row.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var label = _ui.Text(labelText, _fontLabel, UiTheme.TextPrimary);
            label.AutoSize = false;
            label.Width = S(LabelColPx);
            label.Height = S(18);
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Margin = new Padding(0, 0, S(2), 0);
            row.Controls.Add(label, 0, 0);

            var slider = new LabeledSliderControl
            {
                ShowLabels = false,
                Height = S(28),
                MinimumSize = new Size(sliderWidth, S(28)),
                ThumbScale = 0.8f,
                Min = min,
                Max = max,
                Value = defaultValue,
                Step = 1,
                Margin = new Padding(0, 0, S(1), 0),
                Dock = DockStyle.Fill
            };
            row.Controls.Add(slider, 1, 0);

            nudVal = new RNumericUpDown
            {
                Size = new Size(valueWidth, S(24)),
                Font = _fontBold,
                ForeColor = UiTheme.Accent,
                DecimalPlaces = 0,
                Increment = 1m,
                Minimum = min,
                Maximum = max,
                Value = defaultValue,
                TextAlign = HorizontalAlignment.Center,
                BorderStyle = BorderStyle.None,
                InterceptArrowKeys = true,
                Margin = Padding.Empty,
                Dock = DockStyle.Fill
            };
            nudVal.ApplyTheme(true);
            row.Controls.Add(nudVal, 2, 0);

            var localNud = nudVal;
            var localSlider = slider;
            slider.ValueChanged += (_, _) =>
            {
                if (_isUpdatingUI) return;
                _isUpdatingUI = true;
                try { localNud.Value = Math.Clamp(slider.Value, localNud.Minimum, localNud.Maximum); }
                finally { _isUpdatingUI = false; }
                onValueChanged(slider.Value);
            };
            nudVal.ValueChanged += (_, _) =>
            {
                if (_isUpdatingUI) return;
                _isUpdatingUI = true;
                try { localSlider.Value = (int)localNud.Value; }
                finally { _isUpdatingUI = false; }
                onValueChanged((int)localNud.Value);
            };

            parent.Controls.Add(row, 0, parent.RowCount);
            parent.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            parent.RowCount++;
            return slider;
        }

        private void ArmSliderInputs(LabeledSliderControl slider, RNumericUpDown valueBox)
        {
            slider.Slider.MouseDown += (_, _) => ArmHardwareApply();
            valueBox.MouseDown += (_, _) => ArmHardwareApply();
            valueBox.KeyDown += (_, _) => ArmHardwareApply();
        }

        private void ArmDropdown(PredatorDropDown combo)
        {
            combo.SelectionChangeCommitted += (_, _) => ArmHardwareApply();
        }

        private TableLayoutPanel AddComboRow(TableLayoutPanel parent, string labelText, int width, out PredatorDropDown combo)
        {
            combo = _ui.Combo(width, _fontBody, 32);
            return AddControlRow(parent, labelText, combo);
        }

        /// <summary>
        /// Builds a "label | control" row that lines its control's right edge up with
        /// the numeric boxes of the slider rows for a consistent grid.
        /// </summary>
        private TableLayoutPanel AddControlRow(TableLayoutPanel parent, string labelText, Control control)
        {
            var row = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Width = parent.Width,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, S(3)),
                Padding = Padding.Empty,
                BackColor = Color.Transparent
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(LabelColPx)));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            row.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var label = _ui.Text(labelText, _fontLabel, UiTheme.TextPrimary);
            label.AutoSize = false;
            label.Width = S(LabelColPx);
            label.Height = S(28);
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Margin = new Padding(0, 0, S(2), 0);
            row.Controls.Add(label, 0, 0);

            control.Dock = DockStyle.Fill;
            control.Margin = Padding.Empty;
            row.Controls.Add(control, 1, 0);

            parent.Controls.Add(row, 0, parent.RowCount);
            parent.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            parent.RowCount++;
            return row;
        }
    }
}
