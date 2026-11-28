using System.Text.Json;
using System.Threading;
using PreySense.Core.Services;
using PreySense.Frontend.Services;
using PreySense.Mode;

namespace PreySense.Frontend.ViewModels;

public partial class MainViewModel
{
    private (byte Mode, int Pl1, int Pl2)? _streamingCpuLimits;

    internal async Task<StreamingControlReply> HandleStreamingControlAsync(JsonElement input, CancellationToken stop)
    {
        string? action = ReadString(input, "action");
        Guid? undoOwner = Guid.TryParse(ReadString(input, "undo_id"), out Guid owner) ? owner : null;
        if (_disposed || _modeSwitchStopping || (_isRestoringSession && action != "release_fan_duty"))
            return new(false, "PreySense is starting, resuming, or shutting down.");

        if (action == "get_status")
            return await ReadStreamingStatusAsync(undoOwner, stop);
        if (action == "get_capabilities")
            return new(true, "PreySense streaming controls.", StreamingCapabilities());

        if (action == "release_fan_duty" && undoOwner is Guid releaseOwner)
        {
            if (!await TryEnterModeSwitchGateAsync()) return new(false, "PreySense is shutting down.");
            try
            {
                return await Task.Run(() => ReleaseStreamingFanDuty(releaseOwner))
                    ? new(true, "Fan control resumed.") : new(false, "Could not resume fan control.");
            }
            finally { ExitModeSwitchGate(); }
        }

        if (IsBusy || IsModeSelectionPending() || _modeSwitchGate.CurrentCount == 0 || _activeGameTuning.HasValue)
            return new(false, "A mode switch or game tuning operation is active. Try again when it finishes.");

        switch (action)
        {
            case "set_fan_duty":
                if (undoOwner is not Guid dutyOwner || ReadInt(input, "percent") is not int duty || duty is < 0 or > 100)
                    return new(false, "Supply a stream identifier and a fan percentage from 0 to 100.");
                if (await CaptureStreamingRestorePointAsync(dutyOwner, null, false, false) is { } dutyError)
                    return new(false, dutyError);
                if (!await TryEnterModeSwitchGateAsync()) return new(false, "PreySense is shutting down.");
                try
                {
                    bool ok = await Task.Run(() =>
                    {
                        lock (_fanControlGate) return ApplyStreamingFanDutyLocked(dutyOwner, duty);
                    });
                    return ok ? new(true, "Temporary fan duty applied.", new { percent = duty })
                        : new(false, "Could not apply the fan duty.");
                }
                finally { ExitModeSwitchGate(); }

            case "restore_previous":
                if (undoOwner is null) return new(false, "A stream identifier is required.");
                var restoreError = await RestoreStreamingChangesAsync(undoOwner.Value);
                return restoreError is null ? new(true, "Previous performance settings restored.") : new(false, restoreError);

            case "restore_profile":
                if (!ProfileManager.IsSupportedMode(ActivePowerMode))
                    return new(false, "The current performance mode is unavailable.");
                if (undoOwner is Guid profileOwner &&
                    await CaptureStreamingRestorePointAsync(profileOwner, StreamingModeGpuOverride(ActivePowerMode), true, false) is { } profileError)
                    return new(false, profileError);
                return await ApplyModeSwitchAsync(ActivePowerMode, waitForTransition: true)
                    ? new(true, "Saved profile reapplied.") : new(false, DiagnosticMessage);

            case "set_mode":
                byte mode = ReadString(input, "value")?.ToLowerInvariant() switch
                {
                    "eco" => 6, "quiet" => 0, "balanced" => 1, "performance" => 4, "turbo" => 5, _ => 255
                };
                if (!ProfileManager.IsSupportedMode(mode)) return new(false, "Unknown performance mode.");
                if (undoOwner is Guid modeOwner &&
                    await CaptureStreamingRestorePointAsync(modeOwner, StreamingModeGpuOverride(mode), true, false) is { } modeError)
                    return new(false, modeError);
                if (!await ApplyModeSwitchAsync(mode, waitForTransition: true)) return new(false, DiagnosticMessage);
                return new(true, "Performance mode applied.", new { mode = ProfileManager.ModeToProfileName(mode) });

            case "set_fan_mode":
                FanControlMode? fanMode = ReadString(input, "value")?.ToLowerInvariant() switch
                {
                    "auto" => FanControlMode.Auto, "custom" => FanControlMode.Custom, "max" => FanControlMode.Max, _ => null
                };
                if (fanMode is null) return new(false, "Fan mode must be auto, custom, or max.");
                if (undoOwner is Guid fanOwner &&
                    await CaptureStreamingRestorePointAsync(fanOwner, null, false, false) is { } fanError)
                    return new(false, fanError);
                if (!await TryEnterModeSwitchGateAsync()) return new(false, "PreySense is shutting down.");
                try
                {
                    await Task.Run(() => RestoreFanMode(fanMode.Value, forceSavedCurve: fanMode == FanControlMode.Custom));
                    lock (_fanControlGate)
                        return _fanWriteFailed ? new(false, "Fan mode could not be applied.")
                            : new(true, "Fan mode applied.", new { fanMode = fanMode.Value.ToString() });
                }
                finally { ExitModeSwitchGate(); }

            case "set_cpu_power":
            case "set_gpu_power":
            case "set_power":
                return await ApplyStreamingPowerAsync(input, action != "set_gpu_power", action != "set_cpu_power", undoOwner);

            default:
                return new(false, "Unknown streaming control action.");
        }
    }

