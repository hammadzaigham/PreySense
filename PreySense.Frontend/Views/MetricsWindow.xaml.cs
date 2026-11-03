using System;
using System.Windows;
using System.Windows.Controls;
using PreySense.Frontend.Services;
using PreySense.Overlay;

namespace PreySense.Frontend.Views
{
    public partial class MetricsWindow : Window
    {
        private static readonly MetricChoice[] CpuChoices =
        {
            new("None", "None"),
            new("Usage", "CPU Usage"),
            new("Max Core", "CPU Max Core Usage"),
            new("P-Core Usage", "CPU P-Core Usage"),
            new("E-Core Usage", "CPU E-Core Usage"),
            new("Temperature", "CPU Temperature"),
            new("P-Core", "CPU P-Core"),
            new("E-Core", "CPU E-Core"),
            new("Voltage", "CPU Voltage"),
            new("Power", "CPU Power"),
            new("Memory", "CPU Memory"),
            new("Memory Speed", "CPU Memory Speed"),
            new("Fan Speed", "CPU Fan")
        };

        private static readonly MetricChoice[] GpuChoices =
        {
            new("None", "None"),
            new("Usage", "GPU Usage"),
            new("Temperature", "GPU Temperature"),
            new("Clock", "GPU Clock"),
            new("Voltage", "GPU Voltage"),
            new("Power", "GPU Power"),
            new("Memory", "GPU Memory"),
            new("Memory Speed", "GPU Memory Speed"),
            new("Fan Speed", "GPU Fan")
        };
        private static readonly string[] HorizontalPositions = { "Left", "Center", "Right" };
        private static readonly string[] VerticalPositions = { "Top", "Center", "Bottom" };

        private bool _isInitializing = true;

        public MetricsWindow()
        {
            InitializeComponent();
            LoadSettings();
            BuildColumnSlots();
            _isInitializing = false;
            OverlayBridge.EnabledChanged += OnOverlayEnabledChanged;
            Closed += (_, _) =>
            {
                OverlayBridge.EnabledChanged -= OnOverlayEnabledChanged;
            };
        }

        private void OnOverlayEnabledChanged(bool enabled)
        {
            void Apply()
            {
                if (CheckEnableOverlay.IsChecked == enabled)
                    return;

                _isInitializing = true;
                CheckEnableOverlay.IsChecked = enabled;
                _isInitializing = false;
            }

            if (Dispatcher.CheckAccess())
                Apply();
            else
                _ = Dispatcher.BeginInvoke(Apply);
        }

        private void LoadSettings()
        {
            bool isOverlayEnabled = AppConfig.IsOverlay();
            bool showFps = AppConfig.Get("overlay_show_fps", 1) == 1;

            int rawScale = AppConfig.Get("overlay_scale_percent", 100);
            int scale = (int)Math.Round(rawScale / 5.0) * 5;
            scale = Math.Clamp(scale, 50, 200);

            int rawAlpha = AppConfig.Get("overlay_alpha", 50);
            int opacityPercent;
            if (rawAlpha > 100)
            {
                opacityPercent = (int)Math.Round((rawAlpha / 255.0) * 10.0) * 10;
            }
            else
            {
                opacityPercent = (int)Math.Round(rawAlpha / 10.0) * 10;
            }
            opacityPercent = Math.Clamp(opacityPercent, 0, 100);

            CheckEnableOverlay.IsChecked = isOverlayEnabled;
            CheckShowFps.IsChecked = showFps;
            SliderHudScale.Value = scale;
            SliderOpacity.Value = opacityPercent;

            int rawTextAlpha = AppConfig.Get("overlay_text_alpha", 100);
            int textOpacityPercent = rawTextAlpha > 100
                ? (int)Math.Round((rawTextAlpha / 255.0) * 10.0) * 10
                : (int)Math.Round(rawTextAlpha / 10.0) * 10;
            textOpacityPercent = Math.Clamp(textOpacityPercent, 0, 100);
            if (SliderTextOpacity != null)
                SliderTextOpacity.Value = textOpacityPercent;

            LabelHudScale.Text = $"{scale}%";
            LabelOpacity.Text = $"{opacityPercent}%";
            if (LabelTextOpacity != null)
                LabelTextOpacity.Text = $"{textOpacityPercent}%";

            DropdownHorizontalPos.ItemsSource = HorizontalPositions;
            DropdownVerticalPos.ItemsSource = VerticalPositions;

            string hPos = AppConfig.GetString("overlay_pos_horizontal", "Left");
            string vPos = AppConfig.GetString("overlay_pos_vertical", "Top");
            DropdownHorizontalPos.SelectedItem = hPos;
            DropdownVerticalPos.SelectedItem = vPos;
        }

        private void PositionDropdown_SelectionChanged(object? sender, SelectionChangedEventArgs? e)
        {
            if (_isInitializing) return;
            string h = DropdownHorizontalPos.SelectedItem as string ?? "Left";
            string v = DropdownVerticalPos.SelectedItem as string ?? "Top";
            AppConfig.Set("overlay_pos_horizontal", h);
            AppConfig.Set("overlay_pos_vertical", v);
            HardwareOverlayManager.Reposition(h, v);
        }

