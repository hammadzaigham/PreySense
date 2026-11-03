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

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            Services.LocalizationService.Initialize();

            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            Deactivated += (_, _) =>
            {
                // Deactivated is application-wide, so moving between PreySense-owned
                // windows does not trigger this. Wait until activation has settled.
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
                {
                    if (SuppressAutoHide)
                    {
                        return;
                    }
                    if (Windows.OfType<Window>().Any(window => window.IsActive))
                    {
                        return;
                    }

                    if (MainWindow is not MainWindow mainWindow || !mainWindow.IsVisible)
                    {
                        return;
                    }

                    mainWindow.HideApplicationWindowsForAutoHide();
                });
            };

            // Global crash exception logging
            DispatcherUnhandledException += (s, args) =>
            {
                try
                {
                    var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
                    File.WriteAllText(logPath, args.Exception.ToString());
                }
                catch { }
            };
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                try
                {
                    var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash_domain.log");
                    File.WriteAllText(logPath, args.ExceptionObject?.ToString());
                }
                catch { }
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

            if (!startMinimized)
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
