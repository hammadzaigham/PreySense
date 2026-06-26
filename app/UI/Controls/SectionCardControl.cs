using System.ComponentModel;

namespace PreySense.UI;

public class SectionCardControl : Panel
{
    private readonly TableLayoutPanel _header = new();
    private readonly PictureBox _icon = new();
    private readonly Label _title = new();
    private readonly Panel _body = new();

    public SectionCardControl()
    {
        AutoSize = false;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = RForm.formBack;
        Padding = new Padding(0, 2, 2, 0);
        BorderStyle = BorderStyle.None;
        Width = 520;
        Height = 280;

        _header.Dock = DockStyle.Top;
        _header.Height = 40;
        _header.BackColor = BackColor;
        _header.ColumnCount = 2;
        _header.RowCount = 1;
        _header.Margin = Padding.Empty;
        _header.Padding = Padding.Empty;
        _header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 20F));
        _header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _header.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        _icon.Size = new Size(22, 22);
        _icon.Dock = DockStyle.Fill;
        _icon.Margin = new Padding(0, 9, 2, 9);
        _icon.SizeMode = PictureBoxSizeMode.Zoom;

        _title.AutoSize = false;
        _title.Dock = DockStyle.Fill;
        _title.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _title.ForeColor = RForm.foreMain;
        _title.Margin = Padding.Empty;
        _title.TextAlign = ContentAlignment.MiddleLeft;

        _header.Controls.Add(_icon, 0, 0);
        _header.Controls.Add(_title, 1, 0);

        _body.Dock = DockStyle.Fill;
        _body.BackColor = BackColor;
        _body.Padding = new Padding(0, 2, 0, 0);

        Controls.Add(_body);
        Controls.Add(_header);
    }

    [Browsable(false)]
    public Panel Body => _body;

    [Browsable(true)]
    public string SectionTitle
    {
        get => _title.Text;
        set => _title.Text = value;
    }

    [Browsable(true)]
    public Image? SectionIcon
    {
        get => _icon.Image;
        set => _icon.Image = value;
    }

    protected override void OnBackColorChanged(EventArgs e)
    {
        base.OnBackColorChanged(e);
        _header.BackColor = BackColor;
        _body.BackColor = BackColor;
        _icon.BackColor = BackColor;
        _title.BackColor = BackColor;
    }
}
