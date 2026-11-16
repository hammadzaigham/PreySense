using PreySense.Core.Configuration;
using PreySense.Mode;
using PreySense.Frontend.Services;

namespace PreySense.Frontend.ViewModels;

public partial class MainViewModel
{
    private int _fanControlDelaySeconds;
    private LatestBackgroundWorker<(int? Delay, bool? Synchronize)>? _fanSettingsWorker;

    private void QueueFanSettingsSave(int? delay = null, bool? synchronize = null)
    {
        _fanSettingsWorker ??= new(request =>
        {
            ConfigurationService.Instance.Update(config =>
            {
                if (request.Delay is int savedDelay) config.Fans.ControlDelaySeconds = savedDelay;
                if (request.Synchronize is bool savedSync) config.Fans.SynchronizeFanSpeeds = savedSync;
            });
            lock (_fanControlGate)
            {
                if (request.Synchronize.HasValue) _sentCpuFan = _sentGpuFan = null;
                if (request.Delay is int newDelay && _runningFanProfile?.ControlMode == FanControlMode.Custom)
                {
                    _runningFanProfile.RampUpSeconds = newDelay;
                    _fanFollower.Reset();
                }
            }
        }, (previous, latest) => (latest.Delay ?? previous.Delay, latest.Synchronize ?? previous.Synchronize));
        _ = _fanSettingsWorker.Submit((delay, synchronize));
    }

    internal Task WaitForFanSettingsSaveAsync() => _fanSettingsWorker?.WhenIdleAsync() ?? Task.CompletedTask;

    public int FanControlDelaySeconds
    {
        get => _fanControlDelaySeconds;
        set
        {
            int delay = Math.Clamp(value, 0, 5);
            if (_fanControlDelaySeconds == delay) return;
            try
            {
                SetProperty(ref _fanControlDelaySeconds, delay);
                QueueFanSettingsSave(delay: delay);
            }
            catch (Exception ex) { DiagnosticMessage = $"Could not save fan control delay: {ex.Message}"; }
        }
    }

    public void InitializeFanSettings()
    {
        _fanControlDelaySeconds = ConfigurationService.Instance.GetCurrent().Fans.ControlDelaySeconds;
        OnPropertyChanged(nameof(FanControlDelaySeconds));
    }
}
