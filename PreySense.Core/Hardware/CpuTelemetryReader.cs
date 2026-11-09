using System;
using System.Diagnostics;
using System.Runtime.Intrinsics.X86;
using PreySense.Mode;

namespace PreySense.Core.Hardware;

/// <summary>
/// Authoritative reader for CPU hardware telemetry using low-level MSR driver backends
/// with affinity-aware core sampling and robust counter delta/rollover handling.
/// </summary>
public sealed class CpuTelemetryReader : IDisposable
{
    private readonly ICpuDriverBackend _backend;
    private readonly object _syncLock = new();

    // MSR Addresses
    public const uint MSR_PLATFORM_INFO      = 0x0CE;
    public const uint MSR_IA32_PERF_STATUS   = 0x198;
    public const uint MSR_IA32_THERM_STATUS  = 0x19C;
    public const uint MSR_TEMPERATURE_TARGET = 0x1A2;
    public const uint MSR_PACKAGE_THERM_STAT = 0x1B1;
    public const uint MSR_IA32_TIME_STAMP_COUNTER = 0x010;
    public const uint MSR_IA32_MPERF         = 0x0E7;
    public const uint MSR_IA32_APERF         = 0x0E8;
    public const uint MSR_RAPL_POWER_UNIT    = 0x606;
    public const uint MSR_PKG_ENERGY_STATUS  = 0x611;

    // RAPL Energy Unit state
    private bool _raplInitialized;
    private double _energyUnitJoules;
    private uint _lastEnergyStatus;
    private long _lastEnergyTimestamp;

    // Reference clock discovery
    private double? _referenceClockMhz;

    // Per-processor state for APERF/MPERF/TSC tracking
    private sealed class ProcessorFrequencyState
    {
        public ulong LastAperf;
        public ulong LastMperf;
        public ulong LastTsc;
        public long LastTimestamp;

        public void Update(ulong aperf, ulong mperf, ulong tsc, long timestamp)
        {
            LastAperf = aperf;
            LastMperf = mperf;
            LastTsc = tsc;
            LastTimestamp = timestamp;
        }
    }

    private readonly Dictionary<int, ProcessorFrequencyState> _perProcessorState = new();
    private readonly ProcessorFrequencyState _currentProcessorState = new();

    public readonly record struct FrequencySample(
        int ActiveFrequencyMhz,
        int EffectiveFrequencyMhz,
        double ActiveRatioPercent);

    public bool IsInitialized => _backend.IsInitialized;

    /// <summary>
    /// Topology-aware core classification map.
    /// Index = logical processor index; Value = true if Efficiency (E) core, false if Performance (P) core.
    /// </summary>
    public IReadOnlyList<bool> ECoreMap => _isECore;

    /// <summary>
    /// Number of logical processors detected in the system topology.
    /// </summary>
    public int ProcessorCount => _isECore.Length;

    private readonly bool[] _isECore;

    /// <summary>
    /// Gets the resolved reference clock in MHz, discovered from CPUID Leaf 0x16 or MSR 0xCE PLATFORM_INFO.
    /// Returns null if neither source could provide a valid reference frequency.
    /// </summary>
    public double? ReferenceClockMhz => _referenceClockMhz;

