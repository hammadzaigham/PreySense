using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace PreySense.Frontend;

public partial class MainWindow
{
    public void ShowAndRestore()
    {
        RunOnUi(() =>
        {
            if (_isExplicitExit) return;
            long revision = _visibilitySession.BeginRestore();
            try
            {
                ShowInTaskbar = true;
                Topmost = true;
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                if (!IsVisible) Show();
                foreach (Window window in _visibilitySession.HiddenWindows)
                {
                    try
                    {
                        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                        window.Topmost = true;
                        if (!window.IsVisible) window.Show();
                    }
                    catch (InvalidOperationException) { /* Window closed while hidden. */ }
                }
                var preferred = _visibilitySession.PreferredWindow;
                var visibleWindows = Application.Current.Windows.Cast<Window>()
                    .Where(window => window is not Views.ModeToastWindow && window.IsVisible && window.IsEnabled).ToArray();
                Window target = preferred is { IsVisible: true, IsEnabled: true }
                    ? preferred
                    : visibleWindows.LastOrDefault(window => window.IsActive)
                        ?? visibleWindows.OrderBy(OwnerDepth).LastOrDefault() ?? this;
                ForceToForeground(target);
                _visibilitySession.RecordForeground(IsApplicationForeground(GetForegroundWindow()));
            }
            finally
            {
                // Ignore transient activation changes while restoring the window group.
                Dispatcher.BeginInvoke(DispatcherPriority.Input,
                    () =>
                    {
                        _visibilitySession.CompleteRestore(revision);
                        QueueAutoHide();
                    });
            }
        });
    }

    private void GuardToolWindowClose()
    {
        if (_isExplicitExit || !IsVisible || _visibilitySession.IsHidden
            || IsApplicationForeground(GetForegroundWindow()) != true) return;
        // Closing an owned window briefly releases foreground before Windows activates
        // its owner. Invalidate queued hide checks and wait for that activation handoff.
        // Completion also runs when Closing is cancelled; no guard can remain stuck.
        long revision = _visibilitySession.BeginRestore();
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
        {
            _visibilitySession.CompleteRestore(revision);
            _visibilitySession.RecordForeground(IsApplicationForeground(GetForegroundWindow()));
            QueueAutoHide();
        });
    }

    internal void QueueAutoHide()
    {
        if (_isExplicitExit || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        long revision = _visibilitySession.Revision;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (_isExplicitExit || !IsVisible || !_visibilitySession.CanAutoHide(revision)) return;
            if (_visibilitySession.ShouldAutoHide(revision, IsApplicationForeground(GetForegroundWindow())))
                HideApplicationWindowsForAutoHide();
        });
    }

    private static void SetApplicationTopmost(bool topmost)
    {
        foreach (Window window in Application.Current.Windows.Cast<Window>().ToArray())
            if (window is not Views.ModeToastWindow && (!topmost || window.IsVisible))
                window.Topmost = topmost;
    }

    public void HideApplicationWindowsForAutoHide()
    {
        RunOnUi(() =>
        {
            var visibleWindows = Application.Current.Windows.Cast<Window>()
                .Where(window => window is not Views.ModeToastWindow && window.IsVisible).ToArray();
            Window? activeWindow = visibleWindows.LastOrDefault(window => window.IsActive)
                ?? (visibleWindows.Contains(_lastActiveWindow) ? _lastActiveWindow : null)
                ?? visibleWindows.OrderBy(OwnerDepth).LastOrDefault();
            SetApplicationTopmost(false);
            if (!_visibilitySession.BeginHide(visibleWindows.Where(window => window != this), activeWindow)) return;
            // Snapshot first: hiding owned windows changes WPF's window/activation state.
            foreach (Window window in visibleWindows.Where(window => window != this).OrderByDescending(OwnerDepth))
            {
                try
                {
                    window.ShowInTaskbar = false;
                    window.Hide();
                }
                catch (InvalidOperationException) { /* Window closed during this hide cycle. */ }
            }
            ShowInTaskbar = false;
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Hide();
        });
    }

    public void ToggleAppVisibility()
    {
        RunOnUi(() =>
        {
            if (_isExplicitExit) return;
            if (IsVisible && !App.SuppressAutoHide && IsApplicationForeground(GetForegroundWindow()) == true)
                HideApplicationWindowsForAutoHide();
            else ShowAndRestore();
        });
    }

    private static int OwnerDepth(Window window)
    {
        int depth = 0;
        while (window.Owner is Window owner) { depth++; window = owner; }
        return depth;
    }

    private static bool? IsApplicationForeground(IntPtr foreground)
    {
        if (foreground == IntPtr.Zero) return null;
        if (GetWindowThreadProcessId(foreground, out uint processId) == 0 || processId == 0) return null;
        // Includes WPF dropdown HWNDs and native dialogs, not just top-level WPF windows.
        return processId == (uint)Environment.ProcessId;
    }

    private static void ForceToForeground(Window window)
    {
        if (!window.Dispatcher.CheckAccess())
        {
            _ = window.Dispatcher.BeginInvoke(() => ForceToForeground(window));
            return;
        }
        if (!window.IsVisible) window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();
        ShowWindow(hwnd, SwRestore);
        window.Activate();
        if (GetForegroundWindow() == hwnd) return;
        IntPtr foreground = GetForegroundWindow();
        uint targetThread = GetWindowThreadProcessId(hwnd, out _);
        uint foregroundThread = foreground != IntPtr.Zero ? GetWindowThreadProcessId(foreground, out _) : 0;
        if (foregroundThread == 0 || foregroundThread == targetThread ||
            !AttachThreadInput(targetThread, foregroundThread, true))
        {
            SetForegroundWindow(hwnd);
            return;
        }
        try { SetForegroundWindow(hwnd); window.Activate(); }
        finally { AttachThreadInput(targetThread, foregroundThread, false); }
    }

    private const int SwRestore = 9;
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
}
