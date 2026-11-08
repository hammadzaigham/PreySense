using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PreySense.Core.Hardware;

namespace PreySense.Tests;

[TestClass]
public class ThrottleStopCpuDriverBackendTests
{
    private sealed class MockDriverDeviceOps : IThrottleStopDriverDeviceOperations
    {
        public bool IsOpen { get; set; }
        public string? OpenedPath { get; private set; }
        public int CloseDeviceCallCount { get; private set; }
        public int DisposeCallCount { get; private set; }

        public bool ServiceIsRunning { get; set; } = true;
        public string? ServiceStatusDetail { get; set; } = "Service is RUNNING";

        public bool ServiceStartShouldSucceed { get; set; } = true;
        public string? ServiceStartErrorDetail { get; set; }
        public int StartServiceCallCount { get; private set; }

        public string? ServiceBinaryPath { get; set; }

        public Exception? OpenDeviceException { get; set; }

        public Dictionary<uint, ulong> MsrRegisters { get; } = new();
        public Dictionary<ulong, uint> PhysicalDwords { get; } = new();
        public Dictionary<ulong, uint> PciRegisters { get; } = new();

        public List<(uint Code, byte[] InData)> RecordedIoctls { get; } = new();
        public bool FailIoctls { get; set; }

        public void OpenDevice(string devicePath)
        {
            if (OpenDeviceException != null)
                throw OpenDeviceException;

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
            if (!IsOpen || FailIoctls)
                return false;

            byte[] copy = inBuffer.ToArray();
            RecordedIoctls.Add((ioctlCode, copy));

            switch (ioctlCode)
            {
                case 0x800064A0:
                    if (inBuffer.Length == 8 && outBuffer.Length == 4 &&
                        PciRegisters.TryGetValue(MemoryMarshal.Read<ulong>(inBuffer), out uint pci))
                    {
                        MemoryMarshal.Write(outBuffer, in pci);
                        bytesReturned = 4;
                        return true;
                    }
                    return false;
                case ThrottleStopCpuDriverBackend.IOCTL_MSR_READ:
                    if (inBuffer.Length == 4 && outBuffer.Length >= 8)
                    {
                        uint msr = MemoryMarshal.Read<uint>(inBuffer);
                        if (MsrRegisters.TryGetValue(msr, out ulong val))
                        {
                            MemoryMarshal.Write(outBuffer, in val);
                            bytesReturned = 8;
                            return true;
                        }
                    }
                    return false;

                case ThrottleStopCpuDriverBackend.IOCTL_MSR_WRITE:
                    if (inBuffer.Length == 12)
                    {
                        uint msr = MemoryMarshal.Read<uint>(inBuffer[..4]);
                        ulong val = MemoryMarshal.Read<ulong>(inBuffer[4..]);
                        MsrRegisters[msr] = val;
                        bytesReturned = 0;
                        return true;
                    }
                    return false;

                case ThrottleStopCpuDriverBackend.IOCTL_PHYS_READ:
                    if (inBuffer.Length == 8 && outBuffer.Length >= 4)
                    {
                        ulong addr = MemoryMarshal.Read<ulong>(inBuffer);
                        if (PhysicalDwords.TryGetValue(addr, out uint dw))
                        {
                            MemoryMarshal.Write(outBuffer, in dw);
                            bytesReturned = 4;
                            return true;
                        }
                    }
                    return false;

                case ThrottleStopCpuDriverBackend.IOCTL_PHYS_WRITE:
                    if (inBuffer.Length == 12)
                    {
                        ulong addr = MemoryMarshal.Read<ulong>(inBuffer[..8]);
                        uint dw = MemoryMarshal.Read<uint>(inBuffer[8..]);
                        PhysicalDwords[addr] = dw;
                        bytesReturned = 0;
                        return true;
                    }
                    return false;
            }

            return false;
        }

        public bool ServiceExistsResult { get; set; } = true;
        public int ServiceExistsCallCount { get; private set; }
        public bool ServiceExists(string serviceName)
        {
            ServiceExistsCallCount++;
            return ServiceExistsResult;
        }

