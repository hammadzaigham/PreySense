using CommunityToolkit.Mvvm.ComponentModel;
using PreySense.Core.Services;

namespace PreySense.Frontend.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private string _cpuTemperatureText = "—";
    [ObservableProperty] private string _gpuTemperatureText = "—";
    [ObservableProperty] private string _cpuFanText = "—";
    [ObservableProperty] private string _gpuFanText = "—";
    [ObservableProperty] private string _activeGpuDisplayMode = "—";
    private volatile bool _mainWindowTelemetryVisible;
    private int _mainWindowTelemetryRevision;

    internal void SetMainWindowTelemetryVisible(bool visible)
    {
        if (_disposed) visible = false;
        if (_mainWindowTelemetryVisible == visible) return;
        _mainWindowTelemetryVisible = visible;
        _mainWindowTelemetryRevision++;
        if (visible) TelemetryCoordinator.SnapshotAvailable += OnMainWindowTelemetrySnapshot;
        else TelemetryCoordinator.SnapshotAvailable -= OnMainWindowTelemetrySnapshot;
        TelemetryCoordinator.SetDemand(TelemetryDemand.MainWindow, visible);
        // Reopening waits for a new shared sample rather than displaying stale readings.
        CpuTemperatureText = GpuTemperatureText = CpuFanText = GpuFanText = ActiveGpuDisplayMode = "—";
    }

    private void OnMainWindowTelemetrySnapshot(TelemetrySnapshot snapshot)
    {
        if (!_mainWindowTelemetryVisible || _disposed || _dispatcher.HasShutdownStarted) return;
        int revision = _mainWindowTelemetryRevision;
        _dispatcher.BeginInvoke(() =>
        {
            if (revision == _mainWindowTelemetryRevision) ApplyMainWindowTelemetry(snapshot);
        });
    }

    internal void ApplyMainWindowTelemetry(TelemetrySnapshot snapshot)
    {
        if (!_mainWindowTelemetryVisible || _disposed) return;
        static string Format(int? value, string unit) => value is >= 0 ? $"{value}{unit}" : "—";
        CpuTemperatureText = Format(snapshot.IsValid ? snapshot.CpuTemp : null, "°C");
        GpuTemperatureText = Format(snapshot.IsValid ? snapshot.GpuTemp : null, "°C");
        CpuFanText = Format(snapshot.IsValid ? snapshot.CpuRpm : null, " RPM");
        GpuFanText = Format(snapshot.IsValid ? snapshot.GpuRpm : null, " RPM");
        ActiveGpuDisplayMode = snapshot.IsValid && !string.IsNullOrWhiteSpace(snapshot.DisplayGpuName)
            ? snapshot.DisplayGpuName : "—";
    }
}
