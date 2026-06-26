using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace PreySense.UI;

public static class UiTheme
{
    public const string FontFamily = "Segoe UI";
    public const int DialogPadding = 12;
    public const int CardGap = 10;
    public const int CardRadius = 4;
    public const int CardPadding = 12;
    public const int RowGap = 8;
    public const int ColumnGap = 6;

    public static readonly Color FormBackground = Color.FromArgb(28, 28, 28);
    public static readonly Color CardBackground = Color.FromArgb(28, 28, 28);
    public static readonly Color ElevatedCardBackground = Color.FromArgb(38, 38, 40);
    public static readonly Color TextPrimary = Color.White;
    public static readonly Color TextMuted = Color.FromArgb(168, 168, 168);
    public static readonly Color TextSubtle = Color.FromArgb(120, 120, 120);
    public static readonly Color Separator = Color.FromArgb(55, 55, 55);
    public static readonly Color Accent = Color.FromArgb(58, 174, 239);
    public static readonly Color AccentWarn = Color.FromArgb(255, 147, 51);

    public static float DialogScale(Control control, float multiplier = 1.25f)
    {
        return (control.DeviceDpi / 96f) * multiplier;
    }

    public static Font Font(float scale, float size, FontStyle style = FontStyle.Regular)
    {
        return new Font(FontFamily, size * scale, style);
    }

    public static void ApplyFixedDialog(Form form, string title)
    {
        form.BackColor = FormBackground;
        form.ForeColor = TextPrimary;
        form.Text = title;
        form.FormBorderStyle = FormBorderStyle.FixedSingle;
        form.MaximizeBox = false;
        form.MinimizeBox = false;
        form.StartPosition = FormStartPosition.CenterParent;
    }
}

public static class UiGeometry
{
    public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        int diameter = Math.Max(1, radius * 2);

        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        return path;
    }

    public static void ApplyRoundedRegion(Control control, int radius)
    {
        if (!control.IsHandleCreated || control.Width <= 0 || control.Height <= 0)
            return;

        Region? previous = control.Region;
        using GraphicsPath path = RoundedRect(new Rectangle(0, 0, control.Width, control.Height), radius);
        control.Region = new Region(path);
        previous?.Dispose();
    }
}

[DefaultProperty(nameof(CornerRadius))]
public class RoundedPanel : Panel
{
    private int _cornerRadius = UiTheme.CardRadius;

    public RoundedPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    [DefaultValue(UiTheme.CardRadius)]
    public int CornerRadius
    {
        get => _cornerRadius;
        set
        {
            _cornerRadius = Math.Max(0, value);
            UpdateRegion();
            Invalidate();
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateRegion();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateRegion();
    }

    private void UpdateRegion()
    {
        if (CornerRadius <= 0)
        {
            Region? previous = Region;
            Region = null;
            previous?.Dispose();
            return;
        }

        UiGeometry.ApplyRoundedRegion(this, CornerRadius);
    }
}

public sealed class UiBuilder
{
    public UiBuilder(float scale, int formWidth)
    {
        Scale = scale;
        FormWidth = formWidth;
    }

    public float Scale { get; }
    public int FormWidth { get; }

    public int S(int px) => (int)Math.Round(px * Scale);

    public FlowLayoutPanel RootStack(Control parent, int width, Padding? padding = null)
    {
        var stack = Stack(width);
        stack.Padding = padding ?? new Padding(S(UiTheme.DialogPadding));
        stack.BackColor = UiTheme.FormBackground;
        parent.Controls.Add(stack);
        return stack;
    }

    public FlowLayoutPanel Stack(int width, FlowDirection direction = FlowDirection.TopDown)
    {
        return new FlowLayoutPanel
        {
            FlowDirection = direction,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Width = width,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.Transparent
        };
    }

    public TableLayoutPanel Table(int width, params ColumnStyle[] columns)
    {
        var table = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Width = width,
            ColumnCount = Math.Max(1, columns.Length),
            RowCount = 0,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.Transparent
        };

        if (columns.Length == 0)
        {
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        }
        else
        {
            foreach (var column in columns)
                table.ColumnStyles.Add(column);
        }

        return table;
    }

    public void AddRow(TableLayoutPanel table, params Control[] controls)
    {
        int row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        for (int i = 0; i < controls.Length; i++)
        {
            var control = controls[i];
            control.Margin = new Padding(0, 0, i == controls.Length - 1 ? 0 : S(UiTheme.ColumnGap), S(UiTheme.RowGap));
            table.Controls.Add(control, i, row);
        }
    }

