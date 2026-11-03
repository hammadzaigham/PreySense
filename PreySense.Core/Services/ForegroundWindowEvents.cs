using System.Runtime.InteropServices;

namespace PreySense.Core.Services;

/// <summary>Foreground notifications owned by the caller's existing Windows message loop.</summary>
public sealed class ForegroundWindowEvents : IDisposable
{
    private delegate void WinEventCallback(nint hook, uint type, nint window, int objectId, int childId, uint thread, uint time);
    private readonly WinEventCallback _callback;
    private readonly Action<nint> _changed;
    private nint _hook, _current;
    private int _disposed;
    public bool IsAvailable => _hook != 0;
    public nint CurrentWindow => Volatile.Read(ref _current);

    public ForegroundWindowEvents(Action<nint> changed)
    {
        _changed = changed;
        _callback = OnEvent;
        _hook = SetWinEventHook(3, 3, 0, _callback, 0, 0, 0);
        _current = GetForegroundWindow();
        if (_hook == 0) AppLogger.Log("Foreground notifications unavailable; keeping the existing foreground check.");
    }

    private void OnEvent(nint hook, uint type, nint window, int objectId, int childId, uint thread, uint time)
    {
        if (type != 3 || Volatile.Read(ref _disposed) != 0) return;
        Volatile.Write(ref _current, window);
        _changed(window);
    }

    public void Refresh()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        OnEvent(0, 3, GetForegroundWindow(), 0, 0, 0, 0);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_hook != 0) UnhookWinEvent(_hook);
        _hook = 0;
    }

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventCallback callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
}
