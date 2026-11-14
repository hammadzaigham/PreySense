using PreySense.Core.Services;

namespace PreySense.Frontend.Services;

/// <summary>Visible fan readouts share the existing telemetry producer and its sampling rate.</summary>
internal sealed class FanWindowTelemetry : IDisposable
{
    private volatile bool _visible;
    private bool _disposed;
    internal event Action<TelemetrySnapshot>? SnapshotAvailable;

    internal void SetVisible(bool visible)
    {
        visible &= !_disposed;
        if (_visible == visible) return;
        _visible = visible;
        if (visible) TelemetryCoordinator.SnapshotAvailable += OnSnapshot;
        else TelemetryCoordinator.SnapshotAvailable -= OnSnapshot;
        TelemetryCoordinator.SetDemand(TelemetryDemand.FanWindow, visible);
    }

    private void OnSnapshot(TelemetrySnapshot snapshot)
    {
        if (_visible) SnapshotAvailable?.Invoke(snapshot);
    }

    internal static (string CpuTemperature, string CpuFan, string GpuTemperature, string GpuFan) Format(TelemetrySnapshot snapshot)
    {
        static string Temperature(int? value) => value is > 0 and <= 125 ? $"{value}°C" : "—";
        static string Rpm(int? value) => value is >= 0 ? $"{value} RPM" : "—";
        return snapshot.IsValid
            ? (Temperature(snapshot.CpuTemp), Rpm(snapshot.CpuRpm), Temperature(snapshot.GpuTemp), Rpm(snapshot.GpuRpm))
            : ("—", "—", "—", "—");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SetVisible(false);
    }
}
