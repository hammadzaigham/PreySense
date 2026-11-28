using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace PreySense
{
    /// <summary>
    /// The chassis Mode / Turbo button on PHN16 reports as a raw HID input on
    /// ACPI\1025174B (often collection COL05, report 04 85 FF). It is not a
    /// normal WM_KEYDOWN and often never reaches APGeEvent. Opening that HID
    /// path is how PreySense takes control of the key.
    /// </summary>
    public sealed class HidModeKeyWatcher : IDisposable
    {
        public const string AcerEcHardwareId = "1025174B";
        public const ushort ChiconyVendorId = 0x04F2;
        public const ushort ChiconySpecialKeyUsagePage = 0x0088;
        private static readonly ushort[] ChiconyProductIds = { 0x0117, 0x011A, 0x0119 };
        private const int HidpStatusSuccess = 0x00110000;
        private const uint GenericRead = 0x80000000;
        private const uint GenericWrite = 0x40000000;
        private const uint FileShareReadWrite = 3;
        private const uint OpenExisting = 3;
        private const uint FileFlagOverlapped = 0x40000000;
        private const int UniqueReportCap = 48;

        public enum HotkeyKind
        {
            None = 0,
            Mode = 1,
            BrightnessDown = 2,
            BrightnessUp = 3
        }

        private readonly Action _onModeKeyPressed;
        private readonly Action<int>? _onBrightnessDelta;
        private readonly CancellationTokenSource _cts = new();
        private readonly HashSet<string> _seenReports = new(StringComparer.Ordinal);
        private readonly object _debounceLock = new();
        private long _lastModeTimestamp;
        private long _lastBrightnessTimestamp;
        private volatile bool _disposed;

        public HidModeKeyWatcher(Action onModeKeyPressed, Action<int>? onBrightnessDelta = null)
        {
            _onModeKeyPressed = onModeKeyPressed ?? throw new ArgumentNullException(nameof(onModeKeyPressed));
            _onBrightnessDelta = onBrightnessDelta;
            var token = _cts.Token;
            int readers = 0;
            foreach (string path in EnumerateWatchedHidPaths())
            {
                readers++;
                _ = Task.Run(() => ReadLoopAsync(path, token));
            }
            if (readers == 0)
                AppLogger.Log("TurboKey: no 1025174B or Chicony 0x0088 HID collections found for the chassis Mode key.");
        }

        private static IEnumerable<string> EnumerateWatchedHidPaths()
        {
            foreach (string path in EnumerateAcerEcHidPaths())
                yield return path;
            foreach (string path in EnumerateChiconySpecialKeyHidPaths())
                yield return path;
        }

        internal static IEnumerable<string> EnumerateAcerEcHidPaths()
        {
            foreach (string path in EnekRgbClient.EnumerateAllHidPaths())
            {
                if (path.IndexOf(AcerEcHardwareId, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                // Wireless-radio collection is not the Mode key.
                if (path.IndexOf("COL08", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                // Keyboard/mouse class interfaces (…\kbd, …\mou) are exclusive to
                // Windows. CreateFile GENERIC_READ returns ERROR_ACCESS_DENIED (5)
                // even as admin; a reboot does not change that. COL05 on PHN16S is
                // one of those. Mode/Turbo still arrives via APGeEvent and the
                // low-level keyboard hook (scan 0x85).
                if (IsWindowsExclusiveHidPath(path))
                    continue;
                yield return path;
            }
        }

        public static bool IsWindowsExclusiveHidPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;
            return path.EndsWith(@"\kbd", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(@"\mou", StringComparison.OrdinalIgnoreCase);
        }

        internal static IEnumerable<string> EnumerateChiconySpecialKeyHidPaths()
        {
            foreach (string path in EnekRgbClient.EnumerateAllHidPaths())
            {
                if (!IsChiconySpecialKeyPath(path))
                    continue;
                if (TryGetHidCaps(path, out var caps) && IsChiconySpecialKeyCaps(caps.UsagePage, caps.InputReportByteLength))
                    yield return path;
            }
        }

        public static bool IsChiconySpecialKeyPath(string path)
        {
            if (string.IsNullOrEmpty(path) || IsWindowsExclusiveHidPath(path))
                return false;
            if (path.IndexOf($"VID_{ChiconyVendorId:X4}", StringComparison.OrdinalIgnoreCase) < 0)
                return false;
            foreach (ushort pid in ChiconyProductIds)
            {
                if (path.IndexOf($"PID_{pid:X4}", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        public static bool IsChiconySpecialKeyCaps(ushort usagePage, ushort inputReportByteLength) =>
            usagePage == ChiconySpecialKeyUsagePage && inputReportByteLength >= 3;

        private static bool TryGetHidCaps(string path, out HidCaps caps)
        {
            caps = default;
            try
            {
                using SafeFileHandle handle = CreateFile(
                    path, 0, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
                if (handle.IsInvalid || !HidD_GetPreparsedData(handle, out IntPtr preparsed))
                    return false;
                try
                {
                    return HidP_GetCaps(preparsed, out caps) == HidpStatusSuccess;
                }
                finally
                {
                    HidD_FreePreparsedData(preparsed);
                }
            }
            catch
            {
                return false;
            }
        }

        public static HotkeyKind ClassifyReport(byte[] buffer, int length)
        {
            if (buffer == null || length < 2)
                return HotkeyKind.None;
            // Feature/command channel for lighting and EC misc — never a key press.
            if (buffer[0] == 0xA0)
                return HotkeyKind.None;

            byte id = buffer[0];
            byte payload = buffer[1];

            // Fn+F11 / Fn+F12 and keyboard brightness events:
            // Check brightness FIRST so brightness reports are never misinterpreted as Mode.
            bool vendorKey = id == 0x04 && length >= 3 && buffer[2] == 0xFF;
            if (vendorKey && payload is 0x11 or 0x1A or 0x44 or 0x57 or 0x78 or 0x7A or 0x7D or 0x8B or 0xC5)
                return HotkeyKind.BrightnessDown;
            if (vendorKey && payload is 0x12 or 0x1B or 0x45 or 0x58 or 0x07 or 0x79 or 0x7C or 0x8C or 0xC4)
                return HotkeyKind.BrightnessUp;

            // Consumer-control collection: Keyboard Brightness Decrement/Increment.
            if (id is 0x01 or 0x03 && length <= 8)
            {
                if (payload is 0x11 or 0x1A or 0x7A or 0x8B or 0xC5)
                    return HotkeyKind.BrightnessDown;
                if (payload is 0x12 or 0x1B or 0x79 or 0x8C or 0xC4)
                    return HotkeyKind.BrightnessUp;
            }

            if (length <= 8)
            {
                if (ContainsByte(buffer, length, 0x7A) || ContainsByte(buffer, length, 0x11) || ContainsByte(buffer, length, 0x7D))
                    return HotkeyKind.BrightnessDown;
                if (ContainsByte(buffer, length, 0x79) || ContainsByte(buffer, length, 0x12) || ContainsByte(buffer, length, 0x7C))
                    return HotkeyKind.BrightnessUp;
            }

            // PHN16-71 Chicony 0x0088 special-key collection: PREDATOR key sends 04 81 FF
            if (vendorKey && payload == 0x81)
                return HotkeyKind.Mode;
            // PHN16-73 / PHN16S: report ID 0x04, payload 0x85 0xFF
            if (id == 0x04 && payload == 0x85)
                return HotkeyKind.Mode;
            if (length >= 3 && id == 0x04 && payload == 0x05 && buffer[2] == 1)
                return HotkeyKind.Mode;
            for (int i = 0; i < length - 1; i++)
            {
                if (buffer[i] == 0x85 && buffer[i + 1] == 0xFF)
                    return HotkeyKind.Mode;
            }

            return HotkeyKind.None;
        }

        private static bool ContainsByte(byte[] buffer, int length, byte value)
        {
            for (int i = 1; i < length; i++)
            {
                if (buffer[i] == value)
                    return true;
            }
            return false;
        }

        private async Task ReadLoopAsync(string path, CancellationToken token)
        {
            if (token.IsCancellationRequested || _disposed) return;
            AppLogger.Log($"TurboKey: opening HID Mode key path '{path}'.");
            using SafeFileHandle handle = CreateFile(
                path,
                GenericRead | GenericWrite,
                FileShareReadWrite,
                IntPtr.Zero,
                OpenExisting,
                FileFlagOverlapped,
                IntPtr.Zero);

            if (handle.IsInvalid)
            {
                handle.Dispose();
                using SafeFileHandle readOnly = CreateFile(
                    path, GenericRead, FileShareReadWrite, IntPtr.Zero,
                    OpenExisting, FileFlagOverlapped, IntPtr.Zero);
                if (readOnly.IsInvalid)
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err != 5)
                        AppLogger.Log($"TurboKey: HID '{ShortCollection(path)}' unavailable (Win32 {err}).");
                    return;
                }

                await PumpReportsAsync(path, readOnly, token).ConfigureAwait(false);
                return;
            }

            await PumpReportsAsync(path, handle, token).ConfigureAwait(false);
        }

        private async Task PumpReportsAsync(string path, SafeFileHandle handle, CancellationToken token)
        {
            byte[] buffer = new byte[256];
            try
            {
                using var stream = new FileStream(handle, FileAccess.Read, bufferSize: 1, isAsync: true);
                while (!token.IsCancellationRequested)
                {
                    int read;
                    try
                    {
                        // ReadAsync is completed by Windows I/O completion, or canceled
                        // on shutdown. No periodic wait or manually managed OVERLAPPED.
                        read = await stream.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
                    }
                    catch (IOException ex) when (!token.IsCancellationRequested)
                    {
                        int error = ex.HResult & 0xFFFF;
                        if (error is 6 or 995 or 1167) break;
                        await Task.Delay(250, token).ConfigureAwait(false);
                        continue;
                    }
                    if (read <= 0 || token.IsCancellationRequested || _disposed) break;
                    bool logSeen;
                    lock (_seenReports)
                    {
                        logSeen = _seenReports.Count < UniqueReportCap;
                    }
                    if (logSeen)
                    {
                        string hex = ToHex(buffer, read);
                        lock (_seenReports)
                        {
                            if (_seenReports.Count < UniqueReportCap && _seenReports.Add(hex))
                                AppLogger.Log($"TurboKey: HID {ShortCollection(path)} report {hex}");
                        }
                    }

                    HotkeyKind kind = ClassifyReport(buffer, read);
                    if (kind == HotkeyKind.None)
                        continue;

                    long nowTicks = System.Diagnostics.Stopwatch.GetTimestamp();
                    if (kind == HotkeyKind.Mode)
                    {
                        lock (_debounceLock)
                        {
                            if (_lastModeTimestamp != 0 && System.Diagnostics.Stopwatch.GetElapsedTime(_lastModeTimestamp, nowTicks).TotalMilliseconds < 80)
                                continue;
                            _lastModeTimestamp = nowTicks;
                        }
                        AppLogger.Log($"TurboKey: chassis Mode / Turbo HID report {ToHex(buffer, read)}.");
                        try { _onModeKeyPressed(); }
                        catch (Exception ex) { AppLogger.Error($"TurboKey: Mode callback failed: {ex.Message}"); }
                    }
                    else if (_onBrightnessDelta != null)
                    {
                        lock (_debounceLock)
                        {
                            if (_lastBrightnessTimestamp != 0 && System.Diagnostics.Stopwatch.GetElapsedTime(_lastBrightnessTimestamp, nowTicks).TotalMilliseconds < 30)
                                continue;
                            _lastBrightnessTimestamp = nowTicks;
                        }
                        int delta = kind == HotkeyKind.BrightnessUp ? 1 : -1;
                        AppLogger.Log($"TurboKey: Fn brightness HID report {ToHex(buffer, read)} delta={delta}.");
                        try { _onBrightnessDelta(delta); }
                        catch (Exception ex) { AppLogger.Error($"TurboKey: Brightness callback failed: {ex.Message}"); }
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested || _disposed) { }
            catch (ObjectDisposedException) when (_disposed) { }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested && !_disposed) AppLogger.Log($"TurboKey: HID reader failed on '{path}': {ex.Message}");
            }
        }

        private static string ShortCollection(string path)
        {
            int col = path.IndexOf("COL", StringComparison.OrdinalIgnoreCase);
            if (col >= 0 && col + 5 <= path.Length)
                return path.Substring(col, 5);
            return path.IndexOf($"VID_{ChiconyVendorId:X4}", StringComparison.OrdinalIgnoreCase) >= 0
                ? $"{ChiconyVendorId:X4}"
                : AcerEcHardwareId;
        }

        private static string ToHex(byte[] buffer, int length)
        {
            var chars = new char[Math.Min(length, 16) * 3];
            int n = Math.Min(length, 16);
            for (int i = 0; i < n; i++)
            {
                byte b = buffer[i];
                chars[i * 3] = GetHex(b >> 4);
                chars[i * 3 + 1] = GetHex(b & 0xF);
                chars[i * 3 + 2] = i + 1 == n ? ' ' : ' ';
            }
            return new string(chars, 0, n * 3).TrimEnd();
        }

        private static char GetHex(int nibble) => (char)(nibble < 10 ? '0' + nibble : 'A' + (nibble - 10));

        [StructLayout(LayoutKind.Sequential)]
        private struct HidCaps
        {
            public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices;
            public ushort NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern SafeFileHandle CreateFile(
            string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr data);

        [DllImport("hid.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool HidD_FreePreparsedData(IntPtr data);

        [DllImport("hid.dll")]
        private static extern int HidP_GetCaps(IntPtr data, out HidCaps caps);

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
