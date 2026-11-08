using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PreySense.Core.Hardware;

namespace PreySense.Tests;

[TestClass]
public class PreySenseCpuDriverLifecycleTests
{
    private sealed class MockExtractor : IPreySenseDriverExtractor
    {
        public string DriverFilePath { get; set; } = @"C:\TestPath\PreySenseCpuDriver.sys";
        public bool ExtractionResult { get; set; } = true;
        public string? ExtractionErrorMessage { get; set; }
        public int ExtractionCallCount { get; private set; }

        public bool EnsureDriverExtracted(out string? extractedPath, out string? errorMessage)
        {
            ExtractionCallCount++;
            extractedPath = ExtractionResult ? DriverFilePath : null;
            errorMessage = ExtractionErrorMessage;
            return ExtractionResult;
        }
    }

    private sealed class MockDeviceOps : IThrottleStopDriverDeviceOperations
    {
        public bool IsOpen { get; set; }
        public string? OpenedPath { get; private set; }
        public int CloseDeviceCallCount { get; private set; }
        public int DisposeCallCount { get; private set; }

        public bool ServiceExistsResult { get; set; } = false;
        public int ServiceExistsCallCount { get; private set; }
        public bool ServiceIsRunning { get; set; } = false;
        public string? ServiceStatusDetail { get; set; } = "Service is STOPPED";

        public bool CreateServiceShouldSucceed { get; set; } = true;
        public string? CreateServiceErrorDetail { get; set; }
        public int CreateServiceCallCount { get; private set; }

        public bool StartServiceShouldSucceed { get; set; } = true;
        public string? StartServiceErrorDetail { get; set; }
        public int StartServiceCallCount { get; private set; }

        public bool StopServiceShouldSucceed { get; set; } = true;
        public string? StopServiceErrorDetail { get; set; }
        public int StopServiceCallCount { get; private set; }

        public bool DeleteServiceShouldSucceed { get; set; } = true;
        public int DeleteServiceCallCount { get; private set; }

        public string? ServiceBinaryPath { get; set; }

        public void OpenDevice(string devicePath)
        {
            OpenedPath = devicePath;
            IsOpen = true;
        }

        public void CloseDevice()
        {
            CloseDeviceCallCount++;
            IsOpen = false;
        }

        public bool DeviceIoControl(uint ioctlCode, ReadOnlySpan<byte> inBuffer, Span<byte> outBuffer, out uint bytesReturned)
        {
            bytesReturned = 0;
            return true;
        }

        public bool ServiceExists(string serviceName)
        {
            ServiceExistsCallCount++;
            return ServiceExistsResult;
        }

        public bool IsServiceRunning(string serviceName, out string? statusDetail)
        {
            statusDetail = ServiceStatusDetail;
            return ServiceIsRunning;
        }

        public bool TryCreateService(string serviceName, string displayName, string binaryPath, out string? errorDetail)
        {
            CreateServiceCallCount++;
            errorDetail = CreateServiceErrorDetail;
            if (CreateServiceShouldSucceed)
            {
                ServiceExistsResult = true;
                ServiceBinaryPath = binaryPath;
                return true;
            }
            return false;
        }

        public bool TryStartService(string serviceName, out string? errorDetail)
        {
            StartServiceCallCount++;
            errorDetail = StartServiceErrorDetail;
            if (StartServiceShouldSucceed)
            {
                ServiceIsRunning = true;
                return true;
            }
            return false;
        }

        public bool TryStopService(string serviceName, out string? errorDetail)
        {
            StopServiceCallCount++;
            errorDetail = StopServiceErrorDetail;
            if (StopServiceShouldSucceed)
            {
                ServiceIsRunning = false;
                return true;
            }
            return false;
        }

        public bool TryDeleteService(string serviceName, out string? errorDetail)
        {
            DeleteServiceCallCount++;
            errorDetail = null;
            if (DeleteServiceShouldSucceed)
            {
                ServiceExistsResult = false;
                return true;
            }
            return false;
        }

        public string? QueryServiceBinaryPath(string serviceName) => ServiceBinaryPath;

        public void Dispose()
        {
            DisposeCallCount++;
            CloseDevice();
        }
    }

