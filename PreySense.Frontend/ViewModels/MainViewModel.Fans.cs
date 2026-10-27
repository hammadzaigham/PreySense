using System;
using System.Threading.Tasks;
using PreySense.Core.Services;
using PreySense.Mode;

namespace PreySense.Frontend.ViewModels;

public partial class MainViewModel
{
    private readonly object _fanControlGate = new();
    private readonly FanCurveFollower _cpuFanFollower = new();
    private readonly FanCurveFollower _gpuFanFollower = new();
    private readonly FanCoolingSafety _fanCoolingSafety = new();
    private FanCurveProfile? _runningFanProfile;
    private byte _runningFanProfileMode = 0xFF;
    private int? _sentCpuFan, _sentGpuFan;
    private bool _sentMaxFanMode, _fanOverrideActive, _fanFallbackActive;
    private long _fanRetryAfter, _fanModeSettlesAt;

    internal bool RequiresFanTelemetry
    {
        get
        {
            lock (_fanControlGate)
                return _runningFanProfile == null || _runningFanProfile.ControlMode == FanControlMode.Custom;
        }
    }

    private void UpdateFanTelemetryInterval()
    {
        _telemetryTimer.Interval = _telemetryWindowVisible || RequiresFanTelemetry
            ? VisibleTelemetryIntervalMs
            : 5000;
    }

    public int? AppliedCpuFanPercent
    {
        get { lock (_fanControlGate) return !_fanOverrideActive || _fanFallbackActive ? null : _sentMaxFanMode ? 100 : _sentCpuFan; }
    }
    public int? AppliedGpuFanPercent
    {
        get { lock (_fanControlGate) return !_fanOverrideActive || _fanFallbackActive ? null : _sentMaxFanMode ? 100 : _sentGpuFan; }
    }
    public FanCurveProfile LoadFanProfile(byte mode) => FanCurveStorage.Load(mode);
    public void SaveFanProfile(byte mode, FanCurveProfile profile)
    {
        FanCurveStorage.Save(mode, profile);
        lock (_fanControlGate)
        {
            if (_runningFanProfileMode == mode) ResetFanFollower();
        }
        StatusMessage = $"{ProfileManager.ModeToProfileName(mode)} fan settings saved.";
    }
    private void ResetFanFollower()
    {
        _cpuFanFollower.Reset(); _gpuFanFollower.Reset();
        _runningFanProfile = null;
        _runningFanProfileMode = 0xFF;
        _sentCpuFan = _sentGpuFan = null;
        _sentMaxFanMode = false;
        _fanRetryAfter = 0;
        _fanFallbackActive = false;
        // Keep override ownership until hardware successfully returns to Auto.
    }
    private void ProcessFanCurves(HardwareTelemetry telemetry)
    {
        if (_disposed || IsFactoryCalibrationRunning) return;
        lock (_fanControlGate)
        {
            if (_disposed || IsFactoryCalibrationRunning || Environment.TickCount64 < _fanRetryAfter) return;
            if (!_hw.TryGetPowerMode(out byte mode)) { ReleaseCustomFans(); return; }
            if (_runningFanProfileMode != mode || _runningFanProfile == null)
            {
                ResetFanFollower();
                _runningFanProfile = FanCurveStorage.Load(mode);
                _runningFanProfileMode = mode;
                _hw.InvalidateFanTargets();
                _fanModeSettlesAt = Environment.TickCount64 + 3000;
                UpdateFanTelemetryInterval();
            }
            if (_runningFanProfile.ControlMode == FanControlMode.Auto)
            {
                if (!_fanFallbackActive)
                {
                    if (!_hw.SetFanMode(0, 0)) { FanWriteFailed(); return; }
                    _fanOverrideActive = false;
                    _fanFallbackActive = true;
                    _sentCpuFan = _sentGpuFan = null;
                }
                UpdateFanTelemetryInterval();
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
                        _fanFallbackActive = false;
                        _sentCpuFan = _sentGpuFan = null;
                    }
                    else FanWriteFailed();
                }
                return;
            }
            bool emergency = _fanCoolingSafety.Evaluate(telemetry.CpuTemp, telemetry.GpuTemp);
            bool valid = FanCurveProfile.ValidTemperatures(telemetry.CpuTemp, telemetry.GpuTemp);
            if (!emergency && Environment.TickCount64 < _fanModeSettlesAt) return;
            if (!emergency && !valid)
            {
                if (!_fanFallbackActive)
                {
                    if (!_hw.SetFanMode(0, 0)) { FanWriteFailed(); return; }
                    _fanFallbackActive = true;
                    _fanOverrideActive = false;
                    _sentCpuFan = _sentGpuFan = null;
                    _cpuFanFollower.Reset(); _gpuFanFollower.Reset();
                }
                return;
            }
            int temperature = FanCurveProfile.CoolingTemperature(telemetry.CpuTemp, telemetry.GpuTemp);
            long now = Environment.TickCount64;
            int cpu = _cpuFanFollower.Evaluate(temperature, _runningFanProfile.Cpu, _runningFanProfile.RampUpSeconds, now, emergency);
            int gpu = _gpuFanFollower.Evaluate(temperature, _runningFanProfile.Gpu, _runningFanProfile.RampUpSeconds, now, emergency);
            if (_sentCpuFan == cpu && _sentGpuFan == gpu && !_fanFallbackActive) return;
            _fanOverrideActive = true;
            if (_hw.SetFanSpeeds((byte)cpu, (byte)gpu))
            {
                _sentCpuFan = cpu; _sentGpuFan = gpu;
                _fanOverrideActive = true;
                _fanFallbackActive = _sentMaxFanMode = false;
            }
            else FanWriteFailed();
        }
    }
    private void FanWriteFailed()
    {
        bool released = _hw.SetFanMode(0, 0);
        ResetFanFollower();
        if (released) _fanOverrideActive = false;
        _fanFallbackActive = released;
        _fanRetryAfter = Environment.TickCount64 + 5000;
        _dispatcher.InvokeAsync(() => StatusMessage = released
            ? "Fan update failed; using firmware Auto." : "Fan update failed; could not restore firmware Auto.");
    }
    private void RestoreFanMode()
    {
        lock (_fanControlGate)
        {
            ResetFanFollower();
            ProcessFanCurves(_hw.GetCurrentTelemetry());
        }
    }
    private void ReleaseCustomFans()
    {
        lock (_fanControlGate)
        {
            if (!_fanOverrideActive) return;
            try
            {
                _fanFallbackActive = _hw.SetFanMode(0, 0);
                if (_fanFallbackActive) { _fanOverrideActive = _sentMaxFanMode = false; _sentCpuFan = _sentGpuFan = null; }
            }
            catch (Exception ex) { AppLogger.Log($"Could not return fans to Auto: {ex.Message}"); }
        }
    }
    public async Task ApplyFanProfileAsync()
    {
        await Task.Run(RestoreFanMode);
        lock (_fanControlGate)
        {
            if (_runningFanProfile == null || _fanRetryAfter > Environment.TickCount64)
                throw new InvalidOperationException("Fan settings could not be applied.");
        }
        StatusMessage = "Fan settings saved; following the selected mode.";
    }
}
