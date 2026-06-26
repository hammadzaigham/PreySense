using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace PreySense.Overlay
{
    public sealed class EtwFpsMonitor : IDisposable
    {
        // ── ETW Constants ────────────────────────────────────────────────────────
        private const uint ERROR_SUCCESS = 0;
        private const uint ERROR_WMI_INSTANCE_NOT_FOUND = 0x1069;
        private const uint EVENT_CONTROL_CODE_ENABLE_PROVIDER = 1;
        private const uint EVENT_TRACE_CONTROL_FLUSH = 3; // ControlTrace code — deliver buffers now
        private const byte TRACE_LEVEL_INFORMATION = 4;
        private const uint PROCESS_TRACE_MODE_REAL_TIME = 0x00000100;
        private const uint PROCESS_TRACE_MODE_EVENT_RECORD = 0x10000000;
        private const uint PROCESS_TRACE_MODE_RAW_TIMESTAMP = 0x00001000; // EventHeader.TimeStamp = raw QPC ticks
        private const uint WNODE_FLAG_TRACED_GUID = 0x00020000;
        private const int EVENT_DXGI_PRESENT_START = 42;

        // Microsoft-Windows-DXGI provider — DX11 + some DX12 games (user-mode swapchain path)
        private static readonly Guid DxgiProviderId =
            new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");

        // Microsoft-Windows-DxgKrnl provider — DX12 flip-model + Vulkan (kernel present path)
        private static readonly Guid DxgKrnlProviderId =
            new("802EC45A-1E99-4B83-9920-87C98277BA9D");

        private const ushort DXGKRNL_TASK_FLIP = 14;
        private const byte DXGKRNL_OPCODE_START = 1;

        // Keyword mask for the DxgKrnl provider — scopes to flip/present-related events only
        private const ulong DXGKRNL_KEYWORD_PRESENT = 0x0000040000000000UL;

        // Kernel-side filter descriptors
        private const uint EVENT_FILTER_TYPE_PID             = 0x80000004;
        private const uint EVENT_FILTER_TYPE_EVENT_ID        = 0x80000200;
        private const uint ENABLE_TRACE_PARAMETERS_VERSION_2 = 2;

        private const string SessionName = "FpsMonitorSession";

        // ── P/Invoke Structures ──────────────────────────────────────────────────
        [StructLayout(LayoutKind.Sequential)]
        private struct WNODE_HEADER
        {
            public uint BufferSize;
            public uint ProviderId;
            public ulong HistoricalContext;
            public ulong TimeStamp;
            public Guid Guid;
            public uint ClientContext;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct EVENT_TRACE_PROPERTIES
        {
            public WNODE_HEADER Wnode;
            public uint BufferSize;
            public uint MinimumBuffers;
            public uint MaximumBuffers;
            public uint MaximumFileSize;
            public uint LogFileMode;
            public uint FlushTimer;
            public uint EnableFlags;
            public int AgeLimit;
            public uint NumberOfBuffers;
            public uint FreeBuffers;
            public uint EventsLost;
            public uint BuffersWritten;
            public uint LogBuffersLost;
            public uint RealTimeBuffersLost;
            public IntPtr LoggerThreadId;
            public uint LogFileNameOffset;
            public uint LoggerNameOffset;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)]
            public string LoggerName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)]
            public string LogFileName;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct EVENT_RECORD
        {
            public EVENT_HEADER EventHeader;
            public ETW_BUFFER_CONTEXT BufferContext;
            public ushort ExtendedDataCount;
            public ushort UserDataLength;
            public IntPtr ExtendedData;
            public IntPtr UserData;
            public IntPtr UserContext;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct EVENT_HEADER
        {
            public ushort Size;
            public ushort HeaderType;
            public ushort Flags;
            public ushort EventProperty;
            public uint ThreadId;
            public uint ProcessId;
            public long TimeStamp; // raw QPC ticks when PROCESS_TRACE_MODE_RAW_TIMESTAMP is set
            public Guid ProviderId;
            public ushort Id; // 42 = PresentStart (DXGI)
            public byte Version;
            public byte Channel;
            public byte Level;
            public byte Opcode;
            public ushort Task;
            public ulong Keyword;
            public uint KernelTime;
            public uint UserTime;
            public Guid ActivityId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ETW_BUFFER_CONTEXT
        {
            public byte ProcessorNumber;
            public byte Alignment;
            public ushort LoggerId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct EVENT_FILTER_DESCRIPTOR
        {
            public ulong Ptr;
            public uint  Size;
            public uint  Type;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ENABLE_TRACE_PARAMETERS
        {
            public uint   Version;
            public uint   EnableProperty;
            public uint   ControlFlags;
            public Guid   SourceId;
            public IntPtr EnableFilterDesc;
            public uint   FilterDescCount;
        }

        [StructLayout(LayoutKind.Explicit, Size = 448)]
        private struct EVENT_TRACE_LOGFILE
        {
            [FieldOffset(8)] public IntPtr LoggerName;           // LPWSTR
            [FieldOffset(28)] public uint ProcessTraceMode;
            [FieldOffset(400)] public IntPtr BufferCallback;       // unused, zero
            [FieldOffset(424)] public IntPtr EventRecordCallback;  // set via Marshal.GetFunctionPointerForDelegate
            [FieldOffset(440)] public IntPtr Context;              // unused, zero
        }

        private delegate void EventRecordCallback([In] ref EVENT_RECORD eventRecord);

        // ── P/Invoke Functions ───────────────────────────────────────────────────
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern uint StartTrace(out long sessionHandle,
            string sessionName, ref EVENT_TRACE_PROPERTIES properties);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern uint StopTrace(long sessionHandle,
            string sessionName, ref EVENT_TRACE_PROPERTIES properties);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern uint ControlTrace(long sessionHandle,
            string? sessionName, ref EVENT_TRACE_PROPERTIES properties, uint controlCode);

        [DllImport("advapi32.dll")]
        private static extern uint EnableTraceEx2(long sessionHandle,
            in Guid providerId, uint controlCode, byte level,
            ulong matchAnyKeyword, ulong matchAllKeyword,
            uint timeout, IntPtr enableParameters);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern long OpenTrace(ref EVENT_TRACE_LOGFILE logfile);

        [DllImport("advapi32.dll")]
        private static extern uint ProcessTrace(long[] handles, uint count,
            IntPtr startTime, IntPtr endTime);

        [DllImport("advapi32.dll")]
        private static extern uint CloseTrace(long traceHandle);

        // ── State ────────────────────────────────────────────────────────────────
        private long _sessionHandle;
        private long _traceHandle;

        private const int FlushIntervalMs = 200; // flush cadence while a game renders
        private const int MinFlushFps = 10;      // below this fps = idle/browsing, flush only 1×/s
        private System.Threading.Timer? _flushTimer;
        private long _lastFlushTick;             // ≥1 s flush floor
        private volatile int _targetPid;  // written by overlay timer thread, read by ETW callback thread
        private int _lastTargetPid;       // detects PID switches so the window can be reset

        private bool _dxgiActiveForCurrentPid = false;

        private const int RollingWindowSize = 360; // frames — holds a full 1 s window up to 360 fps
        private readonly long[] _frameTimes = new long[RollingWindowSize];
        private volatile int _frameHead = 0;    // next write slot — read by overlay tick thread
        private volatile int _framesFilled = 0; // valid entries

        private static readonly bool FpsDiagLogging = false;
        private long _diagStart, _diagLastEvent;
        private int _diagFrames;
        private double _diagLatMin, _diagLatMax, _diagLatSum, _diagGapMax;

        private EventRecordCallback? _callbackRef; // keep delegate alive

        public int TargetPid
        {
            get => _targetPid;
            set
            {
                if (_targetPid == value) return;
                _targetPid = value;
                if (value != 0 && _sessionHandle != 0)
                    ApplyKernelFilters(value);
            }
        }

        private void ApplyKernelFilters(int pid)
        {
            EnableProviderWithFilters(DxgiProviderId,    0,                       pid, applyDxgiEventIdFilter: true);
            EnableProviderWithFilters(DxgKrnlProviderId, DXGKRNL_KEYWORD_PRESENT, pid, applyDxgiEventIdFilter: false);
        }

        private void EnableProviderWithFilters(Guid providerId, ulong keyword, int pid, bool applyDxgiEventIdFilter)
        {
            int filterCount = applyDxgiEventIdFilter ? 2 : 1;
            int descSize    = Marshal.SizeOf<EVENT_FILTER_DESCRIPTOR>();

            IntPtr descs      = Marshal.AllocHGlobal(filterCount * descSize);
            IntPtr pidBuf     = Marshal.AllocHGlobal(sizeof(uint));
            IntPtr eventIdBuf = applyDxgiEventIdFilter ? Marshal.AllocHGlobal(8) : IntPtr.Zero;
            IntPtr paramsPtr  = Marshal.AllocHGlobal(Marshal.SizeOf<ENABLE_TRACE_PARAMETERS>());

            try
            {
                Marshal.WriteInt32(pidBuf, pid);
                var pidDesc = new EVENT_FILTER_DESCRIPTOR
                {
                    Ptr  = (ulong)pidBuf.ToInt64(),
                    Size = sizeof(uint),
                    Type = EVENT_FILTER_TYPE_PID,
                };
                Marshal.StructureToPtr(pidDesc, descs, false);

                if (applyDxgiEventIdFilter)
                {
                    Marshal.WriteByte (eventIdBuf, 0, 1);                        // FilterIn = true
                    Marshal.WriteByte (eventIdBuf, 1, 0);                        // Reserved
                    Marshal.WriteInt16(eventIdBuf, 2, 1);                        // Count = 1
                    Marshal.WriteInt16(eventIdBuf, 4, EVENT_DXGI_PRESENT_START); // Events[0] = 42

                    var eidDesc = new EVENT_FILTER_DESCRIPTOR
                    {
                        Ptr  = (ulong)eventIdBuf.ToInt64(),
                        Size = 6,
                        Type = EVENT_FILTER_TYPE_EVENT_ID,
                    };
                    Marshal.StructureToPtr(eidDesc, descs + descSize, false);
                }

                var enableParams = new ENABLE_TRACE_PARAMETERS
                {
                    Version          = ENABLE_TRACE_PARAMETERS_VERSION_2,
                    EnableFilterDesc = descs,
                    FilterDescCount  = (uint)filterCount,
                };
                Marshal.StructureToPtr(enableParams, paramsPtr, false);

                uint hr = EnableTraceEx2(_sessionHandle, providerId,
                    EVENT_CONTROL_CODE_ENABLE_PROVIDER,
                    TRACE_LEVEL_INFORMATION, keyword, 0, 0, paramsPtr);
                if (hr != ERROR_SUCCESS)
                    AppLogger.Log($"EnableTraceEx2 (filter) failed for {providerId}: 0x{hr:X}");
            }
            finally
            {
                Marshal.FreeHGlobal(paramsPtr);
                if (eventIdBuf != IntPtr.Zero) Marshal.FreeHGlobal(eventIdBuf);
                Marshal.FreeHGlobal(pidBuf);
                Marshal.FreeHGlobal(descs);
            }
        }

        private static void ForceStopSession()
        {
            try
            {
                var props = BuildSessionProperties();
                uint stopHr = ControlTrace(0, SessionName, ref props, 1); // 1 = EVENT_TRACE_CONTROL_STOP
                if (stopHr != ERROR_SUCCESS && stopHr != ERROR_WMI_INSTANCE_NOT_FOUND)
                {
                    AppLogger.Log($"EtwFpsMonitor.ForceStopSession: ControlTrace(STOP) returned 0x{stopHr:X}");
                }
                
                if (stopHr != ERROR_SUCCESS)
                {
                    var stopProps = BuildSessionProperties();
                    stopHr = StopTrace(0, SessionName, ref stopProps);
                    if (stopHr != ERROR_SUCCESS && stopHr != ERROR_WMI_INSTANCE_NOT_FOUND)
                    {
                        AppLogger.Log($"EtwFpsMonitor.ForceStopSession: StopTrace returned 0x{stopHr:X}");
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"EtwFpsMonitor.ForceStopSession exception: {ex.Message}");
            }
        }

        public void Start(int targetPid = 0)
        {
            _targetPid = targetPid;

            ForceStopSession();

            var props = BuildSessionProperties();
            uint hr = StartTrace(out _sessionHandle, SessionName, ref props);

            if (hr != ERROR_SUCCESS)
            {
                if (hr == 0xB7 /*ERROR_ALREADY_EXISTS*/)
                {
                    var queryProps = BuildSessionProperties();
                    uint queryHr = ControlTrace(0, SessionName, ref queryProps, 0); // 0 = EVENT_TRACE_CONTROL_QUERY
                    if (queryHr == ERROR_SUCCESS)
                    {
                        _sessionHandle = (long)queryProps.Wnode.HistoricalContext;
                        hr = ERROR_SUCCESS;
                    }
                    else
                    {
                        AppLogger.Log($"EtwFpsMonitor: Failed to query existing session: 0x{queryHr:X}");
                    }
                }
            }

            if (hr != ERROR_SUCCESS)
            {
                AppLogger.Log($"EtwFpsMonitor: Failed to start/acquire ETW session. FPS monitoring disabled.");
                return;
            }

            EnableTraceEx2(_sessionHandle, DxgiProviderId,
                EVENT_CONTROL_CODE_ENABLE_PROVIDER,
                TRACE_LEVEL_INFORMATION, 0, 0, 0, IntPtr.Zero);

            EnableTraceEx2(_sessionHandle, DxgKrnlProviderId,
                EVENT_CONTROL_CODE_ENABLE_PROVIDER,
                TRACE_LEVEL_INFORMATION, DXGKRNL_KEYWORD_PRESENT, 0, 0, IntPtr.Zero);

            _callbackRef = OnEventRecord;
            IntPtr loggerNamePtr = Marshal.StringToHGlobalUni(SessionName);
            try
            {
                var logfile = new EVENT_TRACE_LOGFILE
                {
                    LoggerName = loggerNamePtr,
                    ProcessTraceMode = PROCESS_TRACE_MODE_REAL_TIME |
                                       PROCESS_TRACE_MODE_EVENT_RECORD |
                                       PROCESS_TRACE_MODE_RAW_TIMESTAMP,
                    EventRecordCallback = Marshal.GetFunctionPointerForDelegate(_callbackRef),
                };

                _traceHandle = OpenTrace(ref logfile);
            }
            catch (Exception ex)
            {
                AppLogger.Log($"EtwFpsMonitor: OpenTrace failed: {ex.Message}");
            }
            finally
            {
                Marshal.FreeHGlobal(loggerNamePtr);
            }

            if (_traceHandle == 0 || _traceHandle == -1)
            {
                AppLogger.Log($"EtwFpsMonitor: Invalid trace handle. Aborting ProcessTrace.");
                return;
            }

            _flushTimer = new System.Threading.Timer(_ => FlushSession(), null, FlushIntervalMs, FlushIntervalMs);

            uint procHr = ProcessTrace(new[] { _traceHandle }, 1, IntPtr.Zero, IntPtr.Zero);
        }

        public void Stop()
        {
            _flushTimer?.Dispose();
            _flushTimer = null;
            CloseTrace(_traceHandle);
            var props = BuildSessionProperties();
            StopTrace(_sessionHandle, SessionName, ref props);
        }

        private void FlushSession()
        {
            if (_sessionHandle == 0) return;

            long now = Stopwatch.GetTimestamp();
            bool idleFlushDue = now - _lastFlushTick >= Stopwatch.Frequency;
            if (SampleFps() < MinFlushFps && !idleFlushDue) return;

            _lastFlushTick = now;
            var props = BuildSessionProperties();
            ControlTrace(_sessionHandle, null, ref props, EVENT_TRACE_CONTROL_FLUSH);
        }

        public void Dispose() => Stop();

        private void OnEventRecord(ref EVENT_RECORD record)
        {
            bool isDxgiPresent = record.EventHeader.ProviderId == DxgiProviderId
                              && record.EventHeader.Id == EVENT_DXGI_PRESENT_START;

            bool isDxgKrnlPresent = record.EventHeader.ProviderId == DxgKrnlProviderId
                                 && record.EventHeader.Task == DXGKRNL_TASK_FLIP
                                 && record.EventHeader.Opcode == DXGKRNL_OPCODE_START;

            if (!isDxgiPresent && !isDxgKrnlPresent) return;

            int targetPid = _targetPid;
            if (targetPid == 0) return;
            if ((int)record.EventHeader.ProcessId != targetPid) return;

            if (isDxgiPresent)
            {
                int ptrSize = (record.EventHeader.Flags & 0x20) != 0 ? 4 : 8; // 0x20 = EVENT_HEADER_FLAG_32_BIT_HEADER
                if (record.UserDataLength >= ptrSize + 4)
                {
                    uint dxgiFlags = (uint)Marshal.ReadInt32(record.UserData, ptrSize);
                    if ((dxgiFlags & 0x1 /*DXGI_PRESENT_TEST*/) != 0) return;
                }
            }

            if (isDxgiPresent)
                _dxgiActiveForCurrentPid = true;
            else if (_dxgiActiveForCurrentPid)
                return;

            if (targetPid != _lastTargetPid)
            {
                _lastTargetPid = targetPid;
                _frameHead = 0;
                _framesFilled = 0;
                _dxgiActiveForCurrentPid = false;
                return;
            }

            _frameTimes[_frameHead] = record.EventHeader.TimeStamp;
            _frameHead = (_frameHead + 1) % RollingWindowSize;
            if (_framesFilled < RollingWindowSize) _framesFilled++;

            if (FpsDiagLogging) LogFpsDiagnostics(record.EventHeader.TimeStamp);
        }

        public double SampleFps()
        {
            int filled = _framesFilled;
            if (filled < 2) return 0;

            long freq = Stopwatch.Frequency;
            int head = _frameHead;
            long newest = _frameTimes[(head - 1 + RollingWindowSize) % RollingWindowSize];

            if (Stopwatch.GetTimestamp() - newest > freq) return 0;

            long cutoff = newest - freq;
            int count = 1;
            long oldest = newest;
            for (int i = 2; i <= filled; i++)
            {
                long t = _frameTimes[(head - i + RollingWindowSize) % RollingWindowSize];
                if (t < cutoff) break;
                oldest = t;
                count++;
            }

            double elapsed = (double)(newest - oldest) / freq;
            if (elapsed <= 0) return 0;
            return (count - 1) / elapsed;
        }

        public int GetRecentFrameTimes(float[] destination)
        {
            int filled = _framesFilled;
            if (filled < 2) return 0;

            int n = Math.Min(destination.Length, filled - 1);
            int head = _frameHead;
            long freq = Stopwatch.Frequency;

            for (int i = 0; i < n; i++)
            {
                int currIdx = (head - 1 - i + RollingWindowSize) % RollingWindowSize;
                int prevIdx = (head - 2 - i + RollingWindowSize) % RollingWindowSize;
                long diff = _frameTimes[currIdx] - _frameTimes[prevIdx];
                if (diff < 0) diff = 0;
                destination[i] = (float)((double)diff / freq * 1000.0);
            }

            return n;
        }

        private void LogFpsDiagnostics(long presentTick)
        {
            long freq = Stopwatch.Frequency;
            long nowTick = Stopwatch.GetTimestamp();
            double latMs = (double)(nowTick - presentTick) / freq * 1000.0;
            if (_diagFrames == 0) { _diagLatMin = _diagLatMax = latMs; }
            else
            {
                if (latMs < _diagLatMin) _diagLatMin = latMs;
                if (latMs > _diagLatMax) _diagLatMax = latMs;
            }
            _diagLatSum += latMs;
            if (_diagLastEvent != 0)
            {
                double gapMs = (double)(nowTick - _diagLastEvent) / freq * 1000.0;
                if (gapMs > _diagGapMax) _diagGapMax = gapMs;
            }
            _diagLastEvent = nowTick;
            _diagFrames++;
            if (_diagStart == 0) _diagStart = nowTick;
            else if (nowTick - _diagStart >= freq)
            {
                double secs = (double)(nowTick - _diagStart) / freq;
                AppLogger.Log(
                    $"FPS diag: fps={SampleFps():F0} frames={_diagFrames} rate={_diagFrames / secs:F0}/s | " +
                    $"ETW latency ms: min={_diagLatMin:F0} avg={_diagLatSum / _diagFrames:F0} max={_diagLatMax:F0} | " +
                    $"maxgap={_diagGapMax:F0}ms");
                _diagStart = nowTick; _diagFrames = 0; _diagLatSum = 0; _diagGapMax = 0;
            }
        }

        private static EVENT_TRACE_PROPERTIES BuildSessionProperties() => new()
        {
            Wnode = new WNODE_HEADER
            {
                BufferSize = (uint)Marshal.SizeOf<EVENT_TRACE_PROPERTIES>(),
                Flags = WNODE_FLAG_TRACED_GUID,
                ClientContext = 0,
            },
            LogFileMode = 0x00000100, // EVENT_TRACE_REAL_TIME_MODE
            LogFileNameOffset = 0,
            LoggerNameOffset = (uint)Marshal.OffsetOf<EVENT_TRACE_PROPERTIES>(
                nameof(EVENT_TRACE_PROPERTIES.LoggerName)),
            BufferSize = 8,
            MinimumBuffers = 8,
            MaximumBuffers = 16,
        };
    }
}
