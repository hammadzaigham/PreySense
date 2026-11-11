using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace PreySense.Core.Hardware;

/// <summary>
/// Status of driver service ownership.
/// </summary>
public enum DriverServiceOwnership
{
    /// <summary>
    /// Service state is unknown or unmanaged.
    /// </summary>
    Unknown,

    /// <summary>
    /// The service was already running or registered by another process (e.g. an external process).
    /// PreySense attaches to it and MUST NOT stop or delete it upon Dispose.
    /// </summary>
    ExternalAttached,

    /// <summary>
    /// The service was started or created directly by PreySense.
    /// PreySense owns the service lifecycle and should stop it upon Dispose.
    /// </summary>
    PreySenseOwned
}

/// <summary>
/// Abstraction for low-level Win32 and Service Control Manager operations.
/// Enables mock testing of driver lifecycle, service installation, start/stop, IOCTLs, and error reporting without hardware or admin rights.
/// </summary>
public interface IPreySenseDriverDeviceOperations : IDisposable
{
    bool IsOpen { get; }
    void OpenDevice(string devicePath);
    void CloseDevice();
    bool DeviceIoControl(uint ioctlCode, ReadOnlySpan<byte> inBuffer, Span<byte> outBuffer, out uint bytesReturned);
    bool ServiceExists(string serviceName);
    bool IsServiceRunning(string serviceName, out string? statusDetail);
    bool TryCreateService(string serviceName, string displayName, string binaryPath, out string? errorDetail);
    bool TryStartService(string serviceName, out string? errorDetail);
    bool TryStopService(string serviceName, out string? errorDetail);
    bool TryDeleteService(string serviceName, out string? errorDetail);
    string? QueryServiceBinaryPath(string serviceName);
}

/// <summary>
/// Production Win32 and SCM implementation of <see cref="IPreySenseDriverDeviceOperations"/>.
/// </summary>
public sealed class Win32PreySenseDriverDeviceOperations : IPreySenseDriverDeviceOperations
{
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    private const uint SC_MANAGER_CONNECT = 0x0001;
    private const uint SC_MANAGER_CREATE_SERVICE = 0x0002;

    private const uint SERVICE_QUERY_STATUS = 0x0004;
    private const uint SERVICE_START = 0x0010;
    private const uint SERVICE_STOP = 0x0020;
    private const uint DELETE = 0x00010000;

    private const uint SERVICE_KERNEL_DRIVER = 0x00000001;
    private const uint SERVICE_DEMAND_START = 0x00000003;
    private const uint SERVICE_ERROR_NORMAL = 0x00000001;

    private const uint SERVICE_CONTROL_STOP = 0x00000001;
    private const uint SERVICE_RUNNING = 0x00000004;
    private const uint SERVICE_STOPPED = 0x00000001;

