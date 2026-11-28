using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using PreySense.Core.Services;
using PreySense.Mode;

namespace PreySense.Frontend.ViewModels;

public partial class MainViewModel
{
    public string[] FanCurveModeNames { get; } = { "Eco", "Quiet", "Balanced", "Performance", "Turbo" };

    private readonly object _fanControlGate = new();
    private readonly FanCurveFollower _fanFollower = new();
    private FanCurveProfile? _runningFanProfile;
    private byte _runningFanProfileMode = 0xFF;
    private int? _sentCpuFan, _sentGpuFan;
    private (Guid Owner, int Percent)? _streamingFanDuty;
    private bool _sentMaxFanMode, _fanOverrideActive, _autoFanModeApplied, _fanWriteFailed;
    private long _fanModeSettlesAt;
    private bool _fanSnapshotsSubscribed;
    private FanControllerState _fanControllerState;

    /// <summary>Returns cached software diagnostics without making hardware calls.</summary>
    public FanControlDiagnostics GetFanControlDiagnostics()
    {
        lock (_fanControlGate)
        {
            bool custom = _runningFanProfile?.ControlMode == FanControlMode.Custom;
            return new(_fanControllerState,
                _runningFanProfile == null ? null : _runningFanProfileMode,
                AppliedCpuFanPercent, AppliedGpuFanPercent,
                custom ? _fanFollower.CpuStage : null,
                custom ? _fanFollower.GpuStage : null,
                custom ? _fanFollower.CpuPendingStage : null,
                custom ? _fanFollower.GpuPendingStage : null);
        }
    }

    private bool RequiresFanTemperatures
    {
        get
        {
            lock (_fanControlGate)
            {
                return !_fanWriteFailed && _streamingFanDuty is null
                    && _runningFanProfile?.ControlMode == FanControlMode.Custom
                    && !_runningFanProfile.UseSlider;
            }
        }
    }

    public bool FanCustomUseSlider
    {
        get => PreySense.Core.Configuration.ConfigurationService.Instance.GetCurrent().Fans.UseSlider;
        set
        {
            PreySense.Core.Configuration.ConfigurationService.Instance.Update(cfg => cfg.Fans.UseSlider = value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(FanCustomUseCurve));
            if (_runningFanProfile?.ControlMode == FanControlMode.Custom)
                _ = ApplyFanProfileAsync();
        }
    }

    public bool FanCustomUseCurve
    {
        get => !FanCustomUseSlider;
        set => FanCustomUseSlider = !value;
    }

    public int FanCpuMaxRpm
    {
        get => Math.Clamp(PreySense.Core.Configuration.ConfigurationService.Instance.GetCurrent().Fans.CpuMaxRpm, 1000, 10000);
        set
        {
            int clamped = Math.Clamp(value, 1000, 10000);
            PreySense.Core.Configuration.ConfigurationService.Instance.Update(cfg => cfg.Fans.CpuMaxRpm = clamped);
            OnPropertyChanged();
        }
    }

    public int FanGpuMaxRpm
    {
        get => Math.Clamp(PreySense.Core.Configuration.ConfigurationService.Instance.GetCurrent().Fans.GpuMaxRpm, 1000, 10000);
        set
        {
            int clamped = Math.Clamp(value, 1000, 10000);
            PreySense.Core.Configuration.ConfigurationService.Instance.Update(cfg => cfg.Fans.GpuMaxRpm = clamped);
            OnPropertyChanged();
        }
    }

    private void UpdateFanDemand()
    {
        lock (_fanControlGate)
        {
            bool required = !_disposed && RequiresFanTemperatures;
            if (required != _fanSnapshotsSubscribed)
            {
                if (required) TelemetryCoordinator.SnapshotAvailable += OnFanSnapshot;
                else TelemetryCoordinator.SnapshotAvailable -= OnFanSnapshot;
                _fanSnapshotsSubscribed = required;
            }
            TelemetryCoordinator.SetDemand(TelemetryDemand.FanController, required);
        }
    }

