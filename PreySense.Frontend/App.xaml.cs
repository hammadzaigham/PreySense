using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace PreySense.Frontend
{
    public partial class App : Application
    {
        public static bool SuppressAutoHide { get; set; }
        private static Mutex? _singleInstanceMutex;
        private static EventWaitHandle? _showAppEvent;
        private static Thread? _showAppThread;

        public App()
        {
            // Install before InitializeComponent/OnStartup can load resources or configuration.
            DispatcherUnhandledException += (_, args) =>
            {
                args.Handled = true;
                ShowStartupFailure(args.Exception);
                Shutdown(1);
            };
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                Services.StartupDiagnostics.CreateReport(args.ExceptionObject as Exception
                    ?? new Exception(args.ExceptionObject?.ToString() ?? "Unknown application failure."));
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            try { StartApplication(e); }
            catch (Exception ex)
            {
                ShowStartupFailure(ex);
                Shutdown(1);
            }
        }

        private bool _failureReported;
        private void ShowStartupFailure(Exception exception)
        {
            if (_failureReported) return;
            _failureReported = true;
            string report = Services.StartupDiagnostics.CreateReport(exception);
            try { MessageBox.Show(report, "PreySense could not start", MessageBoxButton.OK, MessageBoxImage.Error); }
            catch { /* Logging must still work when Windows cannot display a dialog. */ }
        }

        private void StartApplication(StartupEventArgs e)
        {
            base.OnStartup(e);
            Services.LocalizationService.Initialize();
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Deactivated += (_, _) =>
            {
                if (MainWindow is MainWindow mainWindow) mainWindow.QueueAutoHide();
            };

            // Single instance Mutex check
            _singleInstanceMutex = new Mutex(true, "PreySenseApp_Unique_System_Mutex_999", out bool createdNew);
            if (!createdNew)
            {
                try
                {
                    using var ev = EventWaitHandle.OpenExisting("PreySense_Show_App_Event");
                    ev.Set();
                }
                catch { }

                Shutdown(0);
                return;
            }

            _ = Task.Run(PreySense.Overlay.WinRing0Cleanup.RemoveInstalledDriver);

            // Start cross-process show event listener
            try
            {
                _showAppEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "PreySense_Show_App_Event");
                _showAppThread = new Thread(() =>
                {
                    while (true)
                    {
                        try
                        {
                            _showAppEvent.WaitOne();
                            Dispatcher.BeginInvoke(() =>
                            {
                                if (MainWindow is MainWindow mw)
                                {
                                    mw.ShowAndRestore();
                                }
                            });
                        }
                        catch (ThreadAbortException) { break; }
                        catch { }
                    }
                })
                {
                    IsBackground = true,
                    Name = "PreySense_ShowAppListener"
                };
                _showAppThread.Start();
            }
            catch { }

            bool startMinimized = e.Args.Any(arg =>
                arg.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("-minimized", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("-hidden", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--hidden", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("/minimized", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("/hidden", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--tray", StringComparison.OrdinalIgnoreCase));

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            // Hidden launches must initialize without waiting for WPF Loaded.
            ((ViewModels.MainViewModel)mainWindow.DataContext).NotifyApplicationReady();
            Services.OverlayBridge.StartIfNeeded();
            _ = Task.Run(Services.StartupTaskService.RepairLogonTask);

            _ = Services.UpdateCheckService.CheckForUpdatesAsync();

            if (startMinimized)
            {
                // Ensure HWND/tray exist without a taskbar button; stay tray-only until opened.
                mainWindow.ShowInTaskbar = false;
                mainWindow.WindowState = WindowState.Normal;
                mainWindow.Hide();
            }
            else
            {
                mainWindow.ShowAndRestore();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                if (MainWindow?.DataContext is ViewModels.MainViewModel vm)
                    vm.PersistSessionSettings();
                Views.ModeToastWindow.Shutdown();
                _showAppEvent?.Dispose();
                _singleInstanceMutex?.ReleaseMutex();
                _singleInstanceMutex?.Dispose();
            }
            catch { }

            base.OnExit(e);
        }
    }
}
