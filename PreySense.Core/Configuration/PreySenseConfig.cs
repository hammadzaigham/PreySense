using System;

namespace PreySense.Core.Configuration
{
    /// <summary>
    /// Root configuration model persisted to preysense.json.
    /// Units:
    /// - Power limits: Watts (W)
    /// - Time windows (Tau): Seconds (s)
    /// - Temperatures: Degrees Celsius (°C)
    /// - Fan Speeds / Percentages / Brightness / Opacity: Percent (0-100%)
    /// </summary>
    public sealed class PreySenseConfig
    {
        public const int CurrentSchemaVersion = 1;

        /// <summary>Schema version of the configuration file.</summary>
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        /// <summary>Application preferences such as language, theme, startup, and debloating.</summary>
        public GeneralConfig General { get; set; } = new();

        /// <summary>Display settings including LCD Overdrive and Acer BlueLight Shield.</summary>
        public DisplayConfig Display { get; set; } = new();

        /// <summary>Battery charge limit and USB charging settings.</summary>
        public BatteryConfig Battery { get; set; } = new();

        /// <summary>Session defaults such as active power mode and AC/Battery mode preferences.</summary>
        public SessionConfig Session { get; set; } = new();

        /// <summary>Hardware tuning profiles (CPU limits, GPU limits, and offsets) keyed by mode name.</summary>
        public TuningConfig Tuning { get; set; } = new();

        /// <summary>Per-mode custom fan curve settings.</summary>
        public FanConfig Fans { get; set; } = new();

        /// <summary>RGB Keyboard and Cover lighting configuration.</summary>
        public LightingConfig Lighting { get; set; } = new();

        /// <summary>On-Screen Display (OSD) overlay layout and metric preferences.</summary>
        public OverlayConfig Overlay { get; set; } = new();
    }

    public sealed class GeneralConfig
    {
        /// <summary>UI Language code (e.g., 'en-US', 'zh-CN', 'hi-IN', 'es-ES', 'fr-FR').</summary>
        public string Language { get; set; } = "en-US";

        /// <summary>True for light theme, false for dark theme.</summary>
        public bool LightTheme { get; set; } = false;

        /// <summary>Whether Acer OEM background services are debloated (stopped and disabled).</summary>
        public bool AcerServicesDebloated { get; set; } = false;

        /// <summary>Whether hardware tuning controls are enabled.</summary>
        public bool HardwareTuningEnabled { get; set; } = false;
    }

    public sealed class DisplayConfig
    {
        /// <summary>LCD 3ms response overdrive enabled state.</summary>
        public bool LcdOverdrive { get; set; } = false;

        /// <summary>Acer BlueLight Shield mode: 0 = Off, 1 = LowReduction, 2 = MediumReduction.</summary>
        public int BlueLightShieldMode { get; set; } = 0;

        /// <summary>Automatically lower refresh rate when on battery.</summary>
        public bool AutoRefreshRate { get; set; } = false;
    }

    public sealed class BatteryConfig
    {
        /// <summary>80% battery charge limiter enabled.</summary>
        public bool ChargeLimitEnabled { get; set; } = false;

        /// <summary>USB charging cutoff threshold percent (0, 10, 20, 30).</summary>
        public int UsbChargingPercent { get; set; } = 0;

        /// <summary>Automatically engage Windows Energy Saver when on battery.</summary>
        public bool BatteryEnergySaver { get; set; } = false;

        /// <summary>Automatically switch GPU to Eco mode on battery.</summary>
        public bool GpuBatteryAuto { get; set; } = false;
    }

    public sealed class SessionConfig
    {
        /// <summary>Active power mode (0 = Quiet, 1 = Balanced, 4 = Performance, 5 = Turbo, 6 = Eco).</summary>
        public byte ActivePowerMode { get; set; } = 1;

        /// <summary>Preferred power mode when connected to AC power.</summary>
        public byte AcPowerMode { get; set; } = 1;


        /// <summary>Preferred power mode when running on battery.</summary>
        public byte BatteryPowerMode { get; set; } = 6;

