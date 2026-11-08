namespace PreySense.Core.Hardware;

/// <summary>Owns the application's single bundled-driver connection.</summary>
public static class CpuDriverRuntime
{
    private static readonly object Gate = new();
    private static ThrottleStopCpuDriverBackend? _backend;
    private static CpuTelemetryReader? _telemetry;
    private static CpuPowerLimitController? _powerLimits;
    private static bool _attempted;
    public static string Status { get; private set; } = "CPU driver has not been initialized.";

    public static CpuTelemetryReader? Telemetry { get { Initialize(); return _telemetry; } }
    public static CpuPowerLimitController? PowerLimits { get { Initialize(); return _powerLimits; } }
    public static ThrottleStopCpuDriverBackend? Backend { get { Initialize(); return _backend; } }

    private static void Initialize()
    {
        lock (Gate)
        {
            if (_attempted) return;
            _attempted = true;
            var backend = new ThrottleStopCpuDriverBackend();
            try
            {
                if (!backend.Initialize(out var error, attemptStartService: true))
                {
                    Status = error ?? "CPU driver is unavailable.";
                    backend.Dispose();
                    return;
                }
                _telemetry = new CpuTelemetryReader(backend);
                _powerLimits = new CpuPowerLimitController(backend);
                _backend = backend;
                Status = "CPU driver is ready.";
            }
            catch (Exception ex)
            {
                backend.Dispose();
                Status = $"CPU driver initialization failed: {ex.Message}";
            }
        }
    }

    public static void Dispose()
    {
        lock (Gate)
        {
            _telemetry?.Dispose();
            _backend?.Dispose();
            _telemetry = null;
            _powerLimits = null;
            _backend = null;
            _attempted = true;
            Status = "CPU driver is closed.";
        }
    }
}