    private object StreamingCapabilities()
    {
        return new
        {
            modes = ProfileManager.SourceProfileNames,
            fanModes = new[] { "Auto", "Custom", "Max" },
            fanDuty = new { minPercent = 0, maxPercent = 100, stepPercent = 5 },
            cpuPower = IsHardwareTuningEnabled && EnableCpuPowerLimitControl
                ? new { minWatts = 5, maxWatts = 300, stepWatts = 1 } : null,
            gpuPower = IsHardwareTuningEnabled && EnableGpuPowerLimitControl && GpuBasePowerWatts.HasValue
                && ProfileManager.IsSupportedMode(ActivePowerMode)
                && TryResolveGpuTarget(ActivePowerMode, GpuPowerTargetMin, out _, out _)
                ? new { minWatts = GpuPowerTargetMin, maxWatts = GpuPowerTargetMax, stepWatts = 5 } : null
        };
    }

    private async Task<StreamingControlReply> ApplyStreamingPowerAsync(JsonElement input, bool applyCpu, bool applyGpu, Guid? undoOwner)
    {
        if (!IsHardwareTuningEnabled || (applyCpu && !EnableCpuPowerLimitControl)
            || (applyGpu && !EnableGpuPowerLimitControl))
            return new(false, "Enable the corresponding hardware tuning control in PreySense first.");
        byte mode = ActivePowerMode;
        int? pl1 = ReadInt(input, "pl1_watts");
        int? pl2 = ReadInt(input, "pl2_watts");
        int? gpuWatts = ReadInt(input, "watts");
        if (applyCpu && (pl1 is not (>= 5 and <= 300) || pl2 is not (>= 5 and <= 300) || pl2 < pl1))
            return new(false, "Supply integer limits with 5 ≤ PL1 ≤ PL2 ≤ 300 watts.");
        if (applyGpu && gpuWatts is null)
            return new(false, "Supply an integer GPU target in watts.");
        if (!ProfileManager.IsSupportedMode(mode))
            return new(false, "The current performance mode is unavailable.");
        if (applyGpu && !TryResolveGpuTarget(mode, gpuWatts!.Value, out _, out string error))
            return new(false, error);
        if (undoOwner is Guid owner &&
            await CaptureStreamingRestorePointAsync(owner, applyGpu ? mode : null, true, applyCpu) is { } captureError)
            return new(false, captureError);
        bool ok = await ApplyTuningProfileAsync(mode, pl1 ?? 0, pl2 ?? 0, TauForMode(mode),
            applyGpu ? gpuWatts : null, persist: false, applyCpuLimits: applyCpu);
        if (ok && applyCpu && ActivePowerMode == mode)
            _streamingCpuLimits = (mode, pl1!.Value, pl2!.Value);
        return ok ? new(true, DiagnosticMessage, new
        {
            mode = ProfileManager.ModeToProfileName(mode),
            cpuPl1Watts = applyCpu ? pl1 : null,
            cpuPl2Watts = applyCpu ? pl2 : null,
            gpuTargetWatts = applyGpu ? gpuWatts : null
        }) : new(false, DiagnosticMessage);
    }