    private void OnFanSnapshot(TelemetrySnapshot snapshot)
    {
        if (!_disposed && _fanSnapshotsSubscribed)
            ProcessFanCurves(snapshot.ToHardwareTelemetry());
    }

    private void DisposeFanTelemetry()
    {
        lock (_fanControlGate)
        {
            TelemetryCoordinator.SnapshotAvailable -= OnFanSnapshot;
            _fanSnapshotsSubscribed = false;
            TelemetryCoordinator.SetDemand(TelemetryDemand.FanController, false);
        }
    }

    public int? AppliedCpuFanPercent
    {
        get { lock (_fanControlGate) return !_fanOverrideActive ? null : _sentMaxFanMode ? 100 : _sentCpuFan; }
    }
    public int? AppliedGpuFanPercent
    {
        get { lock (_fanControlGate) return !_fanOverrideActive ? null : _sentMaxFanMode ? 100 : _sentGpuFan; }
    }
    public FanCurveProfile LoadFanProfile(byte mode) => FanCurveStorage.Load(mode);
    public void SaveFanProfile(byte mode, FanCurveProfile profile)
    {
        FanCurveStorage.Save(mode, profile);
        DiagnosticMessage = $"{ProfileManager.ModeToProfileName(mode)} fan settings saved.";
    }
    private void ResetFanFollower()
    {
        _fanFollower.Reset();
        _runningFanProfile = null;
        _runningFanProfileMode = 0xFF;
        _sentCpuFan = _sentGpuFan = null;
        _sentMaxFanMode = false;
        _fanWriteFailed = false;
        _autoFanModeApplied = false;
        _fanControllerState = FanControllerState.Inactive;
        // Keep override ownership until hardware successfully returns to Auto.
    }
    private void ProcessFanCurves(HardwareTelemetry telemetry)
    {
        if (_disposed) return;
        lock (_fanControlGate)
        {
            if (_disposed || _fanWriteFailed || _streamingFanDuty.HasValue) return;
            // Mode changes already invalidate this profile through the mode event/apply paths.
            // Avoid another ACPI query on every telemetry sample.
            byte mode = _runningFanProfileMode;
            if (_runningFanProfile == null && !_hw.TryGetPowerMode(out mode)) return;
            if (_runningFanProfileMode != mode || _runningFanProfile == null)
            {
                ResetFanFollower();
                _runningFanProfile = FanCurveStorage.Load(mode);
                _runningFanProfile.UseSlider = FanCustomUseSlider;
                _runningFanProfileMode = mode;
                _hw.InvalidateFanTargets();
                _fanModeSettlesAt = Environment.TickCount64 + 3000;
            }
            if (_runningFanProfile.ControlMode == FanControlMode.Auto)
            {
                if (!_autoFanModeApplied)
                {
                    // Auto hands control back to firmware; one failure pauses (no OEM reclaim retries).
                    if (!_hw.SetFanMode(0, 0)) { FanWriteFailed(); return; }
                    _fanOverrideActive = false;
                    _autoFanModeApplied = true;
                    _fanControllerState = FanControllerState.FirmwareAuto;
                    _sentCpuFan = _sentGpuFan = null;
                    UpdateFanDemand();
                }
                return;
            }
            if (_runningFanProfile.ControlMode == FanControlMode.Max)
            {
                if (!_sentMaxFanMode)
                {
                    _fanOverrideActive = true;
                    if (_hw.SetFanMode(1, 1))
                    {
                        _sentMaxFanMode = _fanOverrideActive = true;
                        _fanControllerState = FanControllerState.HardwareMax;
                        _autoFanModeApplied = false;
                        _sentCpuFan = _sentGpuFan = null;
                        UpdateFanDemand();
                    }
                    else FanWriteFailed();
                }
                return;
            }
            if (_runningFanProfile.ControlMode == FanControlMode.Custom && _runningFanProfile.UseSlider)
            {
                int manualCpu = Math.Clamp(_runningFanProfile.ManualCpuDutyPercent, 0, 100);
                int manualGpu = Math.Clamp(_runningFanProfile.ManualGpuDutyPercent, 0, 100);
                _fanControllerState = FanControllerState.ManualDuty;
                if (_sentCpuFan == manualCpu && _sentGpuFan == manualGpu) return;
                _fanOverrideActive = true;
                if (_hw.SetFanSpeeds((byte)manualCpu, (byte)manualGpu))
                {
                    _sentCpuFan = manualCpu; _sentGpuFan = manualGpu;
                    _fanOverrideActive = true;
                    _autoFanModeApplied = _sentMaxFanMode = false;
                }
                else FanWriteFailed();
                return;
            }
            bool valid = FanCurveProfile.ValidTemperatures(telemetry.CpuTemp, telemetry.GpuTemp);
            if (Environment.TickCount64 < _fanModeSettlesAt)
            {
                _fanControllerState = FanControllerState.Settling;
                return;
            }
            if (!valid)
            {
                _fanControllerState = FanControllerState.WaitingForCpuTemperature;
                return;
            }
            int gpuTemp = telemetry.GpuTemp is > 0 and <= 125 ? telemetry.GpuTemp : 0;
            var (cpu, gpu, _) = _fanFollower.Evaluate(
                telemetry.CpuTemp, gpuTemp, _runningFanProfile.Stages);
            _fanControllerState = FanControllerState.Following;
            if (_sentCpuFan == cpu && _sentGpuFan == gpu) return;
            _fanOverrideActive = true;
            if (_hw.SetFanSpeeds((byte)cpu, (byte)gpu))
            {
                _sentCpuFan = cpu; _sentGpuFan = gpu;
                _fanOverrideActive = true;
                _autoFanModeApplied = _sentMaxFanMode = false;
            }
            else FanWriteFailed();
        }
    }
    private void FanWriteFailed()
    {
        _fanWriteFailed = true;
        _fanControllerState = FanControllerState.WriteFailed;
        UpdateFanDemand();
        _sentCpuFan = _sentGpuFan = null;
        _sentMaxFanMode = false;
        _dispatcher.InvokeAsync(() => DiagnosticMessage = "Fan update failed. Apply fan settings again to resume.");
    }
    private void RestoreFanMode() => RestoreFanMode(null);