    /// <summary>
    /// Initializes a new CpuTelemetryReader with dynamic reference-clock discovery.
    /// If an explicit referenceClockMhz is passed (> 0), it overrides dynamic discovery.
    /// </summary>
    public CpuTelemetryReader(ICpuDriverBackend backend, double? explicitReferenceClockMhz = null, bool[]? customECoreMap = null)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));

        _isECore = customECoreMap ?? BuildECoreMap(Environment.ProcessorCount);

        if (explicitReferenceClockMhz.HasValue && explicitReferenceClockMhz.Value > 0)
        {
            _referenceClockMhz = explicitReferenceClockMhz.Value;
        }
        else
        {
            _referenceClockMhz = DiscoverReferenceClockMhz(_backend);
        }
    }

    /// <summary>
    /// Discovers reference base clock (nominal frequency) using CPUID leaf 0x16 or MSR 0xCE PLATFORM_INFO.
    /// Does NOT assume any hardcoded defaults like 2700 MHz.
    /// </summary>
    public static double? DiscoverReferenceClockMhz(ICpuDriverBackend? backend)
    {
        // 1. Try CPUID Leaf 0x16 (Processor Frequency Information Leaf)
        if (X86Base.IsSupported)
        {
            try
            {
                var maxCpuid = X86Base.CpuId(0, 0);
                if ((uint)maxCpuid.Eax >= 0x16)
                {
                    var freqInfo = X86Base.CpuId(0x16, 0);
                    int baseMhz = freqInfo.Eax & 0xFFFF;
                    if (baseMhz > 100 && baseMhz < 10000)
                    {
                        return baseMhz;
                    }
                }
            }
            catch
            {
                // Intrinsics failure fallback
            }
        }

        // 2. Try MSR 0xCE (PLATFORM_INFO) bits [15:8] (Maximum Non-Turbo Ratio) * 100 MHz bus clock
        if (backend != null && backend.IsInitialized)
        {
            if (backend.ReadMsr(MSR_PLATFORM_INFO, out ulong platformInfo))
            {
                int maxNonTurboRatio = (int)((platformInfo >> 8) & 0xFF);
                if (maxNonTurboRatio is > 0 and <= 100)
                {
                    return maxNonTurboRatio * 100.0;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Reads instantaneous CPU Package Power in Watts using RAPL MSR 0x611 and MSR 0x606.
    /// Handles 32-bit counter rollover and detect resets safely.
    /// </summary>
    public float? ReadPackagePowerWatts()
    {
        lock (_syncLock)
        {
            if (!_backend.IsInitialized) return null;

            if (!_raplInitialized)
            {
                if (!_backend.ReadMsr(MSR_RAPL_POWER_UNIT, out ulong unitRaw))
                    return null;

                int esu = (int)((unitRaw >> 8) & 0x1F); // Energy status units, bits [12:8]
                _energyUnitJoules = 1.0 / (1UL << esu);
                _raplInitialized = true;
            }

            if (!_backend.ReadMsr(MSR_PKG_ENERGY_STATUS, out ulong energyRaw))
                return null;

            uint currentEnergy = (uint)(energyRaw & 0xFFFFFFFF);
            long currentTimestamp = Stopwatch.GetTimestamp();

            if (_lastEnergyTimestamp == 0)
            {
                _lastEnergyStatus = currentEnergy;
                _lastEnergyTimestamp = currentTimestamp;
                return null;
            }

            double elapsedSeconds = (double)(currentTimestamp - _lastEnergyTimestamp) / Stopwatch.Frequency;
            if (elapsedSeconds < 0.05) // Minimum 50ms window
                return null;

            // Handle sleep/suspend or massive timestamp jump (> 60s without poll)
            if (elapsedSeconds > 60.0)
            {
                _lastEnergyStatus = currentEnergy;
                _lastEnergyTimestamp = currentTimestamp;
                return null;
            }

            // 32-bit wrap-around handling via unchecked subtraction
            uint energyDelta = unchecked(currentEnergy - _lastEnergyStatus);
            double joules = energyDelta * _energyUnitJoules;

            _lastEnergyStatus = currentEnergy;
            _lastEnergyTimestamp = currentTimestamp;

            float watts = (float)Math.Round(joules / elapsedSeconds, 1);
            return watts >= 0 && watts < 500f ? watts : null;
        }
    }

    /// <summary>
    /// Reads CPU package temperature (°C) via MSR 0x1A2 (TJMax) and MSR 0x1B1 (Package Thermal Status),
    /// returning unavailable when package thermal status cannot be read.
    /// </summary>
    public int? ReadPackageTemperature()
    {
        lock (_syncLock)
        {
            if (!_backend.IsInitialized) return null;

            if (!_backend.ReadMsr(MSR_TEMPERATURE_TARGET, out ulong targetRaw))
                return null;

            // Attempt MSR 0x1B1 (Package Thermal Status)
            if (_backend.ReadMsr(MSR_PACKAGE_THERM_STAT, out ulong thermalRaw))
            {
                int? pkgTemp = IntelCpuTemperature.DecodePackageTemperature(targetRaw, thermalRaw);
                if (pkgTemp.HasValue) return pkgTemp;
            }

            return null;
        }
    }

    /// <summary>
    /// Reads CPU requested Core Voltage (VID) explicitly via IA32_PERF_STATUS (MSR 0x198 bits [47:32]), scaled by 1/8192V.
    /// Can target a specific logical core via affinity pinning if provided.
    /// </summary>
    public float? ReadCoreVoltageVid(int? logicalProcessorIndex = null)
    {
        lock (_syncLock)
        {
            if (!_backend.IsInitialized) return null;

            using (new ThreadAffinityScope(logicalProcessorIndex))
            {
                if (!_backend.ReadMsr(MSR_IA32_PERF_STATUS, out ulong perfRaw))
                    return null;

                return IntelCpuVoltage.DecodePerfStatus(perfRaw);
            }
        }
    }

    /// <summary>
    /// Reads measured motherboard Vcore. Because reading measured Vcore directly from VRM / SVID / EC
    /// requires a verified platform-specific telemetry interface not supported by MSR backends alone,
    /// this explicitly returns NaN / unsupported rather than guessing or conflating with VID.
    /// </summary>
    public float ReadMeasuredVcore() => float.NaN;

    /// <summary>
    /// Reads requested core multiplier / clock ratio via IA32_PERF_STATUS (MSR 0x198 bits [15:8] or [7:0])
    /// and calculates requested frequency based on discovered bus/reference clock or standard 100 MHz.
    /// </summary>
    public (int? TargetRatio, int? TargetMhz) ReadTargetFrequency(int? logicalProcessorIndex = null)
    {
        lock (_syncLock)
        {
            if (!_backend.IsInitialized) return (null, null);

            using (new ThreadAffinityScope(logicalProcessorIndex))
            {
                if (!_backend.ReadMsr(MSR_IA32_PERF_STATUS, out ulong perfRaw))
                    return (null, null);

                // Ratio is typically bits [15:8] (current performance state ratio)
                int ratio = (int)((perfRaw >> 8) & 0xFF);

                if (ratio is > 0 and <= 100)
                {
                    return (ratio, ratio * 100);
                }

                return (null, null);
            }
        }
    }

    /// <summary>
    /// Samples CPU frequency over a delta window, distinguishing:
    /// - Active Frequency: (Delta APERF / Delta MPERF) * RefClock when unhalted (C0 busy frequency).
    /// - Wall-time Effective Frequency: Active Frequency * C0 residency, taking idle/sleep into account.
    /// Returns null if reference clock is undiscovered, delta window is insufficient, or counters reset.
    /// </summary>
    /// <param name="logicalProcessorIndex">Specific logical core to sample. If null, samples the current or package context.</param>
    public FrequencySample? SampleFrequency(int? logicalProcessorIndex = null)
    {
        lock (_syncLock)
        {
            if (!_backend.IsInitialized) return null;

            // Ensure reference clock is discovered
            if (!_referenceClockMhz.HasValue)
            {
                _referenceClockMhz = DiscoverReferenceClockMhz(_backend);
                if (!_referenceClockMhz.HasValue) return null;
            }

            double refClock = _referenceClockMhz.Value;
            if (refClock <= 0) return null;

            ulong aperf, mperf;
            ulong tsc = 0;
            long now = Stopwatch.GetTimestamp();

            using (var affinity = new ThreadAffinityScope(logicalProcessorIndex))
            {
                if (logicalProcessorIndex.HasValue && !affinity.IsPinned) return null;
                if (!_backend.ReadMsr(MSR_IA32_APERF, out aperf) ||
                    !_backend.ReadMsr(MSR_IA32_MPERF, out mperf))
                {
                    return null;
                }

                // Read TSC (MSR 0x10) if possible, for precise wall-cycle C0 measurement
                _backend.ReadMsr(MSR_IA32_TIME_STAMP_COUNTER, out tsc);
            }

            // All state access is protected by _syncLock, so no concurrent collection is needed.
            var state = _currentProcessorState;
            if (logicalProcessorIndex is int processorIndex && !_perProcessorState.TryGetValue(processorIndex, out state))
            {
                state = new ProcessorFrequencyState();
                _perProcessorState.Add(processorIndex, state);
            }

            if (state.LastTimestamp == 0)
            {
                state.Update(aperf, mperf, tsc, now);
                return null;
            }

            double elapsedSec = (double)(now - state.LastTimestamp) / Stopwatch.Frequency;
            if (elapsedSec < 0.05) // Minimum 50ms window
                return null;

            // Detect sleep/suspend or massive gap
            if (elapsedSec > 30.0)
            {
                state.Update(aperf, mperf, tsc, now);
                return null;
            }

            // Check for counter reset / rollover (both APERF and MPERF reset to 0 simultaneously per Intel SDM)
            if (aperf < state.LastAperf || mperf < state.LastMperf)
            {
                state.Update(aperf, mperf, tsc, now);
                return null;
            }

            ulong deltaAperf = aperf - state.LastAperf;
            ulong deltaMperf = mperf - state.LastMperf;

            ulong deltaTsc = (tsc >= state.LastTsc && state.LastTsc > 0) ? (tsc - state.LastTsc) : 0;

            state.Update(aperf, mperf, tsc, now);

            return ComputeFrequencySample(deltaAperf, deltaMperf, deltaTsc, elapsedSec, refClock);
        }
    }

    /// <summary>
    /// Pure calculation helper for frequency sampling, computing Active Frequency,
    /// Wall-time Effective Frequency, and Active C0 ratio.
    /// Distinguishes invariant reference frequency from unhalted active cycle counts.
    /// </summary>
    public static FrequencySample? ComputeFrequencySample(
        ulong deltaAperf,
        ulong deltaMperf,
        ulong deltaTsc,
        double elapsedSec,
        double referenceClockMhz)
    {
        if (referenceClockMhz <= 0 || elapsedSec <= 0) return null;

        // If core spent 0 cycles unhalted during the window (deep sleep / suspended)
        if (deltaMperf == 0)
        {
            return new FrequencySample(
                ActiveFrequencyMhz: 0,
                EffectiveFrequencyMhz: 0,
                ActiveRatioPercent: 0.0);
        }

        // Active unhalted frequency = (Delta APERF / Delta MPERF) * RefClock
        // Delta APERF = actual unhalted core clock cycles
        // Delta MPERF = unhalted reference clock cycles (increments at invariant reference clock rate when core is C0)
        double activeRatio = (double)deltaAperf / deltaMperf;
        int activeMhz = (int)Math.Round(referenceClockMhz * activeRatio);

        // Determine C0 active residency (fraction of wall time core was active)
        // deltaTsc = invariant TSC delta (increments at invariant TSC frequency regardless of sleep/frequency)
        // deltaMperf = invariant reference cycles unhalted (C0 only)
        // On modern Intel hybrid (Raptor Lake), invariant TSC frequency equals reference clock frequency.
        // Therefore, C0 residency = Delta MPERF / Delta TSC.
        double c0Residency;
        if (deltaTsc > 0)
        {
            c0Residency = Math.Clamp((double)deltaMperf / deltaTsc, 0.0, 1.0);
        }
        else
        {
            // Without a valid wall-cycle counter, do not substitute a timing estimate.
            return null;
        }

        // Wall-time effective frequency taking C-states into account
        int effectiveMhz = (int)Math.Round(activeMhz * c0Residency);

        return new FrequencySample(
            ActiveFrequencyMhz: Math.Clamp(activeMhz, 0, 10000),
            EffectiveFrequencyMhz: Math.Clamp(effectiveMhz, 0, 10000),
            ActiveRatioPercent: Math.Round(c0Residency * 100.0, 2));
    }

    /// <summary>
    /// Samples all logical cores with processor affinity pinning and aggregates distinct
    /// Performance-core (P-Core) and Efficiency-core (E-Core) active and wall-time effective clocks.
    /// Returns (PCoreActiveMhz, ECoreActiveMhz, PCoreEffectiveMhz, ECoreEffectiveMhz).
    /// </summary>
    public (int? PCoreActiveMhz, int? ECoreActiveMhz, int? PCoreEffectiveMhz, int? ECoreEffectiveMhz) SampleTopologyClocks()
    {
        lock (_syncLock)
        {
            if (!_backend.IsInitialized || _isECore.Length == 0)
                return (null, null, null, null);

            int performanceActiveMaximum = 0, performanceEffectiveMaximum = 0;
            int efficiencyActiveMaximum = 0, efficiencyEffectiveMaximum = 0;
            bool hasPerformanceSample = false, hasEfficiencySample = false;

            for (int core = 0; core < _isECore.Length; core++)
            {
                var sample = SampleFrequency(core);
                if (!sample.HasValue) continue;

                bool isEfficiency = _isECore[core];
                if (isEfficiency)
                {
                    hasEfficiencySample = true;
                    if (sample.Value.ActiveFrequencyMhz > efficiencyActiveMaximum) efficiencyActiveMaximum = sample.Value.ActiveFrequencyMhz;
                    if (sample.Value.EffectiveFrequencyMhz > efficiencyEffectiveMaximum) efficiencyEffectiveMaximum = sample.Value.EffectiveFrequencyMhz;
                }
                else
                {
                    hasPerformanceSample = true;
                    if (sample.Value.ActiveFrequencyMhz > performanceActiveMaximum) performanceActiveMaximum = sample.Value.ActiveFrequencyMhz;
                    if (sample.Value.EffectiveFrequencyMhz > performanceEffectiveMaximum) performanceEffectiveMaximum = sample.Value.EffectiveFrequencyMhz;
                }
            }

            return (
                hasPerformanceSample ? performanceActiveMaximum : null,
                hasEfficiencySample ? efficiencyActiveMaximum : null,
                hasPerformanceSample ? performanceEffectiveMaximum : null,
                hasEfficiencySample ? efficiencyEffectiveMaximum : null
            );
        }
    }

    private const int RelationProcessorCore = 0;

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetLogicalProcessorInformationEx(
        int relationshipType,
        IntPtr buffer,
        ref uint returnedLength);

    private static bool[] BuildECoreMap(int logicalCount)
    {
        var isECore = new bool[Math.Max(0, logicalCount)];
        if (logicalCount <= 0 || !OperatingSystem.IsWindows())
            return isECore;

        try
        {
            uint length = 0;
            GetLogicalProcessorInformationEx(RelationProcessorCore, IntPtr.Zero, ref length);
            if (length == 0)
                return Array.Empty<bool>();

            IntPtr buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal((int)length);
            try
            {
                if (!GetLogicalProcessorInformationEx(RelationProcessorCore, buffer, ref length))
                    return Array.Empty<bool>();

                byte minClass = byte.MaxValue;
                byte maxClass = 0;
                var cores = new List<(byte Efficiency, ushort Group, ulong Mask)>();
                int offset = 0;
                while (offset + 32 <= (int)length)
                {
                    int size = System.Runtime.InteropServices.Marshal.ReadInt32(buffer, offset + 4);
                    if (size < 32)
                        break;

                    byte efficiency = System.Runtime.InteropServices.Marshal.ReadByte(buffer, offset + 9);
                    ushort groupCount = (ushort)System.Runtime.InteropServices.Marshal.ReadInt16(buffer, offset + 30);
                    int maskOffset = offset + 32;
                    for (int g = 0; g < groupCount && maskOffset + 16 <= offset + size; g++)
                    {
                        ulong mask = (ulong)System.Runtime.InteropServices.Marshal.ReadInt64(buffer, maskOffset);
                        ushort group = (ushort)System.Runtime.InteropServices.Marshal.ReadInt16(buffer, maskOffset + 8);
                        cores.Add((efficiency, group, mask));
                        if (efficiency < minClass) minClass = efficiency;
                        if (efficiency > maxClass) maxClass = efficiency;
                        maskOffset += 16;
                    }

                    offset += size;
                }

                if (minClass == maxClass)
                    return isECore;

                foreach (var core in cores)
                {
                    if (core.Efficiency != minClass || core.Group != 0)
                        continue;

                    for (int bit = 0; bit < 64 && bit < logicalCount; bit++)
                    {
                        if ((core.Mask & (1UL << bit)) != 0)
                            isECore[bit] = true;
                    }
                }
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            return Array.Empty<bool>();
        }

        return isECore;
    }

    public void Dispose()
    {
        lock (_syncLock)
        {
            _raplInitialized = false;
            _lastEnergyTimestamp = 0;
            _perProcessorState.Clear();
            _currentProcessorState.LastTimestamp = 0;
        }
    }
}
