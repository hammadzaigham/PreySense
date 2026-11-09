using System.IO;

namespace PreySense
{
    public static class AppLogger
    {
        public static readonly string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug.log");
        private static readonly object LockObj = new();
        private static StreamWriter? _writer;

        public static void Log(string message)
        {
            if (!IsError(message))
                return;
            Write(message);
        }

        public static void Error(string message) => Write(message);

        private static bool IsError(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            return message.Contains("error", StringComparison.OrdinalIgnoreCase)
                || message.Contains("fail", StringComparison.OrdinalIgnoreCase)
                || message.Contains("exception", StringComparison.OrdinalIgnoreCase);
        }

        private static void Write(string message)
        {
            if (string.IsNullOrEmpty(message))
                return;

            lock (LockObj)
            {
                try
                {
                    string logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
                    _writer ??= new StreamWriter(new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), System.Text.Encoding.UTF8)
                    {
                        AutoFlush = true
                    };
                    _writer.WriteLine(logLine);
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
                    _writer?.Dispose();
                    _writer = null;
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