    // Standard Win32 error codes
    private const int ERROR_FILE_NOT_FOUND = 2;
    private const int ERROR_ACCESS_DENIED = 5;
    private const int ERROR_SERVICE_DOES_NOT_EXIST = 1060;
    private const int ERROR_SERVICE_ALREADY_RUNNING = 1056;
    private const int ERROR_SERVICE_NOT_ACTIVE = 1062;
    private const int ERROR_SERVICE_EXISTS = 1073;
    private const int ERROR_SERVICE_MARKED_FOR_DELETE = 1072;
    private const int ERROR_DRIVER_BLOCKED = 1275;
    private const int ERROR_INVALID_IMAGE_HASH = 577;

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_STATUS
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenSCManager(string? lpMachineName, string? lpDatabaseName, uint dwDesiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateService(
        IntPtr hSCManager,
        string lpServiceName,
        string lpDisplayName,
        uint dwDesiredAccess,
        uint dwServiceType,
        uint dwStartType,
        uint dwErrorControl,
        string lpBinaryPathName,
        string? lpLoadOrderGroup,
        IntPtr lpdwTagId,
        string? lpDependencies,
        string? lpServiceStartName,
        string? lpPassword);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenService(IntPtr hSCManager, string lpServiceName, uint dwDesiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool QueryServiceStatus(IntPtr hService, ref SERVICE_STATUS lpServiceStatus);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool StartService(IntPtr hService, uint dwNumServiceArgs, IntPtr lpServiceArgVectors);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool ControlService(IntPtr hService, uint dwControl, ref SERVICE_STATUS lpServiceStatus);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DeleteService(IntPtr hService);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr hSCObject);

    private SafeFileHandle? _deviceHandle;
    private bool _disposed;

    public bool IsOpen => _deviceHandle != null && !_deviceHandle.IsInvalid && !_deviceHandle.IsClosed;

    public void OpenDevice(string devicePath)
    {
        CloseDevice();

        SafeFileHandle handle = CreateFile(
            devicePath,
            GENERIC_READ | GENERIC_WRITE,
            0,
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_ATTRIBUTE_NORMAL,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            int err = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw err switch
            {
                ERROR_FILE_NOT_FOUND => new FileNotFoundException($"Device node '{devicePath}' was not found. Win32 Error 2 (ERROR_FILE_NOT_FOUND). Ensure the driver service is running."),
                ERROR_ACCESS_DENIED => new UnauthorizedAccessException($"Access denied opening device '{devicePath}'. Win32 Error 5 (ERROR_ACCESS_DENIED). Run as Administrator."),
                _ => new InvalidOperationException($"Failed to open driver device '{devicePath}'. Win32 Error: {err}.")
            };
        }

        _deviceHandle = handle;
    }

    public void CloseDevice()
    {
        if (_deviceHandle != null)
        {
            if (!_deviceHandle.IsClosed)
            {
                _deviceHandle.Dispose();
            }
            _deviceHandle = null;
        }
    }

    public unsafe bool DeviceIoControl(uint ioctlCode, ReadOnlySpan<byte> inBuffer, Span<byte> outBuffer, out uint bytesReturned)
    {
        bytesReturned = 0;
        if (!IsOpen || _deviceHandle == null)
            return false;

        fixed (byte* pIn = inBuffer)
        fixed (byte* pOut = outBuffer)
        {
            return DeviceIoControl(
                _deviceHandle,
                ioctlCode,
                (IntPtr)pIn,
                (uint)inBuffer.Length,
                (IntPtr)pOut,
                (uint)outBuffer.Length,
                out bytesReturned,
                IntPtr.Zero);
        }
    }

    public bool ServiceExists(string serviceName)
    {
        IntPtr hSC = OpenSCManager(null, null, SC_MANAGER_CONNECT);
        if (hSC == IntPtr.Zero)
            return false;

        try
        {
            IntPtr hSvc = OpenService(hSC, serviceName, SERVICE_QUERY_STATUS);
            if (hSvc != IntPtr.Zero)
            {
                CloseServiceHandle(hSvc);
                return true;
            }
            return false;
        }
        finally
        {
            CloseServiceHandle(hSC);
        }
    }

    public bool IsServiceRunning(string serviceName, out string? statusDetail)
    {
        statusDetail = null;
        IntPtr hSC = OpenSCManager(null, null, SC_MANAGER_CONNECT);
        if (hSC == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            statusDetail = FormatWin32ErrorMessage("OpenSCManager", err);
            return false;
        }

        try
        {
            IntPtr hSvc = OpenService(hSC, serviceName, SERVICE_QUERY_STATUS);
            if (hSvc == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                statusDetail = FormatWin32ErrorMessage($"OpenService('{serviceName}')", err);
                return false;
            }

            try
            {
                var status = new SERVICE_STATUS();
                if (QueryServiceStatus(hSvc, ref status))
                {
                    if (status.dwCurrentState == SERVICE_RUNNING)
                    {
                        statusDetail = "Service is RUNNING";
                        return true;
                    }
                    statusDetail = $"Service state is {status.dwCurrentState} (expected RUNNING=4)";
                    return false;
                }
                statusDetail = $"QueryServiceStatus failed with error {Marshal.GetLastWin32Error()}";
                return false;
            }
            finally
            {
                CloseServiceHandle(hSvc);
            }
        }
        finally
        {
            CloseServiceHandle(hSC);
        }
    }

    public bool TryCreateService(string serviceName, string displayName, string binaryPath, out string? errorDetail)
    {
        errorDetail = null;
        IntPtr hSC = OpenSCManager(null, null, SC_MANAGER_CREATE_SERVICE | SC_MANAGER_CONNECT);
        if (hSC == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            errorDetail = FormatWin32ErrorMessage("OpenSCManager", err);
            return false;
        }

        try
        {
            IntPtr hSvc = CreateService(
                hSC,
                serviceName,
                displayName,
                SERVICE_START | SERVICE_STOP | DELETE | SERVICE_QUERY_STATUS,
                SERVICE_KERNEL_DRIVER,
                SERVICE_DEMAND_START,
                SERVICE_ERROR_NORMAL,
                binaryPath,
                null,
                IntPtr.Zero,
                null,
                null,
                null);

            if (hSvc == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                if (err == ERROR_SERVICE_EXISTS)
                {
                    return true;
                }
                errorDetail = FormatWin32ErrorMessage($"CreateService('{serviceName}')", err);
                return false;
            }

            CloseServiceHandle(hSvc);
            return true;
        }
        finally
        {
            CloseServiceHandle(hSC);
        }
    }

    public bool TryStartService(string serviceName, out string? errorDetail)
    {
        errorDetail = null;
        IntPtr hSC = OpenSCManager(null, null, SC_MANAGER_CONNECT);
        if (hSC == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            errorDetail = FormatWin32ErrorMessage("OpenSCManager", err);
            return false;
        }

        try
        {
            IntPtr hSvc = OpenService(hSC, serviceName, SERVICE_START | SERVICE_QUERY_STATUS);
            if (hSvc == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                errorDetail = FormatWin32ErrorMessage($"OpenService('{serviceName}')", err);
                return false;
            }

            try
            {
                if (!StartService(hSvc, 0, IntPtr.Zero))
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err == ERROR_SERVICE_ALREADY_RUNNING)
                        return true;

                    errorDetail = FormatWin32ErrorMessage($"StartService('{serviceName}')", err);
                    return false;
                }
                return true;
            }
            finally
            {
                CloseServiceHandle(hSvc);
            }
        }
        finally
        {
            CloseServiceHandle(hSC);
        }
    }

    public bool TryStopService(string serviceName, out string? errorDetail)
    {
        errorDetail = null;
        IntPtr hSC = OpenSCManager(null, null, SC_MANAGER_CONNECT);
        if (hSC == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            errorDetail = FormatWin32ErrorMessage("OpenSCManager", err);
            return false;
        }

        try
        {
            IntPtr hSvc = OpenService(hSC, serviceName, SERVICE_STOP | SERVICE_QUERY_STATUS);
            if (hSvc == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                errorDetail = FormatWin32ErrorMessage($"OpenService('{serviceName}')", err);
                return false;
            }

            try
            {
                var status = new SERVICE_STATUS();
                if (!ControlService(hSvc, SERVICE_CONTROL_STOP, ref status))
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err == ERROR_SERVICE_NOT_ACTIVE)
                        return true;

                    errorDetail = FormatWin32ErrorMessage($"ControlService('{serviceName}', STOP)", err);
                    return false;
                }
                return true;
            }
            finally
            {
                CloseServiceHandle(hSvc);
            }
        }
        finally
        {
            CloseServiceHandle(hSC);
        }
    }

    public bool TryDeleteService(string serviceName, out string? errorDetail)
    {
        errorDetail = null;
        IntPtr hSC = OpenSCManager(null, null, SC_MANAGER_CONNECT);
        if (hSC == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            errorDetail = FormatWin32ErrorMessage("OpenSCManager", err);
            return false;
        }

        try
        {
            IntPtr hSvc = OpenService(hSC, serviceName, DELETE);
            if (hSvc == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                if (err == ERROR_SERVICE_DOES_NOT_EXIST)
                    return true;

                errorDetail = FormatWin32ErrorMessage($"OpenService('{serviceName}', DELETE)", err);
                return false;
            }

            try
            {
                if (!DeleteService(hSvc))
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err == ERROR_SERVICE_MARKED_FOR_DELETE)
                        return true;

                    errorDetail = FormatWin32ErrorMessage($"DeleteService('{serviceName}')", err);
                    return false;
                }
                return true;
            }
            finally
            {
                CloseServiceHandle(hSvc);
            }
        }
        finally
        {
            CloseServiceHandle(hSC);
        }
    }

    public string? QueryServiceBinaryPath(string serviceName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
            if (key == null)
                return null;

            object? rawVal = key.GetValue("ImagePath");
            if (rawVal is string s && !string.IsNullOrWhiteSpace(s))
            {
                if (s.StartsWith(@"\??\"))
                    s = s.Substring(4);
                return s.Trim('"');
            }
        }
        catch
        {
            // Ignore registry read permission issues if restricted
        }

        return null;
    }

    private static string FormatWin32ErrorMessage(string operation, int errorCode)
    {
        return errorCode switch
        {
            ERROR_ACCESS_DENIED => $"{operation} failed with Win32 Error 5 (ERROR_ACCESS_DENIED). Run PreySense as Administrator.",
            ERROR_FILE_NOT_FOUND => $"{operation} failed with Win32 Error 2 (ERROR_FILE_NOT_FOUND). The driver file was not found. PreySense will try to re-extract PreySenseDriver.sys under %ProgramData%\\PreySense\\Drivers and recreate the service; run as Administrator if that fails.",
            ERROR_SERVICE_DOES_NOT_EXIST => $"{operation} failed with Win32 Error 1060 (ERROR_SERVICE_DOES_NOT_EXIST).",
            ERROR_DRIVER_BLOCKED => $"{operation} failed with Win32 Error 1275 (ERROR_DRIVER_BLOCKED). Windows blocked the driver.",
            ERROR_INVALID_IMAGE_HASH => $"{operation} failed with Win32 Error 577 (ERROR_INVALID_IMAGE_HASH). Windows signature verification failed for this driver.",
            _ => $"{operation} failed with Win32 Error {errorCode}."
        };
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            CloseDevice();
            _disposed = true;
        }
    }
}

