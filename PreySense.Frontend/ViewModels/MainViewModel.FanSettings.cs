using PreySense.Core.Configuration;
using PreySense.Mode;
using PreySense.Frontend.Services;

namespace PreySense.Frontend.ViewModels;

public partial class MainViewModel
{
    private int _fanControlDelaySeconds;
    private LatestBackgroundWorker<int>? _fanSettingsWorker;

    private void QueueFanSettingsSave(int delay)
    {
        _fanSettingsWorker ??= new(savedDelay =>
        {
            ConfigurationService.Instance.Update(config =>
            {
                config.Fans.ControlDelaySeconds = savedDelay;
            });
            lock (_fanControlGate)
            {
                if (_runningFanProfile?.ControlMode == FanControlMode.Custom)
                {
                    _runningFanProfile.RampUpSeconds = savedDelay;
                    _fanFollower.Reset();
                }
            }
        }, (_, latest) => latest);
        _ = _fanSettingsWorker.Submit(delay);
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
