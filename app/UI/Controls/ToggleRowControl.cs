namespace PreySense.UI;

public class ToggleRowControl : Panel
{
    private readonly RCheckBox _check = new();

    public ToggleRowControl()
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = RForm.formBack;
        Padding = new Padding(0);

        _check.AutoSize = true;
        _check.Dock = DockStyle.Fill;
        _check.Font = new Font(UiTheme.FontFamily, 9F, FontStyle.Regular);
        _check.ForeColor = UiTheme.TextMuted;
        _check.BackColor = RForm.buttonSecond;
        _check.Cursor = Cursors.Hand;
        _check.Margin = Padding.Empty;
        _check.Padding = new Padding(12, 7, 12, 7);
        _check.BorderRadius = 5;
        _check.FlatStyle = FlatStyle.Flat;
        _check.FlatAppearance.BorderSize = 0;
        _check.UseVisualStyleBackColor = false;
        // RCheckBox instances built in code (after InitTheme) miss the theming pass
        // that assigns the rounded border color, so set it explicitly here.
        _check.FlatAppearance.BorderColor = RForm.borderSecond;

        Controls.Add(_check);
    }

    public string LabelText
    {
        get => _check.Text;
        set => _check.Text = value;
    }

    public bool Checked
    {
        get => _check.Checked;
        set => _check.Checked = value;
    }

    public event EventHandler? CheckedChanged
    {
        add => _check.CheckedChanged += value;
        remove => _check.CheckedChanged -= value;
    }
}
