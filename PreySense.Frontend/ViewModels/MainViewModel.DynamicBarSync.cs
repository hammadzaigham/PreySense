namespace PreySense.Frontend.ViewModels;

public partial class MainViewModel
{
    private bool _syncAllBarLeds;
    public bool ShowSyncAllBarLeds => SelectedLightingDevice?.IsLightBar == true && IsDirectLightingDeviceMode;
    public bool SyncAllBarLeds
    {
        get => _syncAllBarLeds;
        set
        {
            if (!SetProperty(ref _syncAllBarLeds, value) || _loadingDeviceEditor) return;
            if (value && ShowSyncAllBarLeds)
                SynchronizeBarLeds(SelectedLightingKey?.ColorHex ?? DeviceLightingColorHex);
            ScheduleDeviceLighting();
        }
    }

    private void SynchronizeBarLeds(string color)
    {
        _propagatingKeyColor = true;
        try { foreach (var led in LightingLedEditors) led.ColorHex = color; }
        finally { _propagatingKeyColor = false; }
    }
}
