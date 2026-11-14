using System;
using System.ComponentModel;
using System.Windows;
using PreySense.Frontend.ViewModels;

namespace PreySense.Frontend.Views
{
    public partial class TuningSettingsWindow : Window
    {
        private bool _isUpdatingUi;

        public TuningSettingsWindow()
        {
            InitializeComponent();
            Loaded += TuningSettingsWindow_Loaded;
            Closed += (_, _) =>
            {
                if (DataContext is MainViewModel vm)
                    vm.PropertyChanged -= OnVmPropertyChanged;
            };
        }

        private void TuningSettingsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.PropertyChanged -= OnVmPropertyChanged;
                vm.PropertyChanged += OnVmPropertyChanged;
            }
            SyncFromViewModel();
        }

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.IsProchotOffsetEnabled) && !_isUpdatingUi)
            {
                if (SwitchProchotOffset != null &&
                    SwitchProchotOffset.IsChecked != (DataContext as MainViewModel)?.IsProchotOffsetEnabled)
                {
                    _isUpdatingUi = true;
                    SwitchProchotOffset.IsChecked = (DataContext as MainViewModel)?.IsProchotOffsetEnabled == true;
                    _isUpdatingUi = false;
                }
            }
        }

        private void SyncFromViewModel()
        {
            if (DataContext is not MainViewModel vm) return;
            _isUpdatingUi = true;
            if (RowProchotOffset != null)
                RowProchotOffset.Value = vm.TuningEditorProchotOffset is int offset ? offset : double.NaN;
            if (SwitchProchotOffset != null)
                SwitchProchotOffset.IsChecked = vm.IsProchotOffsetEnabled;
            _isUpdatingUi = false;
        }

        private void RowProchotOffset_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingUi || DataContext is not MainViewModel vm) return;
            RowProchotOffset.CommitText(notify: false);
            vm.ProchotOffsetCelsius = double.IsFinite(RowProchotOffset.Value)
                ? (int)Math.Round(RowProchotOffset.Value) : null;
        }

        private void ProchotOffsetToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingUi || DataContext is not MainViewModel vm) return;
            bool enabled = SwitchProchotOffset?.IsChecked == true;
            vm.IsProchotOffsetEnabled = enabled;
            if (enabled && vm.ProchotOffsetCelsius is null)
                RowProchotOffset_LostFocus(RowProchotOffset, new RoutedEventArgs());
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;
            RowProchotOffset_LostFocus(RowProchotOffset, new RoutedEventArgs());
            if (!vm.SaveProchotSettings()) { SaveStatus.Text = vm.DiagnosticMessage; return; }
            Close();
        }
    }
}
