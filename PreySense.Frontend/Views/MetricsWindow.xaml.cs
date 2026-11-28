using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using PreySense.Core.Configuration;
using PreySense.Frontend.Controls;
using PreySense.Frontend.Services;
using PreySense.Overlay;

namespace PreySense.Frontend.Views
{
    public partial class MetricsWindow : Window
{
	private sealed class MetricChoice(string id, string label)
	{
		public string Id { get; } = id;

		public string Label { get; } = label;

		public override string ToString()
		{
			return LocalizationService.Translate(Label);
		}
	}

	private static readonly MetricChoice[] CpuChoices =
	{
		new MetricChoice("None", "None"),
		new MetricChoice("Usage", "CPU Usage (%)"),
		new MetricChoice("Temperature", "CPU Temperature (°C)"),
		new MetricChoice("Active Clock", "CPU Active Clock (MHz)"),
		new MetricChoice("Effective Clock", "CPU Effective Clock (MHz)"),
		new MetricChoice("Voltage", "CPU VID (V)"),
		new MetricChoice("Power", "CPU Power (W)"),
		new MetricChoice("Memory", "RAM Usage (GB)"),
		new MetricChoice("Memory Speed", "RAM Data Rate (MHz)"),
		new MetricChoice("Fan Speed", "CPU Fan (RPM)")
	};

	private static readonly MetricChoice[] GpuChoices =
	{
		new MetricChoice("None", "None"),
		new MetricChoice("Usage", "GPU Usage (%)"),
		new MetricChoice("Temperature", "GPU Temperature (°C)"),
		new MetricChoice("Hotspot", "GPU Hotspot (°C)"),
		new MetricChoice("Memory Temperature", "GPU Memory Junction (°C)"),
		new MetricChoice("Clock", "GPU Clock (MHz)"),
		new MetricChoice("Voltage", "GPU Voltage (V)"),
		new MetricChoice("Power", "GPU Power (W)"),
		new MetricChoice("Power Limit", "GPU Enforced Power Limit (W)"),
		new MetricChoice("PerfCap Reason", "GPU PerfCap Reason"),
		new MetricChoice("Memory", "VRAM Usage (GB)"),
		new MetricChoice("Memory Speed", "VRAM Data Rate (MHz)"),
		new MetricChoice("Fan Speed", "GPU Fan (RPM)")
	};

	private static readonly string[] HorizontalPositions = new string[3] { "Left", "Center", "Right" };

	private static readonly string[] VerticalPositions = new string[3] { "Top", "Center", "Bottom" };

	private bool _isInitializing = true;
    private List<OverlaySlotConfig>? _slots;
    private readonly LatestBackgroundWorker<Dictionary<string, Action>> _settingsWorker;

    private Task QueueSetting(string key, Action save) =>
        _settingsWorker.Submit(new() { [key] = save });

    private void QueueSlots(List<OverlaySlotConfig> slots)
    {
        var snapshot = slots.Select(slot => new OverlaySlotConfig
        { CpuMetric = slot.CpuMetric, GpuMetric = slot.GpuMetric, ShowGraph = slot.ShowGraph }).ToList();
        _ = QueueSetting("slots", () => AppConfig.SaveOverlaySlots(snapshot));
    }

	public MetricsWindow()
	{
        _settingsWorker = new(requests =>
        {
            foreach (var save in requests.Values) save();
            if (!Dispatcher.HasShutdownStarted)
                Dispatcher.BeginInvoke(new Action(HardwareOverlayManager.RefreshDisplayFlags));
        }, (pending, latest) => { foreach (var entry in latest) pending[entry.Key] = entry.Value; return pending; });
		InitializeComponent();
		MaxHeight = SystemParameters.WorkArea.Height - 40;
		LoadSettings();
		BuildColumnSlots();
		_isInitializing = false;
		IsVisibleChanged += (_, _) =>
		{
			if (!IsVisible) return;
			_isInitializing = true;
			try
			{
				LoadSettings();
				BuildColumnSlots();
			}
			finally { _isInitializing = false; }
		};
		OverlayBridge.EnabledChanged += OnOverlayEnabledChanged;
		Closed += async (object? sender, EventArgs args) =>
		{
			OverlayBridge.EnabledChanged -= OnOverlayEnabledChanged;
            await _settingsWorker.StopAsync(drainPending: true);
		};
	}

