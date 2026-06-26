using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;
using PreySense.UI;
using System.Windows.Forms.DataVisualization.Charting;
using PreySense;

namespace PreySense.Fan
{
    partial class Fans
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            ChartArea chartArea1 = new ChartArea();
            Title title1 = new Title();
            ChartArea chartArea2 = new ChartArea();
            Title title2 = new Title();
            ChartArea chartArea3 = new ChartArea();
            Title title3 = new Title();
            ChartArea chartArea4 = new ChartArea();
            Title title4 = new Title();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Fans));
            panelFans = new Panel();
            tableFanCharts = new TableLayoutPanel();
            chartGPU = new RChart();
            chartCPU = new RChart();
            chartXGM = new RChart();
            chartMid = new RChart();
            panelTitleFans = new Panel();
            picturePerf = new PictureBox();
            labelFans = new Label();
            panelApplyFans = new Panel();
            labelFansResult = new Label();
            checkApplyFanCurves = new RCheckBox();
            panelMainControls = new Panel();
            panelCpuLimitsSection = new Panel();
            panelApplyCpuLimits = new Panel();
            checkApplyCpuLimits = new RCheckBox();
            panelCpuLimitsGraph = new Panel();
            labelCPU = new Label();
            labelLeftCPU = new Label();
            trackCPU = new RTrackBar();
            panelPl2 = new Panel();
            labelPl2 = new Label();
            labelLeftPl2 = new Label();
            trackPl2 = new RTrackBar();
            panelPl1 = new Panel();
            labelPl1 = new Label();
            labelLeftPl1 = new Label();
            trackPl1 = new RTrackBar();
            panelCpuLimitsTitle = new Panel();
            pictureBoxCPU = new PictureBox();
            labelCpuLimitsTitle = new Label();
            labelPowerModeTitle = new Label();
            panelCpuLimitsSectionMode = new Panel();
            comboWindowsPowerMode = new RComboBox();
            panelCpuLimitsSectionModeTitle = new Panel();
            picturePowerMode = new PictureBox();
            labelPowerModeTitle = new Label();
            panelGpuOffsetsSection = new Panel();
            panelGpuOffsetsSectionMemory = new Panel();
            labelGpuMemoryValue = new Label();
            labelGpuMemoryTitle = new Label();
            trackGpuMemoryOffset = new RTrackBar();
            panelGpuOffsetsSectionCore = new Panel();
            labelGpuCoreValue = new Label();
            trackGpuCoreOffset = new RTrackBar();
            labelGpuCoreTitle = new Label();
            panelGpuOffsetsTitle = new Panel();
            pictureGPU = new PictureBox();
            labelGpuOffsets = new Label();
            panelNav = new Panel();
            tableNav = new TableLayoutPanel();
            buttonGPU = new RButton();
            buttonCPU = new RButton();
            panelFans.SuspendLayout();
            tableFanCharts.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)chartGPU).BeginInit();
            ((System.ComponentModel.ISupportInitialize)chartCPU).BeginInit();
            ((System.ComponentModel.ISupportInitialize)chartXGM).BeginInit();
            ((System.ComponentModel.ISupportInitialize)chartMid).BeginInit();
            panelTitleFans.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)picturePerf).BeginInit();
            panelApplyFans.SuspendLayout();
            panelMainControls.SuspendLayout();
            panelCpuLimitsSection.SuspendLayout();
            panelApplyCpuLimits.SuspendLayout();
            panelCpuLimitsGraph.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)trackCPU).BeginInit();
            panelPl2.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)trackPl2).BeginInit();
            panelPl1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)trackPl1).BeginInit();
            panelCpuLimitsTitle.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)pictureBoxCPU).BeginInit();
            panelCpuLimitsSectionMode.SuspendLayout();
            panelCpuLimitsSectionModeTitle.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)picturePowerMode).BeginInit();
            panelGpuOffsetsSection.SuspendLayout();
            panelGpuOffsetsSectionMemory.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)trackGpuMemoryOffset).BeginInit();
            panelGpuOffsetsSectionCore.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)trackGpuCoreOffset).BeginInit();
            panelGpuOffsetsTitle.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)pictureGPU).BeginInit();
            panelNav.SuspendLayout();
            tableNav.SuspendLayout();
            SuspendLayout();
            // 
            // panelFans
            // 
            panelFans.AutoSize = true;
            panelFans.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panelFans.Controls.Add(tableFanCharts);
            panelFans.Controls.Add(panelTitleFans);
            panelFans.Controls.Add(panelApplyFans);
            panelFans.Dock = DockStyle.Fill;
            panelFans.Location = new Point(530, 0);
            panelFans.Margin = new Padding(0);
            panelFans.MinimumSize = new Size(816, 0);
            panelFans.Name = "panelFans";
            panelFans.Padding = new Padding(0, 0, 10, 0);
            panelFans.Size = new Size(820, 1100);
            panelFans.TabIndex = 12;
            // tableFanCharts
            // 
            tableFanCharts.AutoSize = true;
            tableFanCharts.ColumnCount = 1;
            tableFanCharts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            tableFanCharts.Controls.Add(chartGPU, 0, 1);
            tableFanCharts.Controls.Add(chartCPU, 0, 0);
            tableFanCharts.Controls.Add(chartXGM, 0, 2);
            tableFanCharts.Controls.Add(chartMid, 0, 3);
            tableFanCharts.Dock = DockStyle.Fill;
            tableFanCharts.Location = new Point(0, 66);
            tableFanCharts.Margin = new Padding(4);
            tableFanCharts.Name = "tableFanCharts";
            tableFanCharts.Padding = new Padding(10, 0, 10, 5);
            tableFanCharts.RowCount = 4;
            tableFanCharts.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));
            tableFanCharts.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));
            tableFanCharts.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));
            tableFanCharts.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));
            tableFanCharts.Size = new Size(810, 918);
            tableFanCharts.TabIndex = 1;
            // 
            // chartGPU
            // 
            chartArea1.Name = "ChartArea1";
            chartGPU.ChartAreas.Add(chartArea1);
            chartGPU.Dock = DockStyle.Fill;
            chartGPU.Location = new Point(12, 238);
            chartGPU.Margin = new Padding(2, 10, 2, 10);
            chartGPU.Name = "chartGPU";
            chartGPU.Size = new Size(786, 208);
            chartGPU.TabIndex = 1;
            chartGPU.Text = "chartGPU";
            title1.Name = "Title1";
            chartGPU.Titles.Add(title1);
            // 
            // chartCPU
            // 
            chartArea2.Name = "ChartArea1";
            chartCPU.ChartAreas.Add(chartArea2);
            chartCPU.Dock = DockStyle.Fill;
            chartCPU.Location = new Point(12, 10);
            chartCPU.Margin = new Padding(2, 10, 2, 10);
            chartCPU.Name = "chartCPU";
            chartCPU.Size = new Size(786, 208);
            chartCPU.TabIndex = 0;
            chartCPU.Text = "chartCPU";
            title2.Name = "Title1";
            chartCPU.Titles.Add(title2);
            // 
            // chartXGM
            // 
            chartArea3.Name = "ChartAreaXGM";
            chartXGM.ChartAreas.Add(chartArea3);
            chartXGM.Dock = DockStyle.Fill;
            chartXGM.Location = new Point(12, 694);
            chartXGM.Margin = new Padding(2, 10, 2, 10);
            chartXGM.Name = "chartXGM";
            chartXGM.Size = new Size(786, 209);
            chartXGM.TabIndex = 3;
            chartXGM.Text = "chartXGM";
            title3.Name = "Title4";
            chartXGM.Titles.Add(title3);
            chartXGM.Visible = false;
            // 
            // chartMid
            // 
            chartArea4.Name = "ChartArea3";
            chartMid.ChartAreas.Add(chartArea4);
            chartMid.Dock = DockStyle.Fill;
            chartMid.Location = new Point(12, 466);
            chartMid.Margin = new Padding(2, 10, 2, 10);
            chartMid.Name = "chartMid";
            chartMid.Size = new Size(786, 208);
            chartMid.TabIndex = 2;
            chartMid.Text = "chartMid";
            title4.Name = "Title3";
            chartMid.Titles.Add(title4);
            chartMid.Visible = false;
            // 
            // panelTitleFans
            // 
            panelTitleFans.Controls.Add(picturePerf);
            panelTitleFans.Controls.Add(labelFans);
            panelTitleFans.Dock = DockStyle.Top;
            panelTitleFans.Location = new Point(0, 0);
            panelTitleFans.Margin = new Padding(4);
            panelTitleFans.Name = "panelTitleFans";
            panelTitleFans.Size = new Size(810, 66);
            panelTitleFans.TabIndex = 0;
            // picturePerf
            // 
            picturePerf.BackgroundImage = Properties.Resources.icons8_fan_32;
            picturePerf.BackgroundImageLayout = ImageLayout.Zoom;
            picturePerf.InitialImage = null;
            picturePerf.Location = new Point(18, 18);
            picturePerf.Margin = new Padding(4, 2, 4, 2);
            picturePerf.Name = "picturePerf";
            picturePerf.Size = new Size(32, 32);
            picturePerf.TabIndex = 41;
            picturePerf.TabStop = false;
            // 
            // labelFans
            // 
            labelFans.AutoSize = true;
            labelFans.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            labelFans.Location = new Point(53, 17);
            labelFans.Margin = new Padding(4, 0, 4, 0);
            labelFans.Name = "labelFans";
            labelFans.Size = new Size(90, 32);
            labelFans.TabIndex = 40;
            labelFans.Text = "Profile";
            // 
            // panelApplyFans
            // 
            panelApplyFans.Controls.Add(labelFansResult);
            panelApplyFans.Controls.Add(checkApplyFanCurves);
            panelApplyFans.Dock = DockStyle.Bottom;
            panelApplyFans.Location = new Point(0, 984);
            panelApplyFans.Margin = new Padding(4);
            panelApplyFans.Name = "panelApplyFans";
            panelApplyFans.Size = new Size(810, 116);
            panelApplyFans.TabIndex = 4;
            // 
            // labelFansResult
            // 
            labelFansResult.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            labelFansResult.ForeColor = Color.Red;
            labelFansResult.Location = new Point(18, 2);
            labelFansResult.Margin = new Padding(4, 0, 4, 0);
            labelFansResult.Name = "labelFansResult";
            labelFansResult.Size = new Size(771, 32);
            labelFansResult.TabIndex = 3;
            labelFansResult.Visible = false;
            // 
            // checkApplyFanCurves
            // 
            checkApplyFanCurves.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            checkApplyFanCurves.AutoSize = true;
            checkApplyFanCurves.BackColor = SystemColors.ControlLight;
            checkApplyFanCurves.Location = new Point(454, 42);
            checkApplyFanCurves.Margin = new Padding(0);
            checkApplyFanCurves.Name = "checkApplyFanCurves";
            checkApplyFanCurves.Padding = new Padding(16, 6, 16, 6);
            checkApplyFanCurves.Size = new Size(341, 48);
            checkApplyFanCurves.TabIndex = 2;
            checkApplyFanCurves.Text = "Apply Custom Fan Curve";
            checkApplyFanCurves.UseVisualStyleBackColor = false;
            // panelMainControls
            // 
            panelMainControls.Controls.Add(panelCpuLimitsSection);
            panelMainControls.Controls.Add(panelGpuOffsetsSection);
            panelMainControls.Controls.Add(panelNav);
            panelMainControls.Dock = DockStyle.Left;
            panelMainControls.Location = new Point(0, 0);
            panelMainControls.Margin = new Padding(0);
            panelMainControls.MinimumSize = new Size(530, 0);
            panelMainControls.Name = "panelMainControls";
            panelMainControls.Padding = new Padding(10, 0, 0, 0);
            panelMainControls.Size = new Size(530, 1100);
            panelMainControls.TabIndex = 13;
            // 
            // 
            // 
            // 
            // 
            // 
            // 
            // 
            // 
            // 
            // 
            // 
            // 
            // 
            // 
            // 
            // 
            // 
            // 
            // 
            // 
            // 
            // 
            // 
            // 
            // 
            // 
            // 
            // 
            // 
            // 
            // 
            // 
            // 
            // 
            // 
            // 
            // 
            // 
            // 
            // 
            // 
            // 
            // panelCpuLimitsSection
            // 
            panelCpuLimitsSection.AutoSize = true;
            panelCpuLimitsSection.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panelCpuLimitsSection.Controls.Add(panelApplyCpuLimits);
            panelCpuLimitsSection.Controls.Add(panelCpuLimitsGraph);
            panelCpuLimitsSection.Controls.Add(panelPl2);
            panelCpuLimitsSection.Controls.Add(panelPl1);
            panelCpuLimitsSection.Controls.Add(panelCpuLimitsTitle);
            panelCpuLimitsSection.Controls.Add(panelCpuLimitsSectionMode);
            panelCpuLimitsSection.Controls.Add(panelCpuLimitsSectionModeTitle);
            panelCpuLimitsSection.Dock = DockStyle.Top;
            panelCpuLimitsSection.Location = new Point(10, 888);
            panelCpuLimitsSection.Margin = new Padding(4);
            panelCpuLimitsSection.Name = "panelCpuLimitsSection";
            panelCpuLimitsSection.Size = new Size(520, 880);
            panelCpuLimitsSection.TabIndex = 2;
            // 
            // panelApplyCpuLimits
            // 
            panelApplyCpuLimits.AutoSize = true;
            panelApplyCpuLimits.Controls.Add(checkApplyCpuLimits);
            panelApplyCpuLimits.Dock = DockStyle.Top;
            panelApplyCpuLimits.Location = new Point(0, 804);
            panelApplyCpuLimits.Name = "panelApplyCpuLimits";
            panelApplyCpuLimits.Padding = new Padding(15);
            panelApplyCpuLimits.Size = new Size(520, 76);
            panelApplyCpuLimits.TabIndex = 9;
            // 
            // checkApplyCpuLimits
            // 
            checkApplyCpuLimits.BackColor = SystemColors.ControlLight;
            checkApplyCpuLimits.Dock = DockStyle.Top;
            checkApplyCpuLimits.Location = new Point(15, 15);
            checkApplyCpuLimits.Margin = new Padding(0);
            checkApplyCpuLimits.Name = "checkApplyCpuLimits";
            checkApplyCpuLimits.Padding = new Padding(16, 6, 16, 6);
            checkApplyCpuLimits.Size = new Size(490, 46);
            checkApplyCpuLimits.TabIndex = 45;
            checkApplyCpuLimits.Text = "Apply Power Limits";
            checkApplyCpuLimits.UseVisualStyleBackColor = false;
            // 
            // panelCpuLimitsGraph
            // 
            panelCpuLimitsGraph.AutoSize = true;
            panelCpuLimitsGraph.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panelCpuLimitsGraph.Controls.Add(labelCPU);
            panelCpuLimitsGraph.Controls.Add(labelLeftCPU);
            panelCpuLimitsGraph.Controls.Add(trackCPU);
            panelCpuLimitsGraph.Dock = DockStyle.Top;
            panelCpuLimitsGraph.Location = new Point(0, 680);
            panelCpuLimitsGraph.Margin = new Padding(4);
            panelCpuLimitsGraph.MaximumSize = new Size(0, 124);
            panelCpuLimitsGraph.Name = "panelCpuLimitsGraph";
            panelCpuLimitsGraph.Size = new Size(520, 124);
            panelCpuLimitsGraph.TabIndex = 8;
            // 
            // labelCPU
            // 
            labelCPU.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            labelCPU.Location = new Point(398, 8);
            labelCPU.Margin = new Padding(4, 0, 4, 0);
            labelCPU.Name = "labelCPU";
            labelCPU.Size = new Size(116, 32);
            labelCPU.TabIndex = 13;
            labelCPU.Text = "CPU";
            labelCPU.TextAlign = ContentAlignment.TopRight;
            // 
            // labelLeftCPU
            // 
            labelLeftCPU.AutoSize = true;
            labelLeftCPU.Location = new Point(10, 8);
            labelLeftCPU.Margin = new Padding(4, 0, 4, 0);
            labelLeftCPU.Name = "labelLeftCPU";
            labelLeftCPU.Size = new Size(58, 32);
            labelLeftCPU.TabIndex = 12;
            labelLeftCPU.Text = "CPU";
            // 
            // trackCPU
            // 
            trackCPU.Location = new Point(6, 44);
            trackCPU.Margin = new Padding(4, 2, 4, 2);
            trackCPU.Maximum = 85;
            trackCPU.Minimum = 5;
            trackCPU.Name = "trackCPU";
            trackCPU.Size = new Size(508, 90);
            trackCPU.TabIndex = 11;
            trackCPU.TickFrequency = 5;
            trackCPU.TickStyle = TickStyle.TopLeft;
            trackCPU.Value = 80;
            // 
            // panelPl2
            // 
            panelPl2.AutoSize = true;
            panelPl2.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panelPl2.Controls.Add(labelPl2);
            panelPl2.Controls.Add(labelLeftPl2);
            panelPl2.Controls.Add(trackPl2);
            panelPl2.Dock = DockStyle.Top;
            panelPl2.Location = new Point(0, 556);
            panelPl2.Margin = new Padding(4);
            panelPl2.MaximumSize = new Size(0, 124);
            panelPl2.Name = "panelPl2";
            panelPl2.Size = new Size(520, 124);
            panelPl2.TabIndex = 7;
            // 
            // labelPl2
            // 
            labelPl2.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            labelPl2.Location = new Point(396, 8);
            labelPl2.Margin = new Padding(4, 0, 4, 0);
            labelPl2.Name = "labelPl2";
            labelPl2.Size = new Size(114, 32);
            labelPl2.TabIndex = 13;
            labelPl2.Text = "FPPT";
            labelPl2.TextAlign = ContentAlignment.TopRight;
            // 
            // labelLeftPl2
            // 
            labelLeftPl2.AutoSize = true;
            labelLeftPl2.Location = new Point(10, 8);
            labelLeftPl2.Margin = new Padding(4, 0, 4, 0);
            labelLeftPl2.Name = "labelLeftPl2";
            labelLeftPl2.Size = new Size(65, 32);
            labelLeftPl2.TabIndex = 12;
            labelLeftPl2.Text = "FPPT";
            // 
            // trackPl2
            // 
            trackPl2.Location = new Point(6, 48);
            trackPl2.Margin = new Padding(4, 2, 4, 2);
            trackPl2.Maximum = 85;
            trackPl2.Minimum = 5;
            trackPl2.Name = "trackPl2";
            trackPl2.Size = new Size(508, 90);
            trackPl2.TabIndex = 11;
            trackPl2.TickFrequency = 5;
            trackPl2.TickStyle = TickStyle.TopLeft;
            trackPl2.Value = 80;
            // 
            // panelPl1
            // 
            panelPl1.AutoSize = true;
            panelPl1.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panelPl1.Controls.Add(labelPl1);
            panelPl1.Controls.Add(labelLeftPl1);
            panelPl1.Controls.Add(trackPl1);
            panelPl1.Dock = DockStyle.Top;
            panelPl1.Location = new Point(0, 432);
            panelPl1.Margin = new Padding(4);
            panelPl1.MaximumSize = new Size(0, 124);
            panelPl1.Name = "panelPl1";
            panelPl1.Size = new Size(520, 124);
            panelPl1.TabIndex = 6;
            // 
            // labelPl1
            // 
            labelPl1.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            labelPl1.Location = new Point(396, 10);
            labelPl1.Margin = new Padding(4, 0, 4, 0);
            labelPl1.Name = "labelPl1";
            labelPl1.Size = new Size(116, 32);
            labelPl1.TabIndex = 12;
            labelPl1.Text = "SPPT";
            labelPl1.TextAlign = ContentAlignment.TopRight;
            // 
            // labelLeftPl1
            // 
            labelLeftPl1.AutoSize = true;
            labelLeftPl1.Location = new Point(10, 10);
            labelLeftPl1.Margin = new Padding(4, 0, 4, 0);
            labelLeftPl1.Name = "labelLeftPl1";
            labelLeftPl1.Size = new Size(66, 32);
            labelLeftPl1.TabIndex = 11;
            labelLeftPl1.Text = "SPPT";
            // 
            // trackPl1
            // 
            trackPl1.Location = new Point(6, 48);
            trackPl1.Margin = new Padding(4, 2, 4, 2);
            trackPl1.Maximum = 180;
            trackPl1.Minimum = 10;
            trackPl1.Name = "trackPl1";
            trackPl1.Size = new Size(508, 90);
            trackPl1.TabIndex = 10;
            trackPl1.TickFrequency = 5;
            trackPl1.TickStyle = TickStyle.TopLeft;
            trackPl1.Value = 125;
            // 
            // 
            // 
            // 
            // 
            // 
            // 
            // 
            // 
            // panelCpuLimitsTitle
            // 
            panelCpuLimitsTitle.AutoSize = true;
            panelCpuLimitsTitle.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panelCpuLimitsTitle.Controls.Add(pictureBoxCPU);
            panelCpuLimitsTitle.Controls.Add(labelCpuLimitsTitle);
            panelCpuLimitsTitle.Dock = DockStyle.Top;
            panelCpuLimitsTitle.Location = new Point(0, 248);
            panelCpuLimitsTitle.Margin = new Padding(4);
            panelCpuLimitsTitle.Name = "panelCpuLimitsTitle";
            panelCpuLimitsTitle.Size = new Size(520, 60);
            panelCpuLimitsTitle.TabIndex = 4;
            // 
            // pictureBoxCPU
            // 
            pictureBoxCPU.BackgroundImage = Properties.Resources.icons8_processor_32;
            pictureBoxCPU.BackgroundImageLayout = ImageLayout.Zoom;
            pictureBoxCPU.InitialImage = null;
            pictureBoxCPU.Location = new Point(10, 18);
            pictureBoxCPU.Margin = new Padding(4, 2, 4, 10);
            pictureBoxCPU.Name = "pictureBoxCPU";
            pictureBoxCPU.Size = new Size(32, 32);
            pictureBoxCPU.TabIndex = 40;
            pictureBoxCPU.TabStop = false;
            // 
            // labelCpuLimitsTitle
            // 
            labelCpuLimitsTitle.AutoSize = true;
            labelCpuLimitsTitle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            labelCpuLimitsTitle.Location = new Point(46, 16);
            labelCpuLimitsTitle.Margin = new Padding(4, 0, 4, 0);
            labelCpuLimitsTitle.Name = "labelCpuLimitsTitle";
            labelCpuLimitsTitle.Size = new Size(160, 32);
            labelCpuLimitsTitle.TabIndex = 39;
            labelCpuLimitsTitle.Text = "Power Limits";
            // 
            // panelCpuLimitsSectionMode
            // 
            panelCpuLimitsSectionMode.Controls.Add(comboWindowsPowerMode);
            panelCpuLimitsSectionMode.Dock = DockStyle.Top;
            panelCpuLimitsSectionMode.Location = new Point(0, 60);
            panelCpuLimitsSectionMode.Margin = new Padding(4);
            panelCpuLimitsSectionMode.Name = "panelCpuLimitsSectionMode";
            panelCpuLimitsSectionMode.Size = new Size(520, 64);
            panelCpuLimitsSectionMode.TabIndex = 1;
            // 
            // comboWindowsPowerMode
            // 
            comboWindowsPowerMode.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            comboWindowsPowerMode.BorderColor = Color.White;
            comboWindowsPowerMode.ButtonColor = Color.FromArgb(255, 255, 255);
            comboWindowsPowerMode.DropDownStyle = ComboBoxStyle.DropDownList;
            comboWindowsPowerMode.FormattingEnabled = true;
            comboWindowsPowerMode.Items.AddRange(new object[] { "Disabled", "Enabled", "Aggressive", "Efficient Enabled", "Efficient Aggressive", "Aggressive at Guaranteed", "Efficient at Guaranteed" });
            comboWindowsPowerMode.Location = new Point(13, 12);
            comboWindowsPowerMode.Margin = new Padding(4);
            comboWindowsPowerMode.Name = "comboWindowsPowerMode";
            comboWindowsPowerMode.Size = new Size(329, 40);
            comboWindowsPowerMode.TabIndex = 42;
            // 
            // panelCpuLimitsSectionModeTitle
            // 
            panelCpuLimitsSectionModeTitle.AutoSize = true;
            panelCpuLimitsSectionModeTitle.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panelCpuLimitsSectionModeTitle.Controls.Add(picturePowerMode);
            panelCpuLimitsSectionModeTitle.Controls.Add(labelPowerModeTitle);
            panelCpuLimitsSectionModeTitle.Dock = DockStyle.Top;
            panelCpuLimitsSectionModeTitle.Margin = new Padding(4);
            panelCpuLimitsSectionModeTitle.Name = "panelCpuLimitsSectionModeTitle";
            panelCpuLimitsSectionModeTitle.Size = new Size(520, 60);
            panelCpuLimitsSectionModeTitle.TabIndex = 0;
            // 
            // picturePowerMode
            // 
            picturePowerMode.BackgroundImage = Properties.Resources.icons8_gauge_32;
            picturePowerMode.BackgroundImageLayout = ImageLayout.Zoom;
            picturePowerMode.InitialImage = null;
            picturePowerMode.Location = new Point(10, 18);
            picturePowerMode.Margin = new Padding(4, 2, 4, 10);
            picturePowerMode.Name = "picturePowerMode";
            picturePowerMode.Size = new Size(32, 32);
            picturePowerMode.TabIndex = 40;
            picturePowerMode.TabStop = false;
            // 
            // labelPowerModeTitle
            // 
            labelPowerModeTitle.AutoSize = true;
            labelPowerModeTitle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            labelPowerModeTitle.Location = new Point(46, 18);
            labelPowerModeTitle.Margin = new Padding(4, 0, 4, 0);
            labelPowerModeTitle.Name = "labelPowerModeTitle";
            labelPowerModeTitle.Size = new Size(271, 32);
            labelPowerModeTitle.TabIndex = 39;
            labelPowerModeTitle.Text = "Windows Power Mode";
            // 
            // panelGpuOffsetsSection
            // 
            panelGpuOffsetsSection.AutoSize = true;
            panelGpuOffsetsSection.Controls.Add(panelGpuOffsetsSectionMemory);
            panelGpuOffsetsSection.Controls.Add(panelGpuOffsetsSectionCore);
            panelGpuOffsetsSection.Controls.Add(panelGpuOffsetsTitle);
            panelGpuOffsetsSection.Dock = DockStyle.Top;
            panelGpuOffsetsSection.Location = new Point(10, 66);
            panelGpuOffsetsSection.Margin = new Padding(4);
            panelGpuOffsetsSection.Name = "panelGpuOffsetsSection";
            panelGpuOffsetsSection.Padding = new Padding(0, 0, 0, 18);
            panelGpuOffsetsSection.Size = new Size(520, 822);
            panelGpuOffsetsSection.TabIndex = 1;
            panelGpuOffsetsSection.Visible = false;
            // 
            // panelGpuOffsetsTitle
            // 
            panelGpuOffsetsTitle.AutoSize = true;
            panelGpuOffsetsTitle.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panelGpuOffsetsTitle.Controls.Add(pictureGPU);
            panelGpuOffsetsTitle.Controls.Add(labelGpuOffsets);
            panelGpuOffsetsTitle.Dock = DockStyle.Top;
            panelGpuOffsetsTitle.Location = new Point(0, 0);
            panelGpuOffsetsTitle.Margin = new Padding(4);
            panelGpuOffsetsTitle.Name = "panelGpuOffsetsTitle";
            panelGpuOffsetsTitle.Size = new Size(520, 60);
            panelGpuOffsetsTitle.TabIndex = 0;
            // 
            // pictureGPU
            // 
            pictureGPU.BackgroundImage = Properties.Resources.icons8_video_card_32;
            pictureGPU.BackgroundImageLayout = ImageLayout.Zoom;
            pictureGPU.ErrorImage = null;
            pictureGPU.InitialImage = null;
            pictureGPU.Location = new Point(10, 18);
            pictureGPU.Margin = new Padding(4, 2, 4, 10);
            pictureGPU.Name = "pictureGPU";
            pictureGPU.Size = new Size(32, 32);
            pictureGPU.TabIndex = 41;
            pictureGPU.TabStop = false;
            // 
            // labelGpuOffsets
            // 
            labelGpuOffsets.AutoSize = true;
            labelGpuOffsets.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            labelGpuOffsets.Location = new Point(45, 17);
            labelGpuOffsets.Margin = new Padding(4, 0, 4, 0);
            labelGpuOffsets.Name = "labelGpuOffsets";
            labelGpuOffsets.Size = new Size(162, 32);
            labelGpuOffsets.TabIndex = 40;
            labelGpuOffsets.Text = "GPU Settings";
            // 
            // panelNav
            // 
            panelNav.AutoSize = true;
            panelNav.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panelNav.Controls.Add(tableNav);
            panelNav.Dock = DockStyle.Top;
            panelNav.Location = new Point(10, 0);
            panelNav.Margin = new Padding(4);
            panelNav.Name = "panelNav";
            panelNav.Size = new Size(520, 66);
            panelNav.TabIndex = 0;
            // 
            // tableNav
            // 
            tableNav.ColumnCount = 3;
            tableNav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3333321F));
            tableNav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3333321F));
            tableNav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3333321F));
            tableNav.Controls.Add(buttonGPU, 1, 0);
            tableNav.Controls.Add(buttonCPU, 0, 0);
            tableNav.Dock = DockStyle.Top;
            tableNav.Location = new Point(0, 0);
            tableNav.MinimumSize = new Size(0, 62);
            tableNav.Name = "tableNav";
            tableNav.Padding = new Padding(0, 3, 0, 1);
            tableNav.RowCount = 1;
            tableNav.RowStyles.Add(new RowStyle());
            tableNav.Size = new Size(520, 66);
            tableNav.TabIndex = 42;
            // 
            // 
            // 
            // buttonGPU
            // 
            buttonGPU.Activated = false;
            buttonGPU.BackColor = SystemColors.ControlLight;
            buttonGPU.BorderColor = Color.Transparent;
            buttonGPU.BorderRadius = 2;
            buttonGPU.Dock = DockStyle.Fill;
            buttonGPU.FlatStyle = FlatStyle.Flat;
            buttonGPU.Location = new Point(177, 5);
            buttonGPU.Margin = new Padding(4, 2, 4, 2);
            buttonGPU.Name = "buttonGPU";
            buttonGPU.Secondary = true;
            buttonGPU.Size = new Size(165, 58);
            buttonGPU.TabIndex = 1;
            buttonGPU.Text = "GPU";
            buttonGPU.TextImageRelation = TextImageRelation.ImageBeforeText;
            buttonGPU.UseVisualStyleBackColor = false;
            // 
            // buttonCPU
            // 
            buttonCPU.Activated = false;
            buttonCPU.BackColor = SystemColors.ControlLight;
            buttonCPU.BorderColor = Color.Transparent;
            buttonCPU.BorderRadius = 2;
            buttonCPU.Dock = DockStyle.Fill;
            buttonCPU.FlatStyle = FlatStyle.Flat;
            buttonCPU.Location = new Point(4, 5);
            buttonCPU.Margin = new Padding(4, 2, 4, 2);
            buttonCPU.Name = "buttonCPU";
            buttonCPU.Secondary = true;
            buttonCPU.Size = new Size(165, 58);
            buttonCPU.TabIndex = 0;
            buttonCPU.Text = "CPU";
            buttonCPU.TextImageRelation = TextImageRelation.ImageBeforeText;
            buttonCPU.UseVisualStyleBackColor = false;
            // 
            // Fans
            // 
            AutoScaleDimensions = new SizeF(192F, 192F);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            ClientSize = new Size(1350, 1100);
            Controls.Add(panelFans);
            Controls.Add(panelMainControls);
            Margin = new Padding(4, 2, 4, 2);
            MinimizeBox = false;
            MinimumSize = new Size(26, 1100);
            Name = "Fans";
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = "Fans and Power";
            panelFans.ResumeLayout(false);
            panelFans.PerformLayout();
            tableFanCharts.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)chartGPU).EndInit();
            ((System.ComponentModel.ISupportInitialize)chartCPU).EndInit();
            ((System.ComponentModel.ISupportInitialize)chartXGM).EndInit();
            ((System.ComponentModel.ISupportInitialize)chartMid).EndInit();
            panelTitleFans.ResumeLayout(false);
            panelTitleFans.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)picturePerf).EndInit();
            panelApplyFans.ResumeLayout(false);
            panelApplyFans.PerformLayout();
            panelMainControls.ResumeLayout(false);
            panelMainControls.PerformLayout();
            panelCpuLimitsSection.ResumeLayout(false);
            panelCpuLimitsSection.PerformLayout();
            panelApplyCpuLimits.ResumeLayout(false);
            panelCpuLimitsGraph.ResumeLayout(false);
            panelCpuLimitsGraph.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)trackCPU).EndInit();
            panelPl2.ResumeLayout(false);
            panelPl2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)trackPl2).EndInit();
            panelPl1.ResumeLayout(false);
            panelPl1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)trackPl1).EndInit();
            panelCpuLimitsTitle.ResumeLayout(false);
            panelCpuLimitsTitle.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)pictureBoxCPU).EndInit();
            panelCpuLimitsSectionMode.ResumeLayout(false);
            panelCpuLimitsSectionModeTitle.ResumeLayout(false);
            panelCpuLimitsSectionModeTitle.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)picturePowerMode).EndInit();
            panelGpuOffsetsSection.ResumeLayout(false);
            panelGpuOffsetsSection.PerformLayout();
            panelGpuOffsetsSectionMemory.ResumeLayout(false);
            panelGpuOffsetsSectionMemory.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)trackGpuMemoryOffset).EndInit();
            panelGpuOffsetsSectionCore.ResumeLayout(false);
            panelGpuOffsetsSectionCore.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)trackGpuCoreOffset).EndInit();
            panelGpuOffsetsTitle.ResumeLayout(false);
            panelGpuOffsetsTitle.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)pictureGPU).EndInit();
            panelNav.ResumeLayout(false);
            tableNav.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        private Panel panelFans;
        private Panel panelMainControls;
        private TableLayoutPanel tableFanCharts;
        private RChart chartGPU;
        private RChart chartCPU;
        private RChart chartMid;
        private RChart chartXGM;
        private Panel panelCpuLimitsSection;
        private Panel panelCpuLimitsGraph;
        private Label labelCPU;
        private Label labelLeftCPU;
        private RTrackBar trackCPU;
        private Panel panelCpuLimitsTitle;
        private PictureBox pictureBoxCPU;
        private Label labelCpuLimitsTitle;
        private Panel panelGpuOffsetsSection;
        private Panel panelGpuOffsetsSectionMemory;
        private Label labelGpuMemoryValue;
        private Label labelGpuMemoryTitle;
        private RTrackBar trackGpuMemoryOffset;
        private Panel panelGpuOffsetsSectionCore;
        private Label labelGpuCoreValue;
        private RTrackBar trackGpuCoreOffset;
        private Label labelGpuCoreTitle;
        private Panel panelGpuOffsetsTitle;
        private PictureBox pictureGPU;
        private Label labelGpuOffsets;
        private RCheckBox checkApplyCpuLimits;
        private Panel panelTitleFans;
        private Panel panelApplyFans;
        private Label labelFansResult;
        private RCheckBox checkApplyFanCurves;
        private PictureBox picturePerf;
        private Label labelFans;
        private Panel panelPl2;
        private Label labelPl2;
        private Label labelLeftPl2;
        private RTrackBar trackPl2;
        private Panel panelApplyCpuLimits;
        private Panel panelNav;
        private TableLayoutPanel tableNav;
        private RButton buttonCPU;
        private RButton buttonGPU;
        private Panel panelCpuLimitsSectionMode;
        private RComboBox comboWindowsPowerMode;
        private Panel panelCpuLimitsSectionModeTitle;
        private PictureBox picturePowerMode;
        private Label labelPowerModeTitle;
        private Panel panelPl1;
        private Label labelPl1;
        private Label labelLeftPl1;
        private RTrackBar trackPl1;
    }
}
