using System;
using Microsoft.Win32;
using PreySense.Helpers;
using PreySense.Overlay;

namespace PreySense.Frontend.Services;

internal static class OverlayBridge
{
    private static Func<WmiController?>? _wmiProvider;
    private static bool _initialized;

    public static event Action<bool>? EnabledChanged;

    public static void Configure(Func<WmiController?> wmiProvider) => _wmiProvider = wmiProvider;

    public static bool IsEnabled
    {
        get
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\PreySense");
            return key?.GetValue("overlay") is int value && value == 1;
        }
    }

    public static bool ShouldRun => AppConfig.ShouldRunOverlay();

        public static void Warmup()
        {
            System.Threading.ThreadPool.QueueUserWorkItem(_ => HardwareControl.Warmup());
            EnsureInitialized();
            HardwareOverlayManager.Warmup();
        }

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

    public static void GameStarted()
    {
        EnsureInitialized();
        HardwareOverlayManager.StartForConfiguredGame();
    }

    public static void GameExited()
    {
        EnsureInitialized();
        HardwareOverlayManager.StopForConfiguredGame();
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
