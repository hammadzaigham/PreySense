using CommunityToolkit.Mvvm.Input;
using PreySense.Frontend.Services;

namespace PreySense.Frontend.ViewModels;

public partial class MainViewModel
{
    private bool _lcdOverdriveAvailable, _lcdOverdriveBusy, _lcdOverdriveEnabled;
    public bool CanChangeLcdOverdrive => _lcdOverdriveAvailable && !_lcdOverdriveBusy;
    public bool IsLcdOverdriveEnabled => _lcdOverdriveEnabled;

    public async Task RefreshLcdOverdriveAsync()
    {
        if (_lcdOverdriveBusy) return;
        _lcdOverdriveBusy = true;
        OnPropertyChanged(nameof(CanChangeLcdOverdrive));
        try
        {
            var state = await Task.Run(() =>
            {
                bool available = _hw.TryGetLcdOverdrive(out bool enabled);
                return (available, enabled);
            });
            _lcdOverdriveAvailable = state.available;
            _lcdOverdriveEnabled = state.available && state.enabled;
        }
        catch { _lcdOverdriveAvailable = _lcdOverdriveEnabled = false; }
        finally
        {
            _lcdOverdriveBusy = false;
            OnPropertyChanged(nameof(IsLcdOverdriveEnabled));
            OnPropertyChanged(nameof(CanChangeLcdOverdrive));
        }
    }

    [RelayCommand]
    private async Task ToggleLcdOverdriveAsync()
    {
        if (!CanChangeLcdOverdrive) return;
        bool requested = !_lcdOverdriveEnabled;
        _lcdOverdriveBusy = true;
        OnPropertyChanged(nameof(CanChangeLcdOverdrive));
        try
        {
            var result = await Task.Run(() =>
            {
                bool written = _hw.SetLcdOverdrive(requested);
                bool readable = _hw.TryGetLcdOverdrive(out bool actual);
                return (success: written && readable && actual == requested, actual);
            });
            _lcdOverdriveAvailable = result.success;
            _lcdOverdriveEnabled = result.success && result.actual;
        }
        catch { _lcdOverdriveAvailable = _lcdOverdriveEnabled = false; }
        finally
        {
            _lcdOverdriveBusy = false;
            OnPropertyChanged(nameof(IsLcdOverdriveEnabled));
            OnPropertyChanged(nameof(CanChangeLcdOverdrive));
        }
    }
}