        private void BuildColumnSlots()
        {
            AppConfig.EnsureDefaultOverlayColumns();
            PanelColumnsContainer.Children.Clear();
            double bodyFontSize = TryFindResource("FontSizeBody") as double? ?? 11d;

            for (int i = 0; i < 8; i++)
            {
                int colNum = i + 1;
                string configKey = $"overlay_col_{colNum}";
                string graphKey = $"overlay_col_{colNum}_graph";

                string defaultVal = AppConfig.DefaultOverlayColumn(colNum);
                int defaultGraph = (i == 4) ? 1 : 0;

                string currentVal = AppConfig.GetString(configKey, defaultVal);
                bool hasGraph = AppConfig.Get(graphKey, defaultGraph) == 1;

                var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var label = new TextBlock
                {
                    Text = $"Slot #{colNum}",
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = bodyFontSize
                };

                var pair = AppConfig.ParseOverlayColumn(currentVal);
                var cpuCombo = new ComboBox
                {
                    ItemsSource = CpuChoices,
                    SelectedItem = CpuChoices.FirstOrDefault(choice => choice.Id == pair.Cpu) ?? CpuChoices[0],
                    Width = 168,
                    Height = 28,
                    Margin = new Thickness(0, 0, 4, 0),
                    FontSize = bodyFontSize
                };
                var gpuCombo = new ComboBox
                {
                    ItemsSource = GpuChoices,
                    SelectedItem = GpuChoices.FirstOrDefault(choice => choice.Id == pair.Gpu) ?? GpuChoices[0],
                    Width = 168,
                    Height = 28,
                    Margin = new Thickness(0),
                    FontSize = bodyFontSize
                };
                var dropdowns = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                };
                dropdowns.Children.Add(cpuCombo);
                dropdowns.Children.Add(gpuCombo);
                void SaveColumn()
                {
                    if (_isInitializing)
                        return;
                    string cpu = (cpuCombo.SelectedItem as MetricChoice)?.Id ?? "None";
                    string gpu = (gpuCombo.SelectedItem as MetricChoice)?.Id ?? "None";
                    AppConfig.Set(configKey, cpu + "|" + gpu);
                    HardwareOverlayManager.RefreshDisplayFlags();
                }
                cpuCombo.SelectionChanged += (_, _) => SaveColumn();
                gpuCombo.SelectionChanged += (_, _) => SaveColumn();

                var checkGraph = new Controls.RectangularSwitch
                {
                    OnText = "│",
                    OffText = "○",
                    Width = 56,
                    Height = 28,
                    Margin = new Thickness(8, 0, 0, 0),
                    IsChecked = hasGraph,
                    VerticalAlignment = VerticalAlignment.Center
                };
                checkGraph.Checked += (s, e) =>
                {
                    if (!_isInitializing)
                    {
                        AppConfig.Set(graphKey, 1);
                        HardwareOverlayManager.RefreshDisplayFlags();
                    }
                };
                checkGraph.Unchecked += (s, e) =>
                {
                    if (!_isInitializing)
                    {
                        AppConfig.Set(graphKey, 0);
                        HardwareOverlayManager.RefreshDisplayFlags();
                    }
                };
                dropdowns.Children.Add(checkGraph);

                Grid.SetColumn(label, 0);
                Grid.SetColumn(dropdowns, 2);

                row.Children.Add(label);
                row.Children.Add(dropdowns);

                PanelColumnsContainer.Children.Add(row);
            }
        }

        private void OverlayToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            bool enabled = CheckEnableOverlay.IsChecked == true;
            if (enabled)
            {
                HardwareOverlayManager.Start();
            }
            else
            {
                HardwareOverlayManager.Stop();
            }
        }

        private void SettingChanged(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            bool showFps = CheckShowFps.IsChecked == true;
            AppConfig.Set("overlay_show_fps", showFps ? 1 : 0);
            HardwareOverlayManager.RefreshDisplayFlags();
        }

        private void SliderHudScale_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (LabelHudScale != null)
            {
                int val = (int)Math.Round(SliderHudScale.Value / 5.0) * 5;
                val = Math.Clamp(val, 50, 200);
                if (Math.Abs(SliderHudScale.Value - val) > 0.01)
                {
                    SliderHudScale.Value = val;
                    return;
                }

                LabelHudScale.Text = $"{val}%";
                if (!_isInitializing)
                {
                    SaveHudScale(val);
                }
            }
        }

        private void SliderOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (LabelOpacity != null)
            {
                int val = (int)Math.Round(SliderOpacity.Value / 10.0) * 10;
                val = Math.Clamp(val, 0, 100);
                if (Math.Abs(SliderOpacity.Value - val) > 0.01)
                {
                    SliderOpacity.Value = val;
                    return;
                }

                LabelOpacity.Text = $"{val}%";
                if (!_isInitializing)
                {
                    SaveOpacity(val);
                }
            }
        }

        private void SaveHudScale(int value)
        {
            AppConfig.Set("overlay_scale_percent", value);
            HardwareOverlayManager.RefreshDisplayFlags();
        }

        private sealed class MetricChoice(string id, string label)
        {
            public string Id { get; } = id;
            public string Label { get; } = label;
            public override string ToString() => LocalizationService.Translate(Label);
        }

                private void SliderTextOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (LabelTextOpacity != null)
            {
                int val = (int)Math.Round(SliderTextOpacity.Value / 10.0) * 10;
                val = Math.Clamp(val, 0, 100);
                if (Math.Abs(SliderTextOpacity.Value - val) > 0.01)
                {
                    SliderTextOpacity.Value = val;
                    return;
                }

                LabelTextOpacity.Text = $"{val}%";
                if (!_isInitializing)
                {
                    SaveTextOpacity(val);
                }
            }
        }

        private void SaveTextOpacity(int value)
        {
            AppConfig.Set("overlay_text_alpha", value);
            HardwareOverlayManager.RefreshDisplayFlags();
        }

        private void SaveOpacity(int value)
        {
            AppConfig.Set("overlay_alpha", value);
            HardwareOverlayManager.RefreshDisplayFlags();
        }

    }
}