    [TestMethod]
    public void Extractor_BundledResource_CanBeExtractedToSafeDirectory()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "PreySenseTestDriver_" + Guid.NewGuid().ToString("N"));
        try
        {
            var extractor = new PreySenseDriverExtractor(tempDir, "embedded_x64.sys");
            bool extracted = extractor.EnsureDriverExtracted(out string? path, out string? err);

            Assert.IsTrue(extracted, $"Failed extraction: {err}");
            Assert.IsNotNull(path);
            Assert.IsTrue(File.Exists(path));
            Assert.IsGreaterThan(10000, new FileInfo(path).Length, "Driver file should not be empty");

            bool secondCall = extractor.EnsureDriverExtracted(out string? secondPath, out string? secondErr);
            Assert.IsTrue(secondCall);
            Assert.AreEqual(path, secondPath);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [TestMethod]
    public void Extractor_MissingResource_ReturnsExplicitErrorMessage()
    {
        var extractor = new PreySenseDriverExtractor(
            targetDirectory: Path.GetTempPath(),
            resourceAssembly: typeof(PreySenseCpuDriverLifecycleTests).Assembly,
            resourceName: "NonExistentResource");

        bool extracted = extractor.EnsureDriverExtracted(out string? path, out string? err);
        Assert.IsFalse(extracted);
        Assert.IsNull(path);
        StringAssert.Contains(err, "NonExistentResource");
    }

    [TestMethod]
    public void ServiceManager_RegistersAndStarts_WhenServiceDoesNotExist()
    {
        var mockOps = new MockDeviceOps
        {
            ServiceExistsResult = false,
            ServiceIsRunning = false
        };
        var mockExt = new MockExtractor();

        using var svc = new PreySenseCpuDriverService("PreySenseCpuDriver", mockOps, mockExt);

        bool started = svc.EnsureServiceRunning(out string? err);
        Assert.IsTrue(started, err);
        Assert.AreEqual(1, mockExt.ExtractionCallCount);
        Assert.AreEqual(1, mockOps.CreateServiceCallCount);
        Assert.AreEqual(1, mockOps.StartServiceCallCount);
        Assert.AreEqual(DriverServiceOwnership.PreySenseOwned, svc.ServiceOwnership);
    }

    [TestMethod]
    public void ServiceManager_ReusesRunningService_AsExternalAttached_NeverStopsOnDispose()
    {
        var mockOps = new MockDeviceOps
        {
            ServiceExistsResult = true,
            ServiceIsRunning = true
        };
        var mockExt = new MockExtractor();

        using (var svc = new PreySenseCpuDriverService("PreySenseCpuDriver", mockOps, mockExt))
        {
            bool started = svc.EnsureServiceRunning(out string? err);
            Assert.IsTrue(started, err);
            Assert.AreEqual(0, mockExt.ExtractionCallCount, "Should not extract if service already running");
            Assert.AreEqual(0, mockOps.CreateServiceCallCount);
            Assert.AreEqual(0, mockOps.StartServiceCallCount);
            Assert.AreEqual(DriverServiceOwnership.ExternalAttached, svc.ServiceOwnership);
        }

        Assert.AreEqual(0, mockOps.StopServiceCallCount);
    }

    [TestMethod]
    public void ServiceManager_AccessDenied_ReturnsElevationGuidance()
    {
        var mockOps = new MockDeviceOps
        {
            ServiceExistsResult = false,
            ServiceIsRunning = false,
            CreateServiceShouldSucceed = false,
            CreateServiceErrorDetail = "CreateService('PreySenseCpuDriver') failed with Win32 Error 5 (ERROR_ACCESS_DENIED). Run PreySense as Administrator."
        };
        var mockExt = new MockExtractor();

        using var svc = new PreySenseCpuDriverService("PreySenseCpuDriver", mockOps, mockExt);

        bool started = svc.EnsureServiceRunning(out string? err);
        Assert.IsFalse(started);
        StringAssert.Contains(err, "ERROR_ACCESS_DENIED");
        StringAssert.Contains(err, "Administrator");
    }

    [TestMethod]
    public void ServiceManager_SignatureBlock_ReturnsClearSignatureError()
    {
        var mockOps = new MockDeviceOps
        {
            ServiceExistsResult = true,
            ServiceIsRunning = false,
            StartServiceShouldSucceed = false,
            StartServiceErrorDetail = "StartService('PreySenseCpuDriver') failed with Win32 Error 577 (ERROR_INVALID_IMAGE_HASH). Windows signature verification failed for this driver."
        };
        var mockExt = new MockExtractor();

        using var svc = new PreySenseCpuDriverService("PreySenseCpuDriver", mockOps, mockExt);

        bool started = svc.EnsureServiceRunning(out string? err);
        Assert.IsFalse(started);
        StringAssert.Contains(err, "ERROR_INVALID_IMAGE_HASH");
    }

    [TestMethod]
    public void ServiceManager_AppUpgrade_UpdatesRegisteredBinaryPath()
    {
        var mockOps = new MockDeviceOps
        {
            ServiceExistsResult = true,
            ServiceIsRunning = false,
            ServiceBinaryPath = @"C:\OldPath\OldDriver.sys"
        };
        var mockExt = new MockExtractor { DriverFilePath = @"C:\NewPath\PreySenseCpuDriver.sys" };

        using var svc = new PreySenseCpuDriverService("PreySenseCpuDriver", mockOps, mockExt);

        bool started = svc.EnsureServiceRunning(out string? err);
        Assert.IsTrue(started, err);
        Assert.AreEqual(1, mockOps.DeleteServiceCallCount, "Should delete obsolete service registration");
        Assert.AreEqual(1, mockOps.CreateServiceCallCount, "Should recreate with updated binary path");
        Assert.AreEqual(@"C:\NewPath\PreySenseCpuDriver.sys", mockOps.ServiceBinaryPath);
    }

    [TestMethod]
    public void Backend_WithPreySenseCpuDriverService_CoordinatesLifecycleAndTeardown()
    {
        var mockOps = new MockDeviceOps
        {
            ServiceExistsResult = false,
            ServiceIsRunning = false
        };
        var mockExt = new MockExtractor();
        var svc = new PreySenseCpuDriverService("PreySenseCpuDriver", mockOps, mockExt);

        using (var backend = new ThrottleStopCpuDriverBackend("PreySenseCpuDriver", mockOps, svc))
        {
            bool ok = backend.Initialize(out string? err, attemptStartService: true);
            Assert.IsTrue(ok, err);
            Assert.AreEqual(DriverServiceOwnership.PreySenseOwned, backend.ServiceOwnership);
            Assert.IsTrue(backend.IsInitialized);
            Assert.AreEqual(1, mockOps.StartServiceCallCount);
        }

        Assert.AreEqual(1, mockOps.StopServiceCallCount);
    }
}
