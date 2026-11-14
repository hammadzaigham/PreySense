using System;
using System.Linq;
using System.Management;
using System.Runtime.Versioning;
using System.Threading;

namespace PreySense
{
    /// <summary>
    /// Event arguments containing details from an ACPI hotkey event.
    /// </summary>
    public class HotKeyEventArgs : EventArgs
    {
        public byte Category { get; }
        public byte SubEventId { get; }
        public ushort Parameter { get; }

        public HotKeyEventArgs(byte category, byte subEventId, ushort parameter)
        {
            Category = category;
            SubEventId = subEventId;
            Parameter = parameter;
        }
    }

    /// <summary>
    /// Listens for ACPI WMI events emitted by the Acer Embedded Controller.
    /// PHN16S chain: EC _QFA → GHKE=5, HKEV(0x07,0) → Notify(WMID, 0xBC) →
    /// APGeEvent.EventDetail uint8[8] = {0x07, 0x05, 0x01, 0, …}.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class TurboKeyWatcher : IDisposable
    {
        public const byte ModeKeyCategory = 0x07;
        public const byte ModeKeyId = 0x05;

        private readonly object _lock = new();
        private ManagementEventWatcher? _apgeWatcher;
        private ManagementEventWatcher? _genericWatcher;
        private Thread? _thread;
        private ManualResetEventSlim? _stop;
        private bool _isDisposed;

        public event EventHandler<HotKeyEventArgs>? TurboKeyPressed;
        public event EventHandler<HotKeyEventArgs>? HotKeyReceived;
        public event EventHandler<PreySense.Core.Services.PowerSourceKind>? PowerSourceReceived;

        public bool IsRunning
        {
            get { lock (_lock) return _thread != null && _thread.IsAlive; }
        }

        public void Start()
        {
            lock (_lock)
            {
                if (_isDisposed || _thread != null) return;
                _stop = new ManualResetEventSlim(false);
                _thread = new Thread(WatchThread)
                {
                    IsBackground = true,
                    Name = "PreySense.TurboKey"
                };
                _thread.SetApartmentState(ApartmentState.MTA);
                _thread.Start();
            }
        }

        private void WatchThread()
        {
            try
            {
                _apgeWatcher = StartClassWatcher("APGeEvent");
                if (_apgeWatcher == null)
                {
                    _genericWatcher = StartClassWatcher("AcerGenericEvent");
                }
                AppLogger.Log($"TurboKey: MTA WMI listeners started (APGe={_apgeWatcher != null}, Generic={_genericWatcher != null}).");
                _stop?.Wait();
            }
            catch (Exception ex)
            {
                AppLogger.Log($"TurboKey: watcher thread failed: {ex.Message}");
            }
            finally
            {
                StopWatcher(ref _apgeWatcher);
                StopWatcher(ref _genericWatcher);
            }
        }

        private ManagementEventWatcher? StartClassWatcher(string className)
        {
            ManagementEventWatcher? watcher = null;
            try
            {
                var scope = new ManagementScope(@"root\wmi");
                scope.Connect();
                watcher = new ManagementEventWatcher(scope, new EventQuery($"SELECT * FROM {className}"));
                watcher.EventArrived += OnEventArrived;
                watcher.Start();
                AppLogger.Log($"TurboKey: subscribed {className}.");
                return watcher;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"TurboKey: failed to start {className}: {ex.Message}");
                if (watcher != null)
                {
                    try { watcher.Stop(); } catch { }
                    try { watcher.Dispose(); } catch { }
                }
                return null;
            }
        }

