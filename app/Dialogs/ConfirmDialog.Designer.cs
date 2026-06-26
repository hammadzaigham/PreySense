using System.Drawing;
using System.Windows.Forms;
using PreySense.UI;

namespace PreySense.Dialogs
{
    public partial class ConfirmDialog
    {
        private Label _labelMessage = null!;
        private RButton _buttonYes = null!;
        private RButton _buttonNo = null!;

        private void InitializeComponent(string message, string title, string yesText = "Apply && Restart Now", string noText = "Cancel")
        {
            float scale = DeviceDpi / 96f;
            int S(int px) => (int)Math.Round(px * scale);

            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var root = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Width = S(420),
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(S(20)),
                Margin = Padding.Empty,
                BackColor = Color.Transparent
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _labelMessage = new Label
            {
                Text = message,
                AutoSize = true,
                MaximumSize = new Size(S(380), 0),
                Font = new Font(UiTheme.FontFamily, 9.5F),
                TextAlign = ContentAlignment.TopLeft,
                Margin = new Padding(0, 0, 0, S(18))
            };

            var footer = new FlowLayoutPanel
            {
                AutoSize = false,
                Width = S(380),
                Height = S(36),
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = Color.Transparent
            };

            int yesWidth = Math.Max(S(80), TextRenderer.MeasureText(yesText, new Font(UiTheme.FontFamily, 9.5F)).Width + S(26));
            int noWidth = Math.Max(S(80), TextRenderer.MeasureText(noText, new Font(UiTheme.FontFamily, 9.5F)).Width + S(26));

            _buttonYes = new RButton
            {
                Text = yesText,
                Size = new Size(yesWidth, S(30)),
                DialogResult = DialogResult.Yes,
                Secondary = true,
                Margin = new Padding(0, 0, 0, 0)
            };
            _buttonYes.Click += (_, _) => Close();

            _buttonNo = new RButton
            {
                Text = noText,
                Size = new Size(noWidth, S(30)),
                DialogResult = DialogResult.No,
                Secondary = true,
                Margin = new Padding(0, 0, S(10), 0)
            };
            _buttonNo.Click += (_, _) => Close();

            footer.Controls.Add(_buttonNo);
            footer.Controls.Add(_buttonYes);

            root.Controls.Add(_labelMessage, 0, 0);
            root.Controls.Add(footer, 0, 1);
            Controls.Add(root);
        }
    }
}
