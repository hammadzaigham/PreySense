namespace PreySense.Mode;

/// <summary>
/// EC-style curves with independent sensor stages when synchronization is off.
/// The same configured delay holds transitions in either synchronization mode.
/// </summary>
public sealed class FanCurveFollower
{
    private readonly SensorStage _cpu = new();
    private readonly SensorStage _gpu = new();
    private bool? _lastSynchronized;
    private int? _stage;
    private int? _pendingStage;
    private long _pendingSince;

    public (int CpuPercent, int GpuPercent, int Stage) Evaluate(
        int cpuTemp, int gpuTemp, FanStage[] stages, int delaySeconds = 0, long nowMs = 0, bool synchronizeFanSpeeds = true)
    {
        if (stages == null || stages.Length == 0)
            return (0, 0, 0);

        if (_lastSynchronized != synchronizeFanSpeeds)
        {
            Reset();
            _lastSynchronized = synchronizeFanSpeeds;
        }
        if (!synchronizeFanSpeeds)
        {
            int cpuStage = _cpu.Evaluate(cpuTemp, stages, false, delaySeconds, nowMs);
            // If the GPU sensor is unavailable, retain conservative CPU-temperature fallback.
            int gpuStage = _gpu.Evaluate(gpuTemp is > 0 and <= 125 ? gpuTemp : cpuTemp, stages, true, delaySeconds, nowMs);
            return (stages[cpuStage].CpuDutyPercent, stages[gpuStage].GpuDutyPercent, Math.Max(cpuStage, gpuStage));
        }

        int stage = Math.Clamp(_stage ?? 0, 0, stages.Length - 1);
        int desired = stage;
        while (desired < stages.Length - 1 && ShouldAdvance(cpuTemp, gpuTemp, stages[desired]))
            desired++;
        while (desired > 0 && ShouldRetreat(cpuTemp, gpuTemp, stages, desired))
            desired--;

        int delay = Math.Clamp(delaySeconds, 0, 5);
        if (delay <= 0 || desired == stage)
        {
            _pendingStage = null;
            _stage = desired;
        }
        else
        {
            if (_pendingStage != desired)
            {
                _pendingStage = desired;
                _pendingSince = nowMs;
            }
            if (nowMs - _pendingSince >= delay * 1000L)
            {
                _stage = desired;
                _pendingStage = null;
            }
        }

        int applied = Math.Clamp(_stage ?? 0, 0, stages.Length - 1);
        var current = stages[applied];
        if (synchronizeFanSpeeds)
        {
            // Curves keep separate CPU/GPU duties; both fans run at the higher of the two when synchronized.
            int duty = Math.Max(current.CpuDutyPercent, current.GpuDutyPercent);
            return (duty, duty, applied);
        }
        return (current.CpuDutyPercent, current.GpuDutyPercent, applied);
    }

    private static bool ShouldAdvance(int cpu, int gpu, FanStage stage)
    {
        bool cpuTrip = stage.CpuRiseC < FanStage.DisabledTripCelsius && cpu >= stage.CpuRiseC;
        bool gpuTrip = stage.GpuRiseC < FanStage.DisabledTripCelsius && gpu >= stage.GpuRiseC;
        return cpuTrip || gpuTrip;
    }

    /// <summary>
    /// Leave the current stage once both sensors are below the rise that entered it
    /// (the previous stage's rise). No separate fall trip.
    /// </summary>
    private static bool ShouldRetreat(int cpu, int gpu, FanStage[] stages, int stageIndex)
    {
        if (stageIndex <= 0) return false;
        var previous = stages[stageIndex - 1];
        bool cpuClear = previous.CpuRiseC >= FanStage.DisabledTripCelsius || cpu < previous.CpuRiseC;
        bool gpuClear = previous.GpuRiseC >= FanStage.DisabledTripCelsius || gpu < previous.GpuRiseC;
        return cpuClear && gpuClear;
    }

    public void Reset()
    {
        _cpu.Reset(); _gpu.Reset();
        _lastSynchronized = null;
        _stage = null;
        _pendingStage = null;
        _pendingSince = 0;
    }
    private sealed class SensorStage
    {
        private int _stage;
        private int? _pending;
        private long _since;
        public void Reset() { _stage = 0; _pending = null; _since = 0; }
        public int Evaluate(int temperature, FanStage[] stages, bool gpu, int delaySeconds, long nowMs)
        {
            int stage = Math.Clamp(_stage, 0, stages.Length - 1);
            int desired = stage;
            int Rise(int index) => gpu ? stages[index].GpuRiseC : stages[index].CpuRiseC;
            while (desired < stages.Length - 1 && Rise(desired) < FanStage.DisabledTripCelsius && temperature >= Rise(desired)) desired++;
            while (desired > 0 && (Rise(desired - 1) >= FanStage.DisabledTripCelsius || temperature < Rise(desired - 1))) desired--;
            if (delaySeconds <= 0 || desired == stage)
            {
                _pending = null; _stage = desired;
            }
            else
            {
                if (_pending != desired) { _pending = desired; _since = nowMs; }
                if (nowMs - _since >= Math.Clamp(delaySeconds, 0, 5) * 1000L) { _stage = desired; _pending = null; }
            }
            return _stage;
        }
    }

}
