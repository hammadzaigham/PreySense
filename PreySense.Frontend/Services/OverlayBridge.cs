using System;
using PreySense.Core.Configuration;
using PreySense.Helpers;
using PreySense.Overlay;

namespace PreySense.Frontend.Services;

internal static class OverlayBridge
{
    private static Func<WmiController?>? _wmiProvider;
    private static bool _initialized;

    public static event Action<bool>? EnabledChanged;

    public static void Configure(Func<WmiController?> wmiProvider) => _wmiProvider = wmiProvider;

    public static bool IsEnabled => ConfigurationService.Instance.GetCurrent().Overlay.Enabled;

    public static bool ShouldRun => AppConfig.ShouldRunOverlay();
    public static bool IsShowing => HardwareOverlayManager.IsShowing;

    public static void Start()
    {
        EnsureInitialized();
        HardwareOverlayManager.Start();
    }

    public static void StartIfNeeded()
    {

        EnsureInitialized();
        HardwareOverlayManager.SyncRunningState();
    }

    public static void Toggle()
    {
        EnsureInitialized();
        HardwareOverlayManager.Toggle();
        NotifyEnabledChanged();
    }

    private static void NotifyEnabledChanged() => EnabledChanged?.Invoke(IsEnabled);

    public static void Dispose()
    {
        if (!_initialized)
            return;

        HardwareOverlayManager.Dispose();
        _initialized = false;
    }

    private static void EnsureInitialized()
    {
        if (_initialized)
            return;

        HardwareOverlayManager.SetWmiProvider(_wmiProvider ?? (() => null));
        _initialized = true;
    }
}
