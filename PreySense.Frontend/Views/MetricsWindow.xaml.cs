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

	public MetricsWindow()
	{
		InitializeComponent();
		LoadSettings();
		BuildColumnSlots();
		_isInitializing = false;
		OverlayBridge.EnabledChanged += OnOverlayEnabledChanged;
		Closed += (object? _, EventArgs _) =>
		{
			OverlayBridge.EnabledChanged -= OnOverlayEnabledChanged;
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
		bool value = AppConfig.IsOverlay();
		bool value2 = AppConfig.Get("overlay_show_fps", 1) == 1;
		int num = AppConfig.Get("overlay_scale_percent", 100);
		int value3 = (int)Math.Round((double)num / 5.0) * 5;
		value3 = Math.Clamp(value3, 50, 200);
		int num2 = AppConfig.Get("overlay_alpha", 50);
		int value4 = ((num2 <= 100) ? ((int)Math.Round((double)num2 / 10.0) * 10) : ((int)Math.Round((double)num2 / 255.0 * 10.0) * 10));
		value4 = Math.Clamp(value4, 0, 100);
		CheckEnableOverlay.IsChecked = value;
		CheckShowFps.IsChecked = value2;
		SliderHudScale.Value = value3;
		SliderOpacity.Value = value4;
		int num3 = AppConfig.Get("overlay_text_alpha", 100);
		int value5 = ((num3 > 100) ? ((int)Math.Round((double)num3 / 255.0 * 10.0) * 10) : ((int)Math.Round((double)num3 / 10.0) * 10));
		value5 = Math.Clamp(value5, 0, 100);
		if (SliderTextOpacity != null)
		{
			SliderTextOpacity.Value = value5;
		}
		LabelHudScale.Text = $"{value3}%";
		LabelOpacity.Text = $"{value4}%";
		if (LabelTextOpacity != null)
		{
			LabelTextOpacity.Text = $"{value5}%";
		}
		DropdownHorizontalPos.ItemsSource = HorizontalPositions;
		DropdownVerticalPos.ItemsSource = VerticalPositions;
		string selectedItem = AppConfig.GetString("overlay_pos_horizontal", "Left");
		string selectedItem2 = AppConfig.GetString("overlay_pos_vertical", "Top");
		DropdownHorizontalPos.SelectedItem = selectedItem;
		DropdownVerticalPos.SelectedItem = selectedItem2;
	}

	private void PositionDropdown_SelectionChanged(object? sender, SelectionChangedEventArgs? e)
	{
		if (!_isInitializing)
		{
			string text = (DropdownHorizontalPos.SelectedItem as string) ?? "Left";
			string text2 = (DropdownVerticalPos.SelectedItem as string) ?? "Top";
			AppConfig.Set("overlay_pos_horizontal", text);
			AppConfig.Set("overlay_pos_vertical", text2);
			HardwareOverlayManager.Reposition(text, text2);
		}
	}

	private void BuildColumnSlots()
	{
		PanelColumnsContainer.Children.Clear();
		double fontSize = (TryFindResource("FontSizeBody") as double?) ?? 12.0;
		double controlFontSize = (TryFindResource("FontSizeControl") as double?) ?? 10.0;
		List<OverlaySlotConfig> slots = AppConfig.LoadOverlaySlots();
		if (slots.Count == 0)
		{
			TextBlock textBlock = new TextBlock();
			textBlock.Text = LocalizationService.Translate("No overlay slots configured. Click '+ Add Slot' to add metrics.");
			textBlock.FontStyle = FontStyles.Italic;
			textBlock.Foreground = (TryFindResource("TextMutedBrush") as Brush) ?? Brushes.Gray;
			textBlock.Margin = new Thickness(0.0, 8.0, 0.0, 8.0);
			textBlock.FontSize = fontSize;
			TextBlock element = textBlock;
			PanelColumnsContainer.Children.Add(element);
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
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
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
				Width = 160.0,
				Height = 28.0,
				Margin = new Thickness(0.0, 0.0, 6.0, 0.0),
				FontSize = controlFontSize
			};
			ComboBox gpuCombo = new ComboBox
			{
				ItemsSource = GpuChoices,
				SelectedItem = GpuChoices.FirstOrDefault(choice => choice.Id == slot.GpuMetric) ?? GpuChoices[0],
				Width = 160.0,
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
			graphSwitch.Checked += (_, _) =>
			{
				if (_isInitializing) return;
				slot.ShowGraph = true;
				AppConfig.SaveOverlaySlots(slots);
				HardwareOverlayManager.RefreshDisplayFlags();
			};
			graphSwitch.Unchecked += (_, _) =>
			{
				if (_isInitializing) return;
				slot.ShowGraph = false;
				AppConfig.SaveOverlaySlots(slots);
				HardwareOverlayManager.RefreshDisplayFlags();
			};

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
				HorizontalAlignment = HorizontalAlignment.Right,
				ToolTip = "Remove slot"
			};
			removeButton.Click += (_, _) =>
			{
				slots.RemoveAt(index);
				AppConfig.SaveOverlaySlots(slots);
				BuildColumnSlots();
				HardwareOverlayManager.RefreshDisplayFlags();
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
				AppConfig.SaveOverlaySlots(slots);
				HardwareOverlayManager.RefreshDisplayFlags();
			}
		}
	}

	private void ButtonAddSlot_Click(object sender, RoutedEventArgs e)
	{
		List<OverlaySlotConfig> list = AppConfig.LoadOverlaySlots();
		list.Add(new OverlaySlotConfig
		{
			CpuMetric = "None",
			GpuMetric = "None",
			ShowGraph = false
		});
		AppConfig.SaveOverlaySlots(list);
		BuildColumnSlots();
		HardwareOverlayManager.RefreshDisplayFlags();
	}

	private void OverlayToggle_Changed(object sender, RoutedEventArgs e)
	{
		if (!_isInitializing)
		{
			if (CheckEnableOverlay.IsChecked == true)
			{
				HardwareOverlayManager.Start();
			}
			else
			{
				HardwareOverlayManager.Stop();
			}
		}
	}

	private void SettingChanged(object sender, RoutedEventArgs e)
	{
		if (!_isInitializing)
		{
			bool valueOrDefault = CheckShowFps.IsChecked == true;
			AppConfig.Set("overlay_show_fps", valueOrDefault ? 1 : 0);
			HardwareOverlayManager.RefreshDisplayFlags();
		}
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
		AppConfig.Set("overlay_scale_percent", value);
		HardwareOverlayManager.RefreshDisplayFlags();
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
