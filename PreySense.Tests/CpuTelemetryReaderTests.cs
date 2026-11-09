using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PreySense.Core.Hardware;
using PreySense.Mode;

namespace PreySense.Tests;

[TestClass]
public class CpuTelemetryReaderTests
{
    private sealed class MockCpuDriverBackend : ICpuDriverBackend
    {
        public bool IsInitialized { get; set; } = true;
        public Dictionary<uint, ulong> MsrRegisters { get; } = new();
        public Func<uint, ulong>? ReadOverride;

        public bool ReadMsr(uint msr, out ulong value)
        {
            if (!IsInitialized)
            {
                value = 0;
                return false;
            }
            if (ReadOverride is not null) { value = ReadOverride(msr); return true; }
            return MsrRegisters.TryGetValue(msr, out value);
        }

        public bool WriteMsr(uint msr, ulong value)
        {
            if (!IsInitialized) return false;
            MsrRegisters[msr] = value;
            return true;
        }

        public bool ReadPhysicalDword(ulong physicalAddress, out uint value)
        {
            value = 0;
            return false;
        }

        public bool WritePhysicalDword(ulong physicalAddress, uint value) => false;
        public bool TryReadMchbar(out ulong address) { address = 0; return false; }

        public void Dispose() { }
    }

    [TestMethod]
    public void MissingPackageThermalStatus_DoesNotSubstituteCoreTemperature()
    {
        var backend = new MockCpuDriverBackend();
        backend.MsrRegisters[CpuTelemetryReader.MSR_TEMPERATURE_TARGET] = 100UL << 16;
        backend.MsrRegisters[CpuTelemetryReader.MSR_IA32_THERM_STATUS] = (1UL << 31) | (20UL << 16);
        using var reader = new CpuTelemetryReader(backend, 2400);
        Assert.IsNull(reader.ReadPackageTemperature());
    }

    [TestMethod]
    public void TopologyClocks_KeepPCoreAndECoreSamplesSeparate()
    {
        int read = 0;
        bool next = false;
        var mock = new MockCpuDriverBackend
        {
            ReadOverride = msr =>
            {
                int core = (read++ / 3) % 2;
                if (!next) return 100;
                return msr switch
                {
                    CpuTelemetryReader.MSR_IA32_APERF => core == 0 ? 600UL : 1100UL,
                    CpuTelemetryReader.MSR_IA32_MPERF => 600UL,
                    CpuTelemetryReader.MSR_IA32_TIME_STAMP_COUNTER => 1100UL,
                    _ => throw new AssertFailedException("Unexpected MSR")
                };
            }
        };
        using var reader = new CpuTelemetryReader(mock, 2400, new[] { false, true });
        Assert.IsNull(reader.SampleTopologyClocks().PCoreActiveMhz);
        Thread.Sleep(60);
        next = true;
        var clocks = reader.SampleTopologyClocks();
        Assert.AreEqual(2400, clocks.PCoreActiveMhz);
        Assert.AreEqual(4800, clocks.ECoreActiveMhz);
        Assert.AreEqual(1200, clocks.PCoreEffectiveMhz);
        Assert.AreEqual(2400, clocks.ECoreEffectiveMhz);
        Assert.AreEqual(12, read);
    }

    [TestMethod]
    public void PackagePower_UnavailableDriverRemainsNull()
    {
        using var reader = new CpuTelemetryReader(new MockCpuDriverBackend { IsInitialized = false }, 2400);
        Assert.IsNull(reader.ReadPackagePowerWatts());
    }

    [TestMethod]
    public void Test_CpuTelemetryReader_Temperature_DecodesCorrectly()
    {
        var mock = new MockCpuDriverBackend();
        // Target: TJMax = 100C (bits 23:16 -> 100 = 0x64 -> 0x00640000)
        mock.MsrRegisters[CpuTelemetryReader.MSR_TEMPERATURE_TARGET] = 0x00640000UL;
        // Thermal status valid bit (31), delta = 30C (bits 22:16 -> 30 = 0x1E -> 0x001E0000)
        // Expected temperature = 100 - 30 = 70C
        mock.MsrRegisters[CpuTelemetryReader.MSR_PACKAGE_THERM_STAT] = (1UL << 31) | (30UL << 16);

        using var reader = new CpuTelemetryReader(mock);
        int? temp = reader.ReadPackageTemperature();

        Assert.IsNotNull(temp);
        Assert.AreEqual(70, temp.Value);
    }

