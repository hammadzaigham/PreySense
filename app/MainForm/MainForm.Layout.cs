namespace PreySense;

public partial class MainForm
{
    private void ApplyCompactSectionLayout()
    {
        panelPerformance.Padding = new Padding(20);
        panelGPU.Padding = new Padding(20, 20, 20, 0);
        panelScreen.Padding = new Padding(20, 11, 20, 0);
        panelRgb.Padding = new Padding(20, 20, 20, 0);
        panelBattery.Padding = new Padding(20, 15, 20, 0);
        panelStartup.Padding = new Padding(20, 10, 20, 20);
        panelFooter.Padding = new Padding(20, 10, 20, 0);
        MatchSectionTitleColors();
    }

    private void MatchSectionTitleColors()
    {
        MatchTitlePanel(panelCPUTitle);
        MatchTitlePanel(panelGPUTitle);
        MatchTitlePanel(panelScreenTitle);
        MatchTitlePanel(panelRgbTitle);
        MatchTitlePanel(panelBatteryTitle);
    }

    private static void MatchTitlePanel(Panel panel)
    {
        panel.BackColor = panel.Parent?.BackColor ?? formBack;

        foreach (Control child in panel.Controls)
        {
            if (child is Label label)
                label.BackColor = panel.BackColor;
            else if (child is PictureBox picture)
                picture.BackColor = panel.BackColor;
        }
    }

    private static void SetDockTop(params Control[] controls)
    {
        foreach (var control in controls)
        {
            if (control is null) continue;
            control.Dock = DockStyle.Top;
        }
    }

    private void ApplyThemeAwareVisibility()
    {
        panelRgb.Visible = true;
        panelBattery.Visible = true;
        panelStartup.Visible = true;
    }
}

