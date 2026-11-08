using System;
using PreySense.Helpers;

namespace PreySense.Overlay
{
    public static class HardwareOverlayManager
    {
        private static HardwareOverlay? _overlay;
        private static readonly object _lock = new();

        public static bool IsActive => _overlay?.IsActive ?? false;
        public static bool IsShowing => _overlay?.IsShowing ?? false;

        public static HardwareOverlay GetOverlay()
        {
            lock (_lock)
            {
                return _overlay ??= new HardwareOverlay();
            }
        }

        public static void SetWmiProvider(Func<WmiController?> provider)
        {
            HardwareControl.WmiProvider = provider;
        }

        public static void Toggle()
        {
            if (AppConfig.IsOverlay())
                Stop();
            else
                Start();
        }

        public static void Start()
        {
            AppConfig.Set("overlay", 1);
            SyncRunningState();
        }

        public static void Stop()
        {
            AppConfig.Set("overlay", 0);
            SyncRunningState();
        }

        public static void SyncRunningState()
        {
            if (AppConfig.IsOverlay())
            {
                var overlay = GetOverlay();
                if (!overlay.IsActive)
                    overlay.StartOverlay();
                else
                    overlay.RefreshDisplayFlags();
            }
            else
            {
                _overlay?.StopOverlay();
            }
        }

        public static void RefreshDisplayFlags() => SyncRunningState();

        public static void Reposition(string horizontal, string vertical)
        {
            AppConfig.Set("overlay_pos_horizontal", horizontal);
            AppConfig.Set("overlay_pos_vertical", vertical);
            var overlay = GetOverlay();
            if (overlay.IsActive)
                overlay.SetConfiguredPosition(horizontal, vertical);
        }

        public static void Dispose()
        {
            lock (_lock)
            {
                _overlay?.StopOverlay();
                _overlay?.Dispose();
                _overlay = null;
            }
        }
    }
}
