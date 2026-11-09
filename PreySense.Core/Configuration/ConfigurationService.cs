using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PreySense.Core.Configuration
{
    /// <summary>
    /// Thread-safe configuration service providing typed access, atomic saves,
    /// validation, and isolated file storage strictly in `preysense.json` in the current working directory.
    /// </summary>
    public sealed class ConfigurationService
    {
        public const string ConfigFileName = "preysense.json";

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true,
            IndentSize = 2,
            NewLine = "\n",
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

        private static readonly Lazy<ConfigurationService> _instance = new(() => new ConfigurationService());
        public static ConfigurationService Instance => _instance.Value;

        private readonly object _gate = new();
        private readonly string _configFilePath;
        private PreySenseConfig _currentConfig;

        public string ConfigPath => _configFilePath;

        public ConfigurationService(string? customPath = null)
        {
            _configFilePath = !string.IsNullOrWhiteSpace(customPath)
                ? customPath
                : Path.Combine(Directory.GetCurrentDirectory(), ConfigFileName);

            _currentConfig = LoadOrInitialize();
        }

        /// <summary>
        /// Gets a clone of the currently loaded configuration.
        /// Changes to the returned object do not affect the active configuration
        /// until persisted via <see cref="Save(PreySenseConfig)"/> or <see cref="Update(Action{PreySenseConfig})"/>.
        /// </summary>
        public PreySenseConfig GetCurrent()
        {
            lock (_gate)
            {
                return Clone(_currentConfig);
            }
        }

        /// <summary>
        /// Reloads configuration strictly from disk. If the file does not exist,
        /// returns default configuration. Malformed JSON throws an InvalidOperationException with details.
        /// </summary>
        public PreySenseConfig Reload()
        {
            lock (_gate)
            {
                _currentConfig = LoadOrInitialize();
                return Clone(_currentConfig);
            }
        }

        /// <summary>
        /// Validates and saves the entire configuration atomically.
        /// Writes to a temporary file in the same directory, then replaces `preysense.json`.
        /// </summary>
        public void Save(PreySenseConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            lock (_gate)
            {
                Validate(config);

                string dir = Path.GetDirectoryName(_configFilePath) ?? Directory.GetCurrentDirectory();
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string tempFile = Path.Combine(dir, $"{ConfigFileName}.{Guid.NewGuid():N}.tmp");
                string json = SerializeForFile(config);

                try
                {
                    File.WriteAllText(tempFile, json);

                    if (File.Exists(_configFilePath))
                    {
                        File.Replace(tempFile, _configFilePath, null);
                    }
                    else
                    {
                        File.Move(tempFile, _configFilePath);
                    }

                    _currentConfig = Clone(config);
                }
                finally
                {
                    if (File.Exists(tempFile))
                    {
                        try { File.Delete(tempFile); } catch { }
                    }
                }
            }
        }

        /// <summary>
        /// Mutates the active configuration in a safe transaction, preserving unrelated sections,
        /// validates, and persists atomically.
        /// </summary>
        public void Update(Action<PreySenseConfig> mutator)
        {
            if (mutator == null) throw new ArgumentNullException(nameof(mutator));

            lock (_gate)
            {
                var draft = Clone(_currentConfig);
                mutator(draft);
                Save(draft);
            }
        }

        /// <summary>
        /// Restores documented defaults and persists to disk.
        /// </summary>
        public PreySenseConfig ResetToDefaults()
        {
            lock (_gate)
            {
                var defaults = new PreySenseConfig();
                Save(defaults);
                return defaults;
            }
        }

        /// <summary>
        /// Validates values against safe operational boundaries.
        /// Throws ArgumentOutOfRangeException or FormatException on violation.
        /// </summary>
        public static void Validate(PreySenseConfig config)
        {
            if (config.SchemaVersion != PreySenseConfig.CurrentSchemaVersion)
                throw new ArgumentOutOfRangeException(nameof(config.SchemaVersion),
                    $"This build requires SchemaVersion {PreySenseConfig.CurrentSchemaVersion}; older and newer configuration files are not migrated.");

            if (config.Tuning.ProchotOffsetCelsius is < 0 or > 63 || config.Tuning.FactoryProchotOffsetCelsius is < 0 or > 63)
                throw new ArgumentOutOfRangeException("Tuning.ProchotOffsetCelsius", "PROCHOT offset must be between 0 and 63°C.");
            if (config.Tuning.ApplyProchotOffset && config.Tuning.ProchotOffsetCelsius is null)
                throw new ArgumentException("Enter a PROCHOT offset, or turn its switch off.");

            // Tuning validation
            ValidateModeTuning("Quiet", config.Tuning.Quiet);
            ValidateModeTuning("Eco", config.Tuning.Eco);
            ValidateModeTuning("Balanced", config.Tuning.Balanced);
            ValidateModeTuning("Performance", config.Tuning.Performance);
            ValidateModeTuning("Turbo", config.Tuning.Turbo);
            config.Tuning.FactoryDefaults ??= new();
            if (config.Tuning.FactoryDefaults is { Count: > 0 })
            {
                foreach (var entry in config.Tuning.FactoryDefaults)
                    NormalizeModeTuning(entry.Value);
                if (!config.Tuning.HasFactoryDefaults)
                    throw new ArgumentException("Factory defaults are incomplete for one or more modes.");
                foreach (var entry in config.Tuning.FactoryDefaults)
                    ValidateModeTuning("FactoryDefaults." + entry.Key, entry.Value);
            }
            if (config.Tuning.FactoryStaticCpuPl1Watts is not null || config.Tuning.FactoryStaticCpuPl2Watts is not null)
            {
                if (!config.Tuning.HasFactoryStaticCpuLimits)
                    throw new ArgumentException("FactoryStaticCpuPl1Watts/FactoryStaticCpuPl2Watts must both be valid PL1/PL2 watts.");
            }

            if (config.Battery.UsbPowerOffChargingCutoffPercent is not (0 or 10 or 20 or 30))
                throw new ArgumentOutOfRangeException(nameof(config.Battery.UsbPowerOffChargingCutoffPercent), "USB power-off charging cutoff must be 0, 10, 20 or 30 percent.");

            // Session validation
            if (config.Session.ActivePowerMode is not (0 or 1 or 4 or 5 or 6))
                throw new ArgumentOutOfRangeException(nameof(config.Session.ActivePowerMode), "ActivePowerMode must be 0, 1, 4, 5, or 6.");

            if (config.Session.GpuModePreference is < 0 or > 2)
                throw new ArgumentOutOfRangeException(nameof(config.Session.GpuModePreference), "GpuModePreference must be 0, 1, or 2.");

            // Overlay validation
            if (config.Overlay.ScalePercent is < 50 or > 200)
                throw new ArgumentOutOfRangeException(nameof(config.Overlay.ScalePercent), "Overlay scale percent must be between 50 and 200.");

            if (config.Overlay.BackgroundAlphaPercent is < 0 or > 100)
                throw new ArgumentOutOfRangeException(nameof(config.Overlay.BackgroundAlphaPercent), "Overlay background alpha must be between 0 and 100.");

            if (config.Overlay.TextAlphaPercent is < 0 or > 100)
                throw new ArgumentOutOfRangeException(nameof(config.Overlay.TextAlphaPercent), "Overlay text alpha must be between 0 and 100.");

            foreach (string color in new[] { config.Overlay.BackgroundColorHex, config.Overlay.CpuTextColorHex,
                config.Overlay.GpuTextColorHex, config.Overlay.CpuGraphColorHex, config.Overlay.GpuGraphColorHex })
                if (color == null || color.Length != 7 || color[0] != '#' ||
                    !uint.TryParse(color.AsSpan(1), System.Globalization.NumberStyles.AllowHexSpecifier,
                        System.Globalization.CultureInfo.InvariantCulture, out _))
                    throw new ArgumentException("Overlay colors must use #RRGGBB hexadecimal notation.");
            if (config.Overlay.CornerRadiusPixels is < 0 or > 16)
                throw new ArgumentOutOfRangeException(nameof(config.Overlay.CornerRadiusPixels), "Overlay corner radius must be 0-16 base pixels.");
            if (!float.IsFinite(config.Overlay.GraphLineWidthPixels) || config.Overlay.GraphLineWidthPixels is < 0.5f or > 4f)
                throw new ArgumentOutOfRangeException(nameof(config.Overlay.GraphLineWidthPixels), "Overlay graph line width must be 0.5-4 screen pixels.");

            if (config.Overlay.Slots == null)
                config.Overlay.Slots = new System.Collections.Generic.List<OverlaySlotConfig>();

            foreach (var slot in config.Overlay.Slots)
            {
                if (slot == null) throw new ArgumentNullException(nameof(slot), "Overlay slot cannot be null.");
                slot.CpuMetric = PreySense.Core.Services.TelemetrySelection.NormalizeCpu(slot.CpuMetric);
                slot.GpuMetric = PreySense.Core.Services.TelemetrySelection.NormalizeGpu(slot.GpuMetric);
            }

            // Lighting validation
            if (config.Lighting.Keyboard.Brightness is < 0 or > 100)
                throw new ArgumentOutOfRangeException(nameof(config.Lighting.Keyboard.Brightness), "Keyboard brightness must be 0-100.");

            if (config.Lighting.Keyboard.Speed is < 0 or > 100)
                throw new ArgumentOutOfRangeException(nameof(config.Lighting.Keyboard.Speed), "Keyboard speed must be 0-100.");

            if (config.Lighting.Keyboard.SelectedZone is < 1 or > 4)
                throw new ArgumentOutOfRangeException(nameof(config.Lighting.Keyboard.SelectedZone), "Selected zone must be 1-4.");

            if (config.Lighting.Cover.Brightness is < 0 or > 100)
                throw new ArgumentOutOfRangeException(nameof(config.Lighting.Cover.Brightness), "Cover brightness must be 0-100.");

            if (config.Lighting.Cover.Speed is < 0 or > 100)
                throw new ArgumentOutOfRangeException(nameof(config.Lighting.Cover.Speed), "Cover speed must be 0-100.");
        }

        private static void ValidateModeTuning(string modeName, ModeTuningConfig mode)
        {
            NormalizeModeTuning(mode);

            if (mode.CpuPl1Watts is < 5 or > 300)
                throw new ArgumentOutOfRangeException($"Tuning.{modeName}.CpuPl1Watts", $"PL1 for {modeName} must be between 5W and 300W.");

            if (mode.CpuPl2Watts is < 5 or > 300)
                throw new ArgumentOutOfRangeException($"Tuning.{modeName}.CpuPl2Watts", $"PL2 for {modeName} must be between 5W and 300W.");

            if (mode.CpuPl1Watts.HasValue && mode.CpuPl2Watts.HasValue && mode.CpuPl2Watts < mode.CpuPl1Watts)
                throw new ArgumentOutOfRangeException($"Tuning.{modeName}.CpuPl2Watts", $"PL2 for {modeName} must be greater than or equal to PL1.");

            if (mode.TauSeconds is < 0 or > 449)
                throw new ArgumentOutOfRangeException($"Tuning.{modeName}.TauSeconds", $"Tau for {modeName} must be between 0s and 449s.");

            if (mode.CpuBoostMode is < 0 or > 6)
                throw new ArgumentOutOfRangeException($"Tuning.{modeName}.CpuBoostMode", $"CpuBoostMode for {modeName} must be between 0 and 6.");

            if (mode.GpuWatts is < 1 or > 999)
                throw new ArgumentOutOfRangeException($"Tuning.{modeName}.GpuWatts", $"GPU target watts for {modeName} must be between 1W and 999W.");

            if (mode.GpuDeltaWatts is < 0
                || mode.GpuDeltaWatts > Hardware.DriverFactoryCalibration.MaxGpuDeltaWatts
                || (mode.GpuDeltaWatts is int delta && delta % 5 != 0))
                throw new ArgumentOutOfRangeException($"Tuning.{modeName}.GpuDeltaWatts",
                    $"GPU delta for {modeName} must be 0–{Hardware.DriverFactoryCalibration.MaxGpuDeltaWatts}W in steps of 5.");

            if (mode.GpuDynamicBoostWatts is int db && !Hardware.DriverFactoryCalibration.IsLegalGpuDynamicBoost(db))
                throw new ArgumentOutOfRangeException($"Tuning.{modeName}.GpuDynamicBoostWatts",
                    $"GPU dynamic boost for {modeName} must be 0–{Hardware.DriverFactoryCalibration.MaxGpuDynamicBoostWatts}W in steps of 5.");

            if (mode.PlatformPowerOffsetWatts is < 0 or > 100 || (mode.PlatformPowerOffsetWatts is int platform && platform % 5 != 0))
                throw new ArgumentOutOfRangeException($"Tuning.{modeName}.PlatformPowerOffsetWatts", $"Platform power offset for {modeName} must be 0–100W in steps of 5.");
        }

        /// <summary>Promotes legacy boost bools and snaps factory GPU offsets to apply-legal steps.</summary>
        private static void NormalizeModeTuning(ModeTuningConfig mode)
        {
            if (mode.CpuBoostMode is null && mode.CpuBoost is bool legacyBoost)
                mode.CpuBoostMode = legacyBoost ? 1 : 0;
            if (mode.CpuBoostMode is int boost)
            {
                mode.CpuBoostMode = Math.Clamp(boost, 0, 6);
                mode.CpuBoost = mode.CpuBoostMode > 0;
            }

            if (mode.GpuDeltaWatts is int rawDelta)
                mode.GpuDeltaWatts = Hardware.DriverFactoryCalibration.SnapGpuDelta(rawDelta);
            if (mode.GpuDynamicBoostWatts is int rawDb)
                mode.GpuDynamicBoostWatts = Hardware.DriverFactoryCalibration.SnapGpuDynamicBoost(rawDb);
            if (mode.PlatformPowerOffsetWatts is int rawPlatform)
                mode.PlatformPowerOffsetWatts = Hardware.DriverFactoryCalibration.SnapPlatformPowerOffset(rawPlatform);
        }

        private PreySenseConfig LoadOrInitialize()
        {
            if (!File.Exists(_configFilePath))
            {
                var fresh = new PreySenseConfig();
                return fresh;
            }

            try
            {
                string json = File.ReadAllText(_configFilePath);
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Object ||
                    !document.RootElement.TryGetProperty(nameof(PreySenseConfig.SchemaVersion), out var version) ||
                    !version.TryGetInt32(out int schemaVersion) || schemaVersion != PreySenseConfig.CurrentSchemaVersion)
                    throw new InvalidDataException($"This build requires an explicit SchemaVersion {PreySenseConfig.CurrentSchemaVersion}; existing files are not migrated.");
                var loaded = JsonSerializer.Deserialize<PreySenseConfig>(json, SerializerOptions);
                if (loaded == null)
                    throw new InvalidDataException("Deserialized configuration was null.");

                // Populate missing sub-objects if user JSON omitted them
                loaded.General ??= new GeneralConfig();
                loaded.Display ??= new DisplayConfig();
                loaded.Battery ??= new BatteryConfig();
                loaded.Session ??= new SessionConfig();
                loaded.Tuning ??= new TuningConfig();
                loaded.Tuning.FactoryDefaults ??= new();
                loaded.Fans ??= new FanConfig();
                loaded.Lighting ??= new LightingConfig();
                loaded.Overlay ??= new OverlayConfig();

                loaded.Tuning.Quiet ??= new ModeTuningConfig();
                loaded.Tuning.Eco ??= new ModeTuningConfig();
                loaded.Tuning.Balanced ??= new ModeTuningConfig();
                loaded.Tuning.Performance ??= new ModeTuningConfig();
                loaded.Tuning.Turbo ??= new ModeTuningConfig();

                loaded.Lighting.Keyboard ??= new KeyboardLightingConfig();
                loaded.Lighting.Cover ??= new CoverLightingConfig();

                loaded.Overlay.Slots ??= OverlayConfig.CreateDefaultSlots();

                NormalizeModeTuning(loaded.Tuning.Quiet);
                NormalizeModeTuning(loaded.Tuning.Eco);
                NormalizeModeTuning(loaded.Tuning.Balanced);
                NormalizeModeTuning(loaded.Tuning.Performance);
                NormalizeModeTuning(loaded.Tuning.Turbo);
                if (loaded.Tuning.FactoryDefaults is { Count: > 0 })
                {
                    foreach (var entry in loaded.Tuning.FactoryDefaults.Values)
                        NormalizeModeTuning(entry);
                }
                // Drop retired root Tuning.CpuBoostMode; boost is per-mode only.
                loaded.Tuning.AdditionalSettings?.Remove("CpuBoostMode");

                Validate(loaded);
                return loaded;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to load configuration file '{_configFilePath}': {ex.Message}", ex);
            }
        }

        private static string SerializeForFile(PreySenseConfig config)
        {
            var saved = Clone(config);
            string[] modeOrder = ["Quiet", "Eco", "Balanced", "Performance", "Turbo"];
            var defaults = saved.Tuning.FactoryDefaults;
            saved.Tuning.FactoryDefaults = modeOrder.Where(defaults.ContainsKey)
                .Concat(defaults.Keys.Except(modeOrder).Order(StringComparer.Ordinal))
                .ToDictionary(mode => mode, mode => defaults[mode]);
            return JsonSerializer.Serialize(saved, SerializerOptions);
        }

        private static PreySenseConfig Clone(PreySenseConfig source)
        {
            string json = JsonSerializer.Serialize(source, SerializerOptions);
            return JsonSerializer.Deserialize<PreySenseConfig>(json, SerializerOptions)!;
        }
    }
}