    [TestMethod]
    public void Test_CpuTelemetryReader_VoltageVid_DecodesCorrectly()
    {
        var mock = new MockCpuDriverBackend();
        // IA32_PERF_STATUS bits 47:32 = raw VID
        // If VID = 8192 (1.0V) -> 8192UL << 32
        mock.MsrRegisters[CpuTelemetryReader.MSR_IA32_PERF_STATUS] = 8192UL << 32;

        using var reader = new CpuTelemetryReader(mock);
        float? vid = reader.ReadCoreVoltageVid();

        Assert.IsNotNull(vid);
        Assert.AreEqual(1.0f, vid.Value, 0.001f);
    }

    [TestMethod]
    public void Test_CpuTelemetryReader_MeasuredVcore_ReturnsNaN_NotConflatedWithVid()
    {
        var mock = new MockCpuDriverBackend();
        mock.MsrRegisters[CpuTelemetryReader.MSR_IA32_PERF_STATUS] = 8192UL << 32;

        using var reader = new CpuTelemetryReader(mock);
        float vcore = reader.ReadMeasuredVcore();

        // Must explicitly return float.NaN (unsupported on this backend) rather than guessing or conflating with VID
        Assert.IsTrue(float.IsNaN(vcore));
    }

    [TestMethod]
    public void Test_CpuTelemetryReader_RequestedClockRatio_DecodesCorrectly()
    {
        var mock = new MockCpuDriverBackend();
        // IA32_PERF_STATUS bits 15:8 = ratio 45 (4500 MHz)
        mock.MsrRegisters[CpuTelemetryReader.MSR_IA32_PERF_STATUS] = 45UL << 8;

        using var reader = new CpuTelemetryReader(mock);
        var (ratio, mhz) = reader.ReadTargetFrequency();

        Assert.AreEqual(45, ratio);
        Assert.AreEqual(4500, mhz);
    }

    [TestMethod]
    public void Test_ReferenceFrequencyDiscovery_FromPlatformInfoMsr()
    {
        var mock = new MockCpuDriverBackend();
        // MSR 0xCE bits 15:8 = Max non-turbo ratio 24 (2400 MHz)
        mock.MsrRegisters[CpuTelemetryReader.MSR_PLATFORM_INFO] = 24UL << 8;

        // When explicit clock is null, it discovers 2400 MHz from MSR 0xCE (or CPUID leaf 0x16 if supported)
        double? discovered = CpuTelemetryReader.DiscoverReferenceClockMhz(mock);
        Assert.IsNotNull(discovered);
        Assert.IsGreaterThan(0, discovered.Value);

        using var reader = new CpuTelemetryReader(mock);
        Assert.IsNotNull(reader.ReferenceClockMhz);
    }

    [TestMethod]
    public void Test_ComputeFrequencySample_DistinguishesActiveVsEffective()
    {
        // 2400 MHz base clock, 1.0 second window
        // Suppose CPU is clocked at 2x base = 4800 MHz when running (Active)
        // Core was awake in C0 50% of the wall time (Residency = 0.50)
        // deltaTsc = 2,400,000,000 wall cycles
        // deltaMperf = 1,200,000,000 C0 cycles (50% unhalted)
        // deltaAperf = 2,400,000,000 (ratio APERF/MPERF = 2.0 -> 4800 MHz active)
        ulong deltaTsc = 2_400_000_000UL;
        ulong deltaMperf = 1_200_000_000UL;
        ulong deltaAperf = 2_400_000_000UL;
        double elapsedSec = 1.0;
        double refClock = 2400.0;

        var sample = CpuTelemetryReader.ComputeFrequencySample(deltaAperf, deltaMperf, deltaTsc, elapsedSec, refClock);

        Assert.IsNotNull(sample);
        // Active Frequency = 2400 * (2.4B / 1.2B) = 4800 MHz
        Assert.AreEqual(4800, sample.Value.ActiveFrequencyMhz);
        // Effective Frequency = 4800 * 0.50 = 2400 MHz
        Assert.AreEqual(2400, sample.Value.EffectiveFrequencyMhz);
        // Active residency percentage = 50.0%
        Assert.AreEqual(50.0, sample.Value.ActiveRatioPercent, 0.01);
    }

