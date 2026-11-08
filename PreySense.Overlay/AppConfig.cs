using System;
using PreySense.Core.Configuration;

namespace PreySense.Overlay
{
    public static class AppConfig
    {
        private static PreySenseConfig Cfg => ConfigurationService.Instance.GetCurrent();

        public static int Get(string name, int empty = -1) => name switch
        {
            "overlay" => Cfg.Overlay.Enabled ? 1 : 0,
            "overlay_show_fps" => Cfg.Overlay.ShowFps ? 1 : 0,
            "overlay_scale_percent" => Cfg.Overlay.ScalePercent,
            "overlay_alpha" => Cfg.Overlay.BackgroundAlphaPercent,
            "overlay_text_alpha" => Cfg.Overlay.TextAlphaPercent,
            "overlay_anchor" => Cfg.Overlay.Anchor,
            "overlay_offset_x" => Cfg.Overlay.OffsetX,
            "overlay_offset_y" => Cfg.Overlay.OffsetY,
            _ => empty
        };

        public static bool Is(string name)
        {
            return Get(name) == 1;
        }

        public static string GetString(string name, string empty = "") => name switch
        {
            "overlay_pos_horizontal" => Cfg.Overlay.PositionHorizontal,
            "overlay_pos_vertical" => Cfg.Overlay.PositionVertical,
            _ => empty
        };

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
                    case "overlay_anchor":
                        cfg.Overlay.Anchor = value;
                        break;
                    case "overlay_offset_x":
                        cfg.Overlay.OffsetX = value;
                        break;
                    case "overlay_offset_y":
                        cfg.Overlay.OffsetY = value;
                        break;
                }
            });
        }

        public static void Set(string name, string value)
        {
            ConfigurationService.Instance.Update(cfg =>
            {
                switch (name)
                {
                    case "overlay_pos_horizontal":
                        cfg.Overlay.PositionHorizontal = value;
                        break;
                    case "overlay_pos_vertical":
                        cfg.Overlay.PositionVertical = value;
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

        public static bool IsOverlayGameOnly()
        {
            return false;
        }

        public static bool ShouldRunOverlay()
        {
            return IsOverlay();
        }
    }
}
