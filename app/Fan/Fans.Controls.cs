using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using PreySense.UI;

namespace PreySense.Fan
{
    public partial class Fans
    {
        private void CreateCpuLimitEditors()
        {
            panelCpuLimitsTitle.Height = (int)(85 * (DeviceDpi / 192f));

            numPl1 = CreateNumControl(panelPl1, labelPl1, 5, _pl1MaxW, 45, val =>
            {
                if (_isUpdatingUi) return;
                trackPl1.Value = Math.Clamp(val, trackPl1.Minimum, trackPl1.Maximum);
                EnforcePowerLimitOrder(pl1IsDriver: true);
            });
            numPl2 = CreateNumControl(panelPl2, labelPl2, 5, _pl2MaxW, 55, val =>
            {
                if (_isUpdatingUi) return;
                trackPl2.Value = Math.Clamp(val, trackPl2.Minimum, trackPl2.Maximum);
                EnforcePowerLimitOrder(pl1IsDriver: false);
            });

            checkApplyCpuLimits.Visible = false;
            panelApplyCpuLimits.Visible = false;

            panelCpuLimitsSection.Controls.SetChildIndex(panelCpuLimitsTitle, 4);
            panelCpuLimitsSection.Controls.SetChildIndex(panelPl1, 3);
            panelCpuLimitsSection.Controls.SetChildIndex(panelPl2, 2);
            panelCpuLimitsSection.Controls.SetChildIndex(panelApplyCpuLimits, 0);
        }

        private void CreateGpuRuntimeControls()
        {
            numGpuCoreOffset = CreateNumControl(panelGpuOffsetsSectionCore, labelGpuCoreValue, -1000, 1000, 0, val =>
            {
                if (_isUpdatingUi) return;
                trackGpuCoreOffset.Value = Math.Clamp(val, trackGpuCoreOffset.Minimum, trackGpuCoreOffset.Maximum);
                labelGpuCoreValue.Text = FormatGpuOffset(trackGpuCoreOffset.Value);
            });
            numGpuMemoryOffset = CreateNumControl(panelGpuOffsetsSectionMemory, labelGpuMemoryValue, -1000, 3000, 0, val =>
            {
                if (_isUpdatingUi) return;
                trackGpuMemoryOffset.Value = Math.Clamp(val, trackGpuMemoryOffset.Minimum, trackGpuMemoryOffset.Maximum);
                labelGpuMemoryValue.Text = FormatGpuOffset(trackGpuMemoryOffset.Value);
            });

            checkApplyGpuLimits = new RCheckBox
            {
                Visible = false
            };
        }

        private void CreateFanCurveCards()
        {
            _curveCpu = new FanCurveControl { Dock = DockStyle.Fill, IsCpu = true, Points = _cpuCurve };
            _curveGpu = new FanCurveControl { Dock = DockStyle.Fill, IsCpu = false, Points = _gpuCurve };

            _cpuCurveCard = new SectionCardControl
            {
                Dock = DockStyle.Fill,
                SectionTitle = "CPU Fan Curve",
                SectionIcon = GetSectionIcon(Properties.Resources.icons8_fan_48)
            };
            _cpuCurveCard.Body.Padding = new Padding(0);
            _cpuCurveCard.Body.Controls.Add(_curveCpu);
            _curveCpu.PointsChanged += (_, _) => OnFanCurveEdited();

            _gpuCurveCard = new SectionCardControl
            {
                Dock = DockStyle.Fill,
                SectionTitle = "GPU Fan Curve",
                SectionIcon = GetSectionIcon(Properties.Resources.icons8_fan_48)
            };
            _gpuCurveCard.Body.Padding = new Padding(0);
            _gpuCurveCard.Body.Controls.Add(_curveGpu);
            _curveGpu.PointsChanged += (_, _) => OnFanCurveEdited();

            tableFanCharts.Visible = false;
            _curveHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(4, 0, 4, 4) };
            _curveFrame = new Panel { Dock = DockStyle.None, BackColor = Color.Transparent, Margin = Padding.Empty };
            _curveHost.Controls.Add(_curveFrame);
            _curveHost.Resize += (_, _) => LayoutCurveFrame();
            panelFans.Controls.Add(_curveHost);
            panelFans.Controls.SetChildIndex(_curveHost, panelFans.Controls.IndexOf(tableFanCharts));
            LayoutCurveFrame();
            ShowCurveForMode(false);
        }