/// <summary>
/// Backend implementation of <see cref="ICpuDriverBackend"/> using the bundled PreySenseDriver IOCTLs.
/// Validates external driver identity and integrity without altering OS security.
/// </summary>
public sealed class PreySenseDriverBackend : ICpuDriverBackend, IPhysicalMemoryByteAccess
{
    // Verified PreySenseDriver IOCTL codes
    public const uint IOCTL_MSR_READ   = 0x80006448;
    public const uint IOCTL_MSR_WRITE  = 0x8000644C;
    public const uint IOCTL_PHYS_READ  = 0x80006498;
    public const uint IOCTL_PHYS_WRITE = 0x8000649C;

    // Safety guardrails: Prevent write access to critical OS kernel MSRs
    // 0x174..0x176: IA32_SYSENTER_CS, IA32_SYSENTER_ESP, IA32_SYSENTER_EIP
    // 0xC0000080..0xC0000084: IA32_EFER, IA32_STAR, IA32_LSTAR, IA32_CSTAR, IA32_SFMASK
    public static readonly (uint Min, uint Max)[] RestrictedMsrRanges = new[]
    {
        (0x00000174u, 0x00000176u),
        (0xC0000080u, 0xC0000084u)
    };

    private readonly string _serviceName;
    private readonly string _devicePath;
    private readonly IPreySenseDriverDeviceOperations _deviceOps;
    private readonly IPreySenseDriverService? _serviceManager;
    private readonly bool _ownsDeviceOps;
    private readonly bool _ownsServiceManager;
    private readonly object _lock = new();

