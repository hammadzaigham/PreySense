using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PreySense;
using PreySense.Mode;

namespace PreySense.Frontend.Services
{
    public sealed class GameLaunchWatcher : IDisposable
    {
        private readonly Func<IReadOnlyList<GameProfile>> _getProfiles;
        private readonly Func<GameProfile, Task> _onLaunch;
        private readonly Func<Task> _onExit;
        private readonly object _tickLock = new();
        private uint _activeProcessId;
        private string? _activeExe;
        private Process? _activeProcess;
        private int _tickRunning;
        private int _disposed;
        private readonly WinEventDelegate _foregroundEventHandler;
        private IntPtr _foregroundEventHook;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, uint processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, uint flags, StringBuilder lpExeName, ref uint lpdwSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const uint STILL_ACTIVE = 259;
        private const uint EventSystemForeground = 0x0003;
        private const uint WinEventOutOfContext = 0x0000;
        private const uint WinEventSkipOwnProcess = 0x0002;

        private delegate void WinEventDelegate(
            IntPtr hook, uint eventType, IntPtr window, int objectId, int childId,
            uint eventThreadId, uint eventTime);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWinEventHook(
            uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate callback,
            uint processId, uint threadId, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWinEvent(IntPtr hook);

        public GameLaunchWatcher(
            Func<IReadOnlyList<GameProfile>> getProfiles,
            Func<GameProfile, Task> onLaunch,
            Func<Task> onExit)
        {
            _getProfiles = getProfiles;
            _onLaunch = onLaunch;
            _onExit = onExit;
            _foregroundEventHandler = OnForegroundWindowChanged;
            _foregroundEventHook = SetWinEventHook(
                EventSystemForeground,
                EventSystemForeground,
                IntPtr.Zero,
                _foregroundEventHandler,
                0,
                0,
                WinEventOutOfContext | WinEventSkipOwnProcess);
            Tick();
        }

        private void OnForegroundWindowChanged(
            IntPtr hook, uint eventType, IntPtr window, int objectId, int childId,
            uint eventThreadId, uint eventTime)
        {
            if (window != IntPtr.Zero)
                Tick();
        }

        private void Tick()
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;
            if (Interlocked.Exchange(ref _tickRunning, 1) == 1)
                return;

            try
            {
                lock (_tickLock)
                {
                    // 1. If a game is currently active, verify its process is still alive.
                    // Games remain active even when temporarily alt-tabbed or running borderless.
                    if (_activeProcessId != 0)
                    {
                        if (IsProcessAlive(_activeProcessId))
                        {
                            return; // Game is still running normally, zero work needed.
                        }

                        // The active game process has terminated.
                        _activeProcessId = 0;
                        _activeExe = null;
                        DetachActiveProcess();
                        _ = RunCallbackAsync(_onExit, "game exit");
                        return;
                    }

                    // 2. No game currently active; check if the foreground window matches any game profile.
                    IntPtr hWnd = GetForegroundWindow();
                    if (hWnd == IntPtr.Zero)
                        return;

                    GetWindowThreadProcessId(hWnd, out uint pid);
                    if (pid == 0 || pid == 4) // 0 = Idle, 4 = System
                        return;

                    string? foregroundExe = GetProcessExeName(pid);
                    if (string.IsNullOrEmpty(foregroundExe))
                        return;

                    string foregroundProcessName = foregroundExe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                        ? foregroundExe[..^4]
                        : foregroundExe;

                    foreach (GameProfile profile in _getProfiles())
                    {
                        if (!profile.Enabled)
                            continue;

                        bool match = string.Equals(profile.ProcessName, foregroundProcessName, StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(profile.ExeName, foregroundExe, StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(profile.ProcessName, foregroundExe, StringComparison.OrdinalIgnoreCase);

                        if (match)
                        {
                            _activeProcessId = pid;
                            _activeExe = profile.ExeName;
                            AttachActiveProcess(pid);
                            _ = RunCallbackAsync(() => _onLaunch(profile), $"game launch ({profile.ExeName})");
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Game launch watcher tick failed: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _tickRunning, 0);
            }
        }

        private static async Task RunCallbackAsync(Func<Task> callback, string operation)
        {
            try
            {
                await callback().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Hardware/profile application runs asynchronously from the timer.
                // Observe failures here so they do not become unobserved task errors.
                AppLogger.Error($"Game launch watcher {operation} callback failed: {ex}");
            }
        }

        private void AttachActiveProcess(uint pid)
        {
            DetachActiveProcess();
            try
            {
                var process = Process.GetProcessById((int)pid);
                process.EnableRaisingEvents = true;
                process.Exited += ActiveProcessExited;
                _activeProcess = process;
            }
            catch
            {
                // The process can exit between foreground detection and attachment.
            }
        }

        private void ActiveProcessExited(object? sender, EventArgs e) => Tick();

        private void DetachActiveProcess()
        {
            if (_activeProcess == null)
                return;
            try
            {
                _activeProcess.Exited -= ActiveProcessExited;
                _activeProcess.Dispose();
            }
            catch { }
            _activeProcess = null;
        }

        private static bool IsProcessAlive(uint pid)
        {
            IntPtr hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (hProcess == IntPtr.Zero)
                return false;

            try
            {
                if (GetExitCodeProcess(hProcess, out uint exitCode))
                {
                    return exitCode == STILL_ACTIVE;
                }
                return false;
            }
            finally
            {
                CloseHandle(hProcess);
            }
        }

        private static string? GetProcessExeName(uint pid)
        {
            IntPtr hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (hProcess != IntPtr.Zero)
            {
                try
                {
                    var sb = new StringBuilder(1024);
                    uint size = (uint)sb.Capacity;
                    if (QueryFullProcessImageName(hProcess, 0, sb, ref size))
                    {
                        return Path.GetFileName(sb.ToString());
                    }
                }
                finally
                {
                    CloseHandle(hProcess);
                }
            }

            try
            {
                using var proc = Process.GetProcessById((int)pid);
                return proc.ProcessName;
            }
            catch
            {
                return null;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            DetachActiveProcess();
            if (_foregroundEventHook != IntPtr.Zero)
            {
                UnhookWinEvent(_foregroundEventHook);
                _foregroundEventHook = IntPtr.Zero;
            }
        }
    }
}
