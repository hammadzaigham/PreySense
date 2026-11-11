using System;
using System.IO;
using System.Threading;

namespace PreySense.Core.Hardware;

public interface IPreySenseDriverService : IDisposable
{
    string ServiceName { get; }
    string DevicePath { get; }
    DriverServiceOwnership ServiceOwnership { get; }
    bool IsRunning { get; }
    bool EnsureServiceRunning(out string? errorMessage);
    bool StopServiceIfOwned(out string? errorMessage);
}

public class PreySenseDriverService : IPreySenseDriverService
{
    public const string DefaultServiceName = "PreySenseDriver";
    public const string DefaultDisplayName = "PreySense Driver";
    private readonly string _serviceName;
    private readonly string _devicePath;
    private readonly IPreySenseDriverDeviceOperations _deviceOps;
    private readonly IPreySenseDriverExtractor _extractor;
    private readonly object _lock = new();

    private DriverServiceOwnership _serviceOwnership = DriverServiceOwnership.Unknown;
    private Mutex? _instanceMutex;
    private bool _hasInstanceOwnership;
    private bool _disposed;

    public string ServiceName => _serviceName;
    public string DevicePath => _devicePath;
    public DriverServiceOwnership ServiceOwnership => _serviceOwnership;

    public bool IsRunning => _deviceOps.IsServiceRunning(_serviceName, out _);

    public PreySenseDriverService(
        string serviceName = DefaultServiceName,
        IPreySenseDriverDeviceOperations? deviceOps = null,
        IPreySenseDriverExtractor? extractor = null)
    {
        _serviceName = string.IsNullOrWhiteSpace(serviceName) ? DefaultServiceName : serviceName;
        if (_serviceName.StartsWith(@"\\.\"))
        {
            _devicePath = _serviceName;
            _serviceName = _serviceName.Substring(4);
        }
        else
        {
            _devicePath = @"\\.\" + _serviceName;
        }

        _deviceOps = deviceOps ?? new Win32PreySenseDriverDeviceOperations();
        _extractor = extractor ?? new PreySenseDriverExtractor();
    }

    private void EnsureInstanceRegistration()
    {
        if (_instanceMutex != null)
            return;

        try
        {
            string mutexName = @"Global\PreySense_DriverService_" + _serviceName;
            _instanceMutex = new Mutex(initiallyOwned: false, name: mutexName, out bool createdNew);
            _hasInstanceOwnership = createdNew;
        }
        catch
        {
            try
            {
                string localMutexName = @"PreySense_DriverService_" + _serviceName;
                _instanceMutex = new Mutex(initiallyOwned: false, name: localMutexName, out bool createdNew);
                _hasInstanceOwnership = createdNew;
            }
            catch
            {
                _hasInstanceOwnership = false;
            }
        }
    }

    internal static string NormalizeDriverPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;
        string s = path.Trim().Trim('"');
        if (s.StartsWith(@"\??\", StringComparison.OrdinalIgnoreCase))
            s = s.Substring(4);
        try { return Path.GetFullPath(s); }
        catch { return s; }
    }

    private static bool DriverPathsEqual(string? left, string? right) =>
        string.Equals(NormalizeDriverPath(left), NormalizeDriverPath(right), StringComparison.OrdinalIgnoreCase);

    private static bool IsMissingDriverBinaryError(string? detail) =>
        !string.IsNullOrWhiteSpace(detail) &&
        (detail.Contains("ERROR_FILE_NOT_FOUND", StringComparison.OrdinalIgnoreCase) ||
         detail.Contains("Win32 Error 2", StringComparison.OrdinalIgnoreCase));

    private bool RegisterServiceAt(string extractedPath, out string? errorMessage)
    {
        errorMessage = null;
        if (_deviceOps.ServiceExists(_serviceName))
            _deviceOps.TryDeleteService(_serviceName, out _);

        if (_deviceOps.TryCreateService(_serviceName, DefaultDisplayName, extractedPath, out string? createError))
            return true;

        errorMessage = $"Failed to register driver service '{_serviceName}' at '{extractedPath}': {createError}";
        return false;
    }

    private bool ServiceRegistrationNeedsRepair(string extractedPath)
    {
        if (!_deviceOps.ServiceExists(_serviceName))
            return true;

        string? registeredPath = _deviceOps.QueryServiceBinaryPath(_serviceName);
        if (string.IsNullOrWhiteSpace(registeredPath))
            return true;
        if (!DriverPathsEqual(registeredPath, extractedPath))
            return true;
        // Stale SCM ImagePath: service points at our path but the .sys was deleted.
        return !File.Exists(NormalizeDriverPath(registeredPath));
    }

    public bool EnsureServiceRunning(out string? errorMessage)
    {
        errorMessage = null;

        lock (_lock)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PreySenseDriverService));

            if (_deviceOps.IsServiceRunning(_serviceName, out _))
            {
                if (_serviceOwnership == DriverServiceOwnership.Unknown)
                {
                    _serviceOwnership = DriverServiceOwnership.ExternalAttached;
                }
                return true;
            }


            EnsureInstanceRegistration();

            if (!_extractor.EnsureDriverExtracted(out string? extractedPath, out string? extractError) ||
                string.IsNullOrWhiteSpace(extractedPath))
            {
                errorMessage = "Failed to extract bundled driver: " + extractError;
                return false;
            }

            if (ServiceRegistrationNeedsRepair(extractedPath))
            {
                if (!RegisterServiceAt(extractedPath, out errorMessage))
                    return false;
            }

            if (!_deviceOps.TryStartService(_serviceName, out string? startError))
            {
                // Common after upgrades / ProgramData cleanup: SCM still has PreySenseDriver but ImagePath is gone.
                if (IsMissingDriverBinaryError(startError))
                {
                    if (!_extractor.EnsureDriverExtracted(out extractedPath, out extractError) ||
                        string.IsNullOrWhiteSpace(extractedPath))
                    {
                        errorMessage = "Failed to re-extract bundled driver after ERROR_FILE_NOT_FOUND: " + extractError;
                        return false;
                    }

                    if (!RegisterServiceAt(extractedPath, out errorMessage))
                        return false;

                    if (_deviceOps.TryStartService(_serviceName, out startError))
                    {
                        _serviceOwnership = DriverServiceOwnership.PreySenseOwned;
                        return true;
                    }

                    errorMessage =
                        $"Failed to start driver service '{_serviceName}' after repairing the ImagePath: {startError} " +
                        $"Expected driver binary at '{extractedPath}'.";
                    return false;
                }

                errorMessage = $"Failed to start driver service '{_serviceName}': {startError}";
                return false;
            }

            _serviceOwnership = DriverServiceOwnership.PreySenseOwned;
            return true;
        }
    }

    public bool StopServiceIfOwned(out string? errorMessage)
    {
        errorMessage = null;

        lock (_lock)
        {
            if (_serviceOwnership != DriverServiceOwnership.PreySenseOwned)
            {
                return true;
            }

            bool stopped = _deviceOps.TryStopService(_serviceName, out string? stopErr);
            if (!stopped)
            {
                errorMessage = stopErr;
                return false;
            }

            return true;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (!_disposed)
            {
                try
                {
                    StopServiceIfOwned(out _);
                }
                catch
                {
                }

                if (_instanceMutex != null)
                {
                    try
                    {
                        _instanceMutex.Dispose();
                    }
                    catch { }
                    _instanceMutex = null;
                }

                _disposed = true;
            }
        }
    }
}
