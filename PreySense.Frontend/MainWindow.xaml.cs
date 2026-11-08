using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using PreySense.Core.Services;
using PreySense.Frontend.Services;
using PreySense.Frontend.ViewModels;

namespace PreySense.Frontend
{
    public partial class MainWindow : Window
    {
        private Services.KeyboardHook? _keyboardHook;
        private Services.TrayIconManager? _trayIconManager;
        private Services.HidDeviceArrivalWatcher? _hidArrivalWatcher;
        private bool _isExplicitExit = false;
        private readonly List<Window> _autoHiddenWindows = new();
        private Window? _autoHiddenActiveWindow;
        private Views.FanCurveWindow? _fanCurveWindow;

        public MainWindow()
        {
            InitializeComponent();

            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            IntPtr hWnd = helper.EnsureHandle();

            _trayIconManager = new Services.TrayIconManager(hWnd, onOpen: ShowAndRestore, onQuit: QuitApplication);
            var source = System.Windows.Interop.HwndSource.FromHwnd(hWnd);
            source?.AddHook(_trayIconManager.WndProc);
            try { _hidArrivalWatcher = new Services.HidDeviceArrivalWatcher(hWnd); } catch { }

            IHardwareService hw;
            try
            {
                hw = new ProductionHardwareService();
            }
            catch (Exception ex)
            {
                System.IO.File.WriteAllText("hw_init.log", ex.ToString());
                hw = new MockHardwareService();
            }

            var vm = new MainViewModel(hw);
            DataContext = vm;
            ApplyResponsivePercentageSize();

            OverlayBridge.Configure(() => (hw as ProductionHardwareService)?.Wmi);
            // Setup Predator Sense Key & Combos
            try
            {
                _keyboardHook = new Services.KeyboardHook(
                    onPredatorSensePressed: ToggleAppVisibility,
                    onPredatorNumberPressed: number =>
                    {
                        byte mode = number switch
                        {
                            1 => 0x06, // Eco
                            2 => 0x00, // Quiet
                            3 => 0x01, // Balanced
                            4 => 0x04, // Performance
                            5 => 0x05, // Turbo
                            _ => 0xFF
                        };
                        if (mode != 0xFF)
                        {
                            RunOnUi(() =>
                            {
                                if (DataContext is MainViewModel mvm)
                                {
                                    _ = mvm.SetModeAsync(mode);
                                }
                            });
                        }
                    },
                    onPredatorRPressed: () =>
                    {
                        RunOnUi(OverlayBridge.Toggle);
                    },
                    onModeKeyPressed: () =>
                    {
                        RunOnUi(() =>
                        {
                            if (DataContext is MainViewModel mvm)
                                mvm.HandleModeKeyPressed();
                        });
                    },
                    onBrightnessDelta: delta =>
                    {
                        RunOnUi(() =>
                        {
                            if (DataContext is MainViewModel mvm)
                                mvm.StepKeyboardBrightness(delta);
                        });
                    });
            }
            catch { }


            StateChanged += (_, _) =>
            {
                if (WindowState == WindowState.Minimized)
                    HideApplicationWindowsForAutoHide();
            };

            Closed += (s, e) =>
            {
                _hidArrivalWatcher?.Dispose();
                _hidArrivalWatcher = null;
                _keyboardHook?.Dispose();
                _keyboardHook = null;
                _trayIconManager?.Dispose();
                _trayIconManager = null;
                OverlayBridge.Dispose();
                vm.Dispose();
            };
        }

        private void RunOnUi(Action action)
        {
            if (Dispatcher.CheckAccess())
                action();
            else
                _ = Dispatcher.BeginInvoke(action);
        }

        public void ShowAndRestore()
        {
            RunOnUi(() =>
            {
                // Ignore click-away hide while we are bringing the app forward.
                App.SuppressAutoHide = true;
                try
                {
                    ShowInTaskbar = false;
                    if (WindowState == WindowState.Minimized)
                        WindowState = WindowState.Normal;
                    if (!IsVisible)
                        Show();

                    if (_autoHiddenWindows.Count > 0)
                    {
                        foreach (Window window in _autoHiddenWindows.ToArray())
                        {
                            try
                            {
                                window.ShowInTaskbar = false;
                                if (window.WindowState == WindowState.Minimized)
                                    window.WindowState = WindowState.Normal;
                                if (!window.IsVisible)
                                    window.Show();
                            }
                            catch (InvalidOperationException)
                            {
                                // A window that was closed while hidden cannot be shown again.
                            }
                        }
                    }

                    Window activationTarget = _autoHiddenActiveWindow is { IsLoaded: true, IsVisible: true } target
                        && new WindowInteropHelper(target).Handle != IntPtr.Zero
                            ? target
                            : this;
                    _autoHiddenWindows.Clear();
                    _autoHiddenActiveWindow = null;

                    ForceToForeground(activationTarget);
                }
                finally
                {
                    // Defer clear so startup/activation focus churn cannot instantly re-hide.
                    Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
                    {
                        App.SuppressAutoHide = false;
                    });
                }
            });
        }

        private static void ForceToForeground(Window window)
        {
            if (!window.Dispatcher.CheckAccess())
            {
                window.Dispatcher.BeginInvoke(() => ForceToForeground(window));
                return;
            }

            if (!window.IsVisible)
            {
                window.Show();
            }
            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }

            IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();

            ShowWindow(hwnd, SwRestore);
            BringWindowToTop(hwnd);

            // 1. Primary activation path: standard OS foreground request and WPF activation
            SetForegroundWindow(hwnd);
            window.Activate();

