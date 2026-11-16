namespace PreySense.Mode;

/// <summary>Immediate boost increases with temperature hysteresis on decreases; no timer or keepalive.</summary>
public sealed class FanCurveFollower
{
    public const int CoolingHysteresisCelsius = 3;
    private int _cpuStage, _gpuStage;
    private bool? _lastSynchronized;
    public int CpuStage => _cpuStage;
    public int GpuStage => _gpuStage;
    public int? CpuPendingStage => null;
    public int? GpuPendingStage => null;

    // Legacy timing arguments remain source-compatible but never delay a request.
    public (int CpuPercent, int GpuPercent, int Stage) Evaluate(
        int cpuTemp, int gpuTemp, FanStage[] stages, int delaySeconds = 0, long nowMs = 0, bool synchronizeFanSpeeds = true)
    {
        if (stages == null || stages.Length == 0) return (0, 0, 0);
        if (_lastSynchronized != synchronizeFanSpeeds)
        {
            Reset();
            _lastSynchronized = synchronizeFanSpeeds;
        }
        if (synchronizeFanSpeeds)
        {
            int stage = Math.Clamp(_cpuStage, 0, stages.Length - 1);
            while (stage < stages.Length - 1 &&
                (Reached(cpuTemp, stages[stage].CpuRiseC) || Reached(gpuTemp, stages[stage].GpuRiseC))) stage++;
            while (stage > 0 && Cooled(cpuTemp, stages[stage - 1].CpuRiseC) &&
                Cooled(gpuTemp, stages[stage - 1].GpuRiseC)) stage--;
            _cpuStage = _gpuStage = stage;
            int request = Math.Max(stages[stage].CpuDutyPercent, stages[stage].GpuDutyPercent);
            return (request, request, stage);
        }
        _cpuStage = Follow(cpuTemp, stages, false, _cpuStage);
        _gpuStage = Follow(gpuTemp is > 0 and <= 125 ? gpuTemp : cpuTemp, stages, true, _gpuStage);
        return (stages[_cpuStage].CpuDutyPercent, stages[_gpuStage].GpuDutyPercent, Math.Max(_cpuStage, _gpuStage));
    }

    private static bool Reached(int temperature, int rise) => rise < FanStage.DisabledTripCelsius && temperature >= rise;
    private static bool Cooled(int temperature, int rise) =>
        rise >= FanStage.DisabledTripCelsius || temperature <= rise - CoolingHysteresisCelsius;
    private static int Follow(int temperature, FanStage[] stages, bool gpu, int current)
    {
        int stage = Math.Clamp(current, 0, stages.Length - 1);
        int Rise(int index) => stages[index].RiseCelsius(gpu);
        while (stage < stages.Length - 1 && Reached(temperature, Rise(stage))) stage++;
        while (stage > 0 && Cooled(temperature, Rise(stage - 1))) stage--;
        return stage;
    }
    public void Reset() { _cpuStage = _gpuStage = 0; _lastSynchronized = null; }
}
