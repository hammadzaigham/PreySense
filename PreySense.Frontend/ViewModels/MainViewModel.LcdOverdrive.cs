using CommunityToolkit.Mvvm.Input;
using PreySense.Core.Configuration;

namespace PreySense.Frontend.ViewModels;

public partial class MainViewModel
{
    private bool _lcdOverdriveAvailable, _lcdOverdriveBusy, _lcdOverdriveEnabled;
    private ConfigurationService? _lcdOverdriveStore;
    public bool IsLcdOverdriveAvailable => _lcdOverdriveAvailable;
    public bool CanChangeLcdOverdrive => _lcdOverdriveAvailable && !_lcdOverdriveBusy;
    public bool IsLcdOverdriveEnabled => _lcdOverdriveEnabled;

    internal async Task InitializeLcdOverdriveAsync(ConfigurationService? store = null)
    {
        if (_lcdOverdriveBusy) return;
        _lcdOverdriveBusy = true;
        OnPropertyChanged(nameof(CanChangeLcdOverdrive));
        try
        {
            _lcdOverdriveStore = store;
            var cached = store?.GetCurrent().Display;
            if (cached?.LcdOverdriveSupported is bool supported)
            {
                _lcdOverdriveAvailable = supported;
                _lcdOverdriveEnabled = supported && cached.LcdOverdriveEnabled;
                return;
            }

            var state = await Task.Run(() =>
            {
                bool available = _hw.TryGetLcdOverdrive(out bool enabled);
                return (available, enabled);
            });
            _lcdOverdriveAvailable = state.available;
            _lcdOverdriveEnabled = state.available && state.enabled;
            await SaveLcdOverdriveStateAsync();
        }
        catch { _lcdOverdriveAvailable = _lcdOverdriveEnabled = false; }
        finally
        {
            _lcdOverdriveBusy = false;
            OnPropertyChanged(nameof(IsLcdOverdriveAvailable));
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
            if (result.success) await SaveLcdOverdriveStateAsync();
        }
        catch { _lcdOverdriveAvailable = _lcdOverdriveEnabled = false; }
        finally
        {
            _lcdOverdriveBusy = false;
            OnPropertyChanged(nameof(IsLcdOverdriveAvailable));
            OnPropertyChanged(nameof(IsLcdOverdriveEnabled));
            OnPropertyChanged(nameof(CanChangeLcdOverdrive));
        }
    }

    private async Task SaveLcdOverdriveStateAsync()
    {
        if (_lcdOverdriveStore is not { } store) return;
        bool available = _lcdOverdriveAvailable;
        bool enabled = _lcdOverdriveEnabled;
        try
        {
            await Task.Run(() => store.Update(config =>
            {
                config.Display.LcdOverdriveSupported = available;
                config.Display.LcdOverdriveEnabled = enabled;
            }));
        }
        catch (Exception ex) { AppLogger.Error($"Could not save LCD overdrive state: {ex.Message}"); }
    }
}
