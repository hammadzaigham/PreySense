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
            Loaded += (_, _) =>
            {
                vm.NotifyMainWindowLoaded();
                OverlayBridge.Warmup();
                OverlayBridge.StartIfNeeded();
            };
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

            IsVisibleChanged += (_, _) => vm.SetTelemetryPollingActive(IsVisible && WindowState != WindowState.Minimized);
            StateChanged += (_, _) => vm.SetTelemetryPollingActive(IsVisible && WindowState != WindowState.Minimized);

            Closed += (s, e) =>
            {
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
                if (!IsVisible)
                {
                    Show();
                }
                if (WindowState == WindowState.Minimized)
                {
                    WindowState = WindowState.Normal;
                }

                if (_autoHiddenWindows.Count > 0)
                {
                    foreach (Window window in _autoHiddenWindows.ToArray())
                    {
                        try
                        {
                            if (!window.IsVisible)
                            {
                                window.Show();
                            }
                        }
                        catch (InvalidOperationException)
                        {
                            // A window that was closed while hidden cannot be shown again.
                        }
                    }
                }

                Window activationTarget = _autoHiddenActiveWindow is { IsVisible: true }
                    ? _autoHiddenActiveWindow
                    : this;
                _autoHiddenWindows.Clear();
                _autoHiddenActiveWindow = null;

                Dispatcher.BeginInvoke(DispatcherPriority.Input, () => ForceToForeground(activationTarget));
            });
        }

        private static void ForceToForeground(Window window)
        {
            if (!window.IsVisible)
            {
                window.Show();
            }
            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }

            IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();
            IntPtr foreground = GetForegroundWindow();
            uint thisThread = GetCurrentThreadId();
            uint foregroundThread = GetWindowThreadProcessId(foreground, IntPtr.Zero);
            bool attached = foregroundThread != 0 && foregroundThread != thisThread
                && AttachThreadInput(foregroundThread, thisThread, true);

            try
            {
                ShowWindow(hwnd, SwRestore);
                SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpShowWindow);
                BringWindowToTop(hwnd);
                SetForegroundWindow(hwnd);
                window.Activate();
                window.Focus();
                SetWindowPos(hwnd, HwndNoTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpShowWindow);
            }
            finally
            {
                if (attached)
                {
                    AttachThreadInput(foregroundThread, thisThread, false);
                }
            }
        }

        private const int SwRestore = 9;
        private static readonly IntPtr HwndTopmost = new(-1);
        private static readonly IntPtr HwndNoTopmost = new(-2);
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpShowWindow = 0x0040;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

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
                    window.Hide();
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
                Hide();
                return;
            }
            base.OnClosing(e);
        }

        public void ToggleAppVisibility()
        {
            RunOnUi(() =>
            {
                bool anyVisible = Application.Current.Windows
                    .OfType<Window>()
                    .Any(window => window.IsVisible);
                if (anyVisible)
                    HideApplicationWindowsForAutoHide();
                else
                    ShowAndRestore();
            });
        }

        private void ApplyResponsivePercentageSize()
        {
            var workArea = SystemParameters.WorkArea;
            Width = 440;
            double h = double.IsNaN(Height) || Height <= 0 ? 580 : Height;
            Left = workArea.Left + (workArea.Width - Width) / 2;
            Top = Math.Max(workArea.Top + 40, workArea.Top + (workArea.Height - h) / 2);
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            ShowDialog<Views.AdvancedSettingsWindow>();
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

        private void AdvancedButton_Click(object sender, RoutedEventArgs e)
        {
            ShowDialog<Views.AdvancedWindow>();
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
            dialog.SyncFromViewModel();
            dialog.ShowDialog();
        }

        private void GamesButton_Click(object sender, RoutedEventArgs e)
        {
            ShowDialog<Views.GamesWindow>();
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
