using System;
using System.Drawing;
using System.Windows.Forms;
using PreySense.UI;

namespace PreySense.UI.Controls
{
    public sealed class FooterQuickActionsControl : UserControl
    {
        private readonly RButton _keyboardButton = new();
        private readonly RButton _metricsButton = new();
        private readonly TableLayoutPanel _layout = new();

        public event EventHandler? KeyboardClicked;
        public event EventHandler? MetricsClicked;

        public FooterQuickActionsControl()
        {
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            BackColor = Color.Transparent;
            Margin = Padding.Empty;
            Padding = Padding.Empty;

            _layout.AutoSize = true;
            _layout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _layout.ColumnCount = 2;
            _layout.RowCount = 1;
            _layout.Dock = DockStyle.Fill;
            _layout.Margin = Padding.Empty;
            _layout.Padding = Padding.Empty;
            _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            ConfigureButton(_keyboardButton, "Keyboard");
            ConfigureButton(_metricsButton, "Metrics");

            _keyboardButton.Click += (_, _) => KeyboardClicked?.Invoke(this, EventArgs.Empty);
            _metricsButton.Click += (_, _) => MetricsClicked?.Invoke(this, EventArgs.Empty);

            _layout.Controls.Add(_keyboardButton, 0, 0);
            _layout.Controls.Add(_metricsButton, 1, 0);
            Controls.Add(_layout);
        }

        public Image? KeyboardIcon
        {
            get => _keyboardButton.Image;
            set => _keyboardButton.Image = value;
        }

        public Image? MetricsIcon
        {
            get => _metricsButton.Image;
            set => _metricsButton.Image = value;
        }

        public void ApplyTheme(Color buttonBack, Color foreColor, Color borderColor)
        {
            StyleButton(_keyboardButton, buttonBack, foreColor, borderColor);
            StyleButton(_metricsButton, buttonBack, foreColor, borderColor);
        }

        private static void ConfigureButton(RButton button, string text)
        {
            button.Name = text.Replace("&", string.Empty).Replace(" ", string.Empty);
            button.Text = text;
            button.Activated = false;
            button.Secondary = true;
            button.BorderRadius = 2;
            button.BorderColor = Color.Transparent;
            button.Dock = DockStyle.Fill;
            button.FlatStyle = FlatStyle.Flat;
            button.ImageAlign = ContentAlignment.MiddleLeft;
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.TextImageRelation = TextImageRelation.ImageBeforeText;
            button.Padding = new Padding(10, 0, 10, 0);
            button.Margin = new Padding(4, 2, 4, 2);
            button.AutoSize = false;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.MinimumSize = new Size(0, 40);
            button.UseVisualStyleBackColor = false;
        }

        private static void StyleButton(RButton button, Color buttonBack, Color foreColor, Color borderColor)
        {
            button.BackColor = buttonBack;
            button.ForeColor = foreColor;
            button.FlatAppearance.BorderColor = borderColor;
        }
    }
}
