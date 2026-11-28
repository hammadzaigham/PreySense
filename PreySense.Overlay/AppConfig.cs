using System;
using PreySense.Core.Configuration;

namespace PreySense.Overlay
{
    public static class AppConfig
    {
        private static PreySenseConfig Cfg => ConfigurationService.Instance.GetCurrent();

        public static void Set(string name, int value)
        {
            ConfigurationService.Instance.Update(cfg =>
            {
                switch (name)
                {
                    case "overlay":
                        cfg.Overlay.Enabled = value == 1;
                        break;
                    case "overlay_show_fps":
                        cfg.Overlay.ShowFps = value == 1;
                        break;
                    case "overlay_scale_percent":
                        cfg.Overlay.ScalePercent = value;
                        break;
                    case "overlay_alpha":
                        cfg.Overlay.BackgroundAlphaPercent = value;
                        break;
                    case "overlay_text_alpha":
                        cfg.Overlay.TextAlphaPercent = value;
                        break;
                }
            });
        }

        public static System.Collections.Generic.List<OverlaySlotConfig> LoadOverlaySlots()
        {
            return Cfg.Overlay.Slots;
        }

        public static void SaveOverlaySlots(System.Collections.Generic.List<OverlaySlotConfig> slots)
        {
            ArgumentNullException.ThrowIfNull(slots, nameof(slots));
            ConfigurationService.Instance.Update(cfg =>
            {
                cfg.Overlay.Slots = new System.Collections.Generic.List<OverlaySlotConfig>(slots);
            });
        }

        public static bool IsOverlay()
        {
            return Cfg.Overlay.Enabled;
        }

        public static bool ShouldRunOverlay()
        {
            return IsOverlay();
        }
    }
}