        public bool CreateServiceShouldSucceed { get; set; } = true;
        public string? CreateServiceErrorDetail { get; set; }
        public int CreateServiceCallCount { get; private set; }
        public string? CreatedBinaryPath { get; private set; }
        public bool TryCreateService(string serviceName, string displayName, string binaryPath, out string? errorDetail)
        {
            CreateServiceCallCount++;
            CreatedBinaryPath = binaryPath;
            errorDetail = CreateServiceErrorDetail;
            if (CreateServiceShouldSucceed)
            {
                ServiceExistsResult = true;
                ServiceBinaryPath = binaryPath;
                return true;
            }
            return false;
        }

        public bool DeleteServiceShouldSucceed { get; set; } = true;
        public string? DeleteServiceErrorDetail { get; set; }
        public int DeleteServiceCallCount { get; private set; }
        public bool TryDeleteService(string serviceName, out string? errorDetail)
        {
            DeleteServiceCallCount++;
            errorDetail = DeleteServiceErrorDetail;
            if (DeleteServiceShouldSucceed)
            {
                ServiceExistsResult = false;
                return true;
            }
            return false;
        }

        public bool IsServiceRunning(string serviceName, out string? statusDetail)
        {
            statusDetail = ServiceStatusDetail;
            return ServiceIsRunning;
        }

        public bool TryStartService(string serviceName, out string? errorDetail)
        {
            StartServiceCallCount++;
            errorDetail = ServiceStartErrorDetail;
            if (ServiceStartShouldSucceed)
            {
                ServiceIsRunning = true;
                OpenDeviceException = null; // Clear failure once service has started
                return true;
            }
            return false;
        }

        public int StopServiceCallCount { get; private set; }

        public bool TryStopService(string serviceName, out string? errorDetail)
        {
            StopServiceCallCount++;
            errorDetail = null;
            ServiceIsRunning = false;
            return true;
        }

        public string? QueryServiceBinaryPath(string serviceName) => ServiceBinaryPath;

        public void Dispose()
        {
            DisposeCallCount++;
            CloseDevice();
        }
    }

    [TestMethod]
    public void MchbarDiscovery_PlacesConfigOffsetInHighDword()
    {
        var ops = new MockDriverDeviceOps();
        ops.PciRegisters[0] = 0x7D1C8086;
        ops.PciRegisters[0x48UL << 32] = 0xFEDC0001;
        ops.PciRegisters[0x4CUL << 32] = 0;
        using var backend = new ThrottleStopCpuDriverBackend("TSDriverProbe", ops);
        Assert.IsTrue(backend.Initialize(out var error), error);
        Assert.IsTrue(backend.TryReadMchbar(out ulong address));
        Assert.AreEqual(0xFEDC0000UL, address);
        Assert.HasCount(3, ops.RecordedIoctls);
        Assert.AreEqual(0x48UL << 32, BitConverter.ToUInt64(ops.RecordedIoctls[1].InData));
        Assert.AreEqual(0x4CUL << 32, BitConverter.ToUInt64(ops.RecordedIoctls[2].InData));
    }

    [TestMethod]
    public void Constructor_NormalizesDeviceAndServiceNames()
    {
        using var backend1 = new ThrottleStopCpuDriverBackend("TSDriverProbe", new MockDriverDeviceOps());
        Assert.AreEqual("TSDriverProbe", backend1.ServiceName);
        Assert.AreEqual(@"\\.\TSDriverProbe", backend1.DevicePath);

        using var backend2 = new ThrottleStopCpuDriverBackend(@"\\.\CustomDriver", new MockDriverDeviceOps());
        Assert.AreEqual("CustomDriver", backend2.ServiceName);
        Assert.AreEqual(@"\\.\CustomDriver", backend2.DevicePath);
    }

    [TestMethod]
    public void Constructor_EmptyServiceName_ThrowsArgumentException()
    {
        bool caught = false;
        try
        {
            _ = new ThrottleStopCpuDriverBackend("   ");
        }
        catch (ArgumentException)
        {
            caught = true;
        }
        Assert.IsTrue(caught, "Expected ArgumentException for empty service name.");
    }

    [TestMethod]
    public void Initialize_WhenServiceRunningAndDeviceAccessible_Succeeds()
    {
        var mockOps = new MockDriverDeviceOps { ServiceIsRunning = true };
        using var backend = new ThrottleStopCpuDriverBackend("TSDriverProbe", mockOps);

        bool ok = backend.Initialize(out string? err);
        Assert.IsTrue(ok);
        Assert.IsNull(err);
        Assert.IsTrue(backend.IsInitialized);
        Assert.AreEqual(@"\\.\TSDriverProbe", mockOps.OpenedPath);
    }

