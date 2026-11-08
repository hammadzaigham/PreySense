using System;
using System.IO;
using System.Threading;

namespace PreySense.Core.Hardware;

public interface IPreySenseCpuDriverService : IDisposable
{
    string ServiceName { get; }
    string DevicePath { get; }
    DriverServiceOwnership ServiceOwnership { get; }
    bool IsRunning { get; }
    bool EnsureServiceRunning(out string? errorMessage);
    bool StopServiceIfOwned(out string? errorMessage);
}

public class PreySenseCpuDriverService : IPreySenseCpuDriverService
{
    public const string DefaultServiceName = "PreySenseCpuDriver";
    public const string DefaultDisplayName = "PreySense CPU Driver";
    public const string FallbackExternalServiceName = "TSDriverProbe";

    private readonly string _serviceName;
    private readonly string _devicePath;
    private readonly IThrottleStopDriverDeviceOperations _deviceOps;
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

    public PreySenseCpuDriverService(
        string serviceName = DefaultServiceName,
        IThrottleStopDriverDeviceOperations? deviceOps = null,
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

        _deviceOps = deviceOps ?? new Win32ThrottleStopDriverDeviceOperations();
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

    public bool EnsureServiceRunning(out string? errorMessage)
    {
        errorMessage = null;

        lock (_lock)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PreySenseCpuDriverService));

            if (_deviceOps.IsServiceRunning(_serviceName, out _))
            {
                if (_serviceOwnership == DriverServiceOwnership.Unknown)
                {
                    _serviceOwnership = DriverServiceOwnership.ExternalAttached;
                }
                return true;
            }

            if (!_serviceName.Equals(FallbackExternalServiceName, StringComparison.OrdinalIgnoreCase) &&
                _deviceOps.IsServiceRunning(FallbackExternalServiceName, out _))
            {
                _serviceOwnership = DriverServiceOwnership.ExternalAttached;
                return true;
            }

            EnsureInstanceRegistration();

            if (!_extractor.EnsureDriverExtracted(out string? extractedPath, out string? extractError))
            {
                errorMessage = "Failed to extract bundled driver: " + extractError;
                return false;
            }

            if (!_deviceOps.ServiceExists(_serviceName))
            {
                if (!_deviceOps.TryCreateService(_serviceName, DefaultDisplayName, extractedPath!, out string? createError))
                {
                    errorMessage = $"Failed to register driver service '{_serviceName}': {createError}";
                    return false;
                }
            }
            else
            {
                string? registeredPath = _deviceOps.QueryServiceBinaryPath(_serviceName);
                if (!string.IsNullOrWhiteSpace(registeredPath) &&
                    !string.Equals(registeredPath, extractedPath, StringComparison.OrdinalIgnoreCase))
                {
                    _deviceOps.TryDeleteService(_serviceName, out _);
                    if (!_deviceOps.TryCreateService(_serviceName, DefaultDisplayName, extractedPath!, out string? recreateError))
                    {
                        errorMessage = $"Failed to update driver service '{_serviceName}': {recreateError}";
                        return false;
                    }
                }
            }

            if (!_deviceOps.TryStartService(_serviceName, out string? startError))
            {
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