    [TestMethod]
    public void Test_ComputeFrequencySample_ZeroUnhaltedCycles_ReturnsZero()
    {
        ulong deltaTsc = 2_400_000_000UL;
        ulong deltaMperf = 0UL;
        ulong deltaAperf = 0UL;
        double elapsedSec = 1.0;
        double refClock = 2400.0;

        var sample = CpuTelemetryReader.ComputeFrequencySample(deltaAperf, deltaMperf, deltaTsc, elapsedSec, refClock);

        Assert.IsNotNull(sample);
        Assert.AreEqual(0, sample.Value.ActiveFrequencyMhz);
        Assert.AreEqual(0, sample.Value.EffectiveFrequencyMhz);
        Assert.AreEqual(0.0, sample.Value.ActiveRatioPercent, 0.01);
    }

    [TestMethod]
    public void Frequency_WithoutTscIsUnavailableInsteadOfEstimated()
    {
        Assert.IsNull(CpuTelemetryReader.ComputeFrequencySample(600_000_000, 600_000_000, 0, 0.5, 2400));
    }

    [TestMethod]
    public void Test_RAPL_PackagePower_Handles32BitRollover()
    {
        var mock = new MockCpuDriverBackend();
        // MSR 0x606 RAPL_POWER_UNIT: ESU = 14 (bits 12:8 -> 14 = 0x0E -> 0x0E00)
        // Energy Unit = 1 / 2^14 = 1/16384 J ~ 61 uJ
        mock.MsrRegisters[CpuTelemetryReader.MSR_RAPL_POWER_UNIT] = 14UL << 8;

        using var reader = new CpuTelemetryReader(mock, 2400.0);

        // First read initializes baseline
        // Near 32-bit rollover: 0xFFFFFFFF - 1000
        mock.MsrRegisters[CpuTelemetryReader.MSR_PKG_ENERGY_STATUS] = 0xFFFFFF00UL;
        Assert.IsNull(reader.ReadPackagePowerWatts()); // First call returns null (timestamp established)

        // Simulate rollover: counter wraps to 0x00000FFF
        // Delta = (0x00000FFF - 0xFFFFFF00) via 32-bit unsigned arithmetic
        uint c1 = 0xFFFFFF00U;
        uint c2 = 0x00000FFFU;
        uint delta = unchecked(c2 - c1); // 4351 increments
        Assert.AreEqual(4351U, delta);

        // Sleep briefly to exceed 50ms window
        System.Threading.Thread.Sleep(60);
        mock.MsrRegisters[CpuTelemetryReader.MSR_PKG_ENERGY_STATUS] = c2;
        float? power = reader.ReadPackagePowerWatts();

        Assert.IsNotNull(power);
        Assert.IsTrue(power.Value >= 0f && power.Value < 500f);
    }

    [TestMethod]
    public void Test_ThreadAffinityScope_RestoresAffinitySafely()
    {
        // Testing that creating and disposing ThreadAffinityScope does not throw on Windows
        using (var scope = new ThreadAffinityScope(0))
        {
            Assert.IsNotNull(scope);
        }

        using (var scope = new ThreadAffinityScope(null))
        {
            Assert.IsNotNull(scope);
            Assert.IsFalse(scope.IsPinned);
        }

        using (var scope = new ThreadAffinityScope(-1))
        {
            Assert.IsNotNull(scope);
            Assert.IsFalse(scope.IsPinned);
        }
    }

    [TestMethod]
    public void Test_CpuTelemetryReader_UnpinnedDefaultSampling_DoesNotPin()
    {
        var mock = new MockCpuDriverBackend();
        mock.MsrRegisters[CpuTelemetryReader.MSR_PLATFORM_INFO] = 24UL << 8;
        mock.MsrRegisters[CpuTelemetryReader.MSR_IA32_APERF] = 1000UL;
        mock.MsrRegisters[CpuTelemetryReader.MSR_IA32_MPERF] = 1000UL;
        mock.MsrRegisters[CpuTelemetryReader.MSR_IA32_TIME_STAMP_COUNTER] = 2000UL;

        using var reader = new CpuTelemetryReader(mock);
        // Calling SampleFrequency with null processor index targets unpinned/package context
        var first = reader.SampleFrequency(null);
        Assert.IsNull(first); // first establishes baseline

        mock.MsrRegisters[CpuTelemetryReader.MSR_IA32_APERF] = 2000UL;
        mock.MsrRegisters[CpuTelemetryReader.MSR_IA32_MPERF] = 2000UL;
        mock.MsrRegisters[CpuTelemetryReader.MSR_IA32_TIME_STAMP_COUNTER] = 4000UL;

        System.Threading.Thread.Sleep(60);
        var sample = reader.SampleFrequency(null);
        Assert.IsNotNull(sample);
        Assert.AreEqual(2400, sample.Value.ActiveFrequencyMhz);
    }
}