    private void RestoreFanMode(FanControlMode? overrideMode, bool forceSavedCurve = false)
    {
        lock (_fanControlGate)
        {
            ResetFanFollower();
            if (!_hw.TryGetPowerMode(out byte mode))
            {
                _fanWriteFailed = true;
                _fanControllerState = FanControllerState.WriteFailed;
                UpdateFanDemand();
                return;
            }
            _runningFanProfile = FanCurveStorage.Load(mode);
            _runningFanProfile.UseSlider = !forceSavedCurve && FanCustomUseSlider;
            if (overrideMode is { } controlMode)
            {
                _runningFanProfile.ControlMode = controlMode;
                _streamingFanDuty = null;
            }
            _runningFanProfileMode = mode;
            _hw.InvalidateFanTargets();
            if (_streamingFanDuty is { } manual)
            {
                ApplyStreamingFanDutyLocked(manual.Owner, manual.Percent);
                return;
            }
            _fanModeSettlesAt = Environment.TickCount64 + 3000;
            _fanControllerState = _runningFanProfile.ControlMode == FanControlMode.Custom
                ? (_runningFanProfile.UseSlider ? FanControllerState.ManualDuty : FanControllerState.Settling)
                : FanControllerState.Inactive;
            if (!RequiresFanTemperatures)
            {
                UpdateFanDemand();
                ProcessFanCurves(default);
                return;
            }
            UpdateFanDemand();
            // Custom fan control waits for the next shared sample.
        }
    }

