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
    /// <summary>Event-driven game profile watcher; no timer or process enumeration.</summary>
    public sealed class GameLaunchWatcher : IDisposable
    {
        private readonly Func<IReadOnlyList<GameProfile>> _getProfiles;
        private readonly Func<GameProfile, Task> _onLaunch;
        private readonly Func<Task> _onExit;
        private readonly SemaphoreSlim _transitionGate = new(1, 1);
        private readonly WinEventDelegate _foregroundEventHandler;
        private IntPtr _foregroundEventHook;
        private Process? _activeProcess;
        private uint _activeProcessId;
        private string? _activeExe;
        private int _disposed;
        private int _foregroundQueued, _foregroundRunning;

        private const uint EventSystemForeground = 0x0003;
        private const uint WinEventOutOfContext = 0x0000;
        private const uint WinEventSkipOwnProcess = 0x0002;
        private const uint ProcessQueryLimitedInformation = 0x1000;

        private delegate void WinEventDelegate(IntPtr hook, uint eventType, IntPtr window,
            int objectId, int childId, uint eventThreadId, uint eventTime);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module,
            WinEventDelegate callback, uint processId, uint threadId, uint flags);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr process, uint flags,
            StringBuilder imageName, ref uint size);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        public GameLaunchWatcher(Func<IReadOnlyList<GameProfile>> getProfiles,
            Func<GameProfile, Task> onLaunch, Func<Task> onExit)
        {
            _getProfiles = getProfiles ?? throw new ArgumentNullException(nameof(getProfiles));
            _onLaunch = onLaunch ?? throw new ArgumentNullException(nameof(onLaunch));
            _onExit = onExit ?? throw new ArgumentNullException(nameof(onExit));
            _foregroundEventHandler = OnForegroundWindowChanged;
            _foregroundEventHook = SetWinEventHook(EventSystemForeground, EventSystemForeground,
                IntPtr.Zero, _foregroundEventHandler, 0, 0,
                WinEventOutOfContext | WinEventSkipOwnProcess);
            QueueForegroundEvaluation();
        }

        private void OnForegroundWindowChanged(IntPtr hook, uint eventType, IntPtr window,
            int objectId, int childId, uint eventThreadId, uint eventTime)
            => QueueForegroundEvaluation();

        private void QueueForegroundEvaluation()
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            Interlocked.Exchange(ref _foregroundQueued, 1);
            if (Interlocked.CompareExchange(ref _foregroundRunning, 1, 0) != 0) return;
            _ = Task.Run(async () =>
            {
                try
                {
                    do
                    {
                        Interlocked.Exchange(ref _foregroundQueued, 0);
                        await EvaluateForegroundAsync().ConfigureAwait(false);
                    } while (Volatile.Read(ref _disposed) == 0 && Volatile.Read(ref _foregroundQueued) != 0);
                }
                finally
                {
                    Volatile.Write(ref _foregroundRunning, 0);
                    if (Volatile.Read(ref _foregroundQueued) != 0) QueueForegroundEvaluation();
                }
            });
        }

        public Task RecheckForegroundAfterResumeAsync() => EvaluateForegroundAsync(reapplyTrackedGame: true);

        private async Task EvaluateForegroundAsync(bool reapplyTrackedGame = false)
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            await _transitionGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (Volatile.Read(ref _disposed) != 0) return;
                var profiles = _getProfiles();
                if (!profiles.Any(profile => profile.Enabled)) return;
                // A queued event may be stale after rapid Alt-Tabbing. Read
                // the current foreground window before doing any process work.
                IntPtr window = GetForegroundWindow();
                if (window == IntPtr.Zero) return;
                GetWindowThreadProcessId(window, out uint pid);
                if (pid == 0 || pid == 4) return;

                // Alt-Tabbing within the tracked game is a no-op.
                if (_activeProcessId == pid && _activeProcess != null)
                {
                    if (reapplyTrackedGame)
                    {
                        var resumedProfile = FindMatchingProfile(GetProcessExecutablePath(pid), profiles);
                        if (resumedProfile != null)
                            await RunCallbackAsync(() => _onLaunch(resumedProfile), "resume foreground game").ConfigureAwait(false);
                    }
                    return;
                }

                string? executablePath = GetProcessExecutablePath(pid);
                GameProfile? profile = FindMatchingProfile(executablePath, profiles);
                if (profile == null) return;
                Process? process = TryOpenProcess(pid);
                if (process == null) return;

                // A different configured game gained focus: restore, then apply.
                if (_activeProcess != null)
                {
                    DetachActiveProcess();
                    _activeProcessId = 0;
                    _activeExe = null;
                    await RunCallbackAsync(_onExit, "profile switch").ConfigureAwait(false);
                }

                if (Volatile.Read(ref _disposed) != 0)
                {
                    process.Dispose();
                    return;
                }

                _activeProcess = process;
                _activeProcessId = pid;
                _activeExe = profile.ExeName;
                process.Exited += ActiveProcessExited;
                if (process.HasExited)
                {
                    DetachActiveProcess();
                    _activeProcessId = 0;
                    _activeExe = null;
                    return;
                }

                await RunCallbackAsync(() => _onLaunch(profile),
                    $"game launch ({profile.ExeName})").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Game launch watcher foreground evaluation failed: {ex}");
            }
            finally
            {
                if (Volatile.Read(ref _disposed) != 0) DetachActiveProcess();
                _transitionGate.Release();
            }
        }

        internal static GameProfile? FindMatchingProfile(string? executablePath, IReadOnlyList<GameProfile> profiles)
            => GameProfileStore.FindMatchingProfile(executablePath, profiles);

        private void ActiveProcessExited(object? sender, EventArgs e)
        {
            if (sender is Process process) _ = HandleProcessExitAsync(process);
        }

        private async Task HandleProcessExitAsync(Process process)
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            await _transitionGate.WaitAsync().ConfigureAwait(false);
            try
            {
                // Reference equality protects against late events and PID reuse.
                if (Volatile.Read(ref _disposed) != 0 || !ReferenceEquals(_activeProcess, process)) return;
                DetachActiveProcess();
                _activeProcessId = 0;
                _activeExe = null;
                await RunCallbackAsync(_onExit, "game exit").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Game launch watcher process exit handling failed: {ex}");
            }
            finally { _transitionGate.Release(); }
        }

        private static Process? TryOpenProcess(uint pid)
        {
            try
            {
                Process process = Process.GetProcessById((int)pid);
                process.EnableRaisingEvents = true;
                if (process.HasExited) { process.Dispose(); return null; }
                return process;
            }
            catch { return null; }
        }

        private void DetachActiveProcess()
        {
            if (_activeProcess == null) return;
            try
            {
                _activeProcess.Exited -= ActiveProcessExited;
                _activeProcess.Dispose();
            }
            catch { }
            _activeProcess = null;
        }

        private static string? GetProcessExecutablePath(uint pid)
        {
            IntPtr handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
            if (handle == IntPtr.Zero) return null;
            try
            {
                var buffer = new StringBuilder(1024);
                uint size = (uint)buffer.Capacity;
                return QueryFullProcessImageName(handle, 0, buffer, ref size)
                    ? GameProfileStore.NormalizeExecutablePath(buffer.ToString()) : null;
            }
            finally { CloseHandle(handle); }
        }

        private static async Task RunCallbackAsync(Func<Task> callback, string operation)
        {
            try { await callback().ConfigureAwait(false); }
            catch (Exception ex) { AppLogger.Error($"Game launch watcher {operation} callback failed: {ex}"); }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            if (_foregroundEventHook != IntPtr.Zero)
            {
                UnhookWinEvent(_foregroundEventHook);
                _foregroundEventHook = IntPtr.Zero;
            }
            DetachActiveProcess();
            // Do not dispose the gate here: a foreground callback may already
            // be waiting on it. The watcher is process-lifetime scoped and the
            // semaphore will be reclaimed with the process.
        }
    }
}