        private void LayoutCurveFrame()
        {
            if (_curveHost == null || _curveFrame == null) return;
            _curveFrame.Size = new Size(Math.Max(0, _curveHost.ClientSize.Width), Math.Max(0, _curveHost.ClientSize.Height));
            _curveFrame.Left = 0;
            _curveFrame.Top = 0;
        }

        private void CreateMaxFanCheck()
        {
            float scale = panelApplyFans.DeviceDpi / 192f;
            int S(int px) => Math.Max(1, (int)Math.Round(px * scale));

            checkApplyFanCurves.AutoSize = true;
            checkApplyFanCurves.Dock = DockStyle.None;
            checkApplyFanCurves.Margin = Padding.Empty;
            checkApplyFanCurves.Padding = new Padding(S(8), S(6), S(8), S(6));
            checkApplyFanCurves.TextAlign = ContentAlignment.MiddleLeft;

            labelFansResult.AutoSize = true;
            labelFansResult.Dock = DockStyle.None;
            labelFansResult.Margin = new Padding(S(8), 0, S(8), S(4));

            var flow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Dock = DockStyle.None,
                Location = new Point(0, 0),
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = Color.Transparent
            };
            flow.Controls.Add(checkApplyFanCurves);
            flow.Controls.Add(labelFansResult);