    private DriverServiceOwnership _serviceOwnership = DriverServiceOwnership.Unknown;
    private bool _initialized;
    private bool _disposed;

    public bool IsInitialized => _initialized && !_disposed && _deviceOps.IsOpen;

    public string ServiceName => _serviceName;
    public string DevicePath => _devicePath;
    public DriverServiceOwnership ServiceOwnership => _serviceOwnership;

    public PreySenseDriverBackend(
        string serviceOrDeviceName = "PreySenseDriver",
        IPreySenseDriverDeviceOperations? deviceOps = null,
        IPreySenseDriverService? serviceManager = null)
    {
        if (string.IsNullOrWhiteSpace(serviceOrDeviceName))
            throw new ArgumentException("Service or device name cannot be empty.", nameof(serviceOrDeviceName));

        if (serviceOrDeviceName.StartsWith(@"\\.\"))
        {
            _devicePath = serviceOrDeviceName;
            _serviceName = serviceOrDeviceName.Substring(4);
        }
        else
        {
            _serviceName = serviceOrDeviceName;
            _devicePath = $@"\\.\{serviceOrDeviceName}";
        }

        if (deviceOps != null)
        {
            _deviceOps = deviceOps;
            _ownsDeviceOps = false;
        }
        else
        {
            _deviceOps = new Win32PreySenseDriverDeviceOperations();
            _ownsDeviceOps = true;
        }

        if (serviceManager != null)
        {
            _serviceManager = serviceManager;
            _ownsServiceManager = false;
        }
        else
        {
            _serviceManager = new PreySenseDriverService(_serviceName, _deviceOps);
            _ownsServiceManager = true;
        }
    }

    public PreySenseDriverBackend(IPreySenseDriverService serviceManager)
        : this(serviceManager?.ServiceName ?? "PreySenseDriver", null, serviceManager)
    {
    }

    /// <summary>
    /// Explicitly initializes the backend, verifying service status, attempting start if configured,
    /// tracking service ownership, and opening the driver device handle.
    /// Note: PreySense bundles a signed driver binary which Windows validates upon loading.
    /// External service reuse is tracked so unowned services are never stopped or deleted upon Dispose.
    /// </summary>
    public bool Initialize(out string? errorMessage, bool attemptStartService = false)
    {
        errorMessage = null;
        lock (_lock)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PreySenseDriverBackend));

            if (IsInitialized)
                return true;

            // Check if primary service is already running prior to start attempt
            bool wasAlreadyRunning = _deviceOps.IsServiceRunning(_serviceName, out string? statusDetail);

            if (wasAlreadyRunning)
            {
                try
                {
                    _deviceOps.OpenDevice(_devicePath);
                    if (_deviceOps.IsOpen)
                    {
                        _serviceOwnership = DriverServiceOwnership.ExternalAttached;
                        _initialized = true;
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    errorMessage = $"Driver service '{_serviceName}' is running but opening '{_devicePath}' failed: {ex.Message}";
                    return false;
                }
            }


            // If not running, check if start was requested
            if (!attemptStartService)
            {
                errorMessage = $"Driver service '{_serviceName}' is not running ({statusDetail}). Service start was not requested.";
                return false;
            }

            // Start via service manager if available or fallback direct
            if (_serviceManager != null)
            {
                if (_serviceManager.EnsureServiceRunning(out string? svcErr))
                {
                    _serviceOwnership = _serviceManager.ServiceOwnership != DriverServiceOwnership.Unknown
                        ? _serviceManager.ServiceOwnership
                        : DriverServiceOwnership.PreySenseOwned;

                    try
                    {
                        _deviceOps.OpenDevice(_devicePath);
                        if (_deviceOps.IsOpen)
                        {
                            _initialized = true;
                            return true;
                        }
                    }
                    catch (Exception openEx)
                    {
                        errorMessage = $"Started service '{_serviceName}' via service manager but failed to open device '{_devicePath}': {openEx.Message}";
                        return false;
                    }
                }
                else
                {
                    errorMessage = $"Driver service '{_serviceName}' could not be started: {svcErr}";
                    return false;
                }
            }
            else if (_deviceOps.TryStartService(_serviceName, out string? startErr))
            {
                _serviceOwnership = DriverServiceOwnership.PreySenseOwned;
                try
                {
                    _deviceOps.OpenDevice(_devicePath);
                    if (_deviceOps.IsOpen)
                    {
                        _initialized = true;
                        return true;
                    }
                }
                catch (Exception openEx)
                {
                    errorMessage = $"Started service '{_serviceName}' but failed to open device '{_devicePath}': {openEx.Message}";
                    return false;
                }
            }
            else
            {
                errorMessage = $"Driver service '{_serviceName}' is not running ({statusDetail}) and could not be started: {startErr}";
                return false;
            }

            _initialized = _deviceOps.IsOpen;
            return _initialized;
        }
    }

