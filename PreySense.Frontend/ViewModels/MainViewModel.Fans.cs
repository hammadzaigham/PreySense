using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using PreySense.Core.Services;
using PreySense.Mode;

namespace PreySense.Frontend.ViewModels;

public partial class MainViewModel
{
    private bool? _synchronizeFanSpeeds;

    public bool SynchronizeFanSpeeds
    {
        get => _synchronizeFanSpeeds ?? true;
        set
        {
            if (_synchronizeFanSpeeds == value) return;
            _synchronizeFanSpeeds = value;
            QueueFanSettingsSave(synchronize: value);
            OnPropertyChanged(nameof(SynchronizeFanSpeeds));
        }
    }

    public string[] FanCurveModeNames { get; } = { "Eco", "Quiet", "Balanced", "Performance", "Turbo" };

    private readonly object _fanControlGate = new();
    private readonly FanCurveFollower _fanFollower = new();
    private FanCurveProfile? _runningFanProfile;
    private byte _runningFanProfileMode = 0xFF;
    private int? _sentCpuFan, _sentGpuFan;
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
                return !_fanWriteFailed && _runningFanProfile?.ControlMode == FanControlMode.Custom;
            }
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
            if (_disposed || _fanWriteFailed || _fanRpmCalibrationActive) return;
            // Mode changes already invalidate this profile through the mode event/apply paths.
            // Avoid another ACPI query on every telemetry sample.
            byte mode = _runningFanProfileMode;
            if (_runningFanProfile == null && !_hw.TryGetPowerMode(out mode)) return;
            if (_runningFanProfileMode != mode || _runningFanProfile == null)
            {
                ResetFanFollower();
                _runningFanProfile = FanCurveStorage.Load(mode);
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
                telemetry.CpuTemp, gpuTemp, _runningFanProfile.Stages,
                synchronizeFanSpeeds: SynchronizeFanSpeeds);
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
    private void RestoreFanMode()
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
            _runningFanProfileMode = mode;
            _hw.InvalidateFanTargets();
            _fanModeSettlesAt = Environment.TickCount64 + 3000;
            _fanControllerState = _runningFanProfile.ControlMode == FanControlMode.Custom
                ? FanControllerState.Settling : FanControllerState.Inactive;
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
}