        internal static bool TryParseEventDetail(object? rawDetail, out byte category, out byte subEventId, out ushort param)
        {
            category = 0;
            subEventId = 0;
            param = 0;

            switch (rawDetail)
            {
                case string text when int.TryParse(text, out int parsedText):
                    return TryParseEventDetail(parsedText, out category, out subEventId, out param);
                case byte[] bytes when bytes.Length >= 1:
                    category = bytes[0];
                    subEventId = bytes.Length > 1 ? bytes[1] : (byte)0;
                    param = bytes.Length >= 4
                        ? (ushort)(bytes[2] | (bytes[3] << 8))
                        : (bytes.Length >= 3 ? bytes[2] : (ushort)0);
                    return true;
                case byte b:
                    category = b;
                    return true;
                case sbyte sb:
                    category = (byte)sb;
                    return true;
                case short s:
                    return TryParseEventDetail(unchecked((ushort)s), out category, out subEventId, out param);
                case ushort us:
                    category = (byte)(us & 0xFF);
                    subEventId = (byte)((us >> 8) & 0xFF);
                    return true;
                case int i:
                    return TryParseEventDetail(unchecked((uint)i), out category, out subEventId, out param);
                case uint u:
                    category = (byte)(u & 0xFF);
                    subEventId = (byte)((u >> 8) & 0xFF);
                    param = (ushort)((u >> 16) & 0xFFFF);
                    return true;
                case long l:
                    return TryParseEventDetail(unchecked((uint)l), out category, out subEventId, out param);
                case ulong ul:
                    return TryParseEventDetail(unchecked((uint)ul), out category, out subEventId, out param);
                case Array arr when arr.Length >= 1:
                    var copy = new byte[Math.Min(arr.Length, 8)];
                    for (int i = 0; i < copy.Length; i++)
                    {
                        object? item = arr.GetValue(i);
                        if (item == null) continue;
                        try { copy[i] = Convert.ToByte(item); }
                        catch { return false; }
                    }
                    return TryParseEventDetail(copy, out category, out subEventId, out param);
                default:
                    return false;
            }
        }

        public static bool IsPhysicalModeKey(object? rawDetail)
        {
            if (!TryParseEventDetail(rawDetail, out byte category, out byte subEventId, out ushort param))
                return false;
            return IsPhysicalModeKey(category, subEventId, param);
        }

        /// <summary>
        /// Checks whether an ACPI/WMI hotkey event represents a keyboard brightness / lighting adjustment
        /// (e.g. Fn+F11, Fn+F12, backlight toggle/level). These must NEVER trigger a mode switch.
        /// </summary>
        public static bool IsBrightnessEvent(byte category, byte subEventId, ushort param)
        {
            // Known Fn+F11 / Fn+F12 and backlight event codes / scancodes:
            // 0x11: Fn+F11 (brightness down) on Acer gaming laptops
            // 0x12: Fn+F12 (brightness up) on Acer gaming laptops
            // 0x02: Keyboard backlight toggle / step
            // 0x1A: Illumination down (acer_wmi)
            // 0x1B: Illumination up (acer_wmi)
            // 0x44: F11 scancode
            // 0x45: F12 scancode
            // 0x57: F11 PS/2 scancode
            // 0x58: F12 PS/2 scancode
            // 0x78: F11 PS/2 set 2
            // 0x07: F12 PS/2 set 2
            // 0x79: Keyboard illumination increment
            // 0x7A: Keyboard illumination decrement
            // 0x7C: PHN16S vendor brightness up
            // 0x7D: PHN16S vendor brightness down
            // 0x84: Automatic keyboard backlight toggle (acer-wmi)
            // 0x8B: Vendor brightness down
            // 0x8C: Vendor brightness up
            // 0xC4: acer-wmi illumination up
            // 0xC5: acer-wmi illumination down
            if (subEventId is 0x11 or 0x12 or 0x02 or 0x1A or 0x1B or 0x44 or 0x45 or
                0x57 or 0x58 or 0x78 or 0x79 or 0x7A or 0x7C or 0x7D or
                0x84 or 0x8B or 0x8C or 0xC4 or 0xC5)
            {
                return true;
            }

            if (category == 0x01 && subEventId is 0x11 or 0x12 or 0x02 or 0x1A or 0x1B or 0x44 or 0x45 or 0x57 or 0x58)
                return true;

            if (param is 0x11 or 0x12 or 0x02 or 0x1A or 0x1B or 0x44 or 0x45 or 0x57 or 0x58 or 0x79 or 0x7A or 0x7C or 0x7D or 0x84 or 0x8B or 0x8C or 0xC4 or 0xC5)
                return true;

            return false;
        }

