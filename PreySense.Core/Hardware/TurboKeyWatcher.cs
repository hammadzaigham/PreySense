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
        public event EventHandler<int>? BrightnessKeyPressed;
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

        public static bool IsPhysicalModeKey(byte category, byte subEventId, ushort param)
        {
            // WMID_GAMING_TURBO_KEY_EVENT — any 0x07 notify is the chassis Mode key family.
            if (category == ModeKeyCategory)
                return true;
            // GHKE stuffed as the first byte, GHKN as the second
            if (category == ModeKeyId && subEventId <= 1)
                return true;
            if (subEventId == ModeKeyId && category is 0 or 1)
                return true;
            if (subEventId == ModeKeyCategory)
                return true;
            if (category == 0x03 && (subEventId == 0x01 || param == ModeKeyCategory))
                return true;
            return false;
        }

        public static bool TryGetBrightnessDelta(byte category, byte subEventId, out int delta)
        {
            delta = 0;
            // Acer WMI function 1: 0x1A keyboard illum down, 0x1B up (acer_wmi / PHN16).
            if (category != 0x01)
                return false;
            if (subEventId is 0x1A or 0x44 or 0x57)
            {
                delta = -1;
                return true;
            }
            if (subEventId is 0x1B or 0x45 or 0x58)
            {
                delta = 1;
                return true;
            }
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
                else if (TryGetBrightnessDelta(category, subEventId, out int brightnessDelta))
                {
                    AppLogger.Log($"TurboKey: Fn brightness WMI 0x{category:X2}/0x{subEventId:X2} delta={brightnessDelta}.");
                    BrightnessKeyPressed?.Invoke(this, brightnessDelta);
                }
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