            // 2. Fallback: If the window is not yet foreground, try guarded thread input attachment
            if (GetForegroundWindow() != hwnd)
            {
                IntPtr foreground = GetForegroundWindow();
                uint targetThread = GetWindowThreadProcessId(hwnd, IntPtr.Zero);
                if (targetThread == 0)
                {
                    targetThread = GetCurrentThreadId();
                }

                uint foregroundThread = foreground != IntPtr.Zero ? GetWindowThreadProcessId(foreground, IntPtr.Zero) : 0;

                if (foregroundThread != 0 && foregroundThread != targetThread)
                {
                    if (AttachThreadInput(targetThread, foregroundThread, true))
                    {
                        try
                        {
                            BringWindowToTop(hwnd);
                            SetForegroundWindow(hwnd);
                            window.Activate();
                        }
                        finally
                        {
                            AttachThreadInput(targetThread, foregroundThread, false);
                        }
                    }
                }
            }

            // Windows may keep focus on shell UI (e.g. ShellExperienceHost); that is expected and not an error.
        }

        private const int SwRestore = 9;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        public void HideApplicationWindowsForAutoHide()
        {
            RunOnUi(() =>
            {
                _autoHiddenWindows.Clear();
                _autoHiddenActiveWindow = Application.Current.Windows
                    .OfType<Window>()
                    .FirstOrDefault(window => window.IsActive && window is not Views.ModeToastWindow);

                foreach (Window window in Application.Current.Windows.OfType<Window>().ToArray())
                {
                    if (!window.IsVisible)
                        continue;
                    if (window is Views.ModeToastWindow)
                    {
                        window.Hide();
                        continue;
                    }
                    if (window != this)
                        _autoHiddenWindows.Add(window);
                }

                foreach (Window window in _autoHiddenWindows)
                {
                    window.ShowInTaskbar = false;
                    if (window.WindowState == WindowState.Minimized)
                        window.WindowState = WindowState.Normal;
                    window.Hide();
                }

                // Tray-only: never leave a minimized taskbar button behind.
                ShowInTaskbar = false;
                if (WindowState == WindowState.Minimized)
                    WindowState = WindowState.Normal;
                Hide();
            });
        }

        public void QuitApplication()
        {
            RunOnUi(() =>
            {
                _isExplicitExit = true;
                Close();
                Application.Current.Shutdown();
            });
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_isExplicitExit)
            {
                e.Cancel = true;
                HideApplicationWindowsForAutoHide();
                return;
            }
            base.OnClosing(e);
        }

        public void ToggleAppVisibility()
        {
            RunOnUi(() =>
            {
                if (IsApplicationActive())
                    HideApplicationWindowsForAutoHide();
                else
                    ShowAndRestore();
            });
        }

        private static bool IsApplicationHwnd(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || Application.Current == null) return false;

            foreach (Window window in Application.Current.Windows.OfType<Window>())
            {
                if (window is Views.ModeToastWindow) continue;
                if (!window.IsVisible) continue;

                IntPtr handle = new WindowInteropHelper(window).Handle;
                if (handle != IntPtr.Zero && handle == hwnd)
                    return true;
            }
            return false;
        }

        private static bool IsApplicationActive()
        {
            if (Application.Current == null) return false;

            IntPtr foreground = GetForegroundWindow();

            // Authoritative Win32 check: Does the OS foreground window belong to a visible PreySense window?
            if (IsApplicationHwnd(foreground))
                return true;

            // Secondary fallback: WPF window activation state
            foreach (Window window in Application.Current.Windows.OfType<Window>())
            {
                if (window is Views.ModeToastWindow) continue;
                if (window.IsVisible && window.IsActive)
                    return true;
            }

            return false;
        }
        private void ApplyResponsivePercentageSize()
        {
            var workArea = SystemParameters.WorkArea;
            Width = 500;
            double h = double.IsNaN(Height) || Height <= 0 ? 580 : Height;
            Left = workArea.Left + (workArea.Width - Width) / 2;
            Top = Math.Max(workArea.Top + 40, workArea.Top + (workArea.Height - h) / 2);
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            HideApplicationWindowsForAutoHide();
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            ShowDialog<Views.SettingsWindow>();
        }

        private void ModeSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
                vm.EnsureGpuHardwareInitialized();

            ShowModeSettingsWindow();
        }

        private void FansButton_Click(object sender, RoutedEventArgs e)
        {
            if (_fanCurveWindow is { } existing)
            {
                if (!existing.IsVisible) existing.Show();
                if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
                existing.Activate();
                return;
            }
            var window = new Views.FanCurveWindow { Owner = this, DataContext = DataContext };
            _fanCurveWindow = window;
            window.Closed += (_, _) => _fanCurveWindow = null;
            window.ShowDialog();
        }

        private Views.ModeSettingsWindow CreateModeSettingsWindow()
        {
            return new Views.ModeSettingsWindow
            {
                Owner = this,
                DataContext = DataContext,
                ShowInTaskbar = false
            };
        }

        private void ShowModeSettingsWindow()
        {
            var dialog = CreateModeSettingsWindow();
            dialog.ShowDialog();
        }

        private void KeyboardButton_Click(object sender, RoutedEventArgs e)
        {
            ShowDialog<Views.KeyboardWindow>();
        }

        private void MetricsButton_Click(object sender, RoutedEventArgs e)
        {
            ShowDialog<Views.MetricsWindow>();
        }

        private void ShowDialog<TWindow>() where TWindow : Window, new()
        {
            var window = new TWindow
            {
                Owner = this,
                DataContext = DataContext
            };
            window.ShowDialog();
        }

        private void QuitButton_Click(object sender, RoutedEventArgs e)
        {
            QuitApplication();
        }
    }
}
