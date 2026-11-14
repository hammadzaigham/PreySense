using System;
using System.Linq;
using System.Text.Json.Serialization;
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
        /// <summary>Preserves unrecognized settings when a newer file is edited by this build.</summary>
        [JsonExtensionData]
        public Dictionary<string, System.Text.Json.JsonElement>? AdditionalSettings { get; set; }

        public const int CurrentSchemaVersion = 2;

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

        /// <summary>Synchronize CPU and GPU fan speeds across custom curve stages / manual controls.</summary>
        [JsonIgnore]
        public bool SynchronizeFanSpeeds
        {
            get => Fans.SynchronizeFanSpeeds;
            set => Fans.SynchronizeFanSpeeds = value;
        }

        /// <summary>RGB Keyboard and Cover lighting configuration.</summary>
        public LightingConfig Lighting { get; set; } = new();

        /// <summary>On-Screen Display (OSD) overlay layout and metric preferences.</summary>
        public OverlayConfig Overlay { get; set; } = new();

        /// <summary>Opt-in game detection and executable-to-operating-mode profiles.</summary>
        public GamesConfig Games { get; set; } = new();
    }

    public sealed class GamesConfig
    {
        public bool Enabled { get; set; } = false;
        public List<GameProfile> Profiles { get; set; } = new();
    }

    [JsonConverter(typeof(JsonStringEnumConverter<ApplicationTheme>))]
    public enum ApplicationTheme { Dark, Light, Glass }

    public sealed class GeneralConfig
    {
        /// <summary>UI Language code (e.g., 'en-US', 'zh-CN', 'hi-IN', 'es-ES', 'fr-FR', 'tr-TR').</summary>
        public string Language { get; set; } = "en-US";

        /// <summary>True for light theme, false for dark theme.</summary>
        public bool LightTheme { get; set; } = false;

        /// <summary>Selected appearance. Null retains a legacy light preference; otherwise the default is Dark.</summary>
        public ApplicationTheme? Theme { get; set; }

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
        /// <summary>Power-off USB charging battery cutoff: 0 disables; 10, 20 or 30 percent.</summary>
        public int UsbPowerOffChargingCutoffPercent { get; set; } = 0;

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
        /// <summary>Preserves unrecognized settings when a newer file is edited by this build.</summary>
        [JsonExtensionData]
        public Dictionary<string, System.Text.Json.JsonElement>? AdditionalSettings { get; set; }

        [JsonPropertyOrder(0)]
        public bool ApplyProchotOffset { get; set; }
        [JsonPropertyOrder(1)]
        public int? ProchotOffsetCelsius { get; set; }

        /// <summary>Master toggle: Enable CPU Power Limit Control across all performance modes.</summary>
        [JsonPropertyOrder(2)]
        public bool EnableCpuPowerLimitControl { get; set; } = true;

        /// <summary>Master toggle: Enable GPU Power Limit Control across all performance modes.</summary>
        [JsonPropertyOrder(3)]
        public bool EnableGpuPowerLimitControl { get; set; } = true;

        /// <summary>Master toggle: Enable GPU Overclocking across all performance modes.</summary>
        [JsonPropertyOrder(4)]
        public bool EnableGpuOverclockingControl { get; set; } = true;

        /// <summary>Apply each selected mode's CPU boost policy on every mode change.</summary>
        public bool EnableCpuBoostTuningControl { get; set; } = true;
        [JsonPropertyOrder(9)]
        public int? FactoryProchotOffsetCelsius { get; set; }
        [JsonPropertyOrder(10)]
        public bool? FactoryProchotOffsetLocked { get; set; }
        /// <summary>Package TjMax (°C) captured during calibration from MSR 0x1A2.</summary>
        [JsonPropertyOrder(11)]
        public int? FactoryTjMaxCelsius { get; set; }

        /// <summary>Hardware default power limit in Watts detected via NVML or factory calibration.</summary>
        [JsonPropertyOrder(8)]
        public int? GpuBasePowerWatts { get; set; }

        /// <summary>Global static CPU PL1 from MSR 0x610 at calibration (RAPL package floor).</summary>
        [JsonPropertyOrder(12)]
        public int? FactoryStaticCpuPl1Watts { get; set; }

        /// <summary>Global static CPU PL2 from MSR 0x610 at calibration (RAPL package floor).</summary>
        [JsonPropertyOrder(13)]
        public int? FactoryStaticCpuPl2Watts { get; set; }

        /// <summary>Driver-read factory profiles, saved only after all five modes were captured.</summary>
        [JsonPropertyOrder(14)]
        public Dictionary<string, ModeTuningConfig> FactoryDefaults { get; set; } = new();

        /// <summary>
        /// True when all five modes have valid CPU factory limits. GPU ERM4 fields may be
        /// absent on SKUs without an Acer profile table (CPU/PROCHOT tuning still works).
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool HasFactoryDefaults => new[] { "Eco", "Quiet", "Balanced", "Performance", "Turbo" }
            .All(name => FactoryDefaults != null && FactoryDefaults.TryGetValue(name, out var profile)
                && profile != null && HasValidFactoryCpu(profile));

        [JsonIgnore]
        public bool HasFactoryGpuProfiles => HasFactoryDefaults && FactoryDefaults.Values.All(p => p.HasGpuLimitValues);

        private static bool HasValidFactoryCpu(ModeTuningConfig profile) =>
            profile.CpuPl1Watts is >= 5 and <= 300
            && profile.CpuPl2Watts is >= 5 and <= 300 && profile.CpuPl2Watts >= profile.CpuPl1Watts
            && profile.TauSeconds is >= 0 and <= 449;

        [System.Text.Json.Serialization.JsonIgnore]
        public bool HasFactoryStaticCpuLimits =>
            FactoryStaticCpuPl1Watts is >= 5 and <= 300
            && FactoryStaticCpuPl2Watts is >= 5 and <= 300
            && FactoryStaticCpuPl2Watts >= FactoryStaticCpuPl1Watts;

        [JsonPropertyOrder(2)]
        public ModeTuningConfig Quiet { get; set; } = new();

        [JsonPropertyOrder(3)]
        public ModeTuningConfig Eco { get; set; } = new();

        [JsonPropertyOrder(4)]
        public ModeTuningConfig Balanced { get; set; } = new();

        [JsonPropertyOrder(5)]
        public ModeTuningConfig Performance { get; set; } = new();

        [JsonPropertyOrder(6)]
        // Acer Predator service stock Turbo OC (+100 / +200); apply stays off until the user enables it.
        public ModeTuningConfig Turbo { get; set; } = new()
        {
            GpuCoreClockOffsetMhz = 100,
            GpuMemoryClockOffsetMhz = 200
        };
    }

    public sealed class ModeTuningConfig
    {
        /// <summary>Preserves unrecognized settings when a newer file is edited by this build.</summary>
        [JsonExtensionData]
        public Dictionary<string, System.Text.Json.JsonElement>? AdditionalSettings { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public bool HasCpuLimitValues => CpuPl1Watts.HasValue && CpuPl2Watts.HasValue && TauSeconds.HasValue;

        [JsonIgnore]
        public bool HasGpuLimitValues => GpuDeltaWatts.HasValue && GpuDynamicBoostWatts.HasValue && PlatformPowerOffsetWatts.HasValue;

        /// <summary>True when at least one apply flag or custom GPU target is active.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool HasValues =>
            (ApplyCpuLimits || ApplyGpuLimits || GpuRuntimeLimitWatts.HasValue || ApplyGpuClockOffsets)
            && (!ApplyCpuLimits || HasCpuLimitValues)
            && (!ApplyGpuLimits || HasGpuLimitValues)
            && (!ApplyGpuClockOffsets || HasGpuClockValues);

        /// <summary>Windows PERFBOOSTMODE 0–6 from <see cref="CpuBoostMode"/>, or legacy on/off <see cref="CpuBoost"/>.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public int? EffectiveCpuBoostMode =>
            CpuBoostMode is >= 0 and <= 6 ? CpuBoostMode
            : CpuBoost == true ? 1
            : CpuBoost == false ? 0
            : null;

        /// <summary>CPU Long-term power limit (PL1) in Watts.</summary>
        [JsonPropertyOrder(1)]
        public int? CpuPl1Watts { get; set; }

        /// <summary>CPU Short-term burst power limit (PL2) in Watts.</summary>
        [JsonPropertyOrder(2)]
        public int? CpuPl2Watts { get; set; }

        /// <summary>CPU Tau burst duration in seconds (0-449).</summary>
        [JsonPropertyName("CpuTurboTimeLimitSeconds")]
        [JsonPropertyOrder(3)]
        public int? TauSeconds { get; set; }

        /// <summary>Legacy on/off boost flag. Prefer <see cref="CpuBoostMode"/>.</summary>
        [JsonPropertyOrder(5)]
        public bool? CpuBoost { get; set; }

        /// <summary>Per-mode Windows PERFBOOSTMODE (0 Disabled … 6 Efficient Aggressive At Guaranteed).</summary>
        [JsonPropertyOrder(4)]
        public int? CpuBoostMode { get; set; }

        /// <summary>Nominal GPU power reference from a verified baseline and offsets; not an enforced limit.</summary>
        [JsonPropertyName("GpuNominalPowerWatts")]
        [JsonPropertyOrder(10)]
        public int? GpuWatts { get; set; }

        /// <summary>Apply CPU limits flag for this profile.</summary>
        [JsonPropertyOrder(0)]
        public bool ApplyCpuLimits { get; set; } = false;

        /// <summary>Apply the selected Windows CPU boost policy independently of power limits.</summary>
        public bool ApplyCpuBoost { get; set; }

        /// <summary>Apply the independent ERM4 GPU offsets for this mode.</summary>
        public bool ApplyGpuLimits { get; set; }

        /// <summary>
        /// GPU target in Watts: null uses the factory profile; custom targets are staged through Acer ERM4.
        /// </summary>
        [JsonPropertyOrder(6)]
        public int? GpuRuntimeLimitWatts { get; set; }

        [JsonPropertyName("GpuPowerOffsetWatts")]
        [JsonPropertyOrder(7)]
        public int? GpuDeltaWatts { get; set; }
        [JsonPropertyOrder(8)]
        public int? GpuDynamicBoostWatts { get; set; }
        [JsonPropertyOrder(9)]
        public int? PlatformPowerOffsetWatts { get; set; }

        [JsonPropertyOrder(11)]
        public bool ApplyGpuClockOffsets { get; set; }

        [JsonPropertyOrder(12)]
        public int? GpuCoreClockOffsetMhz { get; set; }

        [JsonPropertyOrder(13)]
        public int? GpuMemoryClockOffsetMhz { get; set; }

        [JsonIgnore]
        public bool HasGpuClockValues => GpuCoreClockOffsetMhz.HasValue && GpuMemoryClockOffsetMhz.HasValue;
    }

    public sealed class FanConfig
    {
        /// <summary>Synchronize CPU and GPU fan speeds across custom curve stages / manual controls.</summary>
        [JsonPropertyOrder(1)]
        public bool SynchronizeFanSpeeds { get; set; } = true;

        /// <summary>Delay before accepting custom curve stage changes, shared by all modes and both fans.</summary>
        [JsonPropertyOrder(9)]
        public int ControlDelaySeconds { get; set; } = 0;

        /// <summary>CPU fan RPM corresponding to 100% duty; configurable per laptop.</summary>
        [JsonPropertyOrder(7)]
        public int CpuMaxRpm { get; set; } = 6400;

        /// <summary>GPU fan RPM corresponding to 100% duty; configurable per laptop.</summary>
        [JsonPropertyOrder(8)]
        public int GpuMaxRpm { get; set; } = 6400;

        [JsonPropertyOrder(2)]
        public ModeFanCurveConfig Quiet { get; set; } = ModeFanCurveConfig.CreateDefaultQuiet();
        [JsonPropertyOrder(3)]
        public ModeFanCurveConfig Eco { get; set; } = ModeFanCurveConfig.CreateDefaultEco();
        [JsonPropertyOrder(4)]
        public ModeFanCurveConfig Balanced { get; set; } = ModeFanCurveConfig.CreateDefaultBalanced();
        [JsonPropertyOrder(5)]
        public ModeFanCurveConfig Performance { get; set; } = ModeFanCurveConfig.CreateDefaultPerformance();
        [JsonPropertyOrder(6)]
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

        public Mode.FanStage ToFanStage(int cpuMaxRpm = Mode.FanStage.CpuMaxRpm, int gpuMaxRpm = Mode.FanStage.GpuMaxRpm)
        {
            cpuMaxRpm = Math.Clamp(cpuMaxRpm, 1, ushort.MaxValue);
            gpuMaxRpm = Math.Clamp(gpuMaxRpm, 1, ushort.MaxValue);
            int cpuTemp = CpuTempC ?? (CpuRiseC != 0 ? CpuRiseC : Mode.FanStage.DisabledTripCelsius);
            int gpuTemp = GpuTempC ?? (GpuRiseC != 0 ? GpuRiseC : Mode.FanStage.DisabledTripCelsius);
            int cpuDuty = CpuDutyPercent
                ?? DutyPercent
                ?? Mode.FanStage.RpmToDuty(CpuTargetRpm, cpuMaxRpm);
            int gpuDuty = GpuDutyPercent
                ?? DutyPercent
                ?? Mode.FanStage.RpmToDuty(GpuTargetRpm, gpuMaxRpm);

            var stage = new Mode.FanStage
            {
                CpuRiseC = cpuTemp, GpuRiseC = gpuTemp,
                CpuMaximumRpm = cpuMaxRpm, GpuMaximumRpm = gpuMaxRpm
            };
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

        /// <summary>Legacy per-mode value retained for file compatibility; runtime uses Fans.ControlDelaySeconds.</summary>
        public int RampUpSeconds { get; set; } = 2;

        private FanStageConfig[]? _stages;

        /// <summary>Reachable stages, including the terminal plateau; unused EC slots are omitted.</summary>
        public FanStageConfig[]? Stages
        {
            get => _stages;
            set
            {
                if (value is not { Length: > 0 }) { _stages = value; return; }
                int count = Math.Min(value.Length, FanCurveProfile.StageCount);
                for (int i = 0; i < count - 1; i++)
                {
                    var stage = value[i]?.ToFanStage();
                    if (stage is not null && !stage.HasActiveRise(false) && !stage.HasActiveRise(true))
                    {
                        count = i + 1;
                        break;
                    }
                }
                _stages = value.Length == count ? value : value[..count];
            }
        }

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
        public int ModeBrightness { get; set; } = 100;
        /// <summary>Shared color overrides for Mode lighting on every device; hex operating-mode keys.</summary>
        public Dictionary<string, string> PowerModeColors { get; set; } = new();
        public KeyboardLightingConfig Keyboard { get; set; } = new();
        public CoverLightingConfig Cover { get; set; } = new();
        public Dictionary<string, DeviceLightingConfig> Devices { get; set; } = new();
    }

    /// <summary>Explicit settings for a detected device; discovery never creates profiles.</summary>
    public sealed class DeviceLightingConfig
    {
        public int Mode { get; set; } = PreySense.Rgb.ModeKeyLighting.FollowOperatingMode;
        public int Brightness { get; set; } = 100;
        public int Speed { get; set; } = 50;
        /// <summary>Exact native animation level selected in the UI; null uses the existing normalized Speed.</summary>
        public int? AnimationSpeedLevel { get; set; }
        /// <summary>Native effect direction selected from the device's advertised options.</summary>
        public int? Direction { get; set; }
        public string ColorHex { get; set; } = "#00AEC7";
        public bool IndividualColors { get; set; }
        public bool SyncLeds { get; set; }
        public bool RandomColors { get; set; }
        public Dictionary<int, string> LedColors { get; set; } = new();
        /// <summary>Per Acer operating-mode colors for Mode Key "Mode" lighting (keys are hex mode bytes, e.g. "05").</summary>
        public Dictionary<string, string> PowerModeColors { get; set; } = new();
        /// <summary>When true (Mode Key / Turbo Key), follow the keyboard color and brightness.</summary>
        public bool SyncWithKeyboard { get; set; }
    }

    public sealed class KeyboardLightingConfig
    {
        public int? Direction { get; set; }
        public Dictionary<string, string> PowerModeColors { get; set; } = new();
        public int Brightness { get; set; } = 100;
        public int Speed { get; set; } = 50;
        public int BacklightTimeoutSeconds { get; set; } = 30;
        public string SelectedColorHex { get; set; } = "#00AEC7";
        public int SelectedZone { get; set; } = 1;
        public bool SyncZones { get; set; } = false;
        public string Zone1ColorHex { get; set; } = "#00AEC7";
        public string Zone2ColorHex { get; set; } = "#00AEC7";
        public string Zone3ColorHex { get; set; } = "#00E5FF";
        public string Zone4ColorHex { get; set; } = "#00E5FF";
        public string EffectTag { get; set; } = "MODE";
        public string PresetTheme { get; set; } = "Mode";
    }

    public sealed class CoverLightingConfig
    {
        public bool Enabled { get; set; } = true;
        public int Mode { get; set; } = 4;
        public string ColorHex { get; set; } = "#00AEC7";
        public int Brightness { get; set; } = 100;
        public int Speed { get; set; } = 50;
        public bool SyncWithKeyboard { get; set; } = false;
    }

    [JsonConverter(typeof(JsonStringEnumConverter<OverlayTheme>))]
    public enum OverlayTheme
    {
        Dark = 0,
        White = 1
    }

    public sealed class OverlayConfig
    {
        public OverlayTheme Theme { get; set; } = OverlayTheme.Dark;

        /// <summary>Apply an appearance preset without changing metrics, layout, or polling.</summary>
        public void ApplyTheme(OverlayTheme theme)
        {
            if (!Enum.IsDefined(theme)) throw new ArgumentOutOfRangeException(nameof(theme));
            Theme = theme;
            BackgroundColorHex = theme == OverlayTheme.Dark ? "#000000" : "#F5F5F7";
            CpuTextColorHex = GpuTextColorHex = theme == OverlayTheme.Dark ? "#F5F5F7" : "#17191C";
            BackgroundAlphaPercent = 100;
            TextAlphaPercent = 100;
            CpuGraphColorHex = "#2C7CB4";
            GpuGraphColorHex = theme == OverlayTheme.Dark ? "#3AAA54" : "#218739";
            ShowBackgroundShading = false;
        }

        /// <summary>Master toggle for hardware overlay.</summary>
        public bool Enabled { get; set; } = false;

        /// <summary>Display FPS counter in overlay.</summary>
        public bool ShowFps { get; set; } = true;

        /// <summary>HUD scale percent (50-200%).</summary>
        public int ScalePercent { get; set; } = 100;

        /// <summary>Background opacity percent (0-100%).</summary>
        [JsonPropertyName("BackgroundOpacityPercent")]
        public int BackgroundAlphaPercent { get; set; } = 100;

        /// <summary>Text opacity percent (0-100%).</summary>
        [JsonPropertyName("TextOpacityPercent")]
        public int TextAlphaPercent { get; set; } = 100;

        // Advanced appearance settings are intentionally JSON-only.
        // Defaults match OverlayTheme.Dark (ApplyTheme) so a fresh config is consistent.
        public string BackgroundColorHex { get; set; } = "#000000";
        public string CpuTextColorHex { get; set; } = "#F5F5F7";
        public string GpuTextColorHex { get; set; } = "#F5F5F7";
        public string CpuGraphColorHex { get; set; } = "#2C7CB4";
        public string GpuGraphColorHex { get; set; } = "#3AAA54";
        /// <summary>Corner radius in base overlay pixels, scaled with ScalePercent.</summary>
        public int CornerRadiusPixels { get; set; } = 6;
        /// <summary>Graph stroke width in screen pixels.</summary>
        public float GraphLineWidthPixels { get; set; } = 1.5f;
        public bool ShowBackgroundShading { get; set; } = false;

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
