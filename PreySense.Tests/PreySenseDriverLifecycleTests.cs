using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PreySense.Core.Hardware;

namespace PreySense.Tests;

[TestClass]
public class PreySenseDriverLifecycleTests
{
    private sealed class MockExtractor : IPreySenseDriverExtractor
    {
        public string DriverFilePath { get; set; } = @"C:\TestPath\PreySenseDriver.sys";
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

    private class MockDeviceOps : IPreySenseDriverDeviceOperations
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
        public int StartServiceCallCount { get; protected set; }

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

        public virtual bool TryCreateService(string serviceName, string displayName, string binaryPath, out string? errorDetail)
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

        public virtual bool TryStartService(string serviceName, out string? errorDetail)
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
            var extractor = new PreySenseDriverExtractor(tempDir, "PreySenseDriver.sys");
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
            resourceAssembly: typeof(PreySenseDriverLifecycleTests).Assembly,
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

        using var svc = new PreySenseDriverService("PreySenseDriver", mockOps, mockExt);

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

        using (var svc = new PreySenseDriverService("PreySenseDriver", mockOps, mockExt))
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
            CreateServiceErrorDetail = "CreateService('PreySenseDriver') failed with Win32 Error 5 (ERROR_ACCESS_DENIED). Run PreySense as Administrator."
        };
        var mockExt = new MockExtractor();

        using var svc = new PreySenseDriverService("PreySenseDriver", mockOps, mockExt);

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
            StartServiceErrorDetail = "StartService('PreySenseDriver') failed with Win32 Error 577 (ERROR_INVALID_IMAGE_HASH). Windows signature verification failed for this driver."
        };
        var mockExt = new MockExtractor();

        using var svc = new PreySenseDriverService("PreySenseDriver", mockOps, mockExt);

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
        var mockExt = new MockExtractor { DriverFilePath = @"C:\NewPath\PreySenseDriver.sys" };

        using var svc = new PreySenseDriverService("PreySenseDriver", mockOps, mockExt);

        bool started = svc.EnsureServiceRunning(out string? err);
        Assert.IsTrue(started, err);
        Assert.AreEqual(1, mockOps.DeleteServiceCallCount, "Should delete obsolete service registration");
        Assert.AreEqual(1, mockOps.CreateServiceCallCount, "Should recreate with updated binary path");
        Assert.AreEqual(@"C:\NewPath\PreySenseDriver.sys", mockOps.ServiceBinaryPath);
    }

    [TestMethod]
    public void ServiceManager_MissingImagePath_RepairsRegistrationBeforeStart()
    {
        var mockOps = new MockDeviceOps
        {
            ServiceExistsResult = true,
            ServiceIsRunning = false,
            ServiceBinaryPath = null
        };
        var mockExt = new MockExtractor { DriverFilePath = @"C:\ProgramData\PreySense\Drivers\PreySenseDriver.sys" };

        using var svc = new PreySenseDriverService("PreySenseDriver", mockOps, mockExt);

        bool started = svc.EnsureServiceRunning(out string? err);
        Assert.IsTrue(started, err);
        Assert.AreEqual(1, mockOps.DeleteServiceCallCount);
        Assert.AreEqual(1, mockOps.CreateServiceCallCount);
        Assert.AreEqual(1, mockOps.StartServiceCallCount);
    }

    [TestMethod]
    public void ServiceManager_StartFileNotFound_ReextractsRecreatesAndRetries()
    {
        string path = Path.Combine(Path.GetTempPath(), "PreySenseDriverLifecycle", Guid.NewGuid().ToString("N"), "PreySenseDriver.sys");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });
        try
        {
            var ops = new FileNotFoundThenRecoverOps(path);
            var mockExt = new MockExtractor { DriverFilePath = path };

            using var svc = new PreySenseDriverService("PreySenseDriver", ops, mockExt);
            bool started = svc.EnsureServiceRunning(out string? err);
            Assert.IsTrue(started, err);
            Assert.AreEqual(2, ops.StartServiceCallCount);
            Assert.IsGreaterThanOrEqualTo(1, ops.DeleteServiceCallCount);
            Assert.AreEqual(2, mockExt.ExtractionCallCount);
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch { }
        }
    }

    private sealed class FileNotFoundThenRecoverOps : MockDeviceOps
    {
        public FileNotFoundThenRecoverOps(string path)
        {
            ServiceExistsResult = true;
            ServiceIsRunning = false;
            // Path matches extract target and exists, so first pass skips pre-start repair.
            ServiceBinaryPath = path;
        }

        public override bool TryStartService(string serviceName, out string? errorDetail)
        {
            StartServiceCallCount++;
            if (StartServiceCallCount == 1)
            {
                errorDetail = "StartService('PreySenseDriver') failed with Win32 Error 2 (ERROR_FILE_NOT_FOUND). The driver file was not found.";
                return false;
            }

            errorDetail = null;
            ServiceIsRunning = true;
            return true;
        }
    }

    [TestMethod]
    public void NormalizeDriverPath_StripsNtPrefixAndQuotes()
    {
        Assert.AreEqual(
            PreySenseDriverService.NormalizeDriverPath(@"C:\ProgramData\PreySense\Drivers\PreySenseDriver.sys"),
            PreySenseDriverService.NormalizeDriverPath(@"\??\C:\ProgramData\PreySense\Drivers\PreySenseDriver.sys"));
        Assert.AreEqual(
            PreySenseDriverService.NormalizeDriverPath(@"C:\ProgramData\PreySense\Drivers\PreySenseDriver.sys"),
            PreySenseDriverService.NormalizeDriverPath(@"""C:\ProgramData\PreySense\Drivers\PreySenseDriver.sys"""));
    }

    [TestMethod]
    public void Backend_WithPreySenseDriverService_CoordinatesLifecycleAndTeardown()
    {
        var mockOps = new MockDeviceOps
        {
            ServiceExistsResult = false,
            ServiceIsRunning = false
        };
        var mockExt = new MockExtractor();
        var svc = new PreySenseDriverService("PreySenseDriver", mockOps, mockExt);

        using (var backend = new PreySenseDriverBackend("PreySenseDriver", mockOps, svc))
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