        /// <summary>Preferred GPU mode index: 0 = Integrated, 1 = Dynamic/Optimus, 2 = Discrete.</summary>
        public int GpuModePreference { get; set; } = 1;
    }

    public sealed class TuningConfig
    {
        public bool ApplyProchotOffset { get; set; }
        public int? ProchotOffsetCelsius { get; set; }
        public int? FactoryProchotOffsetCelsius { get; set; }
        public bool? FactoryProchotOffsetLocked { get; set; }

        /// <summary>Hardware default power limit in Watts detected via NVML or factory calibration.</summary>
        public int? GpuBasePowerWatts { get; set; }

        /// <summary>Driver-read factory profiles, saved only after all five modes were captured.</summary>
        public Dictionary<string, ModeTuningConfig> FactoryDefaults { get; set; } = new();

        [System.Text.Json.Serialization.JsonIgnore]
        public bool HasFactoryDefaults => new[] { "Eco", "Quiet", "Balanced", "Performance", "Turbo" }
            .All(name => FactoryDefaults != null && FactoryDefaults.TryGetValue(name, out var profile)
                && profile != null && profile.CpuPl1Watts is >= 5 and <= 300
                && profile.CpuPl2Watts is >= 5 and <= 300 && profile.CpuPl2Watts >= profile.CpuPl1Watts
                && profile.TauSeconds is >= 0 and <= 449
                && profile.GpuDeltaWatts is >= 0 and <= 50 && profile.GpuDeltaWatts % 5 == 0
                && profile.GpuDynamicBoostWatts is 0 or 10 or 15 or 20 or 25
                && profile.PlatformPowerOffsetWatts is >= 0 and <= 100 && profile.PlatformPowerOffsetWatts % 5 == 0);

        /// <summary>Global or active CPU boost mode (0 = Disabled, 1 = Enabled).</summary>
        public int CpuBoostMode { get; set; } = 1;

        public ModeTuningConfig Quiet { get; set; } = new();

        public ModeTuningConfig Eco { get; set; } = new();

        public ModeTuningConfig Balanced { get; set; } = new();

        public ModeTuningConfig Performance { get; set; } = new();

        public ModeTuningConfig Turbo { get; set; } = new();
    }

    public sealed class ModeTuningConfig
    {
        [System.Text.Json.Serialization.JsonIgnore]
        public bool HasValues => CpuPl1Watts.HasValue && CpuPl2Watts.HasValue && TauSeconds.HasValue
            && GpuDeltaWatts.HasValue && GpuDynamicBoostWatts.HasValue
            && PlatformPowerOffsetWatts.HasValue && EffectiveCpuBoostMode.HasValue;

        /// <summary>Resolved on/off boost from CpuBoostMode or legacy CpuBoost bool. Values 2–6 migrate to Enabled.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public int? EffectiveCpuBoostMode =>
            CpuBoostMode is >= 0 and <= 6 ? (CpuBoostMode > 0 ? 1 : 0)
            : CpuBoost == true ? 1
            : CpuBoost == false ? 0
            : null;

        /// <summary>CPU Long-term power limit (PL1) in Watts.</summary>
        public int? CpuPl1Watts { get; set; }

        /// <summary>CPU Short-term burst power limit (PL2) in Watts.</summary>
        public int? CpuPl2Watts { get; set; }

        /// <summary>CPU Tau burst duration in seconds (0-449).</summary>
        public int? TauSeconds { get; set; }

        /// <summary>Legacy on/off boost flag. Prefer <see cref="CpuBoostMode"/>.</summary>
        public bool? CpuBoost { get; set; }

        /// <summary>Per-mode Windows CPU boost (0 = Disabled, 1 = Enabled).</summary>
        public int? CpuBoostMode { get; set; }

        /// <summary>Target GPU Total Power limit in Watts.</summary>
        public int? GpuWatts { get; set; }


        /// <summary>GPU NVPCF base offset in Watts.</summary>
        public int? GpuDeltaWatts { get; set; }

        /// <summary>GPU NVPCF dynamic boost offset in Watts.</summary>
        public int? GpuDynamicBoostWatts { get; set; }

