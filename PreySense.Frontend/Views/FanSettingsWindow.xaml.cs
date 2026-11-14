using System.Threading;
using System.Windows;
using PreySense.Frontend.ViewModels;

namespace PreySense.Frontend.Views;

public partial class FanSettingsWindow : Window
{
    private CancellationTokenSource? _calibrationCancellation;
    private bool _closed;

    public FanSettingsWindow()
    {
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height - 40;
        SettingsScroll.MaxHeight = Math.Max(160, MaxHeight - 56);
        Loaded += (_, _) =>
        {
            if (DataContext is MainViewModel vm) vm.InitializeFanSettings();
        };
        Closed += (_, _) =>
        {
            _closed = true;
            _calibrationCancellation?.Cancel();
        };
    }

    private async void CalibrateFanRpms_Click(object sender, RoutedEventArgs e)
    {
        if (_closed || _calibrationCancellation is not null || DataContext is not MainViewModel vm) return;
        using var cancellation = new CancellationTokenSource();
        _calibrationCancellation = cancellation;
        CalibrationError.Visibility = Visibility.Collapsed;
        try { await vm.CalibrateFanRpmsAsync(cancellation.Token); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            AppLogger.Error($"Fan RPM calibration failed: {ex}");
            if (!_closed)
            {
                CalibrationError.Text = $"Fan RPM calibration failed: {ex.Message}";
                CalibrationError.Visibility = Visibility.Visible;
            }
        }
        finally { _calibrationCancellation = null; }
    }
}