	private void OnOverlayEnabledChanged(bool enabled)
	{
		if (((DispatcherObject)this).Dispatcher.CheckAccess())
		{
			Apply();
		}
		else
		{
			((DispatcherObject)this).Dispatcher.BeginInvoke((Delegate)new Action(Apply), Array.Empty<object>());
		}
		void Apply()
		{
			if (CheckEnableOverlay.IsChecked != enabled)
			{
				_isInitializing = true;
				CheckEnableOverlay.IsChecked = enabled;
				_isInitializing = false;
			}
		}
	}

    private void LoadSettings()
    {
        var overlay = ConfigurationService.Instance.GetCurrent().Overlay;
        bool overlayEnabled = overlay.Enabled;
        bool showFps = overlay.ShowFps;
        int savedScalePercent = overlay.ScalePercent;
        int scalePercent = Math.Clamp((int)Math.Round(savedScalePercent / 5.0) * 5, 50, 200);
        int backgroundOpacityPercent = NormalizeSavedOpacity(overlay.BackgroundAlphaPercent);
        int textOpacityPercent = NormalizeSavedOpacity(overlay.TextAlphaPercent);
        _slots = overlay.Slots;

        DropdownOverlayTheme.SelectedIndex = (int)overlay.Theme;
        CheckEnableOverlay.IsChecked = overlayEnabled;
        CheckShowFps.IsChecked = showFps;
        SliderHudScale.Value = scalePercent;
        SliderOpacity.Value = backgroundOpacityPercent;
        SliderTextOpacity.Value = textOpacityPercent;
        LabelHudScale.Text = $"{scalePercent}%";
        LabelOpacity.Text = $"{backgroundOpacityPercent}%";
        LabelTextOpacity.Text = $"{textOpacityPercent}%";
        DropdownHorizontalPos.ItemsSource = HorizontalPositions;
        DropdownVerticalPos.ItemsSource = VerticalPositions;
        DropdownHorizontalPos.SelectedItem = string.IsNullOrWhiteSpace(overlay.PositionHorizontal) ? "Left" : overlay.PositionHorizontal;
        DropdownVerticalPos.SelectedItem = string.IsNullOrWhiteSpace(overlay.PositionVertical) ? "Top" : overlay.PositionVertical;
    }

