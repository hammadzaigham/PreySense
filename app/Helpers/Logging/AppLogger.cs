using System.IO;

namespace PreySense
{
    public static class AppLogger
    {
        private static readonly string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug.log");
        private static readonly object LockObj = new();

        public static void Log(string message)
        {
            lock (LockObj)
            {
                try
                {
                    string logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
                    File.AppendAllText(LogPath, logLine);
                    System.Diagnostics.Debug.Write(logLine);
                }
                catch
                {
                    // Ignore logging errors to prevent crashes
                }
            }
        }

        public static void Clear()
        {
            lock (LockObj)
            {
                try
                {
                    if (File.Exists(LogPath))
                    {
                        File.Delete(LogPath);
                    }
                }
                catch { }
            }
        }
    }
}
