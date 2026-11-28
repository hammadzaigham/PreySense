using PreySense.Core.Driver;
using PreySense.Core.Hardware;
using PreySense.Mode;

namespace PreySense.Frontend.ViewModels;

public partial class MainViewModel
{
    private sealed record StreamingRestorePoint(Guid Owner, byte Mode, FanControlMode? FanMode)
    {
        public DriverTuningRequest? CpuLimits { get; set; }
        public Dictionary<byte, GameGpuProfile> GpuProfiles { get; } = new();
        public bool RestoreMode { get; set; }
    }

    private StreamingRestorePoint? _streamingRestorePoint;

    private async Task<string?> CaptureStreamingRestorePointAsync(Guid owner, byte? gpuMode, bool powerChange, bool requireCpu)
    {
        if (!await TryEnterModeSwitchGateAsync()) return "PreySense is shutting down.";
        try
        {
            byte? current = await Task.Run(() => _hw.TryGetPowerMode(out byte actual) ? (byte?)actual : null);
            if (current is not byte mode || !ProfileManager.IsSupportedMode(mode))
                return "Could not confirm the current performance mode.";
            FanControlMode? fan;
            lock (_fanControlGate)
                fan = _fanWriteFailed ? null : _runningFanProfile?.ControlMode;
            var point = _streamingRestorePoint is { } existing && existing.Owner == owner
                && (existing.RestoreMode || existing.Mode == mode)
                ? existing : new(owner, mode, fan);
            if (powerChange && !point.RestoreMode && IsHardwareTuningEnabled && EnableCpuPowerLimitControl)
                point.CpuLimits = await Task.Run(() => ReadStreamingCpuLimits(point.Mode));
            if (requireCpu && point.CpuLimits is null)
                return "Could not read the CPU limits to make the change reversible.";
            if (gpuMode is byte gpu && !point.GpuProfiles.ContainsKey(gpu))
            {
                var original = await Task.Run(() => ReadGameGpuProfile(gpu));
                if (original is null)
                    return "Could not read the GPU profile to make the change reversible.";
                point.GpuProfiles.Add(gpu, original.Value);
            }
            point.RestoreMode |= powerChange;
            _streamingRestorePoint = point;
            return null;
        }
        finally { ExitModeSwitchGate(); }
    }

    private static DriverTuningRequest? ReadStreamingCpuLimits(byte mode)
    {
        var backend = CpuDriverRuntime.Backend;
        var cpu = CpuDriverRuntime.PowerLimits;
        if (backend is null || cpu is null
            || !backend.TryReadMchbar(out ulong mchbar)
            || !backend.ReadMsr(CpuPowerLimitController.MSR_RAPL_POWER_UNIT, out ulong units)
            || !cpu.TryReadMmioPowerLimits(mchbar, out uint pl1, out uint pl2, out bool locked, out byte tau)
            || locked || pl1 is < 5000 or > 300000 || pl2 < pl1 || pl2 > 300000)
            return null;
        int seconds = (int)Math.Clamp(Math.Round(CpuPowerLimitController.DecodeTau(tau, units)), 0, 449);
        return new(mode, (int)(pl1 / 1000), (int)(pl2 / 1000), seconds);
    }

    private async Task<string?> RestoreStreamingChangesAsync(Guid owner)
    {
        if (_streamingRestorePoint is not { } point || point.Owner != owner)
            return "This stream has no performance changes to restore.";
        if (!await TryEnterModeSwitchGateAsync()) return "PreySense is shutting down.";
        try
        {
            if (!await Task.Run(() => ReleaseStreamingFanDuty(owner)))
                return "Could not resume fan control.";
            foreach (var profile in point.GpuProfiles.Values)
            {
                var restored = await Task.Run(() => RestoreGameGpuProfile(profile));
                if (!restored.Success) return restored.Status;
            }
            if (point.RestoreMode && !await ApplyModeSwitchCoreAsync(point.Mode, applyCustomLimits: false))
                return DiagnosticMessage;
            if (point.CpuLimits is { } request)
            {
                await Task.Delay(AcerOperatingModeTiming.TransitionDelay);
                var restored = await Task.Run(() => ExecuteDriverTuning(request));
                if (!restored.Success) return restored.Status;
            }
            if (point.FanMode is { } fan)
            {
                await Task.Run(() => RestoreFanMode(fan));
                lock (_fanControlGate)
                    if (_fanWriteFailed) return "Could not restore the previous fan mode.";
            }
            _streamingRestorePoint = null;
            return null;
        }
        finally { ExitModeSwitchGate(); }
    }

    private byte? StreamingModeGpuOverride(byte mode)
    {
        if (!IsHardwareTuningEnabled || !EnableGpuPowerLimitControl) return null;
        var tuning = (TuningConfigurationReader?.Invoke() ?? TuningStore.GetCurrent()).Tuning;
        var profile = ProfileForMode(tuning, mode);
        return profile.ApplyGpuLimits || profile.GpuRuntimeLimitWatts.HasValue ? mode : null;
    }
}