    private async void OverlayTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing || DropdownOverlayTheme.SelectedIndex < 0) return;
        var theme = (OverlayTheme)DropdownOverlayTheme.SelectedIndex;
        // Re-selecting the same theme must not wipe hand-edited colors/opacity.
        if (theme == ConfigurationService.Instance.GetCurrent().Overlay.Theme) return;
        _isInitializing = true;
        IsEnabled = false;
        try
        {
            await _settingsWorker.WhenIdleAsync();
            await Task.Run(() => ConfigurationService.Instance.Update(config => config.Overlay.ApplyTheme(theme)));
            LoadSettings();
            HardwareOverlayManager.RefreshDisplayFlags();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Could not apply overlay theme: {ex}");
            LoadSettings();
            if (IsLoaded)
                new CustomModalDialog { Owner = this, HeaderSubtitle = LocalizationService.Translate("Overlay"),
                    Message = ex.Message, ConfirmText = LocalizationService.Translate("OK"), ShowCancelButton = false }.ShowDialog();
        }
        finally { _isInitializing = false; IsEnabled = true; }
    }

    // Older preferences store opacity as an alpha byte; current preferences use percent.
    private static int NormalizeSavedOpacity(int savedOpacity)
    {
        double percent = savedOpacity > 100 ? savedOpacity / 255.0 * 100 : savedOpacity;
        return Math.Clamp((int)Math.Round(percent / 10) * 10, 0, 100);
    }

    private void PositionDropdown_SelectionChanged(object? sender, SelectionChangedEventArgs? e)
    {
        if (_isInitializing) return;
        if (DropdownHorizontalPos.SelectedItem is not string horizontalPosition ||
            DropdownVerticalPos.SelectedItem is not string verticalPosition) return;

        _ = QueueSetting("position", () =>
        {
            ConfigurationService.Instance.Update(config =>
            {
                config.Overlay.PositionHorizontal = horizontalPosition;
                config.Overlay.PositionVertical = verticalPosition;
            });
            if (!Dispatcher.HasShutdownStarted)
                Dispatcher.BeginInvoke(new Action(() => HardwareOverlayManager.Reposition(horizontalPosition, verticalPosition, persist: false)));
        });
    }

	private void BuildColumnSlots()
	{
		PanelColumnsContainer.Children.Clear();
		double fontSize = (TryFindResource("FontSizeBody") as double?) ?? 12.0;
		double controlFontSize = (TryFindResource("FontSizeControl") as double?) ?? 10.0;
		List<OverlaySlotConfig> slots = _slots ??= AppConfig.LoadOverlaySlots();
		if (slots.Count == 0)
		{
			TextBlock textBlock = new TextBlock();
			textBlock.Text = LocalizationService.Translate("No overlay slots configured. Click 'Add Slot' to add metrics.");
			textBlock.FontStyle = FontStyles.Italic;
			textBlock.Foreground = (TryFindResource("TextMutedBrush") as Brush) ?? Brushes.Gray;
			textBlock.Margin = new Thickness(0.0, 8.0, 0.0, 8.0);
			textBlock.FontSize = fontSize;
			PanelColumnsContainer.Children.Add(textBlock);
			return;
		}
		for (int i = 0; i < slots.Count; i++)
		{
			int index = i;
			OverlaySlotConfig slot = slots[i];
			// Label | CPU | GPU | graph switch | remove
			Grid grid = new Grid
			{
				Margin = new Thickness(0.0, 3.0, 0.0, 3.0)
			};
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

			TextBlock label = new TextBlock
			{
				Text = LocalizationService.Format("Slot #{0}", index + 1),
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(0.0, 0.0, 8.0, 0.0),
				FontSize = fontSize
			};
			ComboBox cpuCombo = new ComboBox
			{
				ItemsSource = CpuChoices,
				SelectedItem = CpuChoices.FirstOrDefault(choice => choice.Id == slot.CpuMetric) ?? CpuChoices[0],
				Width = 180.0,
				Height = 28.0,
				Margin = new Thickness(0.0, 0.0, 6.0, 0.0),
				FontSize = controlFontSize
			};
			ComboBox gpuCombo = new ComboBox
			{
				ItemsSource = GpuChoices,
				SelectedItem = GpuChoices.FirstOrDefault(choice => choice.Id == slot.GpuMetric) ?? GpuChoices[0],
				Width = 180.0,
				Height = 28.0,
				Margin = new Thickness(0.0),
				FontSize = controlFontSize
			};
			cpuCombo.SelectionChanged += (_, _) => SaveSlotChanges();
			gpuCombo.SelectionChanged += (_, _) => SaveSlotChanges();

			RectangularSwitch graphSwitch = new RectangularSwitch
			{
				OnText = "│",
				OffText = "○",
				Width = 56.0,
				MinWidth = 56.0,
				MaxWidth = 56.0,
				Height = 28.0,
				Margin = new Thickness(8.0, 0.0, 6.0, 0.0),
				IsChecked = slot.ShowGraph,
				VerticalAlignment = VerticalAlignment.Center,
				HorizontalAlignment = HorizontalAlignment.Right,
				ClipToBounds = true
			};
            graphSwitch.Checked += (_, _) => SaveGraphVisibility();
            graphSwitch.Unchecked += (_, _) => SaveGraphVisibility();
            void SaveGraphVisibility()
            {
                if (_isInitializing) return;
                slot.ShowGraph = graphSwitch.IsChecked == true;
                QueueSlots(slots);
            }

			Button removeButton = new Button
			{
				Content = "–",
				Width = 28.0,
				Height = 28.0,
				Margin = new Thickness(0.0),
				Padding = new Thickness(0.0),
				FontSize = fontSize,
				FontWeight = FontWeights.Bold,
				VerticalAlignment = VerticalAlignment.Center,
				HorizontalAlignment = HorizontalAlignment.Right
			};
			removeButton.Click += (_, _) =>
			{
				slots.RemoveAt(index);
				QueueSlots(slots);
				BuildColumnSlots();
			};

			Grid.SetColumn(label, 0);
			Grid.SetColumn(cpuCombo, 1);
			Grid.SetColumn(gpuCombo, 2);
			Grid.SetColumn(graphSwitch, 3);
			Grid.SetColumn(removeButton, 4);
			grid.Children.Add(label);
			grid.Children.Add(cpuCombo);
			grid.Children.Add(gpuCombo);
			grid.Children.Add(graphSwitch);
			grid.Children.Add(removeButton);
			PanelColumnsContainer.Children.Add(grid);

			void SaveSlotChanges()
			{
				if (_isInitializing) return;
				slot.CpuMetric = (cpuCombo.SelectedItem as MetricChoice)?.Id ?? "None";
				slot.GpuMetric = (gpuCombo.SelectedItem as MetricChoice)?.Id ?? "None";
				QueueSlots(slots);
			}
		}
	}

	private void ButtonAddSlot_Click(object sender, RoutedEventArgs e)
	{
		List<OverlaySlotConfig> list = _slots ??= AppConfig.LoadOverlaySlots();
		list.Add(new OverlaySlotConfig
		{
			CpuMetric = "None",
			GpuMetric = "None",
			ShowGraph = false
		});
		QueueSlots(list);
		BuildColumnSlots();
	}

	private void OverlayToggle_Changed(object sender, RoutedEventArgs e)
	{
		if (!_isInitializing)
		{
			if (CheckEnableOverlay.IsChecked == true)
			{
				_ = QueueSetting("enabled", () => AppConfig.Set("overlay", 1));
			}
			else
			{
				_ = QueueSetting("enabled", () => AppConfig.Set("overlay", 0));
			}
		}
	}

	private void SettingChanged(object sender, RoutedEventArgs e)
	{
		if (!_isInitializing)
		{
			bool showFps = CheckShowFps.IsChecked == true;
			_ = QueueSetting("fps", () => AppConfig.Set("overlay_show_fps", showFps ? 1 : 0));
			}
	}

	private void SliderHudScale_DraftValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		if (LabelHudScale == null) return;
		int value = Math.Clamp((int)Math.Round(e.NewValue / 5.0) * 5, 50, 200);
		LabelHudScale.Text = $"{value}%";
	}

	private void SliderHudScale_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		if (LabelHudScale == null)
		{
			return;
		}
		int value = (int)Math.Round(SliderHudScale.Value / 5.0) * 5;
		value = Math.Clamp(value, 50, 200);
		if (Math.Abs(SliderHudScale.Value - (double)value) > 0.01)
		{
			SliderHudScale.Value = value;
			return;
		}
		LabelHudScale.Text = $"{value}%";
		if (!_isInitializing)
		{
			SaveHudScale(value);
		}
	}

	private void SliderOpacity_DraftValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		if (LabelOpacity == null) return;
		int value = Math.Clamp((int)Math.Round(e.NewValue / 10.0) * 10, 0, 100);
		LabelOpacity.Text = $"{value}%";
	}

	private void SliderOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		if (LabelOpacity == null)
		{
			return;
		}
		int value = (int)Math.Round(SliderOpacity.Value / 10.0) * 10;
		value = Math.Clamp(value, 0, 100);
		if (Math.Abs(SliderOpacity.Value - (double)value) > 0.01)
		{
			SliderOpacity.Value = value;
			return;
		}
		LabelOpacity.Text = $"{value}%";
		if (!_isInitializing)
		{
			SaveOpacity(value);
		}
	}

	private void SaveHudScale(int value)
	{
		_ = QueueSetting("scale", () => AppConfig.Set("overlay_scale_percent", value));
	}

	private void SliderTextOpacity_DraftValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		if (LabelTextOpacity == null) return;
		int value = Math.Clamp((int)Math.Round(e.NewValue / 10.0) * 10, 0, 100);
		LabelTextOpacity.Text = $"{value}%";
	}

	private void SliderTextOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		if (LabelTextOpacity == null)
		{
			return;
		}
		int value = (int)Math.Round(SliderTextOpacity.Value / 10.0) * 10;
		value = Math.Clamp(value, 0, 100);
		if (Math.Abs(SliderTextOpacity.Value - (double)value) > 0.01)
		{
			SliderTextOpacity.Value = value;
			return;
		}
		LabelTextOpacity.Text = $"{value}%";
		if (!_isInitializing)
		{
			SaveTextOpacity(value);
		}
	}

	private void SaveTextOpacity(int value)
	{
		_ = QueueSetting("textOpacity", () => AppConfig.Set("overlay_text_alpha", value));
	}

	private void SaveOpacity(int value)
	{
		_ = QueueSetting("opacity", () => AppConfig.Set("overlay_alpha", value));
	}
}
}
