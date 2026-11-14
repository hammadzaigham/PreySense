using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using PreySense.Frontend.ViewModels;

namespace PreySense.Frontend.Views
{
    public partial class KeyboardWindow : Window
    {
        private bool _isUpdatingHexText = false;
        private bool _isUpdatingColorWheel = false;
        private bool _isUpdatingCoverColorWheel = false;
        private bool _isLoaded = false;
        private string _pendingLightingEffectTag = "HW:0";
        private MainViewModel? _boundVm;

        public KeyboardWindow()
        {
            InitializeComponent();
            MaxHeight = Math.Max(400, SystemParameters.WorkArea.Height - 32);
            Loaded += KeyboardWindow_Loaded;
            Closed += KeyboardWindow_Closed;
        }

        private async void KeyboardWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                BindLightingViewModel(vm);

                // Tab Selection
                if (TabLightingTarget != null)
                {
                    TabLightingTarget.SelectedIndex = vm.IsCoverTabSelected ? 1 : 0;
                }

                // Initialize Keyboard Color Wheel & Hex Input
                _isUpdatingColorWheel = true;
                _isUpdatingHexText = true;
                try
                {
                    var drawingColor = System.Drawing.ColorTranslator.FromHtml(vm.SelectedHexColor);
                    ColorWheel.SelectedColor = System.Windows.Media.Color.FromRgb(drawingColor.R, drawingColor.G, drawingColor.B);
                    if (TextHexColorInput != null) TextHexColorInput.Text = vm.SelectedHexColor;
                }
                catch { }
                finally
                {
                    _isUpdatingHexText = false;
                    _isUpdatingColorWheel = false;
                }

                // Cover Mode ComboBox Selection
                switch (vm.CoverLightingMode)
                {
                    case 4: ComboCoverMode.SelectedIndex = 0; break; // Mode
                    case 2: ComboCoverMode.SelectedIndex = 2; break; // Breathing
                    case 3: ComboCoverMode.SelectedIndex = 3; break; // Neon
                    default: ComboCoverMode.SelectedIndex = 1; break; // Static
                }

                UpdateCoverModePanels(vm.CoverLightingMode);

                // Initialize Cover Color Wheel & Hex Input
                _isUpdatingCoverColorWheel = true;
                _isUpdatingHexText = true;
                try
                {
                    var drawingColor = System.Drawing.ColorTranslator.FromHtml(vm.CoverHexColor);
                    CoverColorWheel.SelectedColor = System.Windows.Media.Color.FromRgb(drawingColor.R, drawingColor.G, drawingColor.B);
                    if (TextCoverHexColorInput != null) TextCoverHexColorInput.Text = vm.CoverHexColor;
                }
                catch { }
                finally
                {
                    _isUpdatingHexText = false;
                    _isUpdatingCoverColorWheel = false;
                }

