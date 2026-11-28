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
            var state = await Task.Run(() =>
            {
                bool available = _hw.TryGetLcdOverdrive(out bool actual);
                if (available && cached?.LcdOverdriveSupported == true && actual != cached.LcdOverdriveEnabled)
                {
                    if (_hw.SetLcdOverdrive(cached.LcdOverdriveEnabled))
                        available = _hw.TryGetLcdOverdrive(out actual);
                }
                return (available, actual);
            });
            _lcdOverdriveAvailable = state.available;
            _lcdOverdriveEnabled = state.available && state.actual;
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

    internal void ApplyPersistedLcdOverdrive()
    {
        try
        {
            var store = _lcdOverdriveStore ?? ConfigurationService.Instance;
            var display = store.GetCurrent().Display;
            if (!_hw.TryGetLcdOverdrive(out bool actual))
            {
                UpdateLcdOverdriveState(false, false);
                return;
            }

            if (display.LcdOverdriveSupported == true)
            {
                if (actual != display.LcdOverdriveEnabled && _hw.SetLcdOverdrive(display.LcdOverdriveEnabled)
                    && !_hw.TryGetLcdOverdrive(out actual))
                {
                    UpdateLcdOverdriveState(false, false);
                    return;
                }
            }
            else
            {
                store.Update(cfg =>
                {
                    cfg.Display.LcdOverdriveSupported = true;
                    cfg.Display.LcdOverdriveEnabled = actual;
                });
            }

            UpdateLcdOverdriveState(true, actual);
        }
        catch (Exception ex) { AppLogger.Error($"Could not apply persisted LCD overdrive state: {ex.Message}"); }
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
                return (success: written && readable && actual == requested, readable, actual);
            });
            if (result.readable)
            {
                _lcdOverdriveAvailable = true;
                _lcdOverdriveEnabled = result.actual;
                if (result.success) await SaveLcdOverdriveStateAsync();
            }
            else
            {
                _lcdOverdriveAvailable = false;
                _lcdOverdriveEnabled = false;
            }
        }
        catch { }
        finally
        {
            _lcdOverdriveBusy = false;
            OnPropertyChanged(nameof(IsLcdOverdriveAvailable));
            OnPropertyChanged(nameof(IsLcdOverdriveEnabled));
            OnPropertyChanged(nameof(CanChangeLcdOverdrive));
        }
    }

    private void UpdateLcdOverdriveState(bool available, bool enabled)
    {
        void Apply()
        {
            _lcdOverdriveAvailable = available;
            _lcdOverdriveEnabled = available && enabled;
            OnPropertyChanged(nameof(IsLcdOverdriveAvailable));
            OnPropertyChanged(nameof(IsLcdOverdriveEnabled));
            OnPropertyChanged(nameof(CanChangeLcdOverdrive));
        }

        if (_dispatcher == null || _dispatcher.CheckAccess())
            Apply();
        else
            _dispatcher.BeginInvoke(Apply);
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