    public Label Text(string text, Font font, Color? color = null, ContentAlignment align = ContentAlignment.MiddleLeft)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Font = font,
            ForeColor = color ?? UiTheme.TextMuted,
            BackColor = Color.Transparent,
            TextAlign = align,
            Margin = Padding.Empty
        };
    }

    public RoundedPanel StackCard(
        FlowLayoutPanel parent,
        string title,
        Font headerFont,
        int width,
        out TableLayoutPanel body,
        Color? backColor = null,
        Color? titleColor = null,
        Color? accentStripe = null,
        int? radius = null)
    {
        int stripeWidth = accentStripe.HasValue ? S(3) : 0;
        int cardPadding = S(UiTheme.CardPadding);
        int contentWidth = width - stripeWidth - cardPadding * 2;

        var card = new RoundedPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Width = width,
            BackColor = backColor ?? UiTheme.CardBackground,
            CornerRadius = S(radius ?? UiTheme.CardRadius),
            Margin = new Padding(0, 0, 0, S(UiTheme.CardGap)),
            Padding = Padding.Empty
        };

        var outer = Table(width,
            accentStripe.HasValue ? new ColumnStyle(SizeType.Absolute, stripeWidth) : new ColumnStyle(SizeType.Absolute, 0),
            new ColumnStyle(SizeType.Percent, 100F));
        outer.ColumnCount = accentStripe.HasValue ? 2 : 1;
        if (!accentStripe.HasValue)
            outer.ColumnStyles.Clear();
        if (!accentStripe.HasValue)
            outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        if (accentStripe.HasValue)
        {
            outer.Controls.Add(new Panel
            {
                BackColor = accentStripe.Value,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty
            }, 0, 0);
        }

        var content = Table(contentWidth, new ColumnStyle(SizeType.Percent, 100F));
        content.Padding = new Padding(cardPadding);
        content.Margin = Padding.Empty;

        if (!string.IsNullOrWhiteSpace(title))
        {
            var titleLabel = Text(title, headerFont, titleColor ?? UiTheme.TextPrimary);
            titleLabel.Margin = new Padding(0, 0, 0, S(UiTheme.RowGap));
            content.Controls.Add(titleLabel, 0, content.RowCount);
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            content.RowCount++;
        }

        body = Table(contentWidth - cardPadding * 2, new ColumnStyle(SizeType.Percent, 100F));
        body.Margin = Padding.Empty;
        content.Controls.Add(body, 0, content.RowCount);
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowCount++;

        outer.Controls.Add(content, accentStripe.HasValue ? 1 : 0, 0);
        card.Controls.Add(outer);
        parent.Controls.Add(card);
        return card;
    }

    public RButton Button(string text, int width, int height, Font font, Color? borderColor = null, bool secondary = true)
    {
        var button = new RButton
        {
            Text = text,
            Size = new Size(width, height),
            Font = font,
            Secondary = secondary,
            Margin = Padding.Empty
        };

        if (borderColor.HasValue)
            button.BorderColor = borderColor.Value;

        return button;
    }

    public PredatorDropDown Combo(int width, Font font, int height = 36)
    {
        return new PredatorDropDown
        {
            Size = new Size(width, S(height)),
            Font = font,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            FlatStyle = FlatStyle.Flat,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = UiTheme.ElevatedCardBackground,
            ForeColor = UiTheme.TextPrimary,
            BorderColor = UiTheme.Separator,
            ButtonColor = Color.FromArgb(46, 46, 46),
            ArrowColor = UiTheme.TextPrimary
        };
    }

    public PictureBox ColorSwatch(Color color, int size = 22, int radius = 3)
    {
        var swatch = new PictureBox
        {
            Size = new Size(S(size), S(size)),
            BackColor = color,
            Cursor = Cursors.Hand,
            Margin = Padding.Empty
        };

        void Round() => UiGeometry.ApplyRoundedRegion(swatch, S(radius));
        swatch.HandleCreated += (_, _) => Round();
        swatch.SizeChanged += (_, _) => Round();

        return swatch;
    }

    public CheckBox Check(string text, Font font, Color? color = null)
    {
        return new CheckBox
        {
            Text = text,
            AutoSize = true,
            Font = font,
            ForeColor = color ?? UiTheme.TextMuted,
            BackColor = Color.Transparent,
            Margin = Padding.Empty
        };
    }
}