        /// <summary>ERM4 platform power offset (CP) in Watts.</summary>
        public int? PlatformPowerOffsetWatts { get; set; }

        /// <summary>Apply CPU limits flag for this profile.</summary>
        public bool ApplyCpuLimits { get; set; } = false;

        /// <summary>Apply GPU limits flag for this profile.</summary>
        public bool ApplyGpuLimits { get; set; } = false;

        /// <summary>Apply fan curve flag for this profile.</summary>
        public bool ApplyFanCurve { get; set; } = false;

        /// <summary>Fan ramp-up rate (0-10 seconds).</summary>
        public int FanRampUp { get; set; } = 1;
    }

    public sealed class FanConfig
    {
        public ModeFanCurveConfig Quiet { get; set; } = ModeFanCurveConfig.CreateDefaultQuiet();
        public ModeFanCurveConfig Eco { get; set; } = ModeFanCurveConfig.CreateDefaultEco();
        public ModeFanCurveConfig Balanced { get; set; } = ModeFanCurveConfig.CreateDefaultBalanced();
        public ModeFanCurveConfig Performance { get; set; } = ModeFanCurveConfig.CreateDefaultPerformance();
        public ModeFanCurveConfig Turbo { get; set; } = ModeFanCurveConfig.CreateDefaultTurbo();
    }

    public sealed class ModeFanCurveConfig
    {
        /// <summary>Fan control mode: 0 = Auto, 1 = Max, 2 = Custom.</summary>
        public int ControlMode { get; set; } = 0;

        /// <summary>Ramp up time in seconds.</summary>
        public int RampUpSeconds { get; set; } = 2;

        /// <summary>CPU fan duty curve points (percentages 0-100%). Default length 13.</summary>
        public int[] CpuCurve { get; set; } = new int[13];

        /// <summary>GPU fan duty curve points (percentages 0-100%). Default length 13.</summary>
        public int[] GpuCurve { get; set; } = new int[13];

        public static ModeFanCurveConfig CreateDefaultQuiet() => new()
        {
            ControlMode = 0,
            RampUpSeconds = 2,
            CpuCurve = new int[] { 20, 20, 20, 20, 26, 29, 32, 38, 42, 42, 42, 100, 100 },
            GpuCurve = new int[] { 20, 20, 20, 20, 29, 31, 34, 40, 45, 45, 45, 100, 100 }
        };

        public static ModeFanCurveConfig CreateDefaultEco() => new()
        {
            ControlMode = 0,
            RampUpSeconds = 2,
            CpuCurve = new int[] { 20, 20, 20, 20, 26, 29, 32, 38, 42, 42, 42, 100, 100 },
            GpuCurve = new int[] { 20, 20, 20, 20, 29, 31, 34, 40, 45, 45, 45, 100, 100 }
        };

        public static ModeFanCurveConfig CreateDefaultBalanced() => new()
        {
            ControlMode = 0,
            RampUpSeconds = 2,
            CpuCurve = new int[] { 20, 20, 26, 26, 29, 35, 38, 47, 53, 53, 53, 100, 100 },
            GpuCurve = new int[] { 20, 20, 29, 29, 31, 37, 40, 50, 56, 56, 56, 100, 100 }
        };

        public static ModeFanCurveConfig CreateDefaultPerformance() => new()
        {
            ControlMode = 0,
            RampUpSeconds = 1,
            CpuCurve = new int[] { 20, 20, 29, 35, 38, 47, 57, 64, 64, 64, 64, 100, 100 },
            GpuCurve = new int[] { 20, 20, 31, 37, 40, 50, 61, 68, 68, 68, 68, 100, 100 }
        };

        public static ModeFanCurveConfig CreateDefaultTurbo() => new()
        {
            ControlMode = 1, // Max
            RampUpSeconds = 0,
            CpuCurve = new int[] { 20, 20, 20, 35, 38, 58, 73, 73, 73, 73, 81, 100, 100 },
            GpuCurve = new int[] { 20, 20, 20, 37, 40, 61, 77, 77, 77, 77, 86, 100, 100 }
        };
    }

