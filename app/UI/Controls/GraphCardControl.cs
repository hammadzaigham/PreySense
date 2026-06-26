namespace PreySense.UI;

public class GraphCardControl : Panel
{
    private readonly Panel _header = new();
    private readonly Label _title = new();
    private readonly Panel _contentHost = new();

    public GraphCardControl()
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = RForm.formBack;
        Padding = new Padding(0);

        _header.Dock = DockStyle.Top;
        _header.Height = 28;
        _header.BackColor = BackColor;

        _title.Dock = DockStyle.Fill;
        _title.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _title.ForeColor = RForm.foreMain;
        _title.TextAlign = ContentAlignment.MiddleLeft;

        _header.Controls.Add(_title);

        _contentHost.Dock = DockStyle.Fill;
        _contentHost.MinimumSize = new Size(0, 320);
        _contentHost.BackColor = BackColor;
        _contentHost.Padding = new Padding(0, 6, 0, 0);

        Controls.Add(_contentHost);
        Controls.Add(_header);
    }

    public string Title
    {
        get => _title.Text;
        set => _title.Text = value;
    }

    public Control ContentHost => _contentHost;
}
