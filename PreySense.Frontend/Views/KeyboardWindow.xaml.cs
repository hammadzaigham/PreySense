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
            Loaded += KeyboardWindow_Loaded;
            Closed += KeyboardWindow_Closed;
        }

        private void KeyboardWindow_Loaded(object sender, RoutedEventArgs e)
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
                    case 2: ComboCoverMode.SelectedIndex = 1; break; // Breathing
                    case 3: ComboCoverMode.SelectedIndex = 2; break; // Neon
                    default: ComboCoverMode.SelectedIndex = 0; break; // Static
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

                RestoreLightingModeUi(vm);
                _isLoaded = true;
            }
        }

        private void RestoreLightingModeUi(MainViewModel vm)
        {
            string tag = vm.LightingEffectTag;
            bool dynamic = tag.StartsWith("DYN:", StringComparison.Ordinal) ||
                           (tag.StartsWith("HW:", StringComparison.Ordinal) && tag != "HW:0");

            if (ComboLightingMode != null)
                ComboLightingMode.SelectedIndex = dynamic ? 1 : 0;

            if (PanelStaticControls != null)
                PanelStaticControls.Visibility = dynamic ? Visibility.Collapsed : Visibility.Visible;
            if (PanelDynamicControls != null)
                PanelDynamicControls.Visibility = dynamic ? Visibility.Visible : Visibility.Collapsed;

            if (dynamic && ComboAnimationSelect != null)
            {
                for (int i = 0; i < ComboAnimationSelect.Items.Count; i++)
                {
                    if (ComboAnimationSelect.Items[i] is ComboBoxItem item &&
                        item.Tag is string itemTag &&
                        string.Equals(itemTag, tag, StringComparison.Ordinal))
                    {
                        ComboAnimationSelect.SelectedIndex = i;
                        break;
                    }
                }
            }

            _pendingLightingEffectTag = tag;

            if (!dynamic && ComboPresetStatic != null)
            {
                int targetIndex = 0;
                for (int i = 0; i < ComboPresetStatic.Items.Count; i++)
                {
                    if (ComboPresetStatic.Items[i] is ComboBoxItem presetItem &&
                        presetItem.Tag is string presetTag &&
                        string.Equals(presetTag, vm.SelectedZonePreset, StringComparison.OrdinalIgnoreCase))
                    {
                        targetIndex = i;
                        break;
                    }
                }
                if (ComboPresetStatic.SelectedIndex != targetIndex)
                    ComboPresetStatic.SelectedIndex = targetIndex;
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
            if (_boundVm == null)
                return;
            _boundVm.PropertyChanged -= OnLightingVmPropertyChanged;
            _boundVm = null;
        }

        private void OnLightingVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if ((e.PropertyName == nameof(MainViewModel.IsCoverSyncWithKeyboard) ||
                 e.PropertyName == nameof(MainViewModel.CoverLightingMode)) &&
                DataContext is MainViewModel vm)
            {
                UpdateCoverModePanels(vm.CoverLightingMode);
            }
            else if (e.PropertyName == nameof(MainViewModel.SelectedZonePreset) &&
                     DataContext is MainViewModel mainVm && ComboPresetStatic != null)
            {
                for (int i = 0; i < ComboPresetStatic.Items.Count; i++)
                {
                    if (ComboPresetStatic.Items[i] is ComboBoxItem presetItem &&
                        presetItem.Tag is string presetTag &&
                        string.Equals(presetTag, mainVm.SelectedZonePreset, StringComparison.OrdinalIgnoreCase))
                    {
                        if (ComboPresetStatic.SelectedIndex != i)
                            ComboPresetStatic.SelectedIndex = i;
                        break;
                    }
                }
            }
        }

        private void UpdateCoverModePanels(int coverMode)
        {
            bool isStatic = coverMode <= 1;
            bool sync = DataContext is MainViewModel vm && vm.IsCoverSyncWithKeyboard;
            if (PanelCoverColorWheel != null)
            {
                PanelCoverColorWheel.Visibility = (isStatic && !sync)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            if (PanelCoverSpeed != null)
            {
                PanelCoverSpeed.Visibility = (!isStatic && !sync)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        private void Zone1_Click(object sender, RoutedEventArgs e)
        {
            SelectZone(1);
        }

        private void Zone2_Click(object sender, RoutedEventArgs e)
        {
            SelectZone(2);
        }

        private void Zone3_Click(object sender, RoutedEventArgs e)
        {
            SelectZone(3);
        }

        private void Zone4_Click(object sender, RoutedEventArgs e)
        {
            SelectZone(4);
        }

        private void SelectZone(int zone)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.SelectedRgbZone = zone;
                SyncKeyboardColorInputs(vm);
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
            if (!_isLoaded || PanelStaticControls == null || PanelDynamicControls == null) return;

            if (ComboLightingMode.SelectedIndex == 0) // Static
            {
                PanelStaticControls.Visibility = Visibility.Visible;
                PanelDynamicControls.Visibility = Visibility.Collapsed;
                _pendingLightingEffectTag = "HW:0";
                if (DataContext is MainViewModel staticVm)
                    staticVm.SetLightingEffect(_pendingLightingEffectTag);
            }
            else // Dynamic
            {
                PanelStaticControls.Visibility = Visibility.Collapsed;
                PanelDynamicControls.Visibility = Visibility.Visible;
                if (ComboAnimationSelect?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
                {
                    _pendingLightingEffectTag = tag;
                    if (DataContext is MainViewModel dynVm)
                        dynVm.SetLightingEffect(tag);
                }
            }
        }

        private void ComboAnimationSelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            if (ComboAnimationSelect?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
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