    [TestMethod]
    public void Initialize_ServiceNotRunning_AttemptStartDisabled_ReturnsExplicitError()
    {
        var mockOps = new MockDriverDeviceOps
        {
            ServiceIsRunning = false,
            ServiceStatusDetail = "Service state is 1 (STOPPED)",
            OpenDeviceException = new FileNotFoundException("Device not found")
        };
        using var backend = new ThrottleStopCpuDriverBackend("TSDriverProbe", mockOps);

        bool ok = backend.Initialize(out string? err, attemptStartService: false);
        Assert.IsFalse(ok);
        Assert.IsNotNull(err);
        StringAssert.Contains(err, "Driver service 'TSDriverProbe' is not running");
        StringAssert.Contains(err, "Service state is 1 (STOPPED)");
        Assert.IsFalse(backend.IsInitialized);
    }

    [TestMethod]
    public void Initialize_ServiceNotRunning_AttemptStartEnabled_StartsServiceAndOpens()
    {
        var mockOps = new MockDriverDeviceOps
        {
            ServiceIsRunning = false,
            ServiceStatusDetail = "Service state is 1 (STOPPED)",
            OpenDeviceException = new FileNotFoundException("Device not found"),
            ServiceStartShouldSucceed = true
        };

        using var backend = new ThrottleStopCpuDriverBackend("TSDriverProbe", mockOps);

        bool ok = backend.Initialize(out string? err, attemptStartService: true);
        Assert.IsTrue(ok);
        Assert.IsNull(err);
        Assert.IsTrue(backend.IsInitialized);
        Assert.AreEqual(1, mockOps.StartServiceCallCount);
    }

    [TestMethod]
    public void Initialize_AccessDeniedOpeningDevice_ReturnsExplicitAdminGuidance()
    {
        var mockOps = new MockDriverDeviceOps
        {
            ServiceIsRunning = true,
            OpenDeviceException = new UnauthorizedAccessException("Access denied opening device '\\\\.\\TSDriverProbe'. Win32 Error 5 (ERROR_ACCESS_DENIED). Run as Administrator.")
        };
        using var backend = new ThrottleStopCpuDriverBackend("TSDriverProbe", mockOps);

        bool ok = backend.Initialize(out string? err);
        Assert.IsFalse(ok);
        Assert.IsNotNull(err);
        StringAssert.Contains(err, "Access denied");
        StringAssert.Contains(err, "Administrator");
    }

    [TestMethod]
    public void ReadMsr_EncodesIoctlAndPayloadCorrectly()
    {
        var mockOps = new MockDriverDeviceOps();
        mockOps.MsrRegisters[0x610] = 0x0042846000DF82A8UL;

        using var backend = new ThrottleStopCpuDriverBackend("TSDriverProbe", mockOps);
        bool ok = backend.ReadMsr(0x610, out ulong value);

        Assert.IsTrue(ok);
        Assert.AreEqual(0x0042846000DF82A8UL, value);
        Assert.HasCount(1, mockOps.RecordedIoctls);
        Assert.AreEqual(ThrottleStopCpuDriverBackend.IOCTL_MSR_READ, mockOps.RecordedIoctls[0].Code);
        Assert.HasCount(4, mockOps.RecordedIoctls[0].InData);
        Assert.AreEqual(0x610u, BitConverter.ToUInt32(mockOps.RecordedIoctls[0].InData, 0));
    }

    [TestMethod]
    public void WriteMsr_EncodesIoctlAnd12BytePayloadCorrectly()
    {
        var mockOps = new MockDriverDeviceOps();

        using var backend = new ThrottleStopCpuDriverBackend("TSDriverProbe", mockOps);
        bool ok = backend.WriteMsr(0x610, 0x0042846000DF82A8UL);

        Assert.IsTrue(ok);
        Assert.HasCount(1, mockOps.RecordedIoctls);
        Assert.AreEqual(ThrottleStopCpuDriverBackend.IOCTL_MSR_WRITE, mockOps.RecordedIoctls[0].Code);
        Assert.HasCount(12, mockOps.RecordedIoctls[0].InData);

        uint writtenMsr = BitConverter.ToUInt32(mockOps.RecordedIoctls[0].InData, 0);
        ulong writtenVal = BitConverter.ToUInt64(mockOps.RecordedIoctls[0].InData, 4);

        Assert.AreEqual(0x610u, writtenMsr);
        Assert.AreEqual(0x0042846000DF82A8UL, writtenVal);
        Assert.AreEqual(0x0042846000DF82A8UL, mockOps.MsrRegisters[0x610]);
    }

