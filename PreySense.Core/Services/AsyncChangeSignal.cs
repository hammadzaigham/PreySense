namespace PreySense.Core.Services;

/// <summary>Wakes asynchronous change observers without a periodic timer.</summary>
public sealed class AsyncChangeSignal
{
    private readonly object _gate = new();
    private TaskCompletionSource? _pending;

    // Capture the wait before checking the observed state to avoid missing a change.
    public Task WaitAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _pending ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
            return _pending.Task.WaitAsync(cancellationToken);
        }
    }

    public void Pulse()
    {
        TaskCompletionSource? pending;
        lock (_gate)
        {
            pending = _pending;
            _pending = null;
        }
        pending?.TrySetResult();
    }
}