        public static bool IsPhysicalModeKey(byte category, byte subEventId, ushort param)
        {
            // Strictly filter out Fn+F11 / Fn+F12 and keyboard backlight events
            if (IsBrightnessEvent(category, subEventId, param))
                return false;

            // WMID_GAMING_TURBO_KEY_EVENT (Category 0x07) — only GHKE = 0x05 (ModeKeyId) is the chassis Mode key.
            // On Acer Predator/Nitro, Notify(WMID, 0xBC) passes Category 0x07 for ALL gaming hotkeys
            // (where GHKE 0x05 = Mode, 0x11 = Fn+F11, 0x12 = Fn+F12). Do NOT treat any 0x07 as Mode!
            if (category == ModeKeyCategory && (subEventId == ModeKeyId || param == ModeKeyId))
                return true;

            // Legacy firmware: Category 0x07 with no subEvent / param (single Turbo button event)
            if (category == ModeKeyCategory && subEventId == 0 && param == 0)
                return true;

            // GHKE stuffed as the first byte, GHKN as the second
            if (category == ModeKeyId && subEventId <= 1)
                return true;

            if (subEventId == ModeKeyId && category is 0 or 1)
                return true;

            if (category == 0x03 && (subEventId == 0x01 || param == ModeKeyCategory))
                return true;

            return false;
        }

        private void OnEventArrived(object sender, EventArrivedEventArgs e)
        {
            try
            {
                var eventObj = e.NewEvent;
                if (eventObj == null) return;

                object? rawDetail = null;
                try { rawDetail = eventObj.Properties["EventDetail"]?.Value; } catch { }
                rawDetail ??= SafeIndexer(eventObj, "EventDetail");

                if (!TryParseEventDetail(rawDetail, out byte category, out byte subEventId, out ushort param))
                {
                    AppLogger.Log($"TurboKey: ignored event EventDetail type {rawDetail?.GetType().FullName ?? "null"} value={FormatRawDetail(rawDetail)}");
                    return;
                }

                AppLogger.Log($"TurboKey: {eventObj.ClassPath?.ClassName} Category=0x{category:X2} ID=0x{subEventId:X2} Param=0x{param:X4} raw={FormatRawDetail(rawDetail)}");

                var args = new HotKeyEventArgs(category, subEventId, param);
                HotKeyReceived?.Invoke(this, args);

                if (category == 0x08)
                {
                    if (PreySense.Helpers.WmiController.TryParseAcerPowerEvent(new byte[] { category, subEventId }, out var source))
                    {
                        AppLogger.Log($"TurboKey: ACPI power source event: {source}.");
                        PowerSourceReceived?.Invoke(this, source);
                    }
                }
                else if (IsPhysicalModeKey(category, subEventId, param))
                {
                    AppLogger.Log("TurboKey: chassis Mode / Turbo key detected.");
                    TurboKeyPressed?.Invoke(this, args);
                }
                // Fn RGB brightness keys are left to firmware; PreySense does not monitor or replay them.
            }
            catch (Exception ex)
            {
                AppLogger.Log($"TurboKey: error processing event: {ex.Message}");
            }
        }

        private static string FormatRawDetail(object? rawDetail) => rawDetail switch
        {
            byte[] bytes => BitConverter.ToString(bytes),
            Array arr => string.Join("-", arr.Cast<object?>().Take(8).Select(v => Convert.ToByte(v).ToString("X2"))),
            null => "null",
            _ => $"{rawDetail.GetType().Name}:{rawDetail}"
        };

        private static object? SafeIndexer(ManagementBaseObject eventObj, string name)
        {
            try { return eventObj[name]; }
            catch { return null; }
        }

        public void Stop()
        {
            Thread? thread;
            lock (_lock)
            {
                _stop?.Set();
                thread = _thread;
                _thread = null;
            }

            if (thread != null && thread.IsAlive && !thread.Join(1000))
                AppLogger.Log("TurboKey: watcher thread did not stop promptly.");

            lock (_lock)
            {
                _stop?.Dispose();
                _stop = null;
            }
        }

        private static void StopWatcher(ref ManagementEventWatcher? watcher)
        {
            if (watcher == null) return;
            try
            {
                watcher.Stop();
                watcher.Dispose();
            }
            catch (Exception ex)
            {
                AppLogger.Log($"TurboKey: error stopping watcher: {ex.Message}");
            }
            watcher = null;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_isDisposed) return;
                _isDisposed = true;
            }
            Stop();
        }
    }
}