    public sealed class LightingConfig
    {
        public KeyboardLightingConfig Keyboard { get; set; } = new();
        public CoverLightingConfig Cover { get; set; } = new();
    }

    public sealed class KeyboardLightingConfig
    {
        public int Brightness { get; set; } = 100;
        public int Speed { get; set; } = 50;
        public int BacklightTimeoutSeconds { get; set; } = 30;
        public string SelectedColorHex { get; set; } = "#00E5FF";
        public int SelectedZone { get; set; } = 1;
        public bool SyncZones { get; set; } = false;
        public string Zone1ColorHex { get; set; } = "#00E5FF";
        public string Zone2ColorHex { get; set; } = "#00E5FF";
        public string Zone3ColorHex { get; set; } = "#00E5FF";
        public string Zone4ColorHex { get; set; } = "#FFFFFF";
        public string EffectTag { get; set; } = "HW:0";
        public string PresetTheme { get; set; } = "Predator";
    }

    public sealed class CoverLightingConfig
    {
        public bool Enabled { get; set; } = true;
        public int Mode { get; set; } = 1;
        public string ColorHex { get; set; } = "#00E5FF";
        public int Brightness { get; set; } = 100;
        public int Speed { get; set; } = 50;
        public bool SyncWithKeyboard { get; set; } = false;
    }

    public sealed class OverlayConfig
    {
        /// <summary>Master toggle for hardware overlay.</summary>
        public bool Enabled { get; set; } = false;

        /// <summary>Display FPS counter in overlay.</summary>
        public bool ShowFps { get; set; } = true;

        /// <summary>HUD scale percent (50-200%).</summary>
        public int ScalePercent { get; set; } = 100;

        /// <summary>Background opacity percent (0-100%).</summary>
        public int BackgroundAlphaPercent { get; set; } = 50;

        /// <summary>Text opacity percent (0-100%).</summary>
        public int TextAlphaPercent { get; set; } = 100;

        /// <summary>Horizontal position preset ('Left', 'Center', 'Right').</summary>
        public string PositionHorizontal { get; set; } = "Left";

        /// <summary>Vertical position preset ('Top', 'Center', 'Bottom').</summary>
        public string PositionVertical { get; set; } = "Top";

        /// <summary>Corner anchor index (-1 if unset).</summary>
        public int Anchor { get; set; } = -1;

        /// <summary>Pixel X offset from screen edge.</summary>
        public int OffsetX { get; set; } = 10;

        /// <summary>Pixel Y offset from screen edge.</summary>
        public int OffsetY { get; set; } = 10;

        /// <summary>Dynamic collection of overlay metric slots.</summary>
        public System.Collections.Generic.List<OverlaySlotConfig> Slots { get; set; } = CreateDefaultSlots();

        public static System.Collections.Generic.List<OverlaySlotConfig> CreateDefaultSlots() => new()
        {
            new() { CpuMetric = "Usage", GpuMetric = "Usage", ShowGraph = false },
            new() { CpuMetric = "Temperature", GpuMetric = "Temperature", ShowGraph = false },
            new() { CpuMetric = "Effective Clock", GpuMetric = "Clock", ShowGraph = false },
            new() { CpuMetric = "Voltage", GpuMetric = "Voltage", ShowGraph = false },
            new() { CpuMetric = "Power", GpuMetric = "Power", ShowGraph = true },
            new() { CpuMetric = "Memory", GpuMetric = "Memory", ShowGraph = false },
            new() { CpuMetric = "Memory Speed", GpuMetric = "Memory Speed", ShowGraph = false }
        };
    }

    public sealed class OverlaySlotConfig
    {
        /// <summary>Metric key displayed for CPU row (or 'None').</summary>
        public string CpuMetric { get; set; } = "None";

        /// <summary>Metric key displayed for GPU row (or 'None').</summary>
        public string GpuMetric { get; set; } = "None";

        /// <summary>Whether graph visualization is enabled for this slot.</summary>
        public bool ShowGraph { get; set; } = false;
    }
}
