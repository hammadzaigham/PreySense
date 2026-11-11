namespace PreySense.Frontend.Services;

/// <summary>Runs one operation at a time, merging pending updates instead of building a backlog.</summary>
internal sealed class LatestBackgroundWorker<T>(Action<T> execute, Func<T, T, T> merge)
{
    private readonly object _gate = new();
    private T _pending = default!;
    private bool _hasPending, _running, _stopped, _drainPending;
    private Task _completion = Task.CompletedTask;

    public Task Submit(T request)
    {
        lock (_gate)
        {
            if (_stopped) return _completion;
            _pending = _hasPending ? merge(_pending, request) : request;
            _hasPending = true;
            if (!_running)
            {
                _running = true;
                _completion = Task.Run(Drain);
            }
            return _completion;
        }
    }

    private void Drain()
    {
        while (true)
        {
            T request;
            lock (_gate)
            {
                if (!_hasPending || (_stopped && !_drainPending)) { _running = false; return; }
                request = _pending;
                _hasPending = false;
            }
            try { execute(request); }
            catch (Exception ex) { AppLogger.Error($"Background operation failed: {ex}"); }
        }
    }

    public Task StopAsync(bool drainPending = false)
    {
        lock (_gate)
        {
            _stopped = true;
            _drainPending = drainPending;
            if (!drainPending) _hasPending = false;
            return _completion;
        }
    }

    public Task WhenIdleAsync()
    {
        lock (_gate) return _completion;
    }
}
