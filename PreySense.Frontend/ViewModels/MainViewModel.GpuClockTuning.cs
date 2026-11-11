using PreySense.Gpu;

namespace PreySense.Frontend.ViewModels;

public partial class MainViewModel
{
    internal IGpuClockTuning? GpuClockTuningController { get; set; }
    private IGpuClockTuning ClockTuning => GpuClockTuningController ?? NvidiaGpuControl.Shared;

    public GpuClockTuningState? GpuClockTuningState { get; private set; }
    public bool IsGpuClockTuningAvailable => GpuClockTuningState is not null;

    // No native calls in binding getters and no extra polling timer.
    internal async Task RefreshGpuClockTuningAsync()
    {
        GpuClockTuningState = await Task.Run(() =>
        {
            try { return ClockTuning.TryReadClockTuning(out var state, out _) ? state : null; }
            catch (Exception ex) { AppLogger.Error($"GPU overclocking discovery failed: {ex.Message}"); return null; }
        });
        OnPropertyChanged(nameof(GpuClockTuningState));
        OnPropertyChanged(nameof(IsGpuClockTuningAvailable));
    }

    private DriverTuningResult ExecuteRequestedTuning(DriverTuningRequest request,
        bool applyClocks, GpuClockOffsets? offsets)
    {
        // Validate and apply clocks first so a clock failure cannot leave watts/CPU already written.
        if (applyClocks)
        {
            if (offsets is null) return new(false, "Set GPU core and memory offsets before applying.");
            if (!ClockTuning.TryReadClockTuning(out var state, out string status)) return new(false, status);
            if (!state!.Allows(offsets.Value)) return new(false, "GPU clock offsets are outside the supported range.");
            if (!ClockTuning.TryApplyClockOffsets(offsets.Value, out string clockStatus))
                return new(false, clockStatus);
        }
        bool hasDriverWrites = request.ApplyCpuLimits || request.ApplyGpuLimits || request.ProchotOffsetCelsius.HasValue;
        if (!hasDriverWrites)
            return applyClocks
                ? new(true, "GPU clock offsets applied.")
                : new(true, "Applied and read back successfully.");
        return ExecuteDriverTuning(request);
    }
}
