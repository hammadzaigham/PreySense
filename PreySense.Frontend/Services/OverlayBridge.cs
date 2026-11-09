using System;
using PreySense.Core.Configuration;
using PreySense.Overlay;

namespace PreySense.Frontend.Services;

internal static class OverlayBridge
{
    public static event Action<bool>? EnabledChanged;
    public static bool IsEnabled => ConfigurationService.Instance.GetCurrent().Overlay.Enabled;
    public static bool ShouldRun => AppConfig.ShouldRunOverlay();
    public static bool IsShowing => HardwareOverlayManager.IsShowing;

    public static void Start() => HardwareOverlayManager.Start();
    public static void StartIfNeeded() => HardwareOverlayManager.SyncRunningState();

    public static void Toggle()
    {
        HardwareOverlayManager.Toggle();
        EnabledChanged?.Invoke(IsEnabled);
    }

    public static void Dispose() => HardwareOverlayManager.Dispose();
}
