using System.Management;
using PreySense.Core.Services;

namespace PreySense.Helpers;

public partial class WmiController
{
    public void StartPowerSourceWatch()
    {
        try
        {
            var watcher = new ManagementEventWatcher(new ManagementScope(@"root\wmi"), new WqlEventQuery("SELECT * FROM APGeEvent"));
            watcher.EventArrived += OnAcerPowerEvent;
            watcher.Start();
            _powerSourceWatcher = watcher;
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Power source watch: {ex.Message}");
        }
    }

    public static bool TryParseAcerPowerEvent(byte[]? detail, out PowerSourceKind source)
    {
        source = default;
        if (detail == null || detail.Length < 2 || detail[0] != 8)
            return false;

        switch (detail[1])
        {
            case 0:
                source = PowerSourceKind.Battery;
                return true;
            case 1:
                source = PowerSourceKind.Ac;
                return true;
            case 4:
                source = PowerSourceKind.Ac;
                return true;
            default:
                return false;
        }
    }

    private void OnAcerPowerEvent(object sender, EventArrivedEventArgs e)
    {
        try
        {
            if (e.NewEvent?["EventDetail"] is not byte[] detail ||
                !TryParseAcerPowerEvent(detail, out PowerSourceKind source))
                return;

            AppLogger.Log($"Acer power source event: {source}.");
            PowerSourceChanged?.Invoke(this, source);
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Acer power source event: {ex.Message}");
        }
    }
}
