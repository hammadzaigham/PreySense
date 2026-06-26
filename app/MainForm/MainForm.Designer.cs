using PreySense.UI;

namespace PreySense
{
    partial class MainForm
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
            panelMatrix = new Panel();
            panelMatrixAuto = new Panel();
            checkMiniLedOnLidClose = new CheckBox();
            checkMiniLedEnabled = new CheckBox();
            tableLayoutMatrix = new TableLayoutPanel();
            comboMiniLedMode = new RComboBox();
            comboMiniLedRunningMode = new RComboBox();
            buttonMiniLed = new RButton();
            panelMatrixTitle = new Panel();
            pictureMiniLedIcon = new PictureBox();
            labelMatrix = new Label();
            panelBattery = new Panel();
            buttonBatteryFull = new RButton();
            sliderBatteryChargeLimit = new LabeledSliderControl();
            panelBatteryTitle = new Panel();
            labelBatteryStatus = new Label();
            pictureBatteryIcon = new PictureBox();
            labelBatteryStatusLimitTitle = new Label();
            panelFooter = new Panel();
            tableButtons = new TableLayoutPanel();
            buttonTurboFanModePower = new RButton();
            buttonQuit = new RButton();
            buttonMetrics = new RButton();
            checkRunOnStartup = new RCheckBox();
            checkAutoGpuBattery = new RCheckBox();
            panelPerformance = new Panel();
            tablePerf = new TableLayoutPanel();
            buttonEcoMode = new RButton();
            buttonBalancedMode = new RButton();
            buttonPerformanceMode = new RButton();
            buttonTurboFanMode = new RButton();
            panelCPUTitle = new Panel();
            picturePerformanceIcon = new PictureBox();
            labelPerformanceMode = new Label();
            labelCpuFanStatus = new Label();
            panelGPU = new Panel();
            labelGpuHint = new Label();
            tableGPU = new TableLayoutPanel();
            buttonEnduranceMode = new RButton();
            buttonGpuStandardMode = new RButton();
            buttonGpuUltimateMode = new RButton();
            panelGPUTitle = new Panel();
            pictureGpuIcon = new PictureBox();
            labelGpuMode = new Label();
            labelGpuModeFan = new Label();
            panelScreen = new Panel();
            tableScreen = new TableLayoutPanel();
            buttonAutoRefreshRate = new RButton();
            button60Hz = new RButton();
            button120Hz = new RButton();
            buttonMaxRefreshRate = new RButton();
            panelScreenTitle = new Panel();
            labelMiddleFanStatus = new Label();
            pictureScreenIcon = new PictureBox();
            labelScreen = new Label();
            buttonColorProfiles = new RButton();
            panelRgb = new Panel();
            labelBacklight = new Label();
            tableLayoutRgb = new TableLayoutPanel();
            buttonRgbLighting = new RButton();
            panelColor = new Panel();
            pictureBacklightSwatch = new PictureBox();
            buttonRgbProfiles = new RButton();
            comboRgbLightingMode = new RComboBox();
            panelRgbTitle = new Panel();
            pictureRgbIcon = new PictureBox();
            labelRgb = new Label();
            panelStartup = new Panel();
            pictureHandheldIcon = new PictureBox();
            labelHandheldController = new Label();
            panelVersion = new Panel();
            panelMatrix.SuspendLayout();
            panelMatrixAuto.SuspendLayout();
            tableLayoutMatrix.SuspendLayout();
            panelMatrixTitle.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)pictureMiniLedIcon).BeginInit();
            panelBattery.SuspendLayout();
            panelBatteryTitle.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)pictureBatteryIcon).BeginInit();
            panelFooter.SuspendLayout();
            tableButtons.SuspendLayout();
            panelPerformance.SuspendLayout();
            tablePerf.SuspendLayout();
            panelCPUTitle.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)picturePerformanceIcon).BeginInit();
            panelGPU.SuspendLayout();
            tableGPU.SuspendLayout();
            panelGPUTitle.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)pictureGpuIcon).BeginInit();
            panelScreen.SuspendLayout();
            tableScreen.SuspendLayout();
            panelScreenTitle.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)pictureScreenIcon).BeginInit();
            panelRgb.SuspendLayout();
            tableLayoutRgb.SuspendLayout();
            panelColor.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)pictureBacklightSwatch).BeginInit();
            panelRgbTitle.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)pictureRgbIcon).BeginInit();
            panelStartup.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)pictureHandheldIcon).BeginInit();
            panelVersion.SuspendLayout();
            SuspendLayout();
            // 
            // panelMatrix
            // 
            panelMatrix.AccessibleRole = AccessibleRole.Grouping;
            panelMatrix.AutoSize = true;
            panelMatrix.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panelMatrix.Controls.Add(panelMatrixAuto);
            panelMatrix.Controls.Add(tableLayoutMatrix);
            panelMatrix.Controls.Add(panelMatrixTitle);
            panelMatrix.Dock = DockStyle.Top;
            panelMatrix.Location = new Point(11, 1071);
            panelMatrix.Margin = new Padding(0);
            panelMatrix.Name = "panelMatrix";
            panelMatrix.Padding = new Padding(20, 20, 20, 11);
            panelMatrix.Size = new Size(827, 183);
            panelMatrix.TabIndex = 4;
            panelMatrix.TabStop = true;
            // 
            // panelMatrixAuto
            // 
            panelMatrixAuto.Controls.Add(checkMiniLedOnLidClose);
            panelMatrixAuto.Controls.Add(checkMiniLedEnabled);
            panelMatrixAuto.Dock = DockStyle.Top;
            panelMatrixAuto.Location = new Point(20, 132);
            panelMatrixAuto.Margin = new Padding(4);
            panelMatrixAuto.Name = "panelMatrixAuto";
            panelMatrixAuto.Padding = new Padding(10, 10, 0, 0);
            panelMatrixAuto.Size = new Size(787, 42);
            panelMatrixAuto.TabIndex = 47;
            // 
            // checkMiniLedOnLidClose
            // 
            checkMiniLedOnLidClose.AutoSize = true;
            checkMiniLedOnLidClose.Dock = DockStyle.Left;
            checkMiniLedOnLidClose.ForeColor = SystemColors.GrayText;
            checkMiniLedOnLidClose.Location = new Point(260, 0);
            checkMiniLedOnLidClose.Margin = new Padding(8, 4, 8, 4);
            checkMiniLedOnLidClose.Name = "checkMiniLedOnLidClose";
            checkMiniLedOnLidClose.Size = new Size(253, 40);
            checkMiniLedOnLidClose.TabIndex = 46;
            checkMiniLedOnLidClose.Text = "Disable on lid close";
            checkMiniLedOnLidClose.UseVisualStyleBackColor = true;
            checkMiniLedOnLidClose.Visible = true;
            // 
            // checkMiniLedEnabled
            // 
            checkMiniLedEnabled.AutoSize = true;
            checkMiniLedEnabled.Dock = DockStyle.Left;
            checkMiniLedEnabled.ForeColor = SystemColors.GrayText;
            checkMiniLedEnabled.Location = new Point(8, 0);
            checkMiniLedEnabled.Margin = new Padding(8, 4, 8, 4);
            checkMiniLedEnabled.Name = "checkMiniLedEnabled";
            checkMiniLedEnabled.Padding = new Padding(0, 0, 4, 0);
            checkMiniLedEnabled.Size = new Size(252, 40);
            checkMiniLedEnabled.TabIndex = 19;
            checkMiniLedEnabled.Text = "Turn off on battery";
            checkMiniLedEnabled.UseVisualStyleBackColor = true;
            // 
            // tableLayoutMatrix
            // 
            tableLayoutMatrix.AutoSize = true;
            tableLayoutMatrix.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            tableLayoutMatrix.ColumnCount = 3;
            tableLayoutMatrix.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            tableLayoutMatrix.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            tableLayoutMatrix.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            tableLayoutMatrix.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            tableLayoutMatrix.Controls.Add(comboMiniLedMode, 0, 0);
            tableLayoutMatrix.Controls.Add(comboMiniLedRunningMode, 1, 0);
            tableLayoutMatrix.Controls.Add(buttonMiniLed, 2, 0);
            tableLayoutMatrix.Dock = DockStyle.Top;
            tableLayoutMatrix.Location = new Point(20, 60);
            tableLayoutMatrix.Margin = new Padding(8, 4, 8, 4);
            tableLayoutMatrix.Name = "tableLayoutMatrix";
            tableLayoutMatrix.Padding = new Padding(3, 0, 3, 0);
            tableLayoutMatrix.RowCount = 1;
            tableLayoutMatrix.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            tableLayoutMatrix.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            tableLayoutMatrix.Size = new Size(787, 72);
            tableLayoutMatrix.TabIndex = 43;
            // 
            // comboMiniLedMode
            // 
            comboMiniLedMode.BorderColor = Color.White;
            comboMiniLedMode.ButtonColor = Color.FromArgb(255, 255, 255);
            comboMiniLedMode.Dock = DockStyle.Top;
            comboMiniLedMode.Font = new Font("Segoe UI", 9F);
            comboMiniLedMode.FormattingEnabled = true;
            comboMiniLedMode.Items.AddRange(new object[] { "Off", "Dim", "Medium", "Bright" });
            comboMiniLedMode.Location = new Point(10, 8);
            comboMiniLedMode.Margin = new Padding(7, 8, 7, 4);
            comboMiniLedMode.Name = "comboMiniLedMode";
            comboMiniLedMode.Size = new Size(246, 40);
            comboMiniLedMode.TabIndex = 16;
            // 
            // comboMiniLedRunningMode
            // 
            comboMiniLedRunningMode.BorderColor = Color.White;
            comboMiniLedRunningMode.ButtonColor = Color.FromArgb(255, 255, 255);
            comboMiniLedRunningMode.Dock = DockStyle.Top;
            comboMiniLedRunningMode.Font = new Font("Segoe UI", 9F);
            comboMiniLedRunningMode.FormattingEnabled = true;
            comboMiniLedRunningMode.Items.AddRange(new object[] { "Banner", "Logo", "Picture", "Clock", "Audio" });
            comboMiniLedRunningMode.Location = new Point(270, 8);
            comboMiniLedRunningMode.Margin = new Padding(7, 8, 7, 4);
            comboMiniLedRunningMode.Name = "comboMiniLedRunningMode";
            comboMiniLedRunningMode.Size = new Size(246, 40);
            comboMiniLedRunningMode.TabIndex = 17;
            // 
            // 
            // 
            // buttonMiniLed
            // 
            buttonMiniLed.Activated = false;
            buttonMiniLed.AutoSize = true;
            buttonMiniLed.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttonMiniLed.BackColor = SystemColors.ControlLight;
            buttonMiniLed.BorderColor = Color.Transparent;
            buttonMiniLed.BorderRadius = 2;
            buttonMiniLed.Dock = DockStyle.Top;
            buttonMiniLed.FlatAppearance.BorderSize = 0;
            buttonMiniLed.FlatStyle = FlatStyle.Flat;
            buttonMiniLed.Location = new Point(527, 4);
            buttonMiniLed.Margin = new Padding(4);
            buttonMiniLed.MaximumSize = new Size(0, 48);
            buttonMiniLed.MinimumSize = new Size(0, 44);
            buttonMiniLed.Name = "buttonMiniLed";
            buttonMiniLed.Secondary = true;
            buttonMiniLed.Size = new Size(253, 44);
            buttonMiniLed.TabIndex = 18;
            buttonMiniLed.Text = "Picture/GIF";
            buttonMiniLed.UseVisualStyleBackColor = false;
            // 
            // panelMatrixTitle
            // 
            panelMatrixTitle.Controls.Add(pictureMiniLedIcon);
            panelMatrixTitle.Controls.Add(labelMatrix);
            panelMatrixTitle.Dock = DockStyle.Top;
            panelMatrixTitle.Location = new Point(20, 20);
            panelMatrixTitle.Margin = new Padding(4);
            panelMatrixTitle.Name = "panelMatrixTitle";
            panelMatrixTitle.Size = new Size(787, 40);
            panelMatrixTitle.TabIndex = 45;
            // 
            // pictureMiniLedIcon
            // 
            pictureMiniLedIcon.BackgroundImage = Properties.Resources.icons8_matrix_32;
            pictureMiniLedIcon.BackgroundImageLayout = ImageLayout.Zoom;
            pictureMiniLedIcon.Location = new Point(8, 3);
            pictureMiniLedIcon.Margin = new Padding(4);
            pictureMiniLedIcon.Name = "pictureMiniLedIcon";
            pictureMiniLedIcon.Size = new Size(32, 32);
            pictureMiniLedIcon.TabIndex = 41;
            pictureMiniLedIcon.TabStop = false;
            // 
            // labelMatrix
            // 
            labelMatrix.AutoSize = true;
            labelMatrix.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            labelMatrix.Location = new Point(44, 0);
            labelMatrix.Margin = new Padding(4, 0, 4, 0);
            labelMatrix.Name = "labelMatrix";
            labelMatrix.Size = new Size(170, 32);
            labelMatrix.TabIndex = 40;
            labelMatrix.Text = "Anime Matrix";
            // 
            // panelBattery
            // 
            panelBattery.AutoSize = true;
            panelBattery.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panelBattery.Controls.Add(buttonBatteryFull);
            panelBattery.Controls.Add(sliderBatteryChargeLimit);
            panelBattery.Controls.Add(panelBatteryTitle);
            panelBattery.Dock = DockStyle.Top;
            panelBattery.Location = new Point(11, 1683);
            panelBattery.Margin = new Padding(0);
            panelBattery.Name = "panelBattery";
            panelBattery.Padding = new Padding(20, 15, 20, 0);
            panelBattery.Size = new Size(827, 104);
            panelBattery.TabIndex = 8;
            // 
            // buttonBatteryFull
            // 
            buttonBatteryFull.Activated = false;
            buttonBatteryFull.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            buttonBatteryFull.BackColor = SystemColors.ControlLight;
            buttonBatteryFull.BorderColor = Color.Transparent;
            buttonBatteryFull.BorderRadius = 2;
            buttonBatteryFull.FlatAppearance.BorderSize = 0;
            buttonBatteryFull.FlatStyle = FlatStyle.Flat;
            buttonBatteryFull.Font = new Font("Segoe UI", 7.125F, FontStyle.Bold);
            buttonBatteryFull.ForeColor = SystemColors.ControlDark;
            buttonBatteryFull.Location = new Point(718, 81);
            buttonBatteryFull.Borderless = true;
            buttonBatteryFull.Margin = new Padding(0);
            buttonBatteryFull.Name = "buttonBatteryFull";
            buttonBatteryFull.Secondary = true;
            buttonBatteryFull.Size = new Size(84, 36);
            buttonBatteryFull.TabIndex = 41;
            buttonBatteryFull.Text = "100%";
            buttonBatteryFull.UseVisualStyleBackColor = false;
            // 
            // sliderBatteryChargeLimit
            // 
            sliderBatteryChargeLimit.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            sliderBatteryChargeLimit.Location = new Point(20, 60);
            sliderBatteryChargeLimit.Margin = new Padding(4);
            sliderBatteryChargeLimit.Max = 100;
            sliderBatteryChargeLimit.Min = 40;
            sliderBatteryChargeLimit.Name = "sliderBatteryChargeLimit";
            sliderBatteryChargeLimit.Size = new Size(694, 40);
            sliderBatteryChargeLimit.Step = 5;
            sliderBatteryChargeLimit.TabIndex = 20;
            sliderBatteryChargeLimit.Text = "sliderBatteryChargeLimit";
            sliderBatteryChargeLimit.Value = 100;
            // 
            // panelBatteryTitle
            // 
            panelBatteryTitle.Controls.Add(labelBatteryStatus);
            panelBatteryTitle.Controls.Add(pictureBatteryIcon);
            panelBatteryTitle.Controls.Add(labelBatteryStatusLimitTitle);
            panelBatteryTitle.Dock = DockStyle.Top;
            panelBatteryTitle.Location = new Point(20, 15);
            panelBatteryTitle.Margin = new Padding(4);
            panelBatteryTitle.Name = "panelBatteryTitle";
            panelBatteryTitle.Padding = new Padding(0, 0, 0, 4);
            panelBatteryTitle.Size = new Size(787, 44);
            panelBatteryTitle.TabIndex = 40;
            // 
            // labelBatteryStatus
            // 
            labelBatteryStatus.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            labelBatteryStatus.Location = new Point(455, 5);
            labelBatteryStatus.Margin = new Padding(8, 0, 8, 0);
            labelBatteryStatus.Name = "labelBatteryStatus";
            labelBatteryStatus.Size = new Size(324, 36);
            labelBatteryStatus.TabIndex = 39;
            labelBatteryStatus.Text = "                ";
            labelBatteryStatus.TextAlign = ContentAlignment.TopRight;
            // 
            // pictureBatteryIcon
            // 
            pictureBatteryIcon.BackgroundImage = Properties.Resources.icons8_charging_battery_32;
            pictureBatteryIcon.BackgroundImageLayout = ImageLayout.Zoom;
            pictureBatteryIcon.Location = new Point(8, 8);
            pictureBatteryIcon.Margin = new Padding(4);
            pictureBatteryIcon.Name = "pictureBatteryIcon";
            pictureBatteryIcon.Size = new Size(32, 32);
            pictureBatteryIcon.TabIndex = 38;
            pictureBatteryIcon.TabStop = false;
            // 
            // labelBatteryStatusLimitTitle
            // 
            labelBatteryStatusLimitTitle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            labelBatteryStatusLimitTitle.Location = new Point(43, 9);
            labelBatteryStatusLimitTitle.Margin = new Padding(8, 0, 8, 0);
            labelBatteryStatusLimitTitle.Name = "labelBatteryStatusLimitTitle";
            labelBatteryStatusLimitTitle.Size = new Size(467, 32);
            labelBatteryStatusLimitTitle.TabIndex = 37;
            labelBatteryStatusLimitTitle.Text = "Battery Charge Limit";
            // 
            // panelFooter
            // 
            panelFooter.AutoSize = true;
            panelFooter.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panelFooter.Controls.Add(tableButtons);
            panelFooter.Dock = DockStyle.Top;
            panelFooter.Location = new Point(11, 1887);
            panelFooter.Margin = new Padding(0);
            panelFooter.Name = "panelFooter";
            panelFooter.Padding = new Padding(20, 0, 20, 10);
            panelFooter.Size = new Size(827, 60);
            panelFooter.TabIndex = 11;
            // 
            // tableButtons
            // 
            tableButtons.AutoSize = true;
            tableButtons.ColumnCount = 3;
            tableButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33333F));
            tableButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33333F));
            tableButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33333F));
            tableButtons.Controls.Add(buttonMetrics, 0, 0);
            tableButtons.Controls.Add(buttonQuit, 2, 0);
            tableButtons.Dock = DockStyle.Top;
            tableButtons.Location = new Point(20, 0);
            tableButtons.Margin = new Padding(8, 4, 8, 4);
            tableButtons.Name = "tableButtons";
            tableButtons.RowCount = 1;
            tableButtons.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            tableButtons.Size = new Size(787, 58);
            tableButtons.TabIndex = 25;
            // 
            // buttonTurboFanModePower
            // 
            buttonTurboFanModePower.Activated = false;
            buttonTurboFanModePower.BackColor = buttonSecond;
            buttonTurboFanModePower.BorderColor = borderSecond;
            buttonTurboFanModePower.BorderRadius = 5;
            buttonTurboFanModePower.Dock = DockStyle.Top;
            buttonTurboFanModePower.FlatAppearance.BorderSize = 0;
            buttonTurboFanModePower.FlatStyle = FlatStyle.Flat;
            buttonTurboFanModePower.ForeColor = foreMain;
            buttonTurboFanModePower.Image = Properties.Resources.icons8_fan_48;
            buttonTurboFanModePower.ImageAlign = ContentAlignment.BottomCenter;
            buttonTurboFanModePower.Location = new Point(4, 132);
            buttonTurboFanModePower.Margin = new Padding(4);
            buttonTurboFanModePower.Name = "buttonTurboFanModePower";
            buttonTurboFanModePower.Secondary = true;
            buttonTurboFanModePower.Size = new Size(188, 120);
            buttonTurboFanModePower.TabIndex = 7;
            buttonTurboFanModePower.Text = "Fans + Power";
            buttonTurboFanModePower.TextImageRelation = TextImageRelation.ImageAboveText;
            buttonTurboFanModePower.UseVisualStyleBackColor = false;
            // 
            // buttonQuit
            // 
            buttonQuit.Activated = false;
            buttonQuit.BackColor = SystemColors.ControlLight;
            buttonQuit.BorderColor = Color.Transparent;
            buttonQuit.BorderRadius = 2;
            buttonQuit.Dock = DockStyle.Fill;
            buttonQuit.FlatStyle = FlatStyle.Flat;
            buttonQuit.Image = Properties.Resources.icons8_quit_32;
            buttonQuit.Location = new Point(528, 5);
            buttonQuit.Margin = new Padding(4, 5, 4, 5);
            buttonQuit.Name = "buttonQuit";
            buttonQuit.Secondary = true;
            buttonQuit.Size = new Size(255, 48);
            buttonQuit.TabIndex = 2;
            buttonQuit.Text = "&Quit";
            buttonQuit.ImageAlign = ContentAlignment.MiddleLeft;
            buttonQuit.Padding = new Padding(10, 0, 10, 0);
            buttonQuit.TextAlign = ContentAlignment.MiddleCenter;
            buttonQuit.TextImageRelation = TextImageRelation.ImageBeforeText;
            buttonQuit.UseVisualStyleBackColor = false;
            // 
            // buttonMetrics
            // 
            buttonMetrics.Activated = false;
            buttonMetrics.BackColor = SystemColors.ControlLight;
            buttonMetrics.BorderColor = Color.Transparent;
            buttonMetrics.BorderRadius = 2;
            buttonMetrics.Dock = DockStyle.Fill;
            buttonMetrics.FlatStyle = FlatStyle.Flat;
            buttonMetrics.Image = Properties.Resources.icons8_soonvibes_32;
            buttonMetrics.ImageAlign = ContentAlignment.MiddleLeft;
            buttonMetrics.Location = new Point(266, 5);
            buttonMetrics.Margin = new Padding(4, 5, 4, 5);
            buttonMetrics.Name = "buttonMetrics";
            buttonMetrics.Secondary = true;
            buttonMetrics.Size = new Size(254, 48);
            buttonMetrics.TabIndex = 1;
            buttonMetrics.Text = "&Metrics";
            buttonMetrics.Padding = new Padding(10, 0, 10, 0);
            buttonMetrics.TextAlign = ContentAlignment.MiddleCenter;
            buttonMetrics.TextImageRelation = TextImageRelation.ImageBeforeText;
            buttonMetrics.UseVisualStyleBackColor = false;
            // 
            // checkRunOnStartup
            // 
            checkRunOnStartup.AutoSize = true;
            checkRunOnStartup.BackColor = buttonSecond;
            checkRunOnStartup.Dock = DockStyle.Left;
            checkRunOnStartup.Location = new Point(20, 0);
            checkRunOnStartup.Margin = new Padding(11, 5, 11, 5);
            checkRunOnStartup.Name = "checkRunOnStartup";
            checkRunOnStartup.Padding = new Padding(10, 0, 0, 0);
            checkRunOnStartup.Size = new Size(216, 50);
            checkRunOnStartup.TabIndex = 21;
            checkRunOnStartup.Text = "Run on startup";
            checkRunOnStartup.UseVisualStyleBackColor = true;
            // 
            // panelPerformance
            // 
            panelPerformance.AccessibleRole = AccessibleRole.Grouping;
            panelPerformance.AutoSize = true;
            panelPerformance.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panelPerformance.Controls.Add(tablePerf);
            panelPerformance.Controls.Add(panelCPUTitle);
            panelPerformance.Dock = DockStyle.Top;
            panelPerformance.Location = new Point(11, 11);
            panelPerformance.Margin = new Padding(0);
            panelPerformance.Name = "panelPerformance";
            panelPerformance.Padding = new Padding(20);
            panelPerformance.Size = new Size(827, 208);
            panelPerformance.TabIndex = 0;
            panelPerformance.TabStop = true;
            // 
            // tablePerf
            // 
            tablePerf.AutoSize = true;
            tablePerf.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            tablePerf.ColumnCount = 4;
            tablePerf.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            tablePerf.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            tablePerf.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            tablePerf.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            tablePerf.Controls.Add(buttonEcoMode, 0, 0);
            tablePerf.Controls.Add(buttonBalancedMode, 1, 0);
            tablePerf.Controls.Add(buttonPerformanceMode, 2, 0);
            tablePerf.Controls.Add(buttonTurboFanMode, 3, 0);
            tablePerf.Dock = DockStyle.Top;
            tablePerf.Location = new Point(20, 60);
            tablePerf.Margin = new Padding(8, 4, 8, 4);
            tablePerf.Name = "tablePerf";
            tablePerf.RowCount = 1;
            tablePerf.RowStyles.Add(new RowStyle(SizeType.Absolute, 128F));
            tablePerf.Size = new Size(787, 128);
            tablePerf.TabIndex = 29;
            // 
            // buttonEcoMode
            // 
            buttonEcoMode.Activated = false;
            buttonEcoMode.BackColor = SystemColors.ControlLightLight;
            buttonEcoMode.BackgroundImageLayout = ImageLayout.None;
            buttonEcoMode.BorderColor = Color.Transparent;
            buttonEcoMode.BorderRadius = 5;
            buttonEcoMode.Dock = DockStyle.Fill;
            buttonEcoMode.FlatAppearance.BorderSize = 0;
            buttonEcoMode.FlatStyle = FlatStyle.Flat;
            buttonEcoMode.ForeColor = SystemColors.ControlText;
            buttonEcoMode.Image = Properties.Resources.icons8_bicycle_48;;
            buttonEcoMode.ImageAlign = ContentAlignment.BottomCenter;
            buttonEcoMode.Location = new Point(4, 4);
            buttonEcoMode.Margin = new Padding(4);
            buttonEcoMode.Name = "buttonEcoMode";
            buttonEcoMode.Secondary = false;
            buttonEcoMode.Size = new Size(188, 120);
            buttonEcoMode.TabIndex = 1;
            buttonEcoMode.Text = "&Silent";
            buttonEcoMode.TextImageRelation = TextImageRelation.ImageAboveText;
            buttonEcoMode.UseVisualStyleBackColor = false;
            // 
            // buttonBalancedMode
            // 
            buttonBalancedMode.Activated = false;
            buttonBalancedMode.BackColor = SystemColors.ControlLightLight;
            buttonBalancedMode.BorderColor = Color.Transparent;
            buttonBalancedMode.BorderRadius = 5;
            buttonBalancedMode.Dock = DockStyle.Fill;
            buttonBalancedMode.FlatAppearance.BorderSize = 0;
            buttonBalancedMode.FlatStyle = FlatStyle.Flat;
            buttonBalancedMode.ForeColor = SystemColors.ControlText;
            buttonBalancedMode.Image = Properties.Resources.icons8_fiat_48;
            buttonBalancedMode.ImageAlign = ContentAlignment.BottomCenter;
            buttonBalancedMode.Location = new Point(200, 4);
            buttonBalancedMode.Margin = new Padding(4);
            buttonBalancedMode.Name = "buttonBalancedMode";
            buttonBalancedMode.Secondary = false;
            buttonBalancedMode.Size = new Size(188, 120);
            buttonBalancedMode.TabIndex = 1;
            buttonBalancedMode.Text = "&Balanced";
            buttonBalancedMode.TextImageRelation = TextImageRelation.ImageAboveText;
            buttonBalancedMode.UseVisualStyleBackColor = false;
            // 
            // buttonPerformanceMode
            // 
            buttonPerformanceMode.Activated = false;
            buttonPerformanceMode.BackColor = SystemColors.ControlLightLight;
            buttonPerformanceMode.BorderColor = Color.Transparent;
            buttonPerformanceMode.BorderRadius = 5;
            buttonPerformanceMode.Dock = DockStyle.Fill;
            buttonPerformanceMode.FlatAppearance.BorderSize = 0;
            buttonPerformanceMode.FlatStyle = FlatStyle.Flat;
            buttonPerformanceMode.ForeColor = SystemColors.ControlText;
            buttonPerformanceMode.Image = ResizeImageToSize(Properties.Resources.icons8_flash_48, 32, 32);
            buttonPerformanceMode.ImageAlign = ContentAlignment.BottomCenter;
            buttonPerformanceMode.Location = new Point(396, 4);
            buttonPerformanceMode.Margin = new Padding(4);
            buttonPerformanceMode.Name = "buttonPerformanceMode";
            buttonPerformanceMode.Secondary = false;
            buttonPerformanceMode.Size = new Size(188, 120);
            buttonPerformanceMode.TabIndex = 2;
            buttonPerformanceMode.Text = "&Turbo";
            buttonPerformanceMode.TextImageRelation = TextImageRelation.ImageAboveText;
            buttonPerformanceMode.UseVisualStyleBackColor = false;
            // 
            // buttonTurboFanMode
            // 
            buttonTurboFanMode.Activated = false;
            buttonTurboFanMode.BackColor = SystemColors.ControlLight;
            buttonTurboFanMode.BorderColor = Color.Transparent;
            buttonTurboFanMode.BorderRadius = 5;
            buttonTurboFanMode.Dock = DockStyle.Fill;
            buttonTurboFanMode.FlatAppearance.BorderSize = 0;
            buttonTurboFanMode.FlatStyle = FlatStyle.Flat;
            buttonTurboFanMode.Image = Properties.Resources.icons8_rocket_32;
            buttonTurboFanMode.ImageAlign = ContentAlignment.BottomCenter;
            buttonTurboFanMode.Location = new Point(592, 4);
            buttonTurboFanMode.Margin = new Padding(4);
            buttonTurboFanMode.Name = "buttonTurboFanMode";
            buttonTurboFanMode.Secondary = true;
            buttonTurboFanMode.Size = new Size(191, 120);
            buttonTurboFanMode.TabIndex = 3;
            buttonTurboFanMode.Text = "&Fans + Power";
            buttonTurboFanMode.TextImageRelation = TextImageRelation.ImageAboveText;
            buttonTurboFanMode.UseVisualStyleBackColor = false;
            // 
            // panelCPUTitle
            // 
            panelCPUTitle.Controls.Add(picturePerformanceIcon);
            panelCPUTitle.Controls.Add(labelPerformanceMode);
            panelCPUTitle.Controls.Add(labelCpuFanStatus);
            panelCPUTitle.Dock = DockStyle.Top;
            panelCPUTitle.Location = new Point(20, 20);
            panelCPUTitle.Margin = new Padding(4);
            panelCPUTitle.Name = "panelCPUTitle";
            panelCPUTitle.Size = new Size(787, 40);
            panelCPUTitle.TabIndex = 30;
            // 
            // picturePerformanceIcon
            // 
            picturePerformanceIcon.BackgroundImage = Properties.Resources.icons8_gauge_32;
            picturePerformanceIcon.BackgroundImageLayout = ImageLayout.Zoom;
            picturePerformanceIcon.InitialImage = null;
            picturePerformanceIcon.Location = new Point(8, 0);
            picturePerformanceIcon.Margin = new Padding(4);
            picturePerformanceIcon.Name = "picturePerformanceIcon";
            picturePerformanceIcon.Size = new Size(32, 32);
            picturePerformanceIcon.TabIndex = 35;
            picturePerformanceIcon.TabStop = false;
            // 
            // labelPerformanceMode
            // 
            labelPerformanceMode.AccessibleRole = AccessibleRole.Caret;
            labelPerformanceMode.AutoSize = true;
            labelPerformanceMode.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            labelPerformanceMode.LiveSetting = System.Windows.Forms.Automation.AutomationLiveSetting.Polite;
            labelPerformanceMode.Location = new Point(40, 0);
            labelPerformanceMode.Margin = new Padding(8, 0, 8, 0);
            labelPerformanceMode.Name = "labelPerformanceMode";
            labelPerformanceMode.Size = new Size(234, 32);
            labelPerformanceMode.TabIndex = 0;
            labelPerformanceMode.Text = "Performance Mode";
            // 
            // labelCpuFanStatus
            // 
            labelCpuFanStatus.AccessibleRole = AccessibleRole.TitleBar;
            labelCpuFanStatus.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            labelCpuFanStatus.Cursor = Cursors.Hand;
            labelCpuFanStatus.Location = new Point(387, 0);
            labelCpuFanStatus.Margin = new Padding(8, 0, 8, 0);
            labelCpuFanStatus.Name = "labelCpuFanStatus";
            labelCpuFanStatus.Size = new Size(400, 36);
            labelCpuFanStatus.TabIndex = 33;
            labelCpuFanStatus.Text = "      ";
            labelCpuFanStatus.TextAlign = ContentAlignment.TopRight;
            // 
            // panelGPU
            // 
            panelGPU.AccessibleRole = AccessibleRole.Grouping;
            panelGPU.AutoSize = true;
            panelGPU.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panelGPU.Controls.Add(labelGpuHint);
            panelGPU.Controls.Add(tableGPU);
            panelGPU.Controls.Add(panelGPUTitle);
            panelGPU.Dock = DockStyle.Top;
            panelGPU.Location = new Point(11, 219);
            panelGPU.Margin = new Padding(0);
            panelGPU.Name = "panelGPU";
            panelGPU.Padding = new Padding(20, 20, 20, 0);
            panelGPU.Size = new Size(827, 432);
            panelGPU.TabIndex = 1;
            panelGPU.TabStop = true;
            // 
            // labelGpuHint
            // 
            labelGpuHint.Dock = DockStyle.Top;
            labelGpuHint.ForeColor = SystemColors.GrayText;
            labelGpuHint.Location = new Point(20, 396);
            labelGpuHint.Margin = new Padding(4, 0, 4, 0);
            labelGpuHint.Name = "labelGpuHint";
            labelGpuHint.Size = new Size(787, 36);
            labelGpuHint.TabIndex = 20;
            // 
            // 
            // 
            // 
            // 
            // 
            // 
            // 
            // 
            // tableGPU
            // 
            tableGPU.AutoSize = true;
            tableGPU.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            tableGPU.ColumnCount = 4;
            tableGPU.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            tableGPU.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            tableGPU.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            tableGPU.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            tableGPU.Controls.Add(buttonEnduranceMode, 0, 0);
            tableGPU.Controls.Add(buttonGpuStandardMode, 1, 0);
            tableGPU.Controls.Add(buttonGpuUltimateMode, 2, 0);
            tableGPU.Controls.Add(buttonTurboFanModePower, 3, 0);
            tableGPU.Dock = DockStyle.Top;
            tableGPU.Location = new Point(20, 60);
            tableGPU.Margin = new Padding(8, 4, 8, 4);
            tableGPU.Name = "tableGPU";
            tableGPU.RowCount = 1;
            tableGPU.RowStyles.Add(new RowStyle(SizeType.Absolute, 128F));
            tableGPU.Size = new Size(787, 128);
            tableGPU.TabIndex = 16;
            // 
            // 
            // 
            // buttonEnduranceMode
            // 
            buttonEnduranceMode.Activated = false;
            buttonEnduranceMode.BackColor = SystemColors.ControlLightLight;
            buttonEnduranceMode.BorderColor = Color.Transparent;
            buttonEnduranceMode.BorderRadius = 5;
            buttonEnduranceMode.CausesValidation = false;
            buttonEnduranceMode.Dock = DockStyle.Top;
            buttonEnduranceMode.FlatAppearance.BorderSize = 0;
            buttonEnduranceMode.FlatStyle = FlatStyle.Flat;
            buttonEnduranceMode.ForeColor = SystemColors.ControlText;
            buttonEnduranceMode.Image = ResizeImageToSize(Properties.Resources.icons8_leaf_48, 32, 32);
            buttonEnduranceMode.ImageAlign = ContentAlignment.BottomCenter;
            buttonEnduranceMode.Location = new Point(4, 4);
            buttonEnduranceMode.Margin = new Padding(4);
            buttonEnduranceMode.Name = "buttonEnduranceMode";
            buttonEnduranceMode.Secondary = false;
            buttonEnduranceMode.Size = new Size(188, 120);
            buttonEnduranceMode.TabIndex = 4;
            buttonEnduranceMode.Text = "Endurance";
            buttonEnduranceMode.TextImageRelation = TextImageRelation.ImageAboveText;
            buttonEnduranceMode.UseVisualStyleBackColor = false;
            // 
            // buttonGpuStandardMode
            // 
            buttonGpuStandardMode.Activated = false;
            buttonGpuStandardMode.BackColor = SystemColors.ControlLightLight;
            buttonGpuStandardMode.BorderColor = Color.Transparent;
            buttonGpuStandardMode.BorderRadius = 5;
            buttonGpuStandardMode.Dock = DockStyle.Top;
            buttonGpuStandardMode.FlatAppearance.BorderSize = 0;
            buttonGpuStandardMode.FlatStyle = FlatStyle.Flat;
            buttonGpuStandardMode.ForeColor = SystemColors.ControlText;
            buttonGpuStandardMode.Image = Properties.Resources.icons8_spa_flower_48;
            buttonGpuStandardMode.ImageAlign = ContentAlignment.BottomCenter;
            buttonGpuStandardMode.Location = new Point(396, 4);
            buttonGpuStandardMode.Margin = new Padding(4);
            buttonGpuStandardMode.Name = "buttonGpuStandardMode";
            buttonGpuStandardMode.Secondary = false;
            buttonGpuStandardMode.Size = new Size(188, 120);
            buttonGpuStandardMode.TabIndex = 5;
            buttonGpuStandardMode.Text = "Standard";
            buttonGpuStandardMode.TextImageRelation = TextImageRelation.ImageAboveText;
            buttonGpuStandardMode.UseVisualStyleBackColor = false;
            // 
            // 

            // 
            // buttonGpuUltimateMode
            // 
            buttonGpuUltimateMode.Activated = false;
            buttonGpuUltimateMode.BackColor = SystemColors.ControlLightLight;
            buttonGpuUltimateMode.BorderColor = Color.Transparent;
            buttonGpuUltimateMode.BorderRadius = 5;
            buttonGpuUltimateMode.Dock = DockStyle.Top;
            buttonGpuUltimateMode.FlatAppearance.BorderSize = 0;
            buttonGpuUltimateMode.FlatStyle = FlatStyle.Flat;
            buttonGpuUltimateMode.ForeColor = SystemColors.ControlText;
            buttonGpuUltimateMode.Image = Properties.Resources.icons8_game_controller_48;
            buttonGpuUltimateMode.ImageAlign = ContentAlignment.BottomCenter;
            buttonGpuUltimateMode.Location = new Point(592, 4);
            buttonGpuUltimateMode.Margin = new Padding(4);
            buttonGpuUltimateMode.Name = "buttonGpuUltimateMode";
            buttonGpuUltimateMode.Secondary = false;
            buttonGpuUltimateMode.Size = new Size(191, 120);
            buttonGpuUltimateMode.TabIndex = 6;
            buttonGpuUltimateMode.Text = "Ultimate";
            buttonGpuUltimateMode.TextImageRelation = TextImageRelation.ImageAboveText;
            buttonGpuUltimateMode.UseVisualStyleBackColor = false;
            // 
            // panelGPUTitle
            // 
            panelGPUTitle.Controls.Add(pictureGpuIcon);
            panelGPUTitle.Controls.Add(labelGpuMode);
            panelGPUTitle.Controls.Add(labelGpuModeFan);
            panelGPUTitle.Dock = DockStyle.Top;
            panelGPUTitle.Location = new Point(20, 20);
            panelGPUTitle.Margin = new Padding(4);
            panelGPUTitle.Name = "panelGPUTitle";
            panelGPUTitle.Size = new Size(787, 40);
            panelGPUTitle.TabIndex = 21;
            // 
            // pictureGpuIcon
            // 
            pictureGpuIcon.BackgroundImage = Properties.Resources.icons8_video_card_32;
            pictureGpuIcon.BackgroundImageLayout = ImageLayout.Zoom;
            pictureGpuIcon.Location = new Point(8, 0);
            pictureGpuIcon.Margin = new Padding(4);
            pictureGpuIcon.Name = "pictureGpuIcon";
            pictureGpuIcon.Size = new Size(32, 32);
            pictureGpuIcon.TabIndex = 22;
            pictureGpuIcon.TabStop = false;
            // 
            // labelGpuMode
            // 
            labelGpuMode.AutoSize = true;
            labelGpuMode.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            labelGpuMode.Location = new Point(40, 0);
            labelGpuMode.Margin = new Padding(8, 0, 8, 0);
            labelGpuMode.Name = "labelGpuMode";
            labelGpuMode.Size = new Size(136, 32);
            labelGpuMode.TabIndex = 21;
            labelGpuMode.Text = "GPU Mode";
            // 
            // labelGpuModeFan
            // 
            labelGpuModeFan.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            labelGpuModeFan.Location = new Point(387, 0);
            labelGpuModeFan.Margin = new Padding(8, 0, 8, 0);
            labelGpuModeFan.Name = "labelGpuModeFan";
            labelGpuModeFan.Size = new Size(400, 35);
            labelGpuModeFan.TabIndex = 20;
            labelGpuModeFan.Text = "         ";
            labelGpuModeFan.TextAlign = ContentAlignment.TopRight;
            // 
            // panelScreen
            // 
            panelScreen.AccessibleRole = AccessibleRole.Grouping;
            panelScreen.AutoSize = true;
            panelScreen.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panelScreen.Controls.Add(tableScreen);
            panelScreen.Controls.Add(panelScreenTitle);
            panelScreen.Dock = DockStyle.Top;
            panelScreen.Location = new Point(11, 651);
            panelScreen.Margin = new Padding(0);
            panelScreen.Name = "panelScreen";
            panelScreen.Padding = new Padding(20, 11, 20, 0);
            panelScreen.Size = new Size(827, 187);
            panelScreen.TabIndex = 2;
            panelScreen.TabStop = true;
            // 
            // 
            // 
            // tableScreen
            // 
            tableScreen.AutoSize = true;
            tableScreen.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            tableScreen.ColumnCount = 4;
            tableScreen.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            tableScreen.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            tableScreen.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            tableScreen.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            tableScreen.Controls.Add(buttonAutoRefreshRate, 0, 0);
            tableScreen.Controls.Add(button60Hz, 1, 0);
            tableScreen.Controls.Add(button120Hz, 2, 0);
            tableScreen.Controls.Add(buttonMaxRefreshRate, 3, 0);
            tableScreen.Dock = DockStyle.Top;
            tableScreen.Location = new Point(20, 51);
            tableScreen.Margin = new Padding(8, 4, 8, 4);
            tableScreen.Name = "tableScreen";
            tableScreen.RowCount = 1;
            tableScreen.RowStyles.Add(new RowStyle(SizeType.Absolute, 80F));
            tableScreen.Size = new Size(787, 80);
            tableScreen.TabIndex = 23;
            // 
            // buttonAutoRefreshRate
            // 
            buttonAutoRefreshRate.Activated = false;
            buttonAutoRefreshRate.BackColor = SystemColors.ControlLightLight;
            buttonAutoRefreshRate.BorderColor = Color.Transparent;
            buttonAutoRefreshRate.BorderRadius = 5;
            buttonAutoRefreshRate.Dock = DockStyle.Fill;
            buttonAutoRefreshRate.FlatAppearance.BorderSize = 0;
            buttonAutoRefreshRate.FlatStyle = FlatStyle.Flat;
            buttonAutoRefreshRate.ForeColor = SystemColors.ControlText;
            buttonAutoRefreshRate.Location = new Point(4, 4);
            buttonAutoRefreshRate.Margin = new Padding(4);
            buttonAutoRefreshRate.Name = "buttonAutoRefreshRate";
            buttonAutoRefreshRate.Secondary = false;
            buttonAutoRefreshRate.Size = new Size(188, 72);
            buttonAutoRefreshRate.TabIndex = 9;
            buttonAutoRefreshRate.Text = "Auto";
            buttonAutoRefreshRate.UseVisualStyleBackColor = false;
            // 
            // button60Hz
            // 
            button60Hz.Activated = false;
            button60Hz.BackColor = SystemColors.ControlLightLight;
            button60Hz.BorderColor = Color.Transparent;
            button60Hz.BorderRadius = 5;
            button60Hz.CausesValidation = false;
            button60Hz.Dock = DockStyle.Fill;
            button60Hz.FlatAppearance.BorderSize = 0;
            button60Hz.FlatStyle = FlatStyle.Flat;
            button60Hz.ForeColor = SystemColors.ControlText;
            button60Hz.Location = new Point(200, 4);
            button60Hz.Margin = new Padding(4);
            button60Hz.Name = "button60Hz";
            button60Hz.Secondary = false;
            button60Hz.Size = new Size(188, 72);
            button60Hz.TabIndex = 10;
            button60Hz.Text = "60Hz";
            button60Hz.UseVisualStyleBackColor = false;
            // 
            // button120Hz
            // 
            button120Hz.Activated = false;
            button120Hz.BackColor = SystemColors.ControlLightLight;
            button120Hz.BorderColor = Color.Transparent;
            button120Hz.BorderRadius = 5;
            button120Hz.Dock = DockStyle.Fill;
            button120Hz.FlatAppearance.BorderSize = 0;
            button120Hz.FlatStyle = FlatStyle.Flat;
            button120Hz.ForeColor = SystemColors.ControlText;
            button120Hz.Location = new Point(396, 4);
            button120Hz.Margin = new Padding(4);
            button120Hz.Name = "button120Hz";
            button120Hz.Secondary = false;
            button120Hz.Size = new Size(188, 72);
            button120Hz.TabIndex = 11;
            button120Hz.Text = "120Hz + OD";
            button120Hz.UseVisualStyleBackColor = false;
            // 
            // 
            // 
            // buttonMaxRefreshRate
            // 
            buttonMaxRefreshRate.Activated = false;
            buttonMaxRefreshRate.BackColor = SystemColors.ControlLightLight;
            buttonMaxRefreshRate.BorderColor = Color.Transparent;
            buttonMaxRefreshRate.BorderRadius = 5;
            buttonMaxRefreshRate.CausesValidation = false;
            buttonMaxRefreshRate.Dock = DockStyle.Fill;
            buttonMaxRefreshRate.FlatAppearance.BorderSize = 0;
            buttonMaxRefreshRate.FlatStyle = FlatStyle.Flat;
            buttonMaxRefreshRate.ForeColor = SystemColors.ControlText;
            buttonMaxRefreshRate.Location = new Point(4, 84);
            buttonMaxRefreshRate.Margin = new Padding(4);
            buttonMaxRefreshRate.Name = "buttonMaxRefreshRate";
            buttonMaxRefreshRate.Secondary = false;
            buttonMaxRefreshRate.Size = new Size(188, 12);
            buttonMaxRefreshRate.TabIndex = 13;
            buttonMaxRefreshRate.Text = "FHD";
            buttonMaxRefreshRate.UseVisualStyleBackColor = false;
            buttonMaxRefreshRate.Visible = false;
            // 
            // panelScreenTitle
            // 
            panelScreenTitle.Controls.Add(buttonColorProfiles);
            panelScreenTitle.Controls.Add(labelMiddleFanStatus);
            panelScreenTitle.Controls.Add(pictureScreenIcon);
            panelScreenTitle.Controls.Add(labelScreen);
            panelScreenTitle.Dock = DockStyle.Top;
            panelScreenTitle.Location = new Point(20, 11);
            panelScreenTitle.Margin = new Padding(4);
            panelScreenTitle.Name = "panelScreenTitle";
            panelScreenTitle.Size = new Size(787, 40);
            panelScreenTitle.TabIndex = 25;
            // 
            // buttonColorProfiles
            // 
            buttonColorProfiles.Activated = false;
            buttonColorProfiles.BackColor = SystemColors.ControlLight;
            buttonColorProfiles.BorderColor = Color.Transparent;
            buttonColorProfiles.BorderRadius = 2;
            buttonColorProfiles.Borderless = true;
            buttonColorProfiles.Dock = DockStyle.Right;
            buttonColorProfiles.FlatAppearance.BorderSize = 0;
            buttonColorProfiles.FlatStyle = FlatStyle.Flat;
            buttonColorProfiles.Font = new Font("Segoe UI", 7.125F, FontStyle.Bold);
            buttonColorProfiles.ForeColor = SystemColors.ControlDark;
            buttonColorProfiles.Location = new Point(675, 0);
            buttonColorProfiles.Margin = new Padding(0);
            buttonColorProfiles.Name = "buttonColorProfiles";
            buttonColorProfiles.Secondary = true;
            buttonColorProfiles.Size = new Size(112, 40);
            buttonColorProfiles.TabIndex = 4;
            buttonColorProfiles.Text = "Color Profile";
            buttonColorProfiles.UseVisualStyleBackColor = false;
            // 
            // labelMiddleFanStatus
            // 
            labelMiddleFanStatus.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            labelMiddleFanStatus.Location = new Point(500, 0);
            labelMiddleFanStatus.Margin = new Padding(8, 0, 8, 0);
            labelMiddleFanStatus.Name = "labelMiddleFanStatus";
            labelMiddleFanStatus.Size = new Size(285, 35);
            labelMiddleFanStatus.TabIndex = 28;
            labelMiddleFanStatus.Text = "         ";
            labelMiddleFanStatus.TextAlign = ContentAlignment.TopRight;
            // 
            // pictureScreenIcon
            // 
            pictureScreenIcon.BackgroundImage = Properties.Resources.icons8_laptop_32;
            pictureScreenIcon.BackgroundImageLayout = ImageLayout.Zoom;
            pictureScreenIcon.Location = new Point(8, 3);
            pictureScreenIcon.Margin = new Padding(4);
            pictureScreenIcon.Name = "pictureScreenIcon";
            pictureScreenIcon.Size = new Size(32, 32);
            pictureScreenIcon.TabIndex = 27;
            pictureScreenIcon.TabStop = false;
            // 
            // labelScreen
            // 
            labelScreen.AutoSize = true;
            labelScreen.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            labelScreen.Location = new Point(40, 0);
            labelScreen.Margin = new Padding(4, 0, 4, 0);
            labelScreen.Name = "labelScreen";
            labelScreen.Size = new Size(176, 32);
            labelScreen.TabIndex = 26;
            labelScreen.Text = "Laptop Screen";
            // 
            // panelRgb
            // 
            panelRgb.AccessibleRole = AccessibleRole.Grouping;
            panelRgb.AutoSize = true;
            panelRgb.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panelRgb.Controls.Add(labelBacklight);
            panelRgb.Controls.Add(tableLayoutRgb);
            panelRgb.Controls.Add(panelRgbTitle);
            panelRgb.Dock = DockStyle.Top;
            panelRgb.Location = new Point(11, 1394);
            panelRgb.Margin = new Padding(0);
            panelRgb.Name = "panelRgb";
            panelRgb.Padding = new Padding(20, 20, 20, 0);
            panelRgb.Size = new Size(827, 146);
            panelRgb.TabIndex = 6;
            panelRgb.TabStop = true;
            // 
            // labelBacklight
            // 
            labelBacklight.Cursor = Cursors.Hand;
            labelBacklight.Dock = DockStyle.Top;
            labelBacklight.Font = new Font("Segoe UI", 9F);
            labelBacklight.ForeColor = SystemColors.GrayText;
            labelBacklight.Location = new Point(20, 112);
            labelBacklight.Margin = new Padding(4, 0, 4, 0);
            labelBacklight.Name = "labelBacklight";
            labelBacklight.Padding = new Padding(4, 0, 4, 0);
            labelBacklight.Size = new Size(787, 34);
            labelBacklight.TabIndex = 43;
            // 
            // tableLayoutRgb
            // 
            tableLayoutRgb.AutoSize = true;
            tableLayoutRgb.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            tableLayoutRgb.ColumnCount = 3;
            tableLayoutRgb.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33F));
            tableLayoutRgb.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33F));
            tableLayoutRgb.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33F));
            tableLayoutRgb.Controls.Add(buttonRgbLighting, 0, 0);
            tableLayoutRgb.Controls.Add(panelColor, 1, 0);
            tableLayoutRgb.Controls.Add(comboRgbLightingMode, 2, 0);
            tableLayoutRgb.Dock = DockStyle.Top;
            tableLayoutRgb.Location = new Point(20, 60);
            tableLayoutRgb.Margin = new Padding(8, 4, 8, 4);
            tableLayoutRgb.Name = "tableLayoutRgb";
            tableLayoutRgb.RowCount = 1;
            tableLayoutRgb.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            tableLayoutRgb.Size = new Size(787, 52);
            tableLayoutRgb.TabIndex = 39;
            // 
            // buttonRgbLighting
            // 
            buttonRgbLighting.Activated = false;
            buttonRgbLighting.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttonRgbLighting.BackColor = SystemColors.ControlLight;
            buttonRgbLighting.BorderColor = Color.Transparent;
            buttonRgbLighting.BorderRadius = 2;
            buttonRgbLighting.Dock = DockStyle.Top;
            buttonRgbLighting.FlatAppearance.BorderSize = 0;
            buttonRgbLighting.FlatStyle = FlatStyle.Flat;
            buttonRgbLighting.Image = Properties.Resources.icons8_color_32;
            buttonRgbLighting.ImageAlign = ContentAlignment.MiddleLeft;
            buttonRgbLighting.Location = new Point(528, 4);
            buttonRgbLighting.Margin = new Padding(4);
            buttonRgbLighting.Name = "buttonRgbLighting";
            buttonRgbLighting.Secondary = true;
            buttonRgbLighting.Size = new Size(255, 48);
            buttonRgbLighting.TabIndex = 37;
            buttonRgbLighting.Text = "RGB";
            buttonRgbLighting.Padding = new Padding(10, 0, 10, 0);
            buttonRgbLighting.TextAlign = ContentAlignment.MiddleCenter;
            buttonRgbLighting.TextImageRelation = TextImageRelation.ImageBeforeText;
            buttonRgbLighting.UseVisualStyleBackColor = false;
            // 
            // panelColor
            // 
            panelColor.AutoSize = true;
            panelColor.Controls.Add(pictureBacklightSwatch);
            panelColor.Controls.Add(buttonRgbProfiles);
            panelColor.Dock = DockStyle.Fill;
            panelColor.Location = new Point(266, 4);
            panelColor.Margin = new Padding(4);
            panelColor.Name = "panelColor";
            panelColor.Size = new Size(254, 44);
            panelColor.TabIndex = 36;
            // 
            // 
            // 
            // pictureBacklightSwatch
            // 
            pictureBacklightSwatch.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            pictureBacklightSwatch.Location = new Point(218, 15);
            pictureBacklightSwatch.Margin = new Padding(8);
            pictureBacklightSwatch.Name = "pictureBacklightSwatch";
            pictureBacklightSwatch.Size = new Size(20, 20);
            pictureBacklightSwatch.TabIndex = 40;
            pictureBacklightSwatch.TabStop = false;
            // 
            // buttonRgbProfiles
            // 
            buttonRgbProfiles.Activated = false;
            buttonRgbProfiles.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttonRgbProfiles.BackColor = SystemColors.ButtonHighlight;
            buttonRgbProfiles.BorderColor = Color.Transparent;
            buttonRgbProfiles.BorderRadius = 2;
            buttonRgbProfiles.Dock = DockStyle.Top;
            buttonRgbProfiles.FlatStyle = FlatStyle.Flat;
            buttonRgbProfiles.ForeColor = SystemColors.ControlText;
            buttonRgbProfiles.Location = new Point(0, 0);
            buttonRgbProfiles.Margin = new Padding(4);
            buttonRgbProfiles.Name = "buttonRgbProfiles";
            buttonRgbProfiles.Secondary = false;
            buttonRgbProfiles.Size = new Size(254, 48);
            buttonRgbProfiles.TabIndex = 14;
            buttonRgbProfiles.Text = "Color Profiles";
            buttonRgbProfiles.UseVisualStyleBackColor = false;
            // 
            // comboRgbLightingMode
            // 
            comboRgbLightingMode.BorderColor = Color.White;
            comboRgbLightingMode.ButtonColor = Color.FromArgb(255, 255, 255);
            comboRgbLightingMode.Dock = DockStyle.Top;
            comboRgbLightingMode.FlatStyle = FlatStyle.Flat;
            comboRgbLightingMode.Font = new Font("Segoe UI", 9F);
            comboRgbLightingMode.FormattingEnabled = true;
            comboRgbLightingMode.Items.AddRange(new object[] { "Static", "Breathe", "Rainbow", "Strobe" });
            comboRgbLightingMode.Location = new Point(7, 7);
            comboRgbLightingMode.Margin = new Padding(7, 7, 7, 4);
            comboRgbLightingMode.Name = "comboRgbLightingMode";
            comboRgbLightingMode.Size = new Size(248, 40);
            comboRgbLightingMode.TabIndex = 13;
            // 
            // panelRgbTitle
            // 
            panelRgbTitle.Controls.Add(pictureRgbIcon);
            panelRgbTitle.Controls.Add(labelRgb);
            panelRgbTitle.Dock = DockStyle.Top;
            panelRgbTitle.Location = new Point(20, 20);
            panelRgbTitle.Margin = new Padding(0);
            panelRgbTitle.Name = "panelRgbTitle";
            panelRgbTitle.Padding = new Padding(0, 0, 5, 0);
            panelRgbTitle.Size = new Size(787, 40);
            panelRgbTitle.TabIndex = 40;
            // 
            // pictureRgbIcon
            // 
            pictureRgbIcon.BackgroundImage = Properties.Resources.icons8_color_32;
            pictureRgbIcon.BackgroundImageLayout = ImageLayout.Zoom;
            pictureRgbIcon.Location = new Point(8, 0);
            pictureRgbIcon.Margin = new Padding(4);
            pictureRgbIcon.Name = "pictureRgbIcon";
            pictureRgbIcon.Size = new Size(32, 32);
            pictureRgbIcon.TabIndex = 35;
            pictureRgbIcon.TabStop = false;
            // 
            // labelRgb
            // 
            labelRgb.AutoSize = true;
            labelRgb.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            labelRgb.Location = new Point(43, 0);
            labelRgb.Margin = new Padding(4, 0, 4, 0);
            labelRgb.Name = "labelRgb";
            labelRgb.Size = new Size(85, 32);
            labelRgb.TabIndex = 34;
            labelRgb.Text = "RGB";
            // 
            // panelStartup
            // 
            panelStartup.Controls.Add(checkAutoGpuBattery);
            panelStartup.Controls.Add(checkRunOnStartup);
            panelStartup.BackColor = buttonSecond;
            panelStartup.Dock = DockStyle.Top;
            panelStartup.Location = new Point(11, 1787);
            panelStartup.Margin = new Padding(0);
            panelStartup.Name = "panelStartup";
            panelStartup.Padding = new Padding(20, 0, 20, 0);
            panelStartup.Size = new Size(827, 44);
            panelStartup.TabIndex = 9;
            // 
            // checkAutoGpuBattery
            // 
            checkAutoGpuBattery.AutoSize = true;
            checkAutoGpuBattery.BackColor = buttonSecond;
            checkAutoGpuBattery.Dock = DockStyle.Right;
            checkAutoGpuBattery.Location = new Point(442, 0);
            checkAutoGpuBattery.Margin = new Padding(11, 5, 11, 5);
            checkAutoGpuBattery.Name = "checkAutoGpuBattery";
            checkAutoGpuBattery.Padding = new Padding(10, 0, 0, 0);
            checkAutoGpuBattery.Size = new Size(365, 50);
            checkAutoGpuBattery.TabIndex = 40;
            checkAutoGpuBattery.Text = "iGPU on Battery";
            checkAutoGpuBattery.UseVisualStyleBackColor = true;
            checkRunOnStartup.BackColor = buttonSecond;
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
            // pictureHandheldIcon
            // 
            pictureHandheldIcon.BackgroundImage = Properties.Resources.icons8_controller_32;
            pictureHandheldIcon.BackgroundImageLayout = ImageLayout.Zoom;
            pictureHandheldIcon.Location = new Point(8, 0);
            pictureHandheldIcon.Margin = new Padding(4);
            pictureHandheldIcon.Name = "pictureHandheldIcon";
            pictureHandheldIcon.Size = new Size(32, 32);
            pictureHandheldIcon.TabIndex = 27;
            pictureHandheldIcon.TabStop = false;
            // 
            // labelHandheldController
            // 
            labelHandheldController.AutoSize = true;
            labelHandheldController.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            labelHandheldController.Location = new Point(43, 0);
            labelHandheldController.Margin = new Padding(4, 0, 4, 0);
            labelHandheldController.Name = "labelHandheldController";
            labelHandheldController.Size = new Size(181, 32);
            labelHandheldController.TabIndex = 26;
            labelHandheldController.Text = "Ally Controller";
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
            // panelVersion
            // 
            panelVersion.AutoSize = true;
            panelVersion.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panelVersion.Dock = DockStyle.Top;
            panelVersion.Location = new Point(11, 1837);
            panelVersion.MinimumSize = new Size(0, 50);
            panelVersion.Name = "panelVersion";
            panelVersion.Padding = new Padding(20, 5, 24, 5);
            panelVersion.Size = new Size(827, 50);
            panelVersion.TabIndex = 10;
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
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(192F, 192F);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoSize = false;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            ClientSize = new Size(849, 1225);
            Controls.Add(panelFooter);
            Controls.Add(panelVersion);
            Controls.Add(panelStartup);
            Controls.Add(panelBattery);
            Controls.Add(panelRgb);
            Controls.Add(panelMatrix);
            Controls.Add(panelScreen);
            Controls.Add(panelGPU);
            Controls.Add(panelPerformance);
            Margin = new Padding(8, 4, 8, 4);
            MaximizeBox = false;
            MdiChildrenMinimizedAnchorBottom = false;
            MinimizeBox = false;
            MinimumSize = new Size(821, 0);
            Name = "MainForm";
            Padding = new Padding(11);
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterScreen;
            Text = "PreySense";
            panelMatrix.ResumeLayout(false);
            panelMatrix.PerformLayout();
            panelMatrixAuto.ResumeLayout(false);
            panelMatrixAuto.PerformLayout();
            tableLayoutMatrix.ResumeLayout(false);
            tableLayoutMatrix.PerformLayout();
            panelMatrixTitle.ResumeLayout(false);
            panelMatrixTitle.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)pictureMiniLedIcon).EndInit();
            panelBattery.ResumeLayout(false);
            panelBatteryTitle.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)pictureBatteryIcon).EndInit();
            panelFooter.ResumeLayout(false);
            panelFooter.PerformLayout();
            tableButtons.ResumeLayout(false);
            panelPerformance.ResumeLayout(false);
            panelPerformance.PerformLayout();
            tablePerf.ResumeLayout(false);
            panelCPUTitle.ResumeLayout(false);
            panelCPUTitle.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)picturePerformanceIcon).EndInit();
            panelGPU.ResumeLayout(false);
            panelGPU.PerformLayout();
            tableGPU.ResumeLayout(false);
            panelGPUTitle.ResumeLayout(false);
            panelGPUTitle.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)pictureGpuIcon).EndInit();
            panelScreen.ResumeLayout(false);
            panelScreen.PerformLayout();
            tableScreen.ResumeLayout(false);
            panelScreenTitle.ResumeLayout(false);
            panelScreenTitle.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)pictureScreenIcon).EndInit();
            panelRgb.ResumeLayout(false);
            panelRgb.PerformLayout();
            tableLayoutRgb.ResumeLayout(false);
            tableLayoutRgb.PerformLayout();
            panelColor.ResumeLayout(false);
            panelColor.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)pictureBacklightSwatch).EndInit();
            panelRgbTitle.ResumeLayout(false);
            panelRgbTitle.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)pictureRgbIcon).EndInit();
            panelStartup.ResumeLayout(false);
            panelStartup.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)pictureHandheldIcon).EndInit();
            panelVersion.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        private Panel panelMatrix;
        private Panel panelBattery;
        private Panel panelFooter;
        private RButton buttonQuit;
        private RCheckBox checkRunOnStartup;
        private Panel panelPerformance;
        private TableLayoutPanel tablePerf;
        private RButton buttonPerformanceMode;
        private RButton buttonBalancedMode;
        private RButton buttonEcoMode;
        private Panel panelGPU;
        private TableLayoutPanel tableGPU;
        private RButton buttonGpuUltimateMode;
        private RButton buttonGpuStandardMode;
        private RButton buttonEnduranceMode;
        private Panel panelScreen;
        private TableLayoutPanel tableScreen;
        private RButton buttonAutoRefreshRate;
        private RButton button60Hz;
        private Panel panelRgb;
        private TableLayoutPanel tableLayoutMatrix;
        private RComboBox comboMiniLedRunningMode;
        private RComboBox comboMiniLedMode;
        private TableLayoutPanel tableLayoutRgb;
        private RComboBox comboRgbLightingMode;
        private Panel panelColor;
        private PictureBox pictureBacklightSwatch;
        private CheckBox checkMiniLedEnabled;
        private RButton button120Hz;

        private Label labelGpuHint;
        private RButton buttonMiniLed;
        private RButton buttonRgbProfiles;
        private RButton buttonTurboFanMode;
        private LabeledSliderControl sliderBatteryChargeLimit;
        private Panel panelGPUTitle;
        private PictureBox pictureGpuIcon;
        private Label labelGpuMode;
        private Label labelGpuModeFan;
        private Panel panelCPUTitle;
        private PictureBox picturePerformanceIcon;
        private Label labelPerformanceMode;
        private Label labelCpuFanStatus;
        private Panel panelScreenTitle;
        private Label labelMiddleFanStatus;
        private PictureBox pictureScreenIcon;
        private Label labelScreen;
        private Panel panelRgbTitle;
        private PictureBox pictureRgbIcon;
        private Label labelRgb;
        private Panel panelMatrixTitle;
        private PictureBox pictureMiniLedIcon;
        private Label labelMatrix;
        private Panel panelBatteryTitle;
        private Label labelBatteryStatus;
        private PictureBox pictureBatteryIcon;
        private Label labelBatteryStatusLimitTitle;
        private Panel panelStartup;
        private TableLayoutPanel tableButtons;
        private RButton buttonRgbLighting;
        private RButton buttonMetrics;
        private RCheckBox checkAutoGpuBattery;
        private RButton buttonBatteryFull;
        private Label labelHandheldController;
        private PictureBox pictureHandheldIcon;
        private CheckBox checkMiniLedOnLidClose;
        private Panel panelMatrixAuto;
        private RButton buttonMaxRefreshRate;
        private Label labelBacklight;
        private Panel panelVersion;
        private RButton buttonTurboFanModePower;
        private RButton buttonColorProfiles;
    }
}

