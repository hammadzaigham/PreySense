using PreySense.Core.Configuration;
using PreySense.Mode;

namespace PreySense.Frontend.ViewModels;

public partial class MainViewModel
{
    private int _fanControlDelaySeconds;

    public int FanControlDelaySeconds
    {
        get => _fanControlDelaySeconds;
        set
        {
            int delay = Math.Clamp(value, 0, 5);
            if (_fanControlDelaySeconds == delay) return;
            try
            {
                ConfigurationService.Instance.Update(config => config.Fans.ControlDelaySeconds = delay);
                SetProperty(ref _fanControlDelaySeconds, delay);
                lock (_fanControlGate)
                {
                    if (_runningFanProfile?.ControlMode == FanControlMode.Custom)
                    {
                        _runningFanProfile.RampUpSeconds = delay;
                        _fanFollower.Reset();
                    }
                }
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