    private async Task<StreamingControlReply> ReadStreamingStatusAsync(Guid? undoOwner, CancellationToken stop)
    {
        var sample = TelemetryCoordinator.CurrentSnapshot;
        TelemetryCoordinator.SetDemand(TelemetryDemand.StreamingControl, true);
        try
        {
            // Wait briefly for the shared sampler to collect the requested metrics, including in tray mode.
            long deadline = Environment.TickCount64 + 2500;
            long previousSample = sample.TimestampTicks;
            do
            {
                await Task.Delay(100, stop);
                sample = TelemetryCoordinator.CurrentSnapshot;
            }
            while ((!sample.IsValid || sample.TimestampTicks == previousSample || sample.IsFanOnly)
                && Environment.TickCount64 < deadline);
        }
        finally { TelemetryCoordinator.SetDemand(TelemetryDemand.StreamingControl, false); }

        string? fanMode;
        int? manualFanDuty;
        int? appliedFanDuty;
        lock (_fanControlGate)
        {
            fanMode = _fanWriteFailed ? null : _runningFanProfile?.ControlMode.ToString();
            manualFanDuty = _streamingFanDuty?.Percent;
            appliedFanDuty = Math.Max(AppliedCpuFanPercent ?? -1, AppliedGpuFanPercent ?? -1);
            if (appliedFanDuty < 0) appliedFanDuty = null;
        }
        byte mode = ActivePowerMode;
        bool modePending = IsModeSelectionPending() || _modeSwitchGate.CurrentCount == 0;
        int? gpuTarget = null;
        var cpuLimits = StreamingCpuProfileLimits(mode);
        if (!modePending && IsHardwareTuningEnabled && EnableGpuPowerLimitControl
            && GpuBasePowerWatts is int baseline && ProfileManager.IsSupportedMode(mode)
            && await TryEnterModeSwitchGateAsync())
        {
            try
            {
                if (await Task.Run(() => ReadGameGpuProfile(mode)) is { } gpu)
                    gpuTarget = baseline + gpu.Delta + gpu.Boost;
            }
            finally { ExitModeSwitchGate(); }
        }
        var power = await Task.Run(_hw.GetPowerStatus);
        return new(true, "Current PreySense status.", new
        {
            mode = ProfileManager.IsSupportedMode(mode) ? ProfileManager.ModeToProfileName(mode) : null,
            modePending,
            canRestore = undoOwner.HasValue && _streamingRestorePoint?.Owner == undoOwner,
            fanMode,
            manualFanDutyPercent = manualFanDuty,
            fanDutyPercent = manualFanDuty ?? appliedFanDuty ?? FanPercentEstimate(sample),
            cpuTemperatureC = sample.IsValid ? sample.CpuTemp : null,
            gpuTemperatureC = sample.IsValid ? sample.GpuTemp : null,
            cpuFanRpm = sample.IsValid ? sample.CpuRpm : null,
            gpuFanRpm = sample.IsValid ? sample.GpuRpm : null,
            cpuUsage = sample.IsValid ? sample.CpuUsage : null,
            gpuUsage = sample.IsValid ? sample.GpuUsage : null,
            cpuClockMhz = sample.IsValid ? sample.CpuActiveFrequencyMhz ?? sample.CpuClockMhz : null,
            gpuClockMhz = sample.IsValid ? sample.GpuActiveClockMhz ?? sample.GpuClockMhz : null,
            powerSource = power.Source.ToString(),
            batteryPercent = power.BatteryPercent,
            cpuPowerWatts = sample.IsValid ? sample.CpuPkgPowerWatts : null,
            gpuPowerWatts = sample.IsValid ? sample.GpuPowerWatts : null,
            cpuPl1Watts = cpuLimits.Pl1,
            cpuPl2Watts = cpuLimits.Pl2,
            gpuPowerLimitWatts = sample.IsValid ? sample.GpuPowerLimitWatts : null,
            gpuTargetWatts = gpuTarget,
            capabilities = StreamingCapabilities()
        });
    }

    internal (int? Pl1, int? Pl2) StreamingCpuProfileLimits(byte mode)
    {
        if (!ProfileManager.IsSupportedMode(mode)) return default;
        if (_streamingCpuLimits is { } applied && applied.Mode == mode)
            return (applied.Pl1, applied.Pl2);
        var profile = TuningEditorMode(GpuModeIndex(mode));
        return (profile.CpuPl1Watts, profile.CpuPl2Watts);
    }

    private static int FanPercentEstimate(TelemetrySnapshot sample)
    {
        static int? Percent(int? rpm, int max) => rpm is >= 0 && max > 0
            ? (int)Math.Clamp(Math.Round(100.0 * rpm.Value / max), 0, 100) : null;
        int estimate = Math.Max(Percent(sample.CpuRpm, sample.CpuFanMaxRpm) ?? -1,
            Percent(sample.GpuRpm, sample.GpuFanMaxRpm) ?? -1);
        return estimate < 0 ? 50 : estimate;
    }

    private static string? ReadString(JsonElement input, string key) =>
        input.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? ReadInt(JsonElement input, string key) =>
        input.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out int number) ? number : null;
}
