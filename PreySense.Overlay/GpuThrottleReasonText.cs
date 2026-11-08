using System;
using System.Collections.Generic;

namespace PreySense.Overlay;

/// <summary>Compact labels for NVIDIA's NVML clocks-event bitmask, not NVAPI PerfCap IDs.</summary>
public static class GpuThrottleReasonText
{
    // https://docs.nvidia.com/deploy/nvml-api/latest/api/group__nvmlClocksEventReasons.html
    private static readonly (ulong Mask, string Label)[] Reasons =
    {
        (0x004, "Pwr"),
        (0x020, "Thrm"),
        (0x040, "HWThrm"),
        (0x080, "Brake"),
        (0x200, "Board"),
        (0x400, "VRel"),
        (0x008, "HWSlw"),
        (0x002, "Clock"),
        (0x010, "Sync"),
        (0x100, "Disp"),
        (0x001, "Idle")
    };

    [ThreadStatic] private static ulong? _lastMask;
    [ThreadStatic] private static string? _lastText;

    public static string Format(ulong? mask)
    {
        if (!mask.HasValue) return "-";
        if (_lastText != null && _lastMask == mask) return _lastText;

        string text;
        if (mask.Value == 0) text = "None";
        else
        {
            var labels = new List<string>();
            ulong remaining = mask.Value;
            foreach (var reason in Reasons)
            {
                if ((remaining & reason.Mask) == 0) continue;
                labels.Add(reason.Label);
                remaining &= ~reason.Mask;
            }
            if (remaining != 0) labels.Add("Other");
            text = string.Join("/", labels);
        }
        _lastMask = mask;
        return _lastText = text;
    }
}
