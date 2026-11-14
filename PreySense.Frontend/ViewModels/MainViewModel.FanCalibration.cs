using PreySense.Core.Configuration;
using PreySense.Core.Services;
using PreySense.Mode;

namespace PreySense.Frontend.ViewModels;

public partial class MainViewModel
{
    private bool _fanRpmCalibrationActive;
    private CancellationTokenSource? _fanCalibrationCancellation;
    private TaskCompletionSource? _fanCalibrationFinished;

    public async Task PrepareFanCalibrationShutdownAsync()
    {
        var finished = _fanCalibrationFinished;
        _fanCalibrationCancellation?.Cancel();
        if (finished is not null) await finished.Task;
    }
    private bool _isFanRpmCalibrationRunning;
    public bool IsFanRpmCalibrationRunning { get => _isFanRpmCalibrationRunning; private set => SetProperty(ref _isFanRpmCalibrationRunning, value); }
    private string _fanRpmCalibrationStatus = "";
    public string FanRpmCalibrationStatus { get => _fanRpmCalibrationStatus; private set => SetProperty(ref _fanRpmCalibrationStatus, value); }
    internal Func<CancellationToken, Task<(int Cpu, int Gpu)>>? FanCalibrationSampler { get; set; }
    internal Func<TimeSpan, CancellationToken, Task>? FanCalibrationWait { get; set; }
    internal static readonly TimeSpan FanCalibrationSpinUp = TimeSpan.FromSeconds(20);

    public async Task<bool> CalibrateFanRpmsAsync(CancellationToken cancellationToken = default)
    {
        if (IsFanRpmCalibrationRunning || _disposed) return false;
        IsFanRpmCalibrationRunning = true;
        _fanCalibrationFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _fanCalibrationCancellation = cancellation;
        bool entered = false, overridden = false;
        FanCurveProfile? previous = null;
        int? cpuBefore = null, gpuBefore = null;
        bool restored = true;
        string finalStatus = "Calibration did not start.";
        try
        {
            entered = await TryEnterModeSwitchGateAsync();
            if (!entered) return false;
            cancellation.Token.ThrowIfCancellationRequested();
            FanRpmCalibrationStatus = "Calibrating Fan RPM...";
            await Task.Run(() =>
            {
                lock (_fanControlGate)
                {
                    previous = _runningFanProfile?.Copy() ?? FanCurveStorage.Load(_hw.CurrentPowerMode);
                    cpuBefore = _sentCpuFan; gpuBefore = _sentGpuFan;
                    _fanRpmCalibrationActive = true;
                    overridden = true;
                    _hw.InvalidateFanTargets();
                    if (!_hw.SetFanSpeeds(100, 100)) throw new InvalidOperationException("Could not set both fans to custom 100% duty.");
                }
            });
            await (FanCalibrationWait?.Invoke(FanCalibrationSpinUp, cancellation.Token)
                ?? Task.Delay(FanCalibrationSpinUp, cancellation.Token));
            var measured = await (FanCalibrationSampler?.Invoke(cancellation.Token) ?? MeasureFanMaximumsAsync(cancellation.Token));
            cancellation.Token.ThrowIfCancellationRequested();
            if (measured.Cpu is <= 0 or > 65500 || measured.Gpu is <= 0 or > 65500)
                throw new InvalidOperationException("Both fans must provide valid RPM readings. Existing maximums were kept.");
            measured = (RoundFanMaximumRpm(measured.Cpu), RoundFanMaximumRpm(measured.Gpu));
            ConfigurationService.Instance.Update(config => { config.Fans.CpuMaxRpm = measured.Cpu; config.Fans.GpuMaxRpm = measured.Gpu; });
            finalStatus = $"Fan RPM Calibrated. CPU: {measured.Cpu} RPM. GPU: {measured.Gpu} RPM.";
            return true;
        }
        catch (OperationCanceledException) { finalStatus = "Calibration cancelled. Saved maximum RPM values were not changed."; return false; }
        catch (Exception ex) { finalStatus = $"Calibration failed. Saved maximum RPM values were not changed. {ex.Message}"; return false; }
        finally
        {
            if (overridden)
            {
                await Task.Run(() =>
                {
                    lock (_fanControlGate)
                    {
                        try
                        {
                            _hw.InvalidateFanTargets();
                            restored = previous?.ControlMode switch
                            {
                                FanControlMode.Max => _hw.SetFanMode(1, 1),
                                FanControlMode.Custom => _hw.SetFanSpeeds((byte)(cpuBefore ?? previous.Stages[0].CpuDutyPercent),
                                    (byte)(gpuBefore ?? previous.Stages[0].GpuDutyPercent)),
                                _ => _hw.SetFanMode(0, 0)
                            };
                        }
                        catch (Exception ex) { restored = false; AppLogger.Error($"Fan calibration restoration failed: {ex}"); }
                        _fanRpmCalibrationActive = false;
                        ResetFanFollower();
                        _fanOverrideActive = previous?.ControlMode != FanControlMode.Auto;
                    }
                    if (!_disposed)
                    {
                        try { RestoreFanMode(); }
                        catch (Exception ex) { restored = false; AppLogger.Error($"Could not resume fan controller: {ex}"); }
                    }
                });
                if (!restored) finalStatus += " Previous fan control could not be restored. Apply fan settings again.";
            }
            if (entered) ExitModeSwitchGate();
            _fanCalibrationCancellation = null;
            FanRpmCalibrationStatus = finalStatus;
            IsFanRpmCalibrationRunning = false;
            _fanCalibrationFinished.TrySetResult();
        }
    }

    internal static int RoundFanMaximumRpm(int rpm) => ((rpm + 99) / 100) * 100;

    private static async Task<(int Cpu, int Gpu)> MeasureFanMaximumsAsync(CancellationToken cancellationToken)
    {
        var result = new TaskCompletionSource<(int, int)>(TaskCreationOptions.RunContinuationsAsynchronously);
        int count = 0, cpu = 0, gpu = 0;
        long lastTimestamp = 0;
        void Sample(TelemetrySnapshot snapshot)
        {
            lock (result)
            {
                if (!snapshot.IsValid
                    || snapshot.TimestampTicks <= lastTimestamp || snapshot.CpuRpm is not (> 0 and <= 65535)
                    || snapshot.GpuRpm is not (> 0 and <= 65535)) return;
                lastTimestamp = snapshot.TimestampTicks;
                cpu = Math.Max(cpu, snapshot.CpuRpm.Value); gpu = Math.Max(gpu, snapshot.GpuRpm.Value);
                if (++count >= 10) result.TrySetResult((cpu, gpu));
            }
        }
        TelemetryCoordinator.SnapshotAvailable += Sample;
        TelemetryCoordinator.SetDemand(TelemetryDemand.FanCalibration, true);
        try { return await result.Task.WaitAsync(TimeSpan.FromSeconds(25), cancellationToken); }
        finally
        {
            TelemetryCoordinator.SnapshotAvailable -= Sample;
            TelemetryCoordinator.SetDemand(TelemetryDemand.FanCalibration, false);
        }
    }
}
