using PreySense.Core.Services;
using System.Runtime.InteropServices;

namespace PreySense.Overlay;

public partial class HardwareOverlay
{
    private readonly object _foregroundGate = new();
    private ForegroundWindowEvents? _foregroundEvents;
    private bool _ownForeground, _desktopForeground;
    private int _foregroundUpdateQueued;

    private void StartForegroundEvents()
    {
        StopForegroundEvents();
        _foregroundEvents = new ForegroundWindowEvents(_ => QueueForegroundUpdate());
        lock (_foregroundGate) UpdateForegroundState(_foregroundEvents.CurrentWindow);
    }

    private void StopForegroundEvents()
    {
        _foregroundEvents?.Dispose();
        _foregroundEvents = null;
        Interlocked.Exchange(ref _foregroundUpdateQueued, 0);
    }

    private const int ForegroundChangedMessage = 0x8000 + 91;

    private void QueueForegroundUpdate()
    {
        if (!_active || IsDisposed || Handle == 0 || Interlocked.Exchange(ref _foregroundUpdateQueued, 1) != 0) return;
        if (!PostMessage(Handle, ForegroundChangedMessage, 0, 0))
            Interlocked.Exchange(ref _foregroundUpdateQueued, 0);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(nint window, int message, nint wParam, nint lParam);

    private void UpdateForegroundState(nint window)
    {
        if (!_active) return;
            NativeMethods.GetWindowThreadProcessId(window, out uint fgPidRaw);
            int fgPid = (int)fgPidRaw;
            bool ownWindow = fgPid == 0 || fgPid == Environment.ProcessId;
            string? foregroundProcessName = ownWindow ? null : GetForegroundProcessName(fgPid);
            bool desktopFg = foregroundProcessName != null && DesktopApps.Contains(foregroundProcessName);
            _ownForeground = ownWindow;
            _desktopForeground = desktopFg;
            if (_wasOwnWindow && desktopFg)
                _keepOverlayAfterUiHide = true;
            else if (!ownWindow && !desktopFg)
                _keepOverlayAfterUiHide = false;

            bool screenshotActive = foregroundProcessName != null && ScreenshotApps.Contains(foregroundProcessName);
            if (UpdateScreenshotVisibility(screenshotActive))
            {
                _wasOwnWindow = ownWindow;
                return;
            }

            if (_fps != null)
            {
                if (ownWindow)
                {
                    if (_shownPid != 0)
                        _fps.TargetPid = _shownPid;
                }
                else if (_keepOverlayAfterUiHide && desktopFg)
                {
                    if (_shownPid != 0)
                        _fps.TargetPid = _shownPid;
                }
                else if (fgPid != _lastFgPid || _fps.TargetPid != fgPid)
                {
                    _lastFgPid = fgPid;
                    if (fgPid != _shownPid)
                        ResetFpsDisplayState();
                    _fps.TargetPid = fgPid;
                    _fgDesktop = desktopFg;
                }
            }

            _wasOwnWindow = ownWindow;
            SyncOverlayTelemetryDemand();
    }
}
