using System;
using System.Threading;

namespace PreySense.Core.Hardware;

/// <summary>
/// Event fan-out for HID interface arrival/removal. Windows posts WM_DEVICECHANGE;
/// callers wait on <see cref="WaitAsync"/> instead of polling SetupDi.
/// </summary>
public static class HidArrivalHub
{
    private static readonly object Gate = new();
    private static event Action? ArrivedHandlers;
    private static int _generation;

    public static int Generation => Volatile.Read(ref _generation);

    public static event Action Arrived
    {
        add { lock (Gate) ArrivedHandlers += value; }
        remove { lock (Gate) ArrivedHandlers -= value; }
    }

    public static void NotifyArrived()
    {
        Interlocked.Increment(ref _generation);
        Action? handlers;
        lock (Gate) handlers = ArrivedHandlers;
        try { handlers?.Invoke(); }
        catch { }
    }

    public static async Task WaitAsync(CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnArrived() => tcs.TrySetResult();
        void OnCancel() => tcs.TrySetCanceled(cancellationToken);

        Arrived += OnArrived;
        using var reg = cancellationToken.Register(OnCancel);
        try
        {
            await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            Arrived -= OnArrived;
        }
    }
}