                SyncCoverPresetSelection(vm);
                RestoreLightingModeUi(vm);
                _isLoaded = true;
                PreySense.Core.Hardware.HidArrivalHub.Arrived += OnLightingDevicesChanged;
                await vm.DetectLightingDevicesAsync();
                UpdateLightingWindowWidth(vm);
            }
        }

        private void OnLightingDevicesChanged()
        {
            _ = Dispatcher.InvokeAsync(async () =>
            {
                if (IsVisible && DataContext is MainViewModel vm) await vm.DetectLightingDevicesAsync();
            });
        }

        private void DeviceKeyColor_Picked(object? sender, System.Windows.Media.Color color)
        {
            if (DataContext is MainViewModel vm)
                vm.SetSelectedLightingKeysColor($"#{color.R:X2}{color.G:X2}{color.B:X2}");
        }

        private void UpdateLightingWindowWidth(MainViewModel vm)
        {
            Width = vm.ShowKeyboardLayout ? Math.Min(1040, Math.Max(500, SystemParameters.WorkArea.Width - 48)) : 500;
            MaxHeight = Math.Max(400, SystemParameters.WorkArea.Height - 48);
        }

        private void RestoreLightingModeUi(MainViewModel vm)
        {
            string tag = vm.LightingEffectTag;
            bool isMode = tag.Equals("MODE", StringComparison.OrdinalIgnoreCase) || tag.Equals("HW:Mode", StringComparison.OrdinalIgnoreCase);
            bool isSoftwareEffect = tag.StartsWith("DYN:", StringComparison.Ordinal);
            bool isHardwareDyn = tag.StartsWith("HW:", StringComparison.Ordinal) && tag != "HW:0" && !isMode;

            int modeIndex = isMode ? 0 : (isSoftwareEffect ? 3 : (isHardwareDyn ? 2 : 1));
            if (ComboLightingMode != null)
                ComboLightingMode.SelectedIndex = modeIndex;

            if (PanelStaticControls != null)
                PanelStaticControls.Visibility = modeIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            if (PanelDynamicControls != null)
                PanelDynamicControls.Visibility = modeIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
            if (PanelSoftwareControls != null)
                PanelSoftwareControls.Visibility = modeIndex == 3 ? Visibility.Visible : Visibility.Collapsed;

            // Always sync static presets so "Custom" (or the active preset) is selected
            SyncStaticPresetSelection(vm);

            // Sync dynamic hardware selection
            if (ComboDynamicSelect != null)
            {
                int defaultDynIndex = 0;
                for (int i = 0; i < ComboDynamicSelect.Items.Count; i++)
                {
                    if (ComboDynamicSelect.Items[i] is ComboBoxItem item &&
                        item.Tag is string itemTag &&
                        string.Equals(itemTag, tag, StringComparison.Ordinal))
                    {
                        defaultDynIndex = i;
                        break;
                    }
                }
                ComboDynamicSelect.SelectedIndex = defaultDynIndex;
            }

            // Sync software effect selection
            if (ComboSoftwareSelect != null)
            {
                int defaultAnimIndex = 0;
                for (int i = 0; i < ComboSoftwareSelect.Items.Count; i++)
                {
                    if (ComboSoftwareSelect.Items[i] is ComboBoxItem item &&
                        item.Tag is string itemTag &&
                        string.Equals(itemTag, tag, StringComparison.Ordinal))
                    {
                        defaultAnimIndex = i;
                        break;
                    }
                }
                ComboSoftwareSelect.SelectedIndex = defaultAnimIndex;
            }

            _pendingLightingEffectTag = tag;
        }

        private void SyncStaticPresetSelection(MainViewModel vm)
        {
            if (ComboPresetStatic == null) return;

            int targetIndex = 1; // Default to "Predator" (index 1 after Custom)
            string preset = vm.SelectedZonePreset;
            if (!string.IsNullOrWhiteSpace(preset) &&
                !string.Equals(preset, "None", StringComparison.OrdinalIgnoreCase))
            {
                for (int i = 0; i < ComboPresetStatic.Items.Count; i++)
                {
                    if (ComboPresetStatic.Items[i] is ComboBoxItem presetItem &&
                        presetItem.Tag is string presetTag &&
                        string.Equals(presetTag, preset, StringComparison.OrdinalIgnoreCase))
                    {
                        targetIndex = i;
                        break;
                    }
                }
            }

            if (ComboPresetStatic.SelectedIndex != targetIndex)
            {
                bool wasLoaded = _isLoaded;
                _isLoaded = false;
                try
                {
                    ComboPresetStatic.SelectedIndex = targetIndex;
                }
                finally
                {
                    _isLoaded = wasLoaded;
                }
            }
        }

        private void CoverColorWheel_ColorChanged(object? sender, System.Windows.Media.Color color)
        {
            if (!_isLoaded || _isUpdatingCoverColorWheel || DataContext is not MainViewModel vm)
                return;

            string hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            vm.CoverHexColor = hex;
            if (ComboCoverPresets != null && ComboCoverPresets.SelectedIndex != 0)
                ComboCoverPresets.SelectedIndex = 0; // Custom
            if (!_isUpdatingHexText && TextCoverHexColorInput != null)
            {
                _isUpdatingHexText = true;
                TextCoverHexColorInput.Text = hex;
                _isUpdatingHexText = false;
            }
        }

        private void TextCoverHexColorInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isLoaded || _isUpdatingHexText || DataContext is not MainViewModel vm) return;
            string text = TextCoverHexColorInput.Text?.Trim() ?? "";
            if (!text.StartsWith("#")) text = "#" + text;
            if (text.Length == 7)
            {
                try
                {
                    var col = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(text);
                    if (string.Equals(text, vm.CoverHexColor, StringComparison.OrdinalIgnoreCase))
                        return;

                    vm.CoverHexColor = text;
                    if (ComboCoverPresets != null && ComboCoverPresets.SelectedIndex != 0)
                        ComboCoverPresets.SelectedIndex = 0; // Custom
                    _isUpdatingHexText = true;
                    _isUpdatingCoverColorWheel = true;
                    CoverColorWheel.SelectedColor = col;
                    _isUpdatingCoverColorWheel = false;
                    _isUpdatingHexText = false;
                }
                catch
                {
                    _isUpdatingCoverColorWheel = false;
                    _isUpdatingHexText = false;
                }
            }
        }

        private void ComboCoverMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            if (ComboCoverMode?.SelectedItem is ComboBoxItem item && item.Tag is string tag && DataContext is MainViewModel vm)
            {
                vm.SetCoverModeCommand.Execute(tag);
                if (int.TryParse(tag, out int mode))
                {
                    UpdateCoverModePanels(mode);
                }
            }
        }

        private void SyncCoverPresetSelection(MainViewModel vm)
        {
            if (ComboCoverPresets == null) return;

            // Default logo theme is Predator (index 1 after Custom).
            int targetIndex = 1;
            string hex = (vm.CoverHexColor ?? string.Empty).Trim();
            for (int i = 1; i < ComboCoverPresets.Items.Count; i++)
            {
                if (ComboCoverPresets.Items[i] is not ComboBoxItem { Tag: string tag } ||
                    string.Equals(tag, "Custom", StringComparison.OrdinalIgnoreCase))
                    continue;

                string presetHex = tag switch
                {
                    "Predator" => "#00AEC7",
                    "Fire" => "#FF0033",
                    "Amber" => "#F59E0B",
                    "Neon" => "#FF007F",
                    "Matrix" => "#00FF66",
                    "White" => "#FFFFFF",
                    "Purple" => "#8B00FF",
                    "Sunset" => "#FF6B00",
                    "Ocean" => "#0077B6",
                    "Sakura" => "#FF8FAB",
                    "Toxic" => "#39FF14",
                    "Lava" => "#FF4500",
                    "Midnight" => "#3A0CA3",
                    "Ice" => "#48CAE4",
                    "Vapor" => "#FF71CE",
                    _ => string.Empty
                };
                if (!string.IsNullOrEmpty(presetHex) &&
                    string.Equals(hex, presetHex, StringComparison.OrdinalIgnoreCase))
                {
                    targetIndex = i;
                    break;
                }
                if (i == 1 && string.IsNullOrEmpty(hex))
                    targetIndex = 1;
            }

            if (ComboCoverPresets.SelectedIndex != targetIndex)
                ComboCoverPresets.SelectedIndex = targetIndex;
        }

        private void ComboCoverPresets_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            if (ComboCoverPresets?.SelectedItem is ComboBoxItem item && item.Tag is string preset &&
                !string.Equals(preset, "Custom", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(preset, "None", StringComparison.OrdinalIgnoreCase) &&
                DataContext is MainViewModel vm)
            {
                _isUpdatingCoverColorWheel = true;
                _isUpdatingHexText = true;
                try
                {
                    vm.ApplyCoverPresetCommand.Execute(preset);
                    var drawingColor = System.Drawing.ColorTranslator.FromHtml(vm.CoverHexColor);
                    CoverColorWheel.SelectedColor = System.Windows.Media.Color.FromRgb(drawingColor.R, drawingColor.G, drawingColor.B);
                    if (TextCoverHexColorInput != null) TextCoverHexColorInput.Text = vm.CoverHexColor;
                }
                finally
                {
                    _isUpdatingHexText = false;
                    _isUpdatingCoverColorWheel = false;
                }
            }
        }

        private void BindLightingViewModel(MainViewModel vm)
        {
            if (_boundVm != null)
                _boundVm.PropertyChanged -= OnLightingVmPropertyChanged;
            _boundVm = vm;
            _boundVm.PropertyChanged += OnLightingVmPropertyChanged;
        }

        private void KeyboardWindow_Closed(object? sender, EventArgs e)
        {
            PreySense.Core.Hardware.HidArrivalHub.Arrived -= OnLightingDevicesChanged;
            if (_boundVm == null)
                return;
            _boundVm.PropertyChanged -= OnLightingVmPropertyChanged;
            _boundVm = null;
        }

        private void OnLightingVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedHexColor) && DataContext is MainViewModel colorVm)
                SyncKeyboardColorInputs(colorVm);
            if (e.PropertyName == nameof(MainViewModel.ShowKeyboardLayout) && DataContext is MainViewModel lightingVm)
                UpdateLightingWindowWidth(lightingVm);
            if ((e.PropertyName == nameof(MainViewModel.IsCoverSyncWithKeyboard) ||
                 e.PropertyName == nameof(MainViewModel.CoverLightingMode)) &&
                DataContext is MainViewModel vm)
            {
                UpdateCoverModePanels(vm.CoverLightingMode);
            }
            else if (e.PropertyName == nameof(MainViewModel.SelectedZonePreset) &&
                     DataContext is MainViewModel mainVm)
            {
                SyncStaticPresetSelection(mainVm);
            }
        }

        private void UpdateCoverModePanels(int coverMode)
        {
            bool isStatic = coverMode == 1;
            bool isMode = coverMode == 4;
            bool sync = DataContext is MainViewModel vm && vm.IsCoverSyncWithKeyboard;
            if (PanelCoverColorWheel != null)
            {
                PanelCoverColorWheel.Visibility = (isStatic && !sync && !isMode)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            if (PanelCoverSpeed != null)
            {
                PanelCoverSpeed.Visibility = (!isStatic && !sync && !isMode)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        private void SyncKeyboardColorInputs(MainViewModel vm)
        {
            _isUpdatingColorWheel = true;
            _isUpdatingHexText = true;
            try
            {
                var drawingColor = System.Drawing.ColorTranslator.FromHtml(vm.SelectedHexColor);
                ColorWheel.SelectedColor = System.Windows.Media.Color.FromRgb(drawingColor.R, drawingColor.G, drawingColor.B);
                if (TextHexColorInput != null)
                    TextHexColorInput.Text = vm.SelectedHexColor;
            }
            catch { }
            finally
            {
                _isUpdatingHexText = false;
                _isUpdatingColorWheel = false;
            }
        }

        private void ComboLightingMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;

            int selected = ComboLightingMode?.SelectedIndex ?? 0;
            if (PanelStaticControls != null)
                PanelStaticControls.Visibility = selected == 1 ? Visibility.Visible : Visibility.Collapsed;
            if (PanelDynamicControls != null)
                PanelDynamicControls.Visibility = selected == 2 ? Visibility.Visible : Visibility.Collapsed;
            if (PanelSoftwareControls != null)
                PanelSoftwareControls.Visibility = selected == 3 ? Visibility.Visible : Visibility.Collapsed;

            if (selected == 0) // Mode
            {
                _pendingLightingEffectTag = "MODE";
                if (DataContext is MainViewModel modeVm)
                {
                    modeVm.SetLightingEffect(_pendingLightingEffectTag);
                    modeVm.UpdateKeyboardForMode(modeVm.ActivePowerMode);
                }
            }
            else if (selected == 1) // Static
            {
                _pendingLightingEffectTag = "HW:0";
                if (DataContext is MainViewModel staticVm)
                {
                    staticVm.SetLightingEffect(_pendingLightingEffectTag);
                    SyncStaticPresetSelection(staticVm);
                }
            }
            else if (selected == 2) // Dynamic (Hardware)
            {
                if (ComboDynamicSelect != null)
                {
                    if (ComboDynamicSelect.SelectedIndex < 0)
                        ComboDynamicSelect.SelectedIndex = 0;

                    if (ComboDynamicSelect.SelectedItem is ComboBoxItem item && item.Tag is string tag)
                    {
                        _pendingLightingEffectTag = tag;
                        if (DataContext is MainViewModel dynVm)
                            dynVm.SetLightingEffect(tag);
                    }
                }
            }
            else if (selected == 3) // Software
            {
                if (ComboSoftwareSelect != null)
                {
                    if (ComboSoftwareSelect.SelectedIndex < 0)
                        ComboSoftwareSelect.SelectedIndex = 0;

                    if (ComboSoftwareSelect.SelectedItem is ComboBoxItem item && item.Tag is string tag)
                    {
                        _pendingLightingEffectTag = tag;
                        if (DataContext is MainViewModel animVm)
                            animVm.SetLightingEffect(tag);
                    }
                }
            }
        }

        private void ComboDynamicSelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            if (ComboDynamicSelect?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                _pendingLightingEffectTag = tag;
                if (DataContext is MainViewModel vm)
                    vm.SetLightingEffect(tag);
            }
        }

        private void ComboSoftwareSelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            if (ComboSoftwareSelect?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                _pendingLightingEffectTag = tag;
                if (DataContext is MainViewModel vm)
                    vm.SetLightingEffect(tag);
            }
        }

        private void ColorWheel_ColorChanged(object? sender, System.Windows.Media.Color color)
        {
            if (!_isLoaded || _isUpdatingColorWheel || DataContext is not MainViewModel vm)
                return;

            string hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            if (string.Equals(hex, vm.SelectedHexColor, StringComparison.OrdinalIgnoreCase))
                return;

            vm.DiscardPendingZonePreset();
            vm.SelectedHexColor = hex;
            if (!_isUpdatingHexText && TextHexColorInput != null)
            {
                _isUpdatingHexText = true;
                TextHexColorInput.Text = hex;
                _isUpdatingHexText = false;
            }
        }

        private void TextHexColorInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isLoaded || _isUpdatingHexText || DataContext is not MainViewModel vm) return;
            string text = TextHexColorInput.Text?.Trim() ?? "";
            if (!text.StartsWith("#")) text = "#" + text;
            if (text.Length == 7)
            {
                try
                {
                    var col = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(text);
                    if (string.Equals(text, vm.SelectedHexColor, StringComparison.OrdinalIgnoreCase))
                        return;

                    vm.DiscardPendingZonePreset();
                    vm.SelectedHexColor = text;
                    _isUpdatingHexText = true;
                    _isUpdatingColorWheel = true;
                    ColorWheel.SelectedColor = col;
                    _isUpdatingColorWheel = false;
                    _isUpdatingHexText = false;
                }
                catch
                {
                    _isUpdatingColorWheel = false;
                    _isUpdatingHexText = false;
                }
            }
        }

        private void ComboPresetStatic_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            if (ComboPresetStatic?.SelectedItem is ComboBoxItem item && item.Tag is string preset && DataContext is MainViewModel vm)
            {
                if (string.Equals(preset, "Custom", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(preset, "None", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.Equals(vm.SelectedZonePreset, "Custom", StringComparison.OrdinalIgnoreCase))
                        vm.DiscardPendingZonePreset();
                    return;
                }

                _isUpdatingColorWheel = true;
                _isUpdatingHexText = true;
                try
                {
                    vm.ApplyZonePresetCommand.Execute(preset);
                    var drawingColor = System.Drawing.ColorTranslator.FromHtml(vm.SelectedHexColor);
                    ColorWheel.SelectedColor = System.Windows.Media.Color.FromRgb(drawingColor.R, drawingColor.G, drawingColor.B);
                    if (TextHexColorInput != null) TextHexColorInput.Text = vm.SelectedHexColor;
                }
                finally
                {
                    _isUpdatingHexText = false;
                    _isUpdatingColorWheel = false;
                }
            }
        }
    }
}
