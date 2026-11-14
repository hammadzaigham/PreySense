using NvAPIWrapper.Native;
using NvAPIWrapper.Native.GPU;
using NvAPIWrapper.Native.GPU.Structures;
using PreySense.Core.Services;

namespace PreySense.Gpu;

public partial class NvidiaGpuControl
{
    public bool TryReadClockTuning(out GpuClockTuningState? state, out string status)
    {
        using var lease = HardwareOperationGate.TryEnterTelemetry(1000);
        state = null;
        if (lease is null) { status = "GPU is busy switching display modes."; return false; }
        try { return ReadClockTuning(out state, out status); }
        catch (Exception ex) { status = $"GPU overclocking unavailable: {ex.Message}"; return false; }
    }

    private bool ReadClockTuning(out GpuClockTuningState? state, out string status)
    {
        state = null;
        status = "GPU overclocking is unavailable while the GPU is asleep or unsupported.";
        if (!IsValid || GetGpuState() != GpuState.Active) return false;
        var states = GPUApi.GetPerformanceStates20(_internalGpu!.Handle);
        if (!states.IsEditable || !states.Clocks.TryGetValue(PerformanceStateId.P0_3DPerformance, out var clocks))
            return false;
        var core = clocks.FirstOrDefault(c => c.DomainId == PublicClockDomain.Graphics);
        var memory = clocks.FirstOrDefault(c => c.DomainId == PublicClockDomain.Memory);
        if (core is null || memory is null || !core.IsEditable || !memory.IsEditable) return false;
        var coreDelta = core.FrequencyDeltaInkHz;
        var memoryDelta = memory.FrequencyDeltaInkHz;
        var coreRange = GpuClockRange.FromKilohertz(Math.Max(0, coreDelta.DeltaRange.Minimum), coreDelta.DeltaRange.Maximum,
            GpuClockTuningState.MaximumCoreOffsetMhz);
        var memoryRange = GpuClockRange.FromKilohertz(Math.Max(0, memoryDelta.DeltaRange.Minimum), memoryDelta.DeltaRange.Maximum);
        if (coreRange.MinimumMhz > coreRange.MaximumMhz || memoryRange.MinimumMhz > memoryRange.MaximumMhz
            || coreDelta.DeltaRange.Minimum >= coreDelta.DeltaRange.Maximum
            || memoryDelta.DeltaRange.Minimum >= memoryDelta.DeltaRange.Maximum) return false;
        state = new(coreRange, memoryRange);
        status = string.Empty;
        return true;
    }

    public bool TryApplyClockOffsets(GpuClockOffsets offsets, out string status)
    {
        using var lease = HardwareOperationGate.TryEnterTelemetry(1000);
        if (lease is null) { status = "GPU is busy switching display modes."; return false; }
        try
        {
            if (!ReadClockTuning(out var state, out status)) return false;
            if (!state!.Allows(offsets))
            {
                status = $"GPU clock offsets are out of range: core {state.Core.MinimumMhz} to {state.Core.MaximumMhz} MHz; memory {state.Memory.MinimumMhz} to {state.Memory.MaximumMhz} MHz.";
                return false;
            }
            PerformanceStates20ClockEntryV1[] clocks = [
                new(PublicClockDomain.Graphics, new PerformanceStates20ParameterDelta(checked(offsets.CoreMhz * 1000))),
                new(PublicClockDomain.Memory, new PerformanceStates20ParameterDelta(checked(offsets.MemoryMhz * 1000)))];
            PerformanceStates20InfoV1.PerformanceState20[] pStates = [
                new(PerformanceStateId.P0_3DPerformance, clocks, Array.Empty<PerformanceStates20BaseVoltageEntryV1>())];
            GPUApi.SetPerformanceStates20(_internalGpu!.Handle, new PerformanceStates20InfoV1(pStates, 2, 0));
            status = "GPU clock offsets applied.";
            return true;
        }
        catch (Exception ex)
        {
            status = $"GPU clock tuning failed: {ex.Message}. Earlier tuning changes may already be applied.";
            AppLogger.Error(status);
            return false;
        }
    }
}
