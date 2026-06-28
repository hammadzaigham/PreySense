namespace PreySense
{
    internal static class Program
    {
        public const string VersionString = "1.1.0";
        public static MainForm? settingsForm;
        public static Overlay.HardwareOverlay? hardwareOverlay;
        private static CancellationTokenSource? _overlayUnloadCts;

        [STAThread]
        static void Main(string[] args)
        {
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