    [TestMethod]
    public void WriteMsr_RestrictedKernelMsrs_BlockedBySafetyGuardrail()
    {
        var mockOps = new MockDriverDeviceOps();
        using var backend = new ThrottleStopCpuDriverBackend("TSDriverProbe", mockOps);

        // Sysenter MSRs (0x174 - 0x176)
        Assert.IsTrue(ThrottleStopCpuDriverBackend.IsMsrRestricted(0x174));
        Assert.IsTrue(ThrottleStopCpuDriverBackend.IsMsrRestricted(0x175));
        Assert.IsTrue(ThrottleStopCpuDriverBackend.IsMsrRestricted(0x176));
        Assert.IsFalse(backend.WriteMsr(0x174, 0x1234));
        Assert.IsFalse(backend.WriteMsr(0x175, 0x1234));
        Assert.IsFalse(backend.WriteMsr(0x176, 0x1234));

        // Syscall / LSTAR MSRs (0xC0000080 - 0xC0000084)
        Assert.IsTrue(ThrottleStopCpuDriverBackend.IsMsrRestricted(0xC0000080));
        Assert.IsTrue(ThrottleStopCpuDriverBackend.IsMsrRestricted(0xC0000081));
        Assert.IsTrue(ThrottleStopCpuDriverBackend.IsMsrRestricted(0xC0000082)); // LSTAR
        Assert.IsTrue(ThrottleStopCpuDriverBackend.IsMsrRestricted(0xC0000083));
        Assert.IsTrue(ThrottleStopCpuDriverBackend.IsMsrRestricted(0xC0000084));
        Assert.IsFalse(backend.WriteMsr(0xC0000082, 0xDEADBEEFCAFEUL));

        // Ensure zero IOCTLs were dispatched for restricted writes
        Assert.IsEmpty(mockOps.RecordedIoctls);
    }

    [TestMethod]
    public void ReadPhysicalDword_EncodesIoctlAndAddressCorrectly()
    {
        var mockOps = new MockDriverDeviceOps();
        mockOps.PhysicalDwords[0xFE8004BC] = 0xFF2D280FU;

        using var backend = new ThrottleStopCpuDriverBackend("TSDriverProbe", mockOps);
        bool ok = backend.ReadPhysicalDword(0xFE8004BC, out uint value);

        Assert.IsTrue(ok);
        Assert.AreEqual(0xFF2D280FU, value);
        Assert.HasCount(1, mockOps.RecordedIoctls);
        Assert.AreEqual(ThrottleStopCpuDriverBackend.IOCTL_PHYS_READ, mockOps.RecordedIoctls[0].Code);
        Assert.HasCount(8, mockOps.RecordedIoctls[0].InData);
        Assert.AreEqual(0xFE8004BCUL, BitConverter.ToUInt64(mockOps.RecordedIoctls[0].InData, 0));
    }

    [TestMethod]
    public void WritePhysicalDword_EncodesIoctlAnd12BytePayloadCorrectly()
    {
        var mockOps = new MockDriverDeviceOps();

        using var backend = new ThrottleStopCpuDriverBackend("TSDriverProbe", mockOps);
        bool ok = backend.WritePhysicalDword(0xFE8004BC, 0xFF2D280FU);

        Assert.IsTrue(ok);
        Assert.HasCount(1, mockOps.RecordedIoctls);
        Assert.AreEqual(ThrottleStopCpuDriverBackend.IOCTL_PHYS_WRITE, mockOps.RecordedIoctls[0].Code);
        Assert.HasCount(12, mockOps.RecordedIoctls[0].InData);

        ulong writtenAddr = BitConverter.ToUInt64(mockOps.RecordedIoctls[0].InData, 0);
        uint writtenVal = BitConverter.ToUInt32(mockOps.RecordedIoctls[0].InData, 8);

        Assert.AreEqual(0xFE8004BCUL, writtenAddr);
        Assert.AreEqual(0xFF2D280FU, writtenVal);
        Assert.AreEqual(0xFF2D280FU, mockOps.PhysicalDwords[0xFE8004BC]);
    }

