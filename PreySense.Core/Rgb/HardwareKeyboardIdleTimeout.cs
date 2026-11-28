using System;
using System.Threading;
using System.Threading.Tasks;
using PreySense.Core.Services;
using PreySense.Helpers;

namespace PreySense.Rgb;

/// <summary>
/// Software keyboard-backlight idle timeout for hardware (firmware-animated) effects on models
/// where the EC timeout does not reach the keyboard controller. Software effects keep using
/// <see cref="CustomLightingEffectsEngine"/>'s own idle handling; this stays out of their way.
/// Like the EC timeout, only key presses count as activity. Lighting writes restart the countdown.
/// </summary>
public sealed class HardwareKeyboardIdleTimeout : IDisposable
{
    private readonly Func<bool> _turnOff;
    private readonly Action _restore;
    private readonly Func<bool> _isSuspended;
    private readonly Func<int> _now;
    private readonly object _lock = new();
    private readonly AsyncChangeSignal _wake = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task? _loop;
    private int _timeoutSeconds;
    private int _lastActivityTick;
    private bool _dimmed;
    private bool _restorePending;
    private bool _disposed;

    /// <param name="turnOff">Turns the keyboard backlight off without changing the saved lighting state.</param>
    /// <param name="restore">Re-applies the saved keyboard lighting.</param>
    /// <param name="isSuspended">True while another component (a software effect) owns the idle timeout.</param>
    public HardwareKeyboardIdleTimeout(Func<bool> turnOff, Action restore, Func<bool> isSuspended)
        : this(turnOff, restore, isSuspended, () => Environment.TickCount, run: true) { }

    internal HardwareKeyboardIdleTimeout(Func<bool> turnOff, Action restore, Func<bool> isSuspended,
        Func<int> now, bool run)
    {
        _turnOff = turnOff ?? throw new ArgumentNullException(nameof(turnOff));
        _restore = restore ?? throw new ArgumentNullException(nameof(restore));
        _isSuspended = isSuspended ?? throw new ArgumentNullException(nameof(isSuspended));
        _now = now;
        _lastActivityTick = _now();
        if (!run) return; // Tests drive Step() directly.
        KeyboardActivity.KeyDown += NoteActivity;
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    public bool IsDimmed { get { lock (_lock) return _dimmed; } }

    public void SetTimeoutSeconds(int seconds)
    {
        lock (_lock)
        {
            _timeoutSeconds = Math.Max(0, seconds);
            _lastActivityTick = _now();
            if (_timeoutSeconds == 0) WakeFromIdleLocked();
        }
        _wake.Pulse();
    }

    /// <summary>A key press. Called from the low-level keyboard hook, so it never touches hardware.</summary>
    public void NoteActivity()
    {
        lock (_lock)
        {
            _lastActivityTick = _now();
            WakeFromIdleLocked();
        }
        _wake.Pulse();
    }

    /// <summary>Keyboard lighting was written, so the backlight is on again: restart the countdown.</summary>
    public void NoteLightingWritten()
    {
        lock (_lock)
        {
            _lastActivityTick = _now();
            _dimmed = false;
            _restorePending = false;
        }
        _wake.Pulse();
    }

    private void WakeFromIdleLocked()
    {
        if (!_dimmed) return;
        _dimmed = false;
        _restorePending = true;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Observe first, then inspect state, so a change between the two is never lost.
                Task wake = _wake.WaitAsync(ct);
                int delayMs = Step();
                if (delayMs < 0) await wake.ConfigureAwait(false);
                else await Task.WhenAny(wake, Task.Delay(delayMs, ct)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                AppLogger.Error($"Keyboard idle timeout error: {ex.Message}");
                try { await Task.Delay(1000, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    /// <summary>One evaluation. Returns ms until the next deadline, or -1 to wait for a change.</summary>
    internal int Step()
    {
        bool restore;
        int timeout, last;
        bool dimmed;
        lock (_lock)
        {
            restore = _restorePending;
            _restorePending = false;
            timeout = _timeoutSeconds;
            last = _lastActivityTick;
            dimmed = _dimmed;
        }

        if (restore)
        {
            _restore();
            lock (_lock) _lastActivityTick = _now();
            return 0;
        }

        if (timeout <= 0 || dimmed || _isSuspended()) return -1;

        long remaining = timeout * 1000L - unchecked(_now() - last);
        if (remaining > 0) return (int)Math.Min(remaining, int.MaxValue);

        if (!_turnOff()) return 5000; // Controller unavailable; retry later.
        lock (_lock)
        {
            // A key or lighting write landed while the backlight was being turned off.
            if (_lastActivityTick != last) _restorePending = true;
            else _dimmed = true;
        }
        return 0;
    }

    public void Dispose()
    {
        bool restore;
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            restore = _dimmed;
            _dimmed = false;
        }
        KeyboardActivity.KeyDown -= NoteActivity;
        _cts.Cancel();
        try { _loop?.Wait(1000); } catch { }
        _cts.Dispose();
        if (restore)
        {
            try { _restore(); } catch { }
        }
    }
}
