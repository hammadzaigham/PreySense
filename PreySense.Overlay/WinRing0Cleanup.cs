using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using PreySense.Helpers;

namespace PreySense.Overlay
{
    /// <summary>
    /// Older builds installed LibreHardwareMonitor's WinRing0 as the kernel service
    /// R0PreySense. Defender flags that driver. Remove the service and the file.
    /// </summary>
    internal static class WinRing0Cleanup
    {
        private static int _started;

        public static void RemoveInstalledDriver()
        {
            if (Interlocked.Exchange(ref _started, 1) != 0)
                return;

            try
            {
                string? processPath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
                string processName = string.IsNullOrWhiteSpace(processPath)
                    ? "PreySense"
                    : Path.GetFileNameWithoutExtension(processPath);
                StopAndDelete("R0" + processName);
                if (!processName.Equals("PreySense", StringComparison.OrdinalIgnoreCase))
                    StopAndDelete("R0PreySense");

                string dataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "PreySense");
                TryDelete(Path.Combine(dataDir, "WinRing0x64.sys"));
                TryDelete(Path.Combine(dataDir, "WinRing0x64.sys.tmp"));
                if (!string.IsNullOrWhiteSpace(processPath))
                    TryDelete(Path.ChangeExtension(processPath, ".sys"));
            }
            catch (Exception ex)
            {
                AppLogger.Log($"WinRing0Cleanup: {ex.Message}");
            }
        }

        private static void StopAndDelete(string serviceName)
        {
            RunSc($"stop \"{serviceName}\"");
            RunSc($"delete \"{serviceName}\"");
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"WinRing0Cleanup: could not delete {path}: {ex.Message}");
            }
        }

        private static void RunSc(string args)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo("sc.exe", args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });
                process?.WaitForExit(4000);
            }
            catch (Exception ex)
            {
                AppLogger.Log($"WinRing0Cleanup: sc {args} failed: {ex.Message}");
            }
        }
    }
}
