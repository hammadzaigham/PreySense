using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace PreySense.Gpu
{
    public enum DdsDisplayMode
    {
        Optimus = 0,
        Automatic = 1,
        NvidiaGpuOnly = 2
    }

    public enum DdsActiveMux
    {
        Unknown = 0,
        Optimus = 1, // iGPU driving display
        dGPU = 2     // NVIDIA GPU driving display directly
    }

    public record DdsStatus(
        bool IsSupported,
        bool IsAutomatic,
        DdsActiveMux ActiveMux,
        uint KmdResponse,
        uint SrType
    )
    {
        public bool Matches(DdsDisplayMode mode) => IsSupported && KmdResponse == 0 && mode switch
        {
            DdsDisplayMode.Automatic => IsAutomatic && ActiveMux != DdsActiveMux.Unknown,
            DdsDisplayMode.Optimus => !IsAutomatic && ActiveMux == DdsActiveMux.Optimus,
            DdsDisplayMode.NvidiaGpuOnly => !IsAutomatic && ActiveMux == DdsActiveMux.dGPU,
            _ => false
        };
    }

    /// <summary>
    /// Native NVIDIA Advanced Optimus (Dynamic Display Switch / DDS) Service.
    /// Interfaces directly with nvcpl.dll (NvCplApi) exported by NVIDIA App / Control Panel
    /// to provide genuine, reboot-free 3-mode display switching.
    /// </summary>
    public sealed class NvidiaDdsService : IDisposable
    {
        private static readonly Lazy<NvidiaDdsService> _instance = new(() => new NvidiaDdsService());
        public static NvidiaDdsService Instance => _instance.Value;

        private const uint InfoTypeDdsStatus = 0x17;
        private const uint SettingIdDdsState = 0x332;
        private const uint SettingIdBlockingApps = 0x333;
        private const uint CommandCommit = 0x10000;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern IntPtr LoadLibraryA(string lpLibFileName);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvCplApiInitDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvCplApiCloseDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvCplApiGetInfoDelegate(uint infoType, uint param, IntPtr pData, ref ulong pSize);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvCplApiGetSettingDelegate(uint target, out uint status, uint settingId, IntPtr pData, ulong dataSize);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvCplApiSetSettingDelegate(uint target, out uint status, uint settingId, IntPtr pData, ulong dataSize);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvCplApiExecuteDelegate(uint command, uint param1, int param2);

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct NativeDdsStatusInfo
        {
            public byte IsSupported;   // 1 if supported
            public byte IsAutomatic;   // 1 if Auto, 0 if Manual
            public byte Reserved1;
            public byte Reserved2;
            public uint MuxState;      // 1 = Optimus, 2 = dGPU
            public uint KmdResponse;   // 0 = Response_Ok
            public uint SrType;        // PSR type
        }

        private readonly object _syncLock = new();
        private IntPtr _hModule = IntPtr.Zero;
        private bool _isInitialized;
        private NvCplApiInitDelegate? _init;
        private NvCplApiCloseDelegate? _close;
        private NvCplApiGetInfoDelegate? _getInfo;
        private NvCplApiGetSettingDelegate? _getSetting;
        private NvCplApiSetSettingDelegate? _setSetting;
        private NvCplApiExecuteDelegate? _execute;
        private int _lastGetInfoRet;
        private DateTime _lastGetInfoLogTime = DateTime.MinValue;

        public bool IsAvailable
        {
            get
            {
                lock (_syncLock)
                {
                    EnsureInitializedLocked();
                    return _isInitialized;
                }
            }
        }

        public NvidiaDdsService()
        {
        }

        private string? ResolveNvCplPath()
        {
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string[] searchPaths =
            [
                Path.Combine(programFiles, "NVIDIA Corporation", "NVIDIA App", "NvCpl", "nvcpl.dll"),
                Path.Combine(programFiles, "NVIDIA Corporation", "Control Panel Client", "nvcpl.dll"),
                Path.Combine(Environment.SystemDirectory, "nvcpl.dll")
            ];

            foreach (var path in searchPaths)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return null;
        }

        private bool EnsureInitializedLocked()
        {
            if (_isInitialized) return true;

            string? dllPath = ResolveNvCplPath();
            if (string.IsNullOrEmpty(dllPath))
            {
                AppLogger.Log("NvidiaDdsService: nvcpl.dll could not be found.");
                return false;
            }

            _hModule = LoadLibraryA(dllPath);
            if (_hModule == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                AppLogger.Log($"NvidiaDdsService: LoadLibrary failed for {dllPath} (Error {error}).");
                return false;
            }

            try
            {
                IntPtr pInit = GetProcAddress(_hModule, "NvCplApiInit");
                IntPtr pClose = GetProcAddress(_hModule, "NvCplApiClose");
                IntPtr pGetInfo = GetProcAddress(_hModule, "NvCplApiGetInfo");
                IntPtr pGetSetting = GetProcAddress(_hModule, "NvCplApiGetSetting");
                IntPtr pSetSetting = GetProcAddress(_hModule, "NvCplApiSetSetting");
                IntPtr pExecute = GetProcAddress(_hModule, "NvCplApiExecute");

                if (pInit == IntPtr.Zero || pClose == IntPtr.Zero || pGetInfo == IntPtr.Zero ||
                    pGetSetting == IntPtr.Zero || pSetSetting == IntPtr.Zero || pExecute == IntPtr.Zero)
                {
                    AppLogger.Log("NvidiaDdsService: Required NvCplApi exports not found in nvcpl.dll.");
                    FreeLibrary(_hModule);
                    _hModule = IntPtr.Zero;
                    return false;
                }

                _init = Marshal.GetDelegateForFunctionPointer<NvCplApiInitDelegate>(pInit);
                _close = Marshal.GetDelegateForFunctionPointer<NvCplApiCloseDelegate>(pClose);
                _getInfo = Marshal.GetDelegateForFunctionPointer<NvCplApiGetInfoDelegate>(pGetInfo);
                _getSetting = Marshal.GetDelegateForFunctionPointer<NvCplApiGetSettingDelegate>(pGetSetting);
                _setSetting = Marshal.GetDelegateForFunctionPointer<NvCplApiSetSettingDelegate>(pSetSetting);
                _execute = Marshal.GetDelegateForFunctionPointer<NvCplApiExecuteDelegate>(pExecute);

                int initResult = _init();
                if (initResult != 0)
                {
                    AppLogger.Log($"NvidiaDdsService: NvCplApiInit returned {initResult}.");
                    FreeLibrary(_hModule);
                    _hModule = IntPtr.Zero;
                    return false;
                }

                _isInitialized = true;
                AppLogger.Log("NvidiaDdsService: Initialized successfully with nvcpl.dll.");
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"NvidiaDdsService: Initialization exception: {ex.Message}");
                if (_hModule != IntPtr.Zero)
                {
                    FreeLibrary(_hModule);
                    _hModule = IntPtr.Zero;
                }
                return false;
            }
        }

        public bool IsDdsSupported()
        {
            var status = GetStatus();
            return status?.IsSupported ?? false;
        }

        public DdsStatus? GetStatus()
        {
            lock (_syncLock)
            {
                if (!EnsureInitializedLocked() || _getInfo == null) return null;

                ulong size = 16;
                IntPtr pStatus = Marshal.AllocHGlobal(16);
                try
                {
                    for (int i = 0; i < 16; i++) Marshal.WriteByte(pStatus, i, 0);

                    int ret = _getInfo(InfoTypeDdsStatus, 0, pStatus, ref size);
                    if (ret == 0 && _lastGetInfoRet != 0)
                    {
                        AppLogger.Log("NvidiaDdsService: NvCplApiGetInfo(0x17) recovered.");
                        _lastGetInfoRet = 0;
                    }
                    if (ret == 0)
                    {
                        var info = Marshal.PtrToStructure<NativeDdsStatusInfo>(pStatus);
                        var activeMux = info.MuxState switch
                        {
                            1 => DdsActiveMux.Optimus,
                            2 => DdsActiveMux.dGPU,
                            _ => DdsActiveMux.Unknown
                        };

                        return new DdsStatus(
                            IsSupported: info.IsSupported != 0,
                            IsAutomatic: info.IsAutomatic != 0,
                            ActiveMux: activeMux,
                            KmdResponse: info.KmdResponse,
                            SrType: info.SrType
                        );
                    }

                    if (ret != _lastGetInfoRet || (DateTime.UtcNow - _lastGetInfoLogTime).TotalSeconds >= 30)
                    {
                        _lastGetInfoRet = ret;
                        _lastGetInfoLogTime = DateTime.UtcNow;
                        AppLogger.Log($"NvidiaDdsService: NvCplApiGetInfo(0x17) failed with ret={ret}.");
                    }
                    return null;
                }
                finally
                {
                    Marshal.FreeHGlobal(pStatus);
                }
            }
        }

        public bool HasBlockingApps()
        {
            lock (_syncLock)
            {
                if (!EnsureInitializedLocked() || _getSetting == null) return false;

                IntPtr pBlocker = Marshal.AllocHGlobal(4);
                try
                {
                    Marshal.WriteInt32(pBlocker, 0);
                    uint status;
                    int ret = _getSetting(1, out status, SettingIdBlockingApps, pBlocker, 1);
                    if (ret == 0 && status == 0)
                    {
                        byte val = Marshal.ReadByte(pBlocker);
                        return val != 0;
                    }
                    return false;
                }
                finally
                {
                    Marshal.FreeHGlobal(pBlocker);
                }
            }
        }

        public (bool Success, string Message) SetMode(DdsDisplayMode mode)
        {
            lock (_syncLock)
            {
                if (!EnsureInitializedLocked() || _setSetting == null || _execute == null)
                {
                    return (false, "NVIDIA DDS Service is unavailable.");
                }

                if (HasBlockingApps())
                {
                    AppLogger.Log("NvidiaDdsService: Warning: Active 3D application or display hook detected during switch.");
                }

                bool isAutomatic = mode == DdsDisplayMode.Automatic;
                uint muxState = mode switch
                {
                    DdsDisplayMode.NvidiaGpuOnly => 2, // dGPU
                    DdsDisplayMode.Optimus => 1,       // Optimus / iGPU
                    _ => 1                             // Auto uses Optimus base
                };

                IntPtr pBuf = Marshal.AllocHGlobal(16);
                try
                {
                    for (int i = 0; i < 16; i++) Marshal.WriteByte(pBuf, i, 0);
                    Marshal.WriteByte(pBuf, 0, 1);                      // Version / Enabled
                    Marshal.WriteByte(pBuf, 1, (byte)(isAutomatic ? 1 : 0)); // IsAutomatic
                    Marshal.WriteInt32(pBuf, 4, (int)muxState);         // Target MUX State

                    uint status = 0;
                    int setRet = _setSetting(1, out status, SettingIdDdsState, pBuf, 16);
                    if (setRet != 0 || status != 0)
                    {
                        AppLogger.Log($"NvidiaDdsService: NvCplApiSetSetting failed (ret={setRet}, status=0x{status:X}).");
                        return (false, $"Setting DDS state failed (Code {setRet}, driver status 0x{status:X}).");
                    }

                    // Apply staged display settings immediately (NVCPL_COMMAND_COMMIT)
                    int execRet = _execute(CommandCommit, 0, -1);
                    if (execRet != 0)
                    {
                        AppLogger.Log($"NvidiaDdsService: NvCplApiExecute(COMMIT) returned {execRet}.");
                        return (false, $"Commit display mode failed (Code {execRet}).");
                    }

                    AppLogger.Log($"NvidiaDdsService: Display switch successfully applied: Mode={mode} (Auto={isAutomatic}, Mux={muxState}).");
                    return (true, $"GPU Display Mode set to {mode}.");
                }
                finally
                {
                    Marshal.FreeHGlobal(pBuf);
                }
            }
        }

        public void Dispose()
        {
            lock (_syncLock)
            {
                if (_isInitialized)
                {
                    try
                    {
                        _close?.Invoke();
                    }
                    catch { }

                    _isInitialized = false;
                }

                if (_hModule != IntPtr.Zero)
                {
                    try
                    {
                        FreeLibrary(_hModule);
                    }
                    catch { }

                    _hModule = IntPtr.Zero;
                }
            }
            GC.SuppressFinalize(this);
        }

        ~NvidiaDdsService()
        {
            Dispose();
        }
    }
}
