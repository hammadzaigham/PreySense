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
        private readonly UiDebouncer _hudScaleDebouncer = new(TimeSpan.FromMilliseconds(100));
        private readonly UiDebouncer _opacityDebouncer = new(TimeSpan.FromMilliseconds(100));

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
                _hudScaleDebouncer.Dispose();
                _opacityDebouncer.Dispose();
            };
        }

        private void OnOverlayEnabledChanged(bool enabled)
        {
            void Apply()
            {
                if (CheckGameOnly.IsChecked == true)
                    return;

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

        private static int GetRegInt(string keyName, int defaultValue)
        {
            try
            {
                return AppConfig.Get(keyName, defaultValue);
            }
            catch { }
            return defaultValue;
        }

        private static string GetRegString(string keyName, string defaultValue)
        {
            try
            {
                return AppConfig.GetString(keyName, defaultValue);
            }
            catch { }
            return defaultValue;
        }

        private static void SetRegInt(string keyName, int value)
        {
            try
            {
                AppConfig.Set(keyName, value);
            }
            catch { }
        }

        private static void SetRegString(string keyName, string value)
        {
            try
            {
                AppConfig.Set(keyName, value);
            }
            catch { }
        }

        private void LoadSettings()
        {
            bool isOverlayEnabled = AppConfig.IsOverlay();
            bool showInGamesOnly = GetRegInt("overlay_game_only", 0) == 1;
            bool showFps = GetRegInt("overlay_show_fps", 1) == 1;

            int rawScale = GetRegInt("overlay_scale_percent", 100);
            int scale = (int)Math.Round(rawScale / 25.0) * 25;
            scale = Math.Clamp(scale, 50, 200);

            int rawAlpha = GetRegInt("overlay_alpha", 50);
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
            CheckGameOnly.IsChecked = showInGamesOnly;
            CheckShowFps.IsChecked = showFps;
            ApplyEnableOverlayAvailability();
            SliderHudScale.Value = scale;
            SliderOpacity.Value = opacityPercent;

            LabelHudScale.Text = $"{scale}%";
            LabelOpacity.Text = $"{opacityPercent}%";

            DropdownHorizontalPos.ItemsSource = HorizontalPositions;
            DropdownVerticalPos.ItemsSource = VerticalPositions;

            string hPos = GetRegString("overlay_pos_horizontal", "Left");
            string vPos = GetRegString("overlay_pos_vertical", "Top");
            DropdownHorizontalPos.SelectedItem = hPos;
            DropdownVerticalPos.SelectedItem = vPos;
        }

        private void PositionDropdown_SelectionChanged(object? sender, SelectionChangedEventArgs? e)
        {
            if (_isInitializing) return;
            string h = DropdownHorizontalPos.SelectedItem as string ?? "Left";
            string v = DropdownVerticalPos.SelectedItem as string ?? "Top";
            SetRegString("overlay_pos_horizontal", h);
            SetRegString("overlay_pos_vertical", v);
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

                string currentVal = GetRegString(configKey, defaultVal);
                bool hasGraph = GetRegInt(graphKey, defaultGraph) == 1;

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
                    SetRegString(configKey, cpu + "|" + gpu);
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
                        SetRegInt(graphKey, 1);
                        HardwareOverlayManager.RefreshDisplayFlags();
                    }
                };
                checkGraph.Unchecked += (s, e) =>
                {
                    if (!_isInitializing)
                    {
                        SetRegInt(graphKey, 0);
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
            if (CheckGameOnly.IsChecked == true)
            {
                CheckEnableOverlay.IsChecked = false;
                return;
            }

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

        private void GameOnly_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            SetRegInt("overlay_game_only", CheckGameOnly.IsChecked == true ? 1 : 0);
            ApplyEnableOverlayAvailability();
            HardwareOverlayManager.RefreshDisplayFlags();
        }

        private void ApplyEnableOverlayAvailability()
        {
            bool gameOnly = CheckGameOnly.IsChecked == true;
            CheckEnableOverlay.IsEnabled = !gameOnly;
            if (!gameOnly)
                return;

            if (CheckEnableOverlay.IsChecked == true)
            {
                _isInitializing = true;
                CheckEnableOverlay.IsChecked = false;
                _isInitializing = false;
            }

            HardwareOverlayManager.Stop();
        }

        private void SettingChanged(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            bool showFps = CheckShowFps.IsChecked == true;
            SetRegInt("overlay_show_fps", showFps ? 1 : 0);
            HardwareOverlayManager.RefreshDisplayFlags();
        }

        private void SliderHudScale_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (LabelHudScale != null)
            {
                int val = (int)Math.Round(SliderHudScale.Value / 25.0) * 25;
                val = Math.Clamp(val, 50, 200);
                if (Math.Abs(SliderHudScale.Value - val) > 0.01)
                    SliderHudScale.Value = val;

                LabelHudScale.Text = $"{val}%";
                if (!_isInitializing)
                {
                    _hudScaleDebouncer.Debounce(SaveHudScale);
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
                    SliderOpacity.Value = val;

                LabelOpacity.Text = $"{val}%";
                if (!_isInitializing)
                {
                    _opacityDebouncer.Debounce(SaveOpacity);
                }
            }
        }

        private void SaveHudScale()
        {
            int value = (int)Math.Round(SliderHudScale.Value / 25.0) * 25;
            SetRegInt("overlay_scale_percent", value);
            HardwareOverlayManager.RefreshDisplayFlags();
        }

        private sealed class MetricChoice(string id, string label)
        {
            public string Id { get; } = id;
            public string Label { get; } = label;
            public override string ToString() => LocalizationService.Translate(Label);
        }

        private void SaveOpacity()
        {
            int value = (int)Math.Round(SliderOpacity.Value / 10.0) * 10;
            SetRegInt("overlay_alpha", value);
            HardwareOverlayManager.RefreshDisplayFlags();
        }

    }
}