            panelApplyFans.Controls.Clear();
            panelApplyFans.Controls.Add(flow);
            panelApplyFans.AutoSize = true;
            panelApplyFans.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        }

        private Image GetSectionIcon(Image icon)
        {
            return UI.ControlHelper.TintImage(ResizeImageToSize(icon, 32, 32), foreMain);
        }

        private static Image ResizeImageToSize(Image image, int width, int height)
        {
            if (image is null)
            {
                return new Bitmap(width, height);
            }

            var destRect = new Rectangle(0, 0, width, height);
            var destImage = new Bitmap(width, height);

            if (image.HorizontalResolution > 0 && image.VerticalResolution > 0)
            {
                destImage.SetResolution(image.HorizontalResolution, image.VerticalResolution);
            }

            using var graphics = Graphics.FromImage(destImage);
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            using var wrapMode = new ImageAttributes();
            wrapMode.SetWrapMode(WrapMode.TileFlipXY);
            graphics.DrawImage(image, destRect, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, wrapMode);
            return destImage;
        }

        private NumericUpDown CreateNumControl(Panel parent, Control hideLabel, int min, int max, int val, Action<int> onValChanged)
        {
            float scale = parent.DeviceDpi / 96f;
            int numWidth = Math.Clamp((int)(60 * scale), 52, 76);
            int numHeight = Math.Clamp((int)(24 * scale), 22, 30);
            int marginRight = (int)(15 * scale);
            int marginTop = Math.Max(4, (int)(8 * (parent.DeviceDpi / 192f)));

            var num = new NumericUpDown
            {
                Minimum = min, Maximum = max, Value = Math.Clamp(val, min, max),
                Size = new Size(numWidth, numHeight), BackColor = buttonMain, ForeColor = foreMain,
                BorderStyle = BorderStyle.None, TextAlign = HorizontalAlignment.Center, Margin = Padding.Empty
            };
            num.ValueChanged += (_, _) => onValChanged((int)num.Value);

            var host = new FlowLayoutPanel
            {
                Dock = DockStyle.Top, FlowDirection = FlowDirection.RightToLeft, WrapContents = false,
                Height = Math.Max(42, numHeight + marginTop),
                Padding = new Padding(0, marginTop, marginRight, 0),
                Margin = Padding.Empty, BackColor = Color.Transparent
            };
            host.Controls.Add(num);
            parent.Controls.Add(host);
            parent.Controls.SetChildIndex(host, 0);
            hideLabel.Visible = false;
            return num;
        }

        private void ConfigureFansLayout()
        {
            float scale = DeviceDpi / 192f;
            int S(int px) => Math.Max(1, (int)Math.Round(px * scale));
            const int leftWidth = 460;
            const int rightWidth = 920;
            const int formHeight = 860;

            AutoSize = false;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Padding = new Padding(S(8));
            MinimumSize = Size.Empty;
            MaximumSize = Size.Empty;
            ClientSize = new Size(S(leftWidth + rightWidth + 24), S(formHeight));

            panelMainControls.Dock = DockStyle.Left;
            panelMainControls.MinimumSize = new Size(S(leftWidth), 0);
            panelMainControls.Width = S(leftWidth);
            panelMainControls.Padding = new Padding(S(18), S(4), S(12), S(14));
            panelMainControls.BackColor = formBack;

            panelFans.Dock = DockStyle.Fill;
            panelFans.MinimumSize = new Size(S(rightWidth), 0);
            panelFans.Width = S(rightWidth);
            panelFans.Padding = new Padding(0, S(2), S(2), S(2));
            panelFans.BackColor = formBack;
            panelTitleFans.Visible = false;

            ConfigureNavButtons(S);
            ConfigureSectionPanels(S);
            ConfigurePerfModeSelector(S);
            ConfigureResetButton(S);
            picturePerf.BackgroundImage = GetSectionIcon(Properties.Resources.icons8_fan_32);

            if (_curveHost != null)
                _curveHost.MinimumSize = new Size(0, 0);

            ConfigureFanCurvePanel(S);
            LayoutCurveFrame();
            ToggleNav(0);
        }

        private void ConfigureFanCurvePanel(Func<int, int> S)
        {
            panelApplyFans.AutoSize = true;
            panelApplyFans.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panelApplyFans.Dock = DockStyle.None;
            panelApplyFans.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            panelApplyFans.Padding = new Padding(0);
            panelApplyFans.BackColor = formBack;
            checkApplyFanCurves.BackColor = buttonSecond;

            var container = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = formBack
            };

            labelFanRampUp = new Label
            {
                Text = "Ramp Up Time:",
                ForeColor = foreMain,
                Font = new Font("Segoe UI", 9f),
                TextAlign = ContentAlignment.MiddleLeft,
                Height = S(28),
                AutoSize = true,
                Margin = new Padding(0, S(6), 0, 0)
            };

            numFanRampUp = new RNumericUpDown
            {
                Minimum = 0,
                Maximum = 60,
                Value = 0,
                Width = S(75),
                Height = S(28),
                TextAlign = HorizontalAlignment.Center,
                Margin = new Padding(S(4), S(4), 0, 0),
                TabStop = false
            };
            numFanRampUp.ApplyTheme(true);

            panelApplyFans.Controls.Clear();
            container.Controls.Add(labelFanRampUp);
            container.Controls.Add(numFanRampUp);
            
            // Adjust margin of the checkbox since it is now on the right
            checkApplyFanCurves.Margin = new Padding(S(12), S(2), 0, 0);
            
            container.Controls.Add(checkApplyFanCurves);
            panelApplyFans.Controls.Add(container);
            panelApplyFans.Controls.Add(labelFansResult);

            if (_curveHost != null)
            {
                _curveHost.Dock = DockStyle.Fill;
                _curveHost.Padding = new Padding(0, S(2), 0, 0);
                panelFans.Controls.SetChildIndex(_curveHost, panelFans.Controls.Count - 1);
            }

            panelApplyFans.BringToFront();
            void position()
            {
                int x = panelFans.ClientSize.Width - panelFans.Padding.Right - panelApplyFans.Width - S(6);
                int headerCenter = panelFans.Padding.Top + S(2) + 22;
                int y = headerCenter - panelApplyFans.Height / 2;
                panelApplyFans.Location = new Point(Math.Max(0, x), Math.Max(0, y));
            }
            position();
            panelFans.Resize += (_, _) => position();
            panelApplyFans.SizeChanged += (_, _) => position();
        }

        private void ConfigurePerfModeSelector(Func<int, int> S)
        {
            _perfModeHost?.Dispose();
            _perfModeHost = new Panel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = formBack,
                Margin = new Padding(0),
                Padding = new Padding(0, S(8), 0, S(10))
            };

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 2,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = formBack
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var caption = new Label
            {
                Dock = DockStyle.Fill,
                Text = "Performance Mode",
                ForeColor = foreMain,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = Padding.Empty,
                AutoSize = true
            };
            comboPerfMode = new RComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = buttonMain,
                ForeColor = foreMain,
                BorderColor = formBack,
                ButtonColor = buttonMain,
                ArrowColor = foreMain,
                FormattingEnabled = true,
                Margin = new Padding(0, S(8), 0, 0),
                Height = S(40)
            };

            root.Controls.Add(caption, 0, 0);
            root.Controls.Add(comboPerfMode, 0, 1);
            _perfModeHost.Controls.Add(root);
            panelMainControls.Controls.Add(_perfModeHost); panelMainControls.Controls.SetChildIndex(_perfModeHost, panelMainControls.Controls.Count - 1);
        }

        private void ConfigureResetButton(Func<int, int> S)
        {
            var host = new Panel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = formBack, Margin = new Padding(0), Padding = new Padding(0, S(12), 0, 0) };
            
            var buttonRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, Padding = Padding.Empty, BackColor = formBack };
            buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            buttonRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _buttonSaveSettings = CreateFooterButton("Save Settings", S);
            _buttonSaveSettings.Margin = new Padding(S(4), 0, 0, S(8));

            _buttonApplySettings = CreateFooterButton("Apply Power Limits", S);
            _buttonApplySettings.Margin = new Padding(0, 0, S(4), S(8));

            _buttonApplySettings.Click += (_, _) =>
            {
                if (buttonCPU.Activated)
                {
                    ApplyCpuPowerLimits();
                }
                else if (buttonGPU.Activated)
                {
                    CommitGpuSettings();
                }
            };

            buttonRow.Controls.Add(_buttonApplySettings, 0, 0);
            buttonRow.Controls.Add(_buttonSaveSettings, 1, 0);

            var stack = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, Padding = Padding.Empty, BackColor = formBack };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _buttonResetDefaults = CreateFooterButton("Factory Defaults", S);
            _buttonSaveSettings.Click += (_, _) => SaveVisibleSettings();
            _buttonResetDefaults.Click += (_, _) => ResetDefaults();

            stack.Controls.Add(buttonRow, 0, 0);
            stack.Controls.Add(_buttonResetDefaults, 0, 1);
            host.Controls.Add(stack);
            panelMainControls.Controls.Add(host);
            panelMainControls.Controls.SetChildIndex(host, 0);
        }

        private RButton CreateFooterButton(string text, Func<int, int> S)
        {
            var button = new RButton
            {
                Dock = DockStyle.Top,
                Text = text,
                Secondary = true,
                BorderRadius = 2,
                BorderColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                BackColor = buttonSecond,
                ForeColor = foreMain,
                TextAlign = ContentAlignment.MiddleCenter,
                Height = S(52),
                Margin = new Padding(0, 0, 0, S(8)),
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderColor = borderSecond;
            return button;
        }

        private void ConfigureNavButtons(Func<int, int> S)
        {
            int navHeight = S(64);
            panelNav.AutoSize = false;
            panelNav.Dock = DockStyle.Top;
            panelNav.Height = navHeight;
            panelNav.MinimumSize = new Size(0, navHeight);
            panelNav.Margin = new Padding(0);
            panelNav.Padding = new Padding(0, 0, 0, S(10));
            tableNav.ColumnCount = 2;
            tableNav.ColumnStyles.Clear();
            tableNav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            tableNav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            tableNav.RowStyles.Clear();
            tableNav.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            tableNav.Dock = DockStyle.Fill;
            tableNav.MinimumSize = Size.Empty;
            tableNav.Padding = new Padding(0);
            tableNav.Margin = new Padding(0, 0, 0, S(12));
            StyleNavButton(buttonCPU, S);
            StyleNavButton(buttonGPU, S);
        }

        private static void StyleNavButton(RButton button, Func<int, int> S)
        {
            button.Dock = DockStyle.Fill;
            button.Secondary = true;
            button.BorderRadius = 2;
            button.BorderColor = colorGray;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = borderSecond;
            button.BackColor = buttonSecond;
            button.ForeColor = foreMain;
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.Padding = new Padding(S(8), 0, S(8), 0);
            button.Margin = new Padding(S(3), S(6), S(3), S(6));
            button.MinimumSize = Size.Empty;
            button.UseVisualStyleBackColor = false;
        }

        private void ConfigureSectionPanels(Func<int, int> S)
        {
            foreach (var label in new[] { labelCpuLimitsTitle, labelPowerModeTitle, labelGpuOffsets, labelFans })
                label.ForeColor = foreMain;
            FixSectionTitle(panelCpuLimitsSectionModeTitle, picturePowerMode, labelPowerModeTitle, S);
            FixSectionTitle(panelCpuLimitsTitle, pictureBoxCPU, labelCpuLimitsTitle, S);
            FixSectionTitle(panelGpuOffsetsTitle, pictureGPU, labelGpuOffsets, S);
            var powerModeHost = new Panel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = formBack, Margin = new Padding(0), Padding = new Padding(0, 0, 0, S(6)) };
            panelCpuLimitsSection.Controls.Remove(panelCpuLimitsSectionModeTitle);
            panelCpuLimitsSection.Controls.Remove(panelCpuLimitsSectionMode);
            panelCpuLimitsSectionModeTitle.Visible = true;
            panelCpuLimitsSectionModeTitle.Dock = DockStyle.Top;
            panelCpuLimitsSectionModeTitle.Padding = new Padding(0, 0, 0, S(2));
            panelCpuLimitsSectionMode.Visible = true;
            panelCpuLimitsSectionMode.AutoSize = false;
            panelCpuLimitsSectionMode.Dock = DockStyle.Top;
            panelCpuLimitsSectionMode.Height = S(68);
            panelCpuLimitsSectionMode.Padding = new Padding(0, S(8), 0, S(20));
            comboWindowsPowerMode.Visible = true;
            comboWindowsPowerMode.Dock = DockStyle.Fill;
            comboWindowsPowerMode.Margin = Padding.Empty;
            comboWindowsPowerMode.BackColor = buttonMain;
            comboWindowsPowerMode.ForeColor = foreMain;
            comboWindowsPowerMode.BorderColor = formBack;
            comboWindowsPowerMode.ButtonColor = buttonMain;
            comboWindowsPowerMode.ArrowColor = foreMain;
            powerModeHost.Controls.Add(panelCpuLimitsSectionMode);
            powerModeHost.Controls.Add(panelCpuLimitsSectionModeTitle);
            panelMainControls.Controls.Add(powerModeHost);
            panelMainControls.Controls.SetChildIndex(powerModeHost, panelMainControls.Controls.Count - 1);
            if (_perfModeHost != null && _perfModeHost.Parent == panelMainControls)
                panelMainControls.Controls.SetChildIndex(_perfModeHost, Math.Max(0, panelMainControls.Controls.GetChildIndex(powerModeHost) - 1));
            BuildSliderRow(panelPl1, labelLeftPl1, "PL1 (W)", labelPl1, trackPl1, numPl1, S);
            BuildSliderRow(panelPl2, labelLeftPl2, "PL2 (W)", labelPl2, trackPl2, numPl2, S);
            panelApplyCpuLimits.AutoSize = true;
            panelApplyCpuLimits.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panelApplyCpuLimits.Dock = DockStyle.Top;
            panelApplyCpuLimits.Padding = new Padding(S(4), S(6), S(4), S(6));
            panelCpuLimitsSection.AutoSize = true;
            SetTopDockOrder(panelCpuLimitsSection, panelApplyCpuLimits, panelPl2, panelPl1, panelCpuLimitsTitle);
            BuildSliderRow(panelGpuOffsetsSectionCore, labelGpuCoreTitle, "Core Offset (MHz)", labelGpuCoreValue, trackGpuCoreOffset, numGpuCoreOffset, S);
            BuildSliderRow(panelGpuOffsetsSectionMemory, labelGpuMemoryTitle, "Memory Offset (MHz)", labelGpuMemoryValue, trackGpuMemoryOffset, numGpuMemoryOffset, S);
            panelGpuOffsetsSection.AutoSize = true;
            panelGpuOffsetsSection.Padding = new Padding(0, 0, 0, S(10));
            SetTopDockOrder(panelGpuOffsetsSection, panelGpuOffsetsSectionMemory, panelGpuOffsetsSectionCore, panelGpuOffsetsTitle);
        }

        private void BuildSliderRow(Panel panel, Label titleLabel, string titleText, Label hideLabel, RTrackBar track, NumericUpDown numeric, Func<int, int> S)
        {
            hideLabel.Visible = false;
            Control host = numeric.Parent ?? panel;
            track.Parent?.Controls.Remove(track);
            host.Parent?.Controls.Remove(host);
            titleLabel.Parent?.Controls.Remove(titleLabel);
            panel.Controls.Clear();
            panel.AutoSize = true;
            panel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panel.MinimumSize = Size.Empty;
            panel.MaximumSize = Size.Empty;
            panel.Dock = DockStyle.Top;
            panel.Margin = new Padding(0, 0, 0, S(8));
            panel.Padding = new Padding(0);
            var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 2, Margin = new Padding(0), Padding = new Padding(S(4), S(2), S(4), S(2)), BackColor = Color.Transparent };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            titleLabel.AutoSize = true; titleLabel.Visible = true; titleLabel.Text = titleText; titleLabel.ForeColor = foreMain; titleLabel.Font = new Font("Segoe UI", 9f, FontStyle.Bold); titleLabel.Anchor = AnchorStyles.Left; titleLabel.Margin = new Padding(0, S(4), 0, 0);
            host.AutoSize = true; if (host is FlowLayoutPanel flow) flow.AutoSizeMode = AutoSizeMode.GrowAndShrink; host.Dock = DockStyle.None; host.Anchor = AnchorStyles.Right; host.Margin = new Padding(S(8), 0, 0, 0);
            track.Dock = DockStyle.Fill; track.Visible = true; track.Margin = new Padding(0, S(4), 0, 0); track.MinimumSize = new Size(0, S(96)); track.Height = S(96); track.BackColor = formBack; StyleTrackTicks(track);
            table.Controls.Add(titleLabel, 0, 0); table.Controls.Add(host, 1, 0); table.Controls.Add(track, 0, 1); table.SetColumnSpan(track, 2);
            panel.Controls.Add(table);
        }

        private static void StyleTrackTicks(System.Windows.Forms.TrackBar track)
        {
            track.TickStyle = TickStyle.TopLeft;
            int range = track.Maximum - track.Minimum;
            if (range <= 0) return;
            int[] steps = { 1, 2, 5, 10, 20, 25, 50, 100, 200, 250, 500, 1000 };
            int freq = range;
            foreach (int step in steps)
            {
                if (range / step <= 20) { freq = step; break; }
            }
            track.TickFrequency = Math.Max(1, freq);
        }

        private void StyleSectionCheckbox(CheckBox box, Func<int, int> S)
        {
            box.AutoSize = true;
            box.Dock = DockStyle.Top;
            box.ForeColor = foreMain;
            box.BackColor = formBack;
            box.Padding = new Padding(S(4), S(6), 0, S(6));
            box.Margin = new Padding(0);
            if (box is RCheckBox) box.FlatAppearance.BorderColor = borderSecond;
        }

        private void StyleApplyButton(RButton button, Func<int, int> S)
        {
            button.Dock = DockStyle.Top;
            button.Secondary = true;
            button.BorderRadius = 2;
            button.BorderColor = Color.Transparent;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = borderSecond;
            button.BackColor = buttonSecond;
            button.ForeColor = foreMain;
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.Height = S(42);
            button.Margin = Padding.Empty;
            button.UseVisualStyleBackColor = false;
        }

        private static void SetTopDockOrder(Panel parent, params Control[] bottomToTop)
        {
            for (int i = 0; i < bottomToTop.Length; i++)
                if (bottomToTop[i] != null && bottomToTop[i].Parent == parent)
                    parent.Controls.SetChildIndex(bottomToTop[i], i);
        }

        private static void FixSectionTitle(Panel panel, PictureBox icon, Label title, Func<int, int> S)
        {
            panel.AutoSize = false;
            panel.Dock = DockStyle.Top;
            panel.Height = S(44);
            panel.Padding = new Padding(S(8), 0, S(8), 0);
            panel.BackColor = panel.Parent?.BackColor ?? formBack;
            icon.BackColor = panel.BackColor;
            title.BackColor = panel.BackColor;
            icon.Size = new Size(S(24), S(24));
            title.AutoSize = true;
            void center()
            {
                int h = panel.Height;
                icon.Location = new Point(S(8), Math.Max(0, (h - icon.Height) / 2));
                int titleX = icon.Right + S(10);
                title.MaximumSize = new Size(Math.Max(0, panel.ClientSize.Width - titleX - S(4)), 0);
                title.Location = new Point(titleX, Math.Max(0, (h - title.PreferredHeight) / 2));
            }
            center();
            panel.Resize += (_, _) => center();
        }
    }
}
