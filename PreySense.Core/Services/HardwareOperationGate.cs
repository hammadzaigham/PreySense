namespace PreySense.Core.Services;

// Sensor readers must not retain NVIDIA handles while the display adapter switches.
// A single gate also prevents the window and overlay from issuing overlapping batches.
public static class HardwareOperationGate
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static IDisposable? TryEnterTelemetry(int timeoutMs = 0) =>
        Gate.Wait(timeoutMs) ? new Lease() : null;

    public static async Task<IDisposable> EnterDisplaySwitchAsync()
    {
        if (!await Gate.WaitAsync(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("A sensor read is still running. GPU switching was postponed.");
        return new Lease();
    }

    private sealed class Lease : IDisposable
    {
        private int _released;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) Gate.Release();
        }
    }
}
