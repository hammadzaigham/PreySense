using System;
using System.Threading.Tasks;
using PreySense.Core.Services;
using PreySense.Mode;

namespace PreySense.Frontend.ViewModels;

public partial class MainViewModel
{
    public string[] FanCurveModeNames { get; } = { "Eco", "Quiet", "Balanced", "Performance", "Turbo" };

    private readonly object _fanControlGate = new();
    private readonly FanCurveFollower _cpuFanFollower = new();
    private readonly FanCurveFollower _gpuFanFollower = new();
    private FanCurveProfile? _runningFanProfile;
    private byte _runningFanProfileMode = 0xFF;
    private int? _sentCpuFan, _sentGpuFan;
    private bool _sentMaxFanMode, _fanOverrideActive, _autoFanModeApplied, _fanWriteFailed;
    private long _fanModeSettlesAt;
    private bool _fanSnapshotsSubscribed;

    private bool RequiresFanTemperatures
    {
        get
        {
            lock (_fanControlGate)
            {
                if (_runningFanProfile == null && _hw.TryGetPowerMode(out byte mode))
                {
                    _runningFanProfile = FanCurveStorage.Load(mode);
                    _runningFanProfileMode = mode;
                }
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
        lock (_fanControlGate)
        {
            if (_runningFanProfileMode == mode) ResetFanFollower();
        }
        UpdateFanDemand();
        if (_runningFanProfileMode == mode && _runningFanProfile?.ControlMode != FanControlMode.Custom)
            ProcessFanCurves(default);
        DiagnosticMessage = $"{ProfileManager.ModeToProfileName(mode)} fan settings saved.";
    }
    private void ResetFanFollower()
    {
        _cpuFanFollower.Reset(); _gpuFanFollower.Reset();
        _runningFanProfile = null;
        _runningFanProfileMode = 0xFF;
        _sentCpuFan = _sentGpuFan = null;
        _sentMaxFanMode = false;
        _fanWriteFailed = false;
        _autoFanModeApplied = false;
        // Keep override ownership until hardware successfully returns to Auto.
    }
    private void ProcessFanCurves(HardwareTelemetry telemetry)
    {
        if (_disposed) return;
        lock (_fanControlGate)
        {
            if (_disposed || _fanWriteFailed) return;
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
                    if (!_hw.SetFanMode(0, 0)) { FanWriteFailed(); return; }
                    _fanOverrideActive = false;
                    _autoFanModeApplied = true;
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
                        _autoFanModeApplied = false;
                        _sentCpuFan = _sentGpuFan = null;
                        UpdateFanDemand();
                    }
                    else FanWriteFailed();
                }
                return;
            }
            bool valid = FanCurveProfile.ValidTemperatures(telemetry.CpuTemp, telemetry.GpuTemp);
            if (Environment.TickCount64 < _fanModeSettlesAt || !valid) return;
            int temperature = FanCurveProfile.CoolingTemperature(telemetry.CpuTemp, telemetry.GpuTemp);
            long now = Environment.TickCount64;
            int cpu = _cpuFanFollower.Evaluate(temperature, _runningFanProfile.Cpu, _runningFanProfile.RampUpSeconds, now);
            int gpu = _gpuFanFollower.Evaluate(temperature, _runningFanProfile.Gpu, _runningFanProfile.RampUpSeconds, now);
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
        UpdateFanDemand();
        _sentCpuFan = _sentGpuFan = null;
        _sentMaxFanMode = false;
        _dispatcher.InvokeAsync(() => DiagnosticMessage = "Fan update failed; automatic writes paused. Apply fan settings again to resume.");
    }
    private void RestoreFanMode()
    {
        lock (_fanControlGate)
        {
            ResetFanFollower();
            if (!_hw.TryGetPowerMode(out byte mode)) { _fanWriteFailed = true; return; }
            _runningFanProfile = FanCurveStorage.Load(mode);
            _runningFanProfileMode = mode;
            _hw.InvalidateFanTargets();
            _fanModeSettlesAt = Environment.TickCount64 + 3000;
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
                if (_autoFanModeApplied) { _fanOverrideActive = _sentMaxFanMode = false; _sentCpuFan = _sentGpuFan = null; }
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
