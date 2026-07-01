namespace PreySense
{
    internal static class Program
    {
        public const string VersionString = "1.3.1";
        public static MainForm? settingsForm;
        public static Overlay.HardwareOverlay? hardwareOverlay;
        private static CancellationTokenSource? _overlayUnloadCts;
        private static System.Threading.Mutex? _appMutex;

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern uint RegisterWindowMessage(string lpString);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        [STAThread]
        static void Main(string[] args)
        {
            _appMutex = new System.Threading.Mutex(true, "PreySenseApp_Unique_System_Mutex_999", out bool createdNew);
            if (!createdNew)
            {
                uint wmShowMe = RegisterWindowMessage("PREY_SENSE_SHOW_INSTANCE");
                EnumWindows((hWnd, lParam) =>
                {
                    var sb = new System.Text.StringBuilder(256);
                    GetWindowText(hWnd, sb, sb.Capacity);
                    if (sb.ToString().StartsWith("Prey Sense", StringComparison.OrdinalIgnoreCase))
                    {
                        PostMessage(hWnd, wmShowMe, IntPtr.Zero, IntPtr.Zero);
                        return false; // Found our main window, stop enumerating
                    }
                    return true;
                }, IntPtr.Zero);
                return;
            }

            ApplicationConfiguration.Initialize();
            RegisterUnhandledExceptionHandlers();

            if (TryRunHeadlessCommand(args))
            {
                return;
            }

            if (!PawnIO.IntelMsr.IsPawnIoAvailable(out _))
            {
                var result = Dialogs.ConfirmDialog.Show(
                    null,
                    "PawnIO is not installed. It is required to apply CPU power limits.\n\nWould you like to install it now?",
                    "PawnIO Required",
                    "Install PawnIO",
                    "Continue"
                );
                if (result == DialogResult.Yes)
                {
                    Dialogs.ConfirmDialog.Show(
                        null,
                        "Downloading the latest PawnIO driver installer.",
                        "Installing PawnIO",
                        "OK",
                        ""
                    );

                    bool installed = Task.Run(async () =>
                    {
                        string tempPath = Path.Combine(Path.GetTempPath(), "PawnIO_setup.exe");
                        try
                        {
                            using var client = new System.Net.Http.HttpClient();
                            var responseBytes = await client.GetByteArrayAsync("https://github.com/namazso/PawnIO.Setup/releases/latest/download/PawnIO_setup.exe");
                            await File.WriteAllBytesAsync(tempPath, responseBytes);

                            var psi = new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = tempPath,
                                Arguments = "-install -silent",
                                UseShellExecute = true,
                                Verb = "runas"
                            };

                            using var process = System.Diagnostics.Process.Start(psi);
                            if (process != null)
                            {
                                await process.WaitForExitAsync();
                                return process.ExitCode == 0;
                            }
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Log($"PawnIO automatic installation failed: {ex.Message}");
                        }
                        finally
                        {
                            try
                            {
                                if (File.Exists(tempPath))
                                    File.Delete(tempPath);
                            }
                            catch { }
                        }
                        return false;
                    }).GetAwaiter().GetResult();

                    if (installed && PawnIO.IntelMsr.IsPawnIoAvailable(out _))
                    {
                        Dialogs.ConfirmDialog.Show(null, "PawnIO has been successfully installed!", "PawnIO Installed", "OK", "");
                    }
                    else
                    {
                        var openUrl = Dialogs.ConfirmDialog.Show(
                            null,
                            "PawnIO automatic installation did not complete successfully.\n\nWould you like to open the website to download and install it manually?",
                            "Installation Failed",
                            "Yes",
                            "No"
                        );
                        if (openUrl == DialogResult.Yes)
                        {
                            try
                            {
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = "https://pawnio.eu/",
                                    UseShellExecute = true
                                });
                            }
                            catch (Exception ex)
                            {
                                AppLogger.Log($"Failed to open PawnIO URL: {ex.Message}");
                            }
                        }
                    }
                }
            }

            settingsForm = new MainForm();
            Application.Run(settingsForm);
        }

        private static bool TryRunHeadlessCommand(string[] args)
        {
            if (args.Length == 0)
            {
                return false;
            }

            if (args[0].Equals("--nvapi-diagnostics", StringComparison.OrdinalIgnoreCase))
            {
                using var control = new Gpu.NvidiaGpuControl();
                string description = control.DescribePerformanceStates();
                Console.WriteLine(description);
                return true;
            }

            if (args[0].Equals("--nvapi-verify", StringComparison.OrdinalIgnoreCase))
            {
                int core = args.Length > 1 && int.TryParse(args[1], out int parsedCore) ? parsedCore : 100;
                int memory = args.Length > 2 && int.TryParse(args[2], out int parsedMemory) ? parsedMemory : 200;

                using var control = new Gpu.NvidiaGpuControl();
                AppLogger.Log($"NVAPI verify: requested core=+{core}MHz, memory=+{memory}MHz.");
                Console.WriteLine(control.DescribePerformanceStates());

                bool readBefore = control.GetClocks(out int beforeCore, out int beforeMemory);
                int result = control.SetClocks(core, memory);
                Thread.Sleep(500);
                bool readAfter = control.GetClocks(out int afterCore, out int afterMemory);

                string summary =
                    $"NVAPI verify: readBefore={readBefore} before=+{beforeCore}/+{beforeMemory}MHz, " +
                    $"setResult={result}, readAfter={readAfter} after=+{afterCore}/+{afterMemory}MHz.";
                AppLogger.Log(summary);
                Console.WriteLine(summary);

                Environment.ExitCode = result < 0 || !readAfter ? 1 : 0;
                return true;
            }

            return false;
        }

        private static void RegisterUnhandledExceptionHandlers()
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) =>
                ShowError("Prey Sense - Error", e.Exception);

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                {
                    ShowError("Prey Sense - Fatal Error", ex);
                }
            };
        }

        private static void ShowError(string title, Exception ex)
        {
            MessageBox.Show(
                $"{ex.Message}\n\n{ex.StackTrace}",
                title,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        public static Overlay.HardwareOverlay GetHardwareOverlay()
        {
            CancelHardwareOverlayUnload();
            return hardwareOverlay ??= new Overlay.HardwareOverlay();
        }

        public static void CancelHardwareOverlayUnload()
        {
            _overlayUnloadCts?.Cancel();
            _overlayUnloadCts?.Dispose();
            _overlayUnloadCts = null;
        }

        public static void ScheduleHardwareOverlayUnload()
        {
            CancelHardwareOverlayUnload();

            var cts = new CancellationTokenSource();
            _overlayUnloadCts = cts;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(5000, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (cts.IsCancellationRequested || Overlay.AppConfig.IsOverlay())
                    return;

                settingsForm?.BeginInvoke(new Action(() =>
                {
                    if (cts.IsCancellationRequested || Overlay.AppConfig.IsOverlay())
                        return;

                    hardwareOverlay?.Dispose();
                    hardwareOverlay = null;
                    CancelHardwareOverlayUnload();
                    Overlay.ProcessMemoryHelper.TrimAfter();
                }));
            });
        }
    }
}