    public void EnsureHandle()
    {
        lock (_lock)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PreySenseDriverBackend));

            if (IsInitialized)
                return;

            if (!Initialize(out string? err, attemptStartService: false))
            {
                throw new InvalidOperationException(err ?? $"Failed to initialize PreySenseDriver backend for device '{_devicePath}'.");
            }
        }
    }

    public bool ReadMsr(uint msr, out ulong value)
    {
        value = 0;
        lock (_lock)
        {
            if (_disposed || !IsInitialized)
            {
                try
                {
                    EnsureHandle();
                }
                catch
                {
                    return false;
                }
            }

            Span<byte> inBuf = stackalloc byte[4];
            MemoryMarshal.Write(inBuf, in msr);

            Span<byte> outBuf = stackalloc byte[8];
            if (_deviceOps.DeviceIoControl(IOCTL_MSR_READ, inBuf, outBuf, out uint bytesReturned) && bytesReturned == 8)
            {
                value = MemoryMarshal.Read<ulong>(outBuf);
                return true;
            }

            return false;
        }
    }

    public bool WriteMsr(uint msr, ulong value)
    {
        // Safety Guardrail Check
        if (IsMsrRestricted(msr))
        {
            return false;
        }

        lock (_lock)
        {
            if (_disposed || !IsInitialized)
            {
                try
                {
                    EnsureHandle();
                }
                catch
                {
                    return false;
                }
            }

            // Input buffer: 12 bytes = uint32 msr (0..3) + uint64 value (4..11)
            Span<byte> inBuf = stackalloc byte[12];
            MemoryMarshal.Write(inBuf[..4], in msr);
            MemoryMarshal.Write(inBuf[4..], in value);

            return _deviceOps.DeviceIoControl(IOCTL_MSR_WRITE, inBuf, Span<byte>.Empty, out _);
        }
    }

    public bool ReadPhysicalByte(ulong physicalAddress, out byte value)
    {
        lock (_lock)
        {
            value = 0;
            if (_disposed || !IsInitialized) return false;
            Span<byte> input = stackalloc byte[8];
            MemoryMarshal.Write(input, in physicalAddress);
            Span<byte> output = stackalloc byte[1];
            if (!_deviceOps.DeviceIoControl(IOCTL_PHYS_READ, input, output, out uint count) || count != 1) return false;
            value = output[0];
            return true;
        }
    }

    public bool TryReadMchbar(out ulong address)
    {
        lock (_lock)
        {
            address = 0;
            if (_disposed || !IsInitialized) return false;
            bool ReadConfig(byte offset, out uint value) => TryReadHostBridgeConfig(offset, out value);
            // Raptor Lake host bridges share MCHBAR at config 0x48/0x4C; no hardcoded physical base.
            if (!ReadConfig(0, out uint id) || !IsSupportedHostBridge(id) ||
                !ReadConfig(0x48, out uint low) || !ReadConfig(0x4C, out uint high) || (low & 1) == 0)
                return false;
            address = ((ulong)high << 32 | low) & ~0x7FFFUL;
            return address != 0;
        }
    }

    public bool TryReadHostBridgeConfig(byte offset, out uint value)
    {
        lock (_lock)
        {
            value = 0;
            if (_disposed || !IsInitialized || offset is not (0 or 0x48 or 0x4C)) return false;
            // HalGetBusDataByOffset: low byte = bus, bits 8/16 = device/function, high dword = offset.
            ulong packed = (ulong)offset << 32;
            Span<byte> input = stackalloc byte[8];
            Span<byte> output = stackalloc byte[4];
            MemoryMarshal.Write(input, in packed);
            if (!_deviceOps.DeviceIoControl(0x800064A0, input, output, out uint count) || count != 4) return false;
            value = MemoryMarshal.Read<uint>(output);
            return true;
        }
    }

    public bool WritePhysicalByte(ulong physicalAddress, byte value)
    {
        lock (_lock)
        {
            if (_disposed || !IsInitialized) return false;
            Span<byte> input = stackalloc byte[9];
            MemoryMarshal.Write(input[..8], in physicalAddress);
            input[8] = value;
            return _deviceOps.DeviceIoControl(IOCTL_PHYS_WRITE, input, Span<byte>.Empty, out _);
        }
    }

    public bool ReadPhysicalDword(ulong physicalAddress, out uint value)
    {
        value = 0;
        lock (_lock)
        {
            if (_disposed || !IsInitialized)
            {
                try
                {
                    EnsureHandle();
                }
                catch
                {
                    return false;
                }
            }

            // Input buffer: 8 bytes = uint64 physicalAddress
            // Output buffer: 4 bytes = uint32 value
            Span<byte> inBuf = stackalloc byte[8];
            MemoryMarshal.Write(inBuf, in physicalAddress);

            Span<byte> outBuf = stackalloc byte[4];
            if (_deviceOps.DeviceIoControl(IOCTL_PHYS_READ, inBuf, outBuf, out uint bytesReturned) && bytesReturned == 4)
            {
                value = MemoryMarshal.Read<uint>(outBuf);
                return true;
            }

            return false;
        }
    }

    public bool WritePhysicalDword(ulong physicalAddress, uint value)
    {
        lock (_lock)
        {
            if (_disposed || !IsInitialized)
            {
                try
                {
                    EnsureHandle();
                }
                catch
                {
                    return false;
                }
            }

            // Input buffer: 12 bytes = uint64 physicalAddress (0..7) + uint32 value (8..11)
            Span<byte> inBuf = stackalloc byte[12];
            MemoryMarshal.Write(inBuf[..8], in physicalAddress);
            MemoryMarshal.Write(inBuf[8..], in value);

            return _deviceOps.DeviceIoControl(IOCTL_PHYS_WRITE, inBuf, Span<byte>.Empty, out _);
        }
    }

    /// <summary>Intel vendor + known Alder Lake / Raptor Lake host-bridge device IDs that expose MCHBAR at 0x48/0x4C.</summary>
    public static bool IsSupportedHostBridge(uint configId)
    {
        if ((configId & 0xFFFF) != 0x8086) return false;
        ushort device = (ushort)(configId >> 16);
        return device is 0x7D1C or 0x4637 or 0x463B or 0x4647 or 0x4649
            or 0x4641 or 0x4621 or 0x4601 or 0x4660 or 0x4668
            or 0xA702 or 0xA729 or 0xA728 or 0xA72A or 0xA719
            or 0xA700 or 0xA701 or 0xA703 or 0xA704 or 0xA705;
    }
    /// <summary>
    /// Checks whether an MSR register address falls within a restricted kernel range.
    /// </summary>
    public static bool IsMsrRestricted(uint msr)
    {
        foreach (var (min, max) in RestrictedMsrRanges)
        {
            if (msr >= min && msr <= max)
                return true;
        }
        return false;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (!_disposed)
            {
                _initialized = false;

                // Service ownership cleanup:
                // Only stop the service if PreySense started/owned it. Never stop or delete an external/attached service!
                if (_ownsServiceManager && _serviceManager != null)
                {
                    _serviceManager.Dispose();
                }
                else if (_serviceOwnership == DriverServiceOwnership.PreySenseOwned)
                {
                    try
                    {
                        _deviceOps.TryStopService(_serviceName, out _);
                    }
                    catch
                    {
                        // Best effort stop on dispose
                    }
                }

                if (_ownsDeviceOps)
                {
                    _deviceOps.Dispose();
                }
                else
                {
                    _deviceOps.CloseDevice();
                }
                _disposed = true;
            }
        }
    }
}
