using System;
using System.Threading.Tasks;
using PreySense.Core.Configuration;
using PreySense.Frontend.Services;

namespace PreySense.Frontend.ViewModels;

public partial class MainViewModel
{
    private int _usbChargingCutoffPercent;
    private bool _usbChargingBusy;
    private string _usbChargingStatus = string.Empty;
    public string UsbChargingStatus => LocalizationService.Translate(_usbChargingStatus);
    public bool CanChangeUsbCharging => !_usbChargingBusy;
    public int UsbChargingCutoffPercent => _usbChargingCutoffPercent;

    public async Task SetUsbChargingAsync(int percent)
    {
        if (percent is not (0 or 10 or 20 or 30) || _usbChargingBusy || percent == _usbChargingCutoffPercent) return;
        _usbChargingBusy = true;
        OnPropertyChanged(nameof(CanChangeUsbCharging));
        try
        {
            await Task.Run(() => ApplyUsbChargingCutoff(percent));
            ConfigurationService.Instance.Update(cfg => cfg.Battery.UsbPowerOffChargingCutoffPercent = percent);
            _usbChargingCutoffPercent = percent;
            DiagnosticMessage = "USB power-off charging applied and read back successfully.";
        }
        catch (Exception ex)
        {
            SetUsbChargingStatus("USB charging: —.");
            DiagnosticMessage = "USB power-off charging: —. " + ex.Message;
        }
        finally
        {
            _usbChargingBusy = false;
            OnPropertyChanged(nameof(UsbChargingCutoffPercent));
            OnPropertyChanged(nameof(CanChangeUsbCharging));
        }
    }

    private void ApplyUsbChargingCutoff(int percent)
    {
        try
        {
            _hw.SetUsbChargingLevel(percent);
            if (_hw.GetUsbChargingLevel() != percent)
                throw new InvalidOperationException("USB power-off charging readback failed.");
            SetUsbChargingStatus(string.Empty);
        }
        catch
        {
            SetUsbChargingStatus("USB charging: —.");
            throw;
        }
    }

    private void SetUsbChargingStatus(string status)
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(() => SetUsbChargingStatus(status));
            return;
        }
        _usbChargingStatus = status;
        OnPropertyChanged(nameof(UsbChargingStatus));
    }
}
