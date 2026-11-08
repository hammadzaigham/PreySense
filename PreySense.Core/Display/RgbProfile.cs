using System.Runtime.Versioning;

namespace PreySense.Helpers
{
    /// <summary>Display names for Predator Sense keyboard RGB modes.</summary>
    [SupportedOSPlatform("windows")]
    public static class RgbProfile
    {
        public static readonly string[] UiModeNames =
        {
            "Static (4-Zone Custom)", "Breathing", "Neon", "Wave", "Ripple", "Zoom", "Snake", "Disco",
            "Rainbow Cycle", "CPU Thermal Heatmap", "Battery Fuel Gauge",
            "Police Strobe", "Fire Ember", "Matrix Cascade", "Cylon Scanner", "Aurora Drift",
            "Ocean Tide", "Heartbeat", "Lightning Storm", "Starlight", "Ice Crystal"
        };
    }
}