    [TestMethod]
    public void IDisposable_ClosesHandleAndPreventsSubsequentCalls()
    {
        var mockOps = new MockDriverDeviceOps();
        var backend = new ThrottleStopCpuDriverBackend("TSDriverProbe", mockOps);

        Assert.IsTrue(backend.Initialize(out _));
        Assert.IsTrue(backend.IsInitialized);

        backend.Dispose();
        Assert.IsFalse(backend.IsInitialized);
        Assert.AreEqual(1, mockOps.CloseDeviceCallCount);

        // Post-dispose should return false gracefully or throw ObjectDisposedException on EnsureHandle
        Assert.IsFalse(backend.ReadMsr(0x610, out _));
        Assert.IsFalse(backend.WriteMsr(0x610, 100));
        Assert.IsFalse(backend.ReadPhysicalDword(0xFE8004BC, out _));
        Assert.IsFalse(backend.WritePhysicalDword(0xFE8004BC, 100));
    }

    [TestMethod]
    public void ServiceOwnership_AttachedService_IsNotStoppedOnDispose()
    {
        var mockOps = new MockDriverDeviceOps
        {
            ServiceIsRunning = true
        };

        using (var backend = new ThrottleStopCpuDriverBackend("TSDriverProbe", mockOps))
        {
            Assert.IsTrue(backend.Initialize(out string? err));
            Assert.AreEqual(DriverServiceOwnership.ExternalAttached, backend.ServiceOwnership);
        }

        // On Dispose, because the service was already running (attached), TryStopService must NOT be called
        Assert.AreEqual(0, mockOps.StopServiceCallCount, "External service must not be stopped on Dispose");
        Assert.AreEqual(1, mockOps.CloseDeviceCallCount);
    }

    [TestMethod]
    public void ServiceOwnership_PreySenseOwnedService_IsStoppedOnDispose()
    {
        var mockOps = new MockDriverDeviceOps
        {
            ServiceIsRunning = false,
            ServiceStatusDetail = "Service state is 1 (STOPPED)",
            ServiceStartShouldSucceed = true
        };

        using (var backend = new ThrottleStopCpuDriverBackend("TSDriverProbe", mockOps))
        {
            bool init = backend.Initialize(out string? err, attemptStartService: true);
            Assert.IsTrue(init, $"Initialize failed with error: {err}");
            Assert.AreEqual(DriverServiceOwnership.PreySenseOwned, backend.ServiceOwnership);
            Assert.AreEqual(1, mockOps.StartServiceCallCount);
        }

        // On Dispose, because PreySense started the service, TryStopService MUST be called
        Assert.AreEqual(1, mockOps.StopServiceCallCount, "PreySense-started service must be stopped on Dispose");
        Assert.AreEqual(1, mockOps.CloseDeviceCallCount);
    }

    #region Hardware Tests (Opt-In Disabled by default via PREYSENSE_RUN_HARDWARE_TESTS=1)

    [TestMethod]
    [TestCategory("Hardware")]
    public void Hardware_ReadMsrAndMmio_MatchesPhysicalState()
    {
        if (Environment.GetEnvironmentVariable("PREYSENSE_RUN_HARDWARE_TESTS") != "1")
        {
            Assert.Inconclusive("Hardware tests require PREYSENSE_RUN_HARDWARE_TESTS=1 environment variable.");
            return;
        }

        using var backend = new ThrottleStopCpuDriverBackend("TSDriverProbe");
        Assert.IsTrue(backend.Initialize(out string? err), $"Initialize failed: {err}");

        // Read MSR 0xCE (PLATFORM_INFO)
        bool msrOk = backend.ReadMsr(0xCE, out ulong platformInfo);
        Assert.IsTrue(msrOk, "MSR 0xCE read must succeed.");
        Assert.AreNotEqual(0UL, platformInfo);

        // Read ERM4 Turbo dynamic boost physical memory
        bool mmioOk = backend.ReadPhysicalDword(0xFE8004BC, out uint erm4Dword);
        Assert.IsTrue(mmioOk, "Physical memory read at 0xFE8004BC must succeed.");
        Assert.AreEqual(0x0FU, erm4Dword & 0xFF, "Dynamic Boost byte must be 15 (0x0F).");
    }

    #endregion
}
