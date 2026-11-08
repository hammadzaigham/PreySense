using System;
using System.Linq;
using PreySense.Mode;

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

        /// <summary>Display settings including auto refresh rate.</summary>
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
        /// <summary>Automatically lower refresh rate when on battery.</summary>
        public bool AutoRefreshRate { get; set; } = false;
    }

    public sealed class BatteryConfig
    {
        /// <summary>80% battery charge limiter enabled.</summary>
        public bool ChargeLimitEnabled { get; set; } = true;

        /// <summary>Automatically engage Windows Energy Saver when on battery.</summary>
        public bool BatteryEnergySaver { get; set; } = false;
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
        /// <summary>Package TjMax (°C) captured during calibration from MSR 0x1A2.</summary>
        public int? FactoryTjMaxCelsius { get; set; }

        /// <summary>Hardware default power limit in Watts detected via NVML or factory calibration.</summary>
        public int? GpuBasePowerWatts { get; set; }

        /// <summary>Global static CPU PL1 from MSR 0x610 at calibration (RAPL package floor).</summary>
        public int? FactoryStaticCpuPl1Watts { get; set; }

        /// <summary>Global static CPU PL2 from MSR 0x610 at calibration (RAPL package floor).</summary>
        public int? FactoryStaticCpuPl2Watts { get; set; }

        /// <summary>Driver-read factory profiles, saved only after all five modes were captured.</summary>
        public Dictionary<string, ModeTuningConfig> FactoryDefaults { get; set; } = new();

        [System.Text.Json.Serialization.JsonIgnore]
        public bool HasFactoryDefaults => new[] { "Eco", "Quiet", "Balanced", "Performance", "Turbo" }
            .All(name => FactoryDefaults != null && FactoryDefaults.TryGetValue(name, out var profile)
                && profile != null && profile.CpuPl1Watts is >= 5 and <= 300
                && profile.CpuPl2Watts is >= 5 and <= 300 && profile.CpuPl2Watts >= profile.CpuPl1Watts
                && profile.TauSeconds is >= 0 and <= 449
                && profile.GpuDeltaWatts is >= 0 and <= Hardware.DriverFactoryCalibration.MaxGpuDeltaWatts
                && profile.GpuDeltaWatts % 5 == 0
                && profile.GpuDynamicBoostWatts is int db
                && Hardware.DriverFactoryCalibration.IsLegalGpuDynamicBoost(db)
                && profile.PlatformPowerOffsetWatts is >= 0 and <= 100 && profile.PlatformPowerOffsetWatts % 5 == 0);

        [System.Text.Json.Serialization.JsonIgnore]
        public bool HasFactoryStaticCpuLimits =>
            FactoryStaticCpuPl1Watts is >= 5 and <= 300
            && FactoryStaticCpuPl2Watts is >= 5 and <= 300
            && FactoryStaticCpuPl2Watts >= FactoryStaticCpuPl1Watts;

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
        public bool HasCpuLimitValues => CpuPl1Watts.HasValue && CpuPl2Watts.HasValue && TauSeconds.HasValue;

        [System.Text.Json.Serialization.JsonIgnore]
        public bool HasGpuLimitValues => GpuDeltaWatts.HasValue && GpuDynamicBoostWatts.HasValue
            && PlatformPowerOffsetWatts.HasValue;

        /// <summary>True when at least one apply flag is on and that side has complete values.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool HasValues =>
            (ApplyCpuLimits || ApplyGpuLimits)
            && (!ApplyCpuLimits || HasCpuLimitValues)
            && (!ApplyGpuLimits || HasGpuLimitValues);

        /// <summary>Windows PERFBOOSTMODE 0–6 from <see cref="CpuBoostMode"/>, or legacy on/off <see cref="CpuBoost"/>.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public int? EffectiveCpuBoostMode =>
            CpuBoostMode is >= 0 and <= 6 ? CpuBoostMode
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

        /// <summary>Per-mode Windows PERFBOOSTMODE (0 Disabled … 6 Efficient Aggressive At Guaranteed).</summary>
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
    }

    public sealed class FanConfig
    {
        public ModeFanCurveConfig Quiet { get; set; } = ModeFanCurveConfig.CreateDefaultQuiet();
        public ModeFanCurveConfig Eco { get; set; } = ModeFanCurveConfig.CreateDefaultEco();
        public ModeFanCurveConfig Balanced { get; set; } = ModeFanCurveConfig.CreateDefaultBalanced();
        public ModeFanCurveConfig Performance { get; set; } = ModeFanCurveConfig.CreateDefaultPerformance();
        public ModeFanCurveConfig Turbo { get; set; } = ModeFanCurveConfig.CreateDefaultTurbo();
    }

    /// <summary>Persisted fan stage: separate CPU/GPU trip °C and duties. No fall/RPM fields.</summary>
    public sealed class FanStageConfig
    {
        public int? CpuTempC { get; set; }
        public int? GpuTempC { get; set; }
        public int? CpuDutyPercent { get; set; }
        public int? GpuDutyPercent { get; set; }

        // Legacy fields — read for migration only; omitted when writing defaults (0).
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
        public int? DutyPercent { get; set; }
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
        public int CpuRiseC { get; set; }
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
        public int CpuFallC { get; set; }
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
        public int GpuRiseC { get; set; }
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
        public int GpuFallC { get; set; }
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
        public int CpuTargetRpm { get; set; }
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
        public int GpuTargetRpm { get; set; }

        public FanStageConfig() { }

        public FanStageConfig(Mode.FanStage stage)
        {
            CpuTempC = stage.CpuRiseC;
            GpuTempC = stage.GpuRiseC;
            CpuDutyPercent = stage.CpuDutyPercent;
            GpuDutyPercent = stage.GpuDutyPercent;
        }

        public Mode.FanStage ToFanStage()
        {
            int cpuTemp = CpuTempC ?? (CpuRiseC != 0 ? CpuRiseC : Mode.FanStage.DisabledTripCelsius);
            int gpuTemp = GpuTempC ?? (GpuRiseC != 0 ? GpuRiseC : Mode.FanStage.DisabledTripCelsius);
            int cpuDuty = CpuDutyPercent
                ?? DutyPercent
                ?? Mode.FanStage.RpmToDuty(CpuTargetRpm, Mode.FanStage.CpuMaxRpm);
            int gpuDuty = GpuDutyPercent
                ?? DutyPercent
                ?? Mode.FanStage.RpmToDuty(GpuTargetRpm, Mode.FanStage.GpuMaxRpm);

            var stage = new Mode.FanStage { CpuRiseC = cpuTemp, GpuRiseC = gpuTemp };
            stage.SetDuty(gpu: false, cpuDuty);
            stage.SetDuty(gpu: true, gpuDuty);
            stage.Normalize();
            return stage;
        }
    }

    public sealed class ModeFanCurveConfig
    {
        /// <summary>Fan control mode: 0 = Auto, 1 = Max, 2 = Custom.</summary>
        public int ControlMode { get; set; } = 0;

        /// <summary>Shared delay before accepting a stage change (0-5s).</summary>
        public int RampUpSeconds { get; set; } = 2;

        /// <summary>EC-style stages (12): CpuTempC, GpuTempC, CpuDutyPercent, GpuDutyPercent.</summary>
        public FanStageConfig[]? Stages { get; set; }

        public static ModeFanCurveConfig CreateDefaultQuiet() => FromProfile(FanCurveProfile.CreateDefault(0));
        public static ModeFanCurveConfig CreateDefaultEco() => FromProfile(FanCurveProfile.CreateDefault(6));
        public static ModeFanCurveConfig CreateDefaultBalanced() => FromProfile(FanCurveProfile.CreateDefault(1));
        public static ModeFanCurveConfig CreateDefaultPerformance() => FromProfile(FanCurveProfile.CreateDefault(4));
        public static ModeFanCurveConfig CreateDefaultTurbo() => FromProfile(FanCurveProfile.CreateDefault(5));

        private static ModeFanCurveConfig FromProfile(FanCurveProfile profile) => new()
        {
            ControlMode = (int)profile.ControlMode,
            RampUpSeconds = profile.RampUpSeconds,
            Stages = profile.Stages.Select(s => new FanStageConfig(s)).ToArray()
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