    private bool ApplyStreamingFanDutyLocked(Guid owner, int percent)
    {
        _fanOverrideActive = true;
        // A failed hardware call may have applied one fan target before failing.
        // Preserve ownership so the stream's cleanup can still reset both fans.
        _streamingFanDuty = (owner, percent);
        if (!_hw.SetFanSpeeds((byte)percent, (byte)percent))
        {
            FanWriteFailed();
            return false;
        }
        _sentCpuFan = _sentGpuFan = percent;
        _autoFanModeApplied = _sentMaxFanMode = _fanWriteFailed = false;
        _fanControllerState = FanControllerState.ManualDuty;
        UpdateFanDemand();
        return true;
    }

    private bool ReleaseStreamingFanDuty(Guid owner)
    {
        lock (_fanControlGate)
        {
            if (_streamingFanDuty is not { } manual || manual.Owner != owner) return true;
            var mode = _runningFanProfile?.ControlMode;
            _streamingFanDuty = null;
            RestoreFanMode(mode);
            if (_fanWriteFailed) _streamingFanDuty = manual;
            return !_fanWriteFailed;
        }
    }

    private void ReleaseCustomFans()
    {
        lock (_fanControlGate)
        {
            if (!_fanOverrideActive) return;
            try
            {
                _autoFanModeApplied = _hw.SetFanMode(0, 0);
                if (_autoFanModeApplied)
                {
                    _fanOverrideActive = _sentMaxFanMode = false;
                    _sentCpuFan = _sentGpuFan = null;
                    _fanControllerState = FanControllerState.FirmwareAuto;
                }
            }
            catch (Exception ex) { AppLogger.Log($"Could not return fans to Auto: {ex.Message}"); }
        }
    }
    public async Task ApplyFanProfileAsync()
    {
        await Task.Run(RestoreFanMode);
        lock (_fanControlGate)
        {
            if (_runningFanProfile == null || _fanWriteFailed)
                throw new InvalidOperationException("Fan settings could not be applied.");
        }
        DiagnosticMessage = "Fan settings saved; following the selected mode.";
    }

    public async Task ApplyManualFanDutyAsync(byte mode, FanCurveProfile snapshot)
    {
        int cpu = Math.Clamp(snapshot.ManualCpuDutyPercent, 0, 100);
        int gpu = Math.Clamp(snapshot.ManualGpuDutyPercent, 0, 100);
        if (!await TryEnterModeSwitchGateAsync()) return;
        try
        {
            bool applied = await Task.Run(() =>
            {
                FanCurveStorage.Save(mode, snapshot);
                if (mode != ActivePowerMode || !_hw.TryGetPowerMode(out byte actual) || actual != mode)
                    return false;

                lock (_fanControlGate)
                {
                    _runningFanProfile = snapshot.Copy();
                    _runningFanProfileMode = mode;
                    _streamingFanDuty = null;
                    _fanOverrideActive = true;
                    _fanControllerState = FanControllerState.ManualDuty;
                    if (_sentCpuFan != cpu || _sentGpuFan != gpu || _fanWriteFailed)
                    {
                        if (!_hw.SetFanSpeeds((byte)cpu, (byte)gpu))
                        {
                            FanWriteFailed();
                            throw new InvalidOperationException("Could not apply manual fan speeds.");
                        }

                        _sentCpuFan = cpu;
                        _sentGpuFan = gpu;
                        _autoFanModeApplied = _sentMaxFanMode = _fanWriteFailed = false;
                    }
                    UpdateFanDemand();
                    return true;
                }
            });
            string name = ProfileManager.ModeToProfileName(mode);
            DiagnosticMessage = applied ? $"{name} fan speed set ({cpu}% / {gpu}%)." : $"{name} fan speeds saved.";
        }
        finally { ExitModeSwitchGate(); }
    }
}
