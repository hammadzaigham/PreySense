using System.Drawing;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PreySense;
using PreySense.Core.Services;
using PreySense.Mode;
using PreySense.Rgb;

namespace PreySense.Helpers
{
    [SupportedOSPlatform("windows")]
    public partial class WmiController : IDisposable
    {

        private ManagementObject? _cachedObj;
        private ManagementObject? _cachedApgeObj;
        private ManagementObject? _cachedBatteryObj;
        private readonly Dictionary<string, (ManagementBaseObject Params, string InputName, CimType InputType)> _cachedGamingMethodParams = new(StringComparer.Ordinal);
        private ManagementBaseObject? _cachedGetGamingSysInfoParams;
        private ManagementBaseObject? _cachedApgeSetParams;
        private string? _cachedApgeSetParamName;
        private ManagementBaseObject? _cachedApgeGetParams;
        private string? _cachedApgeGetParamName;
        private readonly object _lock = new();

        [DllImport("powrprof.dll")]
        private static extern uint PowerSetActiveOverlayScheme(Guid scheme);

        private static readonly Guid OVERLAY_EFFICIENCY = AcerWmi.WindowsPowerOverlayEfficiency;
        private static readonly Guid OVERLAY_BALANCED = AcerWmi.WindowsPowerOverlayBalanced;
        private static readonly Guid OVERLAY_PERFORMANCE = AcerWmi.WindowsPowerOverlayPerformance;

        private byte _lastR = 0, _lastG = 150, _lastB = 255;
        private byte _brightness = 100;
        private byte _speed = 5;       
        private byte _direction = 1;   
        private int _lastMode = 0;     
        private int _lastSetFanMode = -1;
        private int _lastSetCpuSpeed = -1;
        private int _lastSetGpuSpeed = -1;
        // Acer SetGamingMiscSetting(0x0B) can succeed while GetGamingMiscSetting(0x0B)
        // fails or returns an unknown byte on some firmwares. Cache the last known
        // Quiet/Balanced/Performance/Turbo/Eco value so mode-gated GPU apply can proceed.
        private byte? _lastKnownPowerMode;
        private Color[] _zoneColors = new Color[4] { 
            Color.FromArgb(0, 150, 255), 
            Color.FromArgb(0, 150, 255), 
            Color.FromArgb(0, 150, 255), 
            Color.FromArgb(0, 150, 255) 
        };

        public byte Brightness => _brightness;
        public byte Speed => _speed;
        public byte Direction => _direction;
        public Color[] ZoneColors => _zoneColors;

        private static ManagementObject? FirstOrDefaultAndDisposeRest(ManagementObjectSearcher searcher)
        {
            using var results = searcher.Get();
            ManagementObject? first = null;
            foreach (ManagementBaseObject item in results)
            {
                if (first is null && item is ManagementObject mo)
                {
                    first = mo;
                }
                else
                {
                    item.Dispose();
                }
            }
            return first;
        }

        public ManagementObject? GetWmiObject()
        {
            lock (_lock)
            {
                if (_cachedObj != null) return _cachedObj;
                try
                {
                    using var searcher = new ManagementObjectSearcher(AcerWmi.Namespace, $"SELECT * FROM {AcerWmi.Classes.GamingFunction}");
                    _cachedObj = FirstOrDefaultAndDisposeRest(searcher);
                }
                catch { _cachedObj = null; }
                return _cachedObj;
            }
        }

        private ManagementObject? GetApgeObject()
        {
            lock (_lock)
            {
                if (_cachedApgeObj != null) return _cachedApgeObj;
                try
                {
                    using var searcher = new ManagementObjectSearcher(AcerWmi.Namespace, $"SELECT * FROM {AcerWmi.Classes.ApgeAction}");
                    _cachedApgeObj = FirstOrDefaultAndDisposeRest(searcher);
                }
                catch { _cachedApgeObj = null; }
                return _cachedApgeObj;
            }
        }

        private ManagementObject? GetBatteryObject()
        {
            lock (_lock)
            {
                if (_cachedBatteryObj != null) return _cachedBatteryObj;
                try
                {
                    using var searcher = new ManagementObjectSearcher(AcerWmi.Namespace, $"SELECT * FROM {AcerWmi.Classes.BatteryControl}");
                    _cachedBatteryObj = FirstOrDefaultAndDisposeRest(searcher);
                }
                catch { _cachedBatteryObj = null; }
                return _cachedBatteryObj;
            }
        }

        public void InvalidateCache()
        {
            lock (_lock)
            {
                foreach (var entry in _cachedGamingMethodParams.Values)
                {
                    try { entry.Params.Dispose(); } catch { }
                }
                _cachedGamingMethodParams.Clear();
                _cachedGetGamingSysInfoParams?.Dispose();
                _cachedGetGamingSysInfoParams = null;
                _cachedApgeSetParams?.Dispose();
                _cachedApgeSetParams = null;
                _cachedApgeSetParamName = null;
                _cachedApgeGetParams?.Dispose();
                _cachedApgeGetParams = null;
                _cachedApgeGetParamName = null;
                _cachedObj?.Dispose();
                _cachedObj = null;
                _cachedApgeObj?.Dispose();
                _cachedApgeObj = null;
                _cachedBatteryObj?.Dispose();
                _cachedBatteryObj = null;
            }
        }

        private (bool success, ulong output) SendCommand(string method, ulong input)
        {
            lock (_lock)
            {
                try
                {
                    var obj = GetWmiObject();
                    if (obj == null) return (false, 0);

                    if (!_cachedGamingMethodParams.TryGetValue(method, out var cached))
                    {
                        var created = obj.GetMethodParameters(method);
                        string inputName = "gmInput";
                        foreach (var inProp in created.Properties.Cast<PropertyData>())
                        {
                            if (!inProp.Name.StartsWith("__"))
                            {
                                inputName = inProp.Name;
                                break;
                            }
                        }
                        var inputProp = created.Properties[inputName];
                        cached = (created, inputName, inputProp.Type);
                        _cachedGamingMethodParams[method] = cached;
                    }

                    object typedInput = cached.InputType switch
                    {
                        System.Management.CimType.UInt32 => Convert.ToUInt32(input),
                        System.Management.CimType.UInt16 => Convert.ToUInt16(input),
                        System.Management.CimType.SInt32 => Convert.ToInt32(input),
                        System.Management.CimType.SInt64 => Convert.ToInt64(input),
                        System.Management.CimType.UInt64 => Convert.ToUInt64(input),
                        _ => input
                    };
                    cached.Params[cached.InputName] = typedInput;
                    using var outParams = obj.InvokeMethod(method, cached.Params, null);
                    // Dynamically discover the output parameter name
                    string outputName = "gmOutput";
                    foreach (var outProp in outParams.Properties.Cast<PropertyData>())
                    {
                        if (outProp.Name != "ReturnValue" && !outProp.Name.StartsWith("__"))
                        {
                            outputName = outProp.Name;
                            break;
                        }
                    }
                    ulong result = Convert.ToUInt64(outParams[outputName]);
                    return ((result & 0xFF) == 0, result);
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"WMI {method}(0x{input:X}) FAILED: {ex.Message}");
                    InvalidateCache();
                    return (false, 0);
                }
            }
        }

        private readonly Lazy<EnekRgbClient> _enekRgbClient = new(() => new EnekRgbClient(), true);
        private CustomLightingEffectsEngine? _attachedEffects;
        private readonly Lazy<CustomLightingEffectsEngine> _customEffects;
        private readonly Lazy<OemLightingController> _oemLighting = new(() => new OemLightingController(), true);
        public OemLightingController OemLighting => _oemLighting.Value;

        private EnekRgbClient EnekRgbClient => _enekRgbClient.Value;
        /// <summary>Prefer the Production-owned engine when attached so FrameUpdated/idle stay single-instance.</summary>
        private CustomLightingEffectsEngine Effects => _attachedEffects ?? _customEffects.Value;

        public void AttachLightingEngine(CustomLightingEffectsEngine engine) =>
            _attachedEffects = engine ?? throw new ArgumentNullException(nameof(engine));

        /// <summary>Raised after keyboard lighting was written successfully (the backlight is on again).</summary>
        public event Action? KeyboardLightingWritten;

        private void RaiseKeyboardLightingWritten()
        {
            try { KeyboardLightingWritten?.Invoke(); }
            catch (Exception ex) { AppLogger.Error($"KeyboardLightingWritten handler failed: {ex.Message}"); }
        }

        /// <summary>Turns the keyboard backlight off without touching the cached lighting state.</summary>
        public bool TurnOffKeyboardBacklight() => EnekRgbClient.IsAvailable && EnekRgbClient.TurnOffBacklight();

        public bool IsEnekRgbAvailable => EnekRgbClient.IsAvailable;
        public bool IsCoverLogoHidAvailable => AcerHidLightingCapabilities.GetSupportedTargets().Contains(0x83);

        public WmiController()
        {
            _customEffects = new Lazy<CustomLightingEffectsEngine>(() => new CustomLightingEffectsEngine(this), true);
        }

        private static bool IsKnownOperatingMode(byte mode)
        {
            return mode is 0x00 or 0x01 or 0x04 or 0x05 or 0x06;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

        public bool TryGetPowerStatus(out bool onAc, out float batteryPercent)
        {
            if (GetSystemPowerStatus(out var status) && status.ACLineStatus is 0 or 1)
            {
                onAc = status.ACLineStatus == 1;
                batteryPercent = status.BatteryLifePercent <= 100 ? status.BatteryLifePercent : float.NaN;
                return true;
            }
            onAc = false;
            batteryPercent = float.NaN;
            return false;
        }

        public PowerSourceKind ReadPowerSource()
        {
            return TryGetPowerStatus(out bool onAc, out _) ? ReadPowerSource(onAc) : PowerSourceKind.Unknown;
        }

        public PowerSourceKind ReadPowerSource(bool onAc)
        {
            if (!onAc)
                return PowerSourceKind.Battery;

            return PowerSourceKind.Ac;
        }


        public void SetRgbMode(int mode, byte r, byte g, byte b, byte brightness, byte speed, byte direction)
        {
            _lastR = r; _lastG = g; _lastB = b;
            _brightness = (byte)Math.Clamp((int)brightness, 0, 100);
            _speed = speed;
            _direction = direction;
            _lastMode = mode;
            if (mode == 0)
            {
                Color c = Color.FromArgb(r, g, b);
                for (int i = 0; i < 4; i++)
                    _zoneColors[i] = c;
            }
            ApplyLightingMode(mode);
        }

        public void ApplyRgbState()
        {
            if (_lastMode == 0)
            {
                ApplyZoneLighting();
            }
            else
            {
                ApplyLightingMode(_lastMode);
            }
        }




        public int? GetKeyboardBrightness()
        {
            // RGB is controlled through ENEK HID. ACPI-WMI has no verified
            // keyboard-brightness contract, so return cached state.
            return Math.Clamp((int)_brightness, 0, 100);
        }

        public void NoteBrightness(byte brightness)
        {
            _brightness = (byte)Math.Clamp((int)brightness, 0, 100);
        }

        public void SetBrightness(byte brightness)
        {
            _brightness = (byte)Math.Clamp((int)brightness, 0, 100);
            ApplyLightingMode(_lastMode);
        }

        public void SetSpeed(byte speed)
        {
            _speed = speed;
            ApplyLightingMode(_lastMode);
        }

        public void SetLogoLighting(int mode, byte r, byte g, byte b, byte brightness, byte speed, bool enabled = true)
        {
            if (!IsCoverLogoHidAvailable) return;
            byte scaledBrightness = !enabled || mode == 0 ? (byte)0 : Math.Clamp(brightness, (byte)0, (byte)100);
            // HID / Acer logo modes are 1–5. UI speed is 0–100%.
            byte logoSpeed = (byte)Math.Clamp(speed <= 5 ? speed : (int)Math.Round(Math.Max((int)speed, 1) / 20.0), 1, 5);
            int activeMode = (!enabled || scaledBrightness == 0) ? 0 : mode;
            Color color = Color.FromArgb(r, g, b);

            if (EnekRgbClient.IsAvailable && EnekRgbClient.SetLogoLighting(activeMode, color, scaledBrightness, logoSpeed))
            {
                AppLogger.Log($"Cover logo lighting set via ENEK5130 HID target 0x83 (mode={activeMode}, color=#{r:X2}{g:X2}{b:X2}, brightness={scaledBrightness}, speed={logoSpeed}, enabled={enabled}).");
                return;
            }

            AppLogger.Log($"Cover logo mode {activeMode} was not applied: no supported RGB transport is available.");
        }

        public void SetZoneColors(Color[] colors, byte brightness)
        {
            if (colors != null && colors.Length >= 4)
            {
                _zoneColors = colors;
                _lastR = colors[0].R;
                _lastG = colors[0].G;
                _lastB = colors[0].B;
            }
            _brightness = brightness;
            _lastMode = 0;
            ApplyZoneLighting();
        }

        public void ApplyZoneLighting()
        {
            byte scaledBrightness = (byte)Math.Clamp((int)_brightness, 0, 100);

            if ((EnekRgbClient.IsAvailable && EnekRgbClient.SetZoneColors(_zoneColors, scaledBrightness))
                || OemLighting.ApplyKeyboardFallback(_zoneColors, scaledBrightness))
            {
                RaiseKeyboardLightingWritten();
                return;
            }

            AppLogger.Log("ApplyZoneLighting: no supported RGB transport is available.");
        }

        private void ApplyLightingMode(int mode)
        {
            if (mode == 0)
            {
                Effects.StopAndWait();
                ApplyZoneLighting();
                return;
            }

            if (mode >= 8)
            {
                byte customScaledBrightness = (byte)Math.Clamp((int)_brightness, 0, 100);
                Effects.StartEffect((CustomEffectMode)mode, customScaledBrightness, _speed);
                string customModeName = mode >= 0 && mode < RgbProfile.UiModeNames.Length ? RgbProfile.UiModeNames[mode] : $"Custom ({mode})";
                AppLogger.Log($"Keyboard custom dynamic effect activated: {customModeName} (brightness={_brightness}, speed={_speed})");
                return;
            }

            // Stop any running custom software effect when switching back to hardware modes (1..7)
            Effects.StopAndWait();

            byte scaledBrightness = (byte)Math.Clamp((int)_brightness, 0, 100);
            byte scaledSpeed = (byte)Math.Clamp((int)_speed, 1, 10);
            // Match Static: full RGB + brightness byte. Pre-dimming RGB made HW effects look darker.
            var effectColor = Color.FromArgb(_lastR, _lastG, _lastB);

            if (EnekRgbClient.IsAvailable)
            {
                if (EnekRgbClient.SetDynamicEffect(mode, effectColor, scaledBrightness, scaledSpeed, _direction))
                {
                    string modeName = mode >= 0 && mode < RgbProfile.UiModeNames.Length ? RgbProfile.UiModeNames[mode] : $"Unknown (0x{mode:X})";
                    AppLogger.Log($"Keyboard lighting mode set via ENEK5130 Direct HID: {modeName} (brightness={_brightness}, speed={_speed}, direction={_direction})");
                    RaiseKeyboardLightingWritten();
                    return;
                }
            }

            if (OemLighting.ApplyKeyboardModeFallback(mode, effectColor, scaledBrightness, scaledSpeed, _direction))
            {
                RaiseKeyboardLightingWritten();
                return;
            }
            AppLogger.Log($"ApplyLightingMode: no supported RGB transport is available (mode={mode}).");
        }



        public readonly record struct ThermalSnapshot(int CpuTemp, int GpuTemp, int CpuRpm, int GpuRpm);

        /// <summary>
        /// One WMI parameter object and one lock for the four live thermal sensors.
        /// </summary>
        public ThermalSnapshot ReadThermalSensors(bool includeTemperatures = true, bool readCpuRpm = true, bool readGpuRpm = true)
        {
            lock (_lock)
            {
                try
                {
                    var obj = GetWmiObject();
                    if (obj == null)
                        return new ThermalSnapshot(-1, -1, -1, -1);

                    var inParams = _cachedGetGamingSysInfoParams ??= obj.GetMethodParameters(AcerWmi.GamingMethods.GetSystemInfo);
                    int cpuTemp = includeTemperatures ? ReadSensor(ref obj, inParams, AcerWmi.Sensors.CpuTemperature) : -1;
                    // GPU temperature comes from NVIDIA; avoid the stale EC GPU sensor.
                    int gpuTemp = 0;
                    int cpuRpm = readCpuRpm && obj != null ? RoundToNearest100(ReadSensor(ref obj, _cachedGetGamingSysInfoParams ??= obj.GetMethodParameters(AcerWmi.GamingMethods.GetSystemInfo), AcerWmi.Sensors.CpuFanRpm)) : -1;
                    int gpuRpm = readGpuRpm && obj != null ? RoundToNearest100(ReadSensor(ref obj, _cachedGetGamingSysInfoParams ??= obj.GetMethodParameters(AcerWmi.GamingMethods.GetSystemInfo), AcerWmi.Sensors.GpuFanRpm)) : -1;
                    return new ThermalSnapshot(cpuTemp, gpuTemp, cpuRpm, gpuRpm);
                }
                catch
                {
                    InvalidateCache();
                    return new ThermalSnapshot(-1, -1, -1, -1);
                }
            }
        }

        private int GetSensorReading(ulong sensorId)
        {
            lock (_lock)
            {
                try
                {
                    var obj = GetWmiObject();
                    if (obj == null) return -1;

                    var inParams = _cachedGetGamingSysInfoParams ??= obj.GetMethodParameters(AcerWmi.GamingMethods.GetSystemInfo);
                    return ReadSensor(ref obj, inParams, sensorId);
                }
                catch
                {
                    InvalidateCache();
                    return -1;
                }
            }
        }

        private int ReadSensor(ref ManagementObject? obj, ManagementBaseObject inParams, ulong sensorId)
        {
            if (obj == null) return -1;
            long started = Stopwatch.GetTimestamp();
            try
            {
                inParams["gmInput"] = (ulong)(0x0001 | (sensorId << 8));
                using var outParams = obj.InvokeMethod(AcerWmi.GamingMethods.GetSystemInfo, inParams, null);
                var raw = (ulong)outParams["gmOutput"];
                if ((raw & 0xFF) == 0) return (int)((raw >> 8) & 0xFFFF);
                AppLogger.Error($"EC sensor 0x{sensorId:X} failed: firmware returned 0x{raw:X}.");
            }
            catch (Exception ex)
            {
                AppLogger.Error($"EC sensor 0x{sensorId:X} failed: {ex.Message}");
                InvalidateCache();
                obj = GetWmiObject();
            }
            finally
            {
                double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                if (elapsed >= 500)
                    AppLogger.Error($"Slow EC sensor 0x{sensorId:X}: {elapsed:F0} ms. Other firmware operations may have waited.");
            }
            return -1;
        }


        public bool SetPowerMode(byte mode)
        {
            ulong payload = (ulong)0x0B | ((ulong)mode << 8);
            AppLogger.Log($"[SetPowerMode-Diag] START: Requesting mode 0x{mode:X2} (Payload: 0x{payload:X8})");

            var (success, rawOutput) = SendCommand(AcerWmi.GamingMethods.SetMiscSetting, payload);
            AppLogger.Log($"[SetPowerMode-Diag] SendCommand(SetGamingMiscSetting): success={success}, rawOutput=0x{rawOutput:X}");
            if (!success)
                return false;

            lock (_lock)
            {
                _lastSetFanMode = -1;
                if (IsKnownOperatingMode(mode))
                    _lastKnownPowerMode = mode;
            }
            SyncWindowsPowerMode(mode);
            return true;
        }

        public bool TryGetPowerProfile(out byte mode)
        {
            if (TryGetPowerProfileWmi(out mode))
            {
                lock (_lock) { _lastKnownPowerMode = mode; }
                return true;
            }

            lock (_lock)
            {
                if (_lastKnownPowerMode is byte known)
                {
                    mode = known;
                    AppLogger.Log($"TryGetPowerProfile: WMI read failed; using last-known mode 0x{mode:X2}.");
                    return true;
                }
            }

            mode = 0;
            return false;
        }

        public bool TryGetPowerProfileWmi(out byte mode)
        {
            var (success, output) = SendCommand(AcerWmi.GamingMethods.GetMiscSetting, 0x0Bul);
            mode = (byte)((output >> 8) & 0xFF);
            if (success && IsKnownOperatingMode(mode))
                return true;

            AppLogger.Log($"TryGetPowerProfileWmi: invalid mode 0x{mode:X2} (success={success}, output=0x{output:X}).");
            return false;
        }

        private static int RoundToNearest100(int value) => value < 0 ? -1 : (int)(Math.Round(value / 100.0) * 100);

        public int CpuTemp => GetSensorReading(AcerWmi.Sensors.CpuTemperature);
        public int GpuTemp => GetSensorReading(AcerWmi.Sensors.GpuTemperature);
        public int CpuFanRpm => RoundToNearest100(GetSensorReading(AcerWmi.Sensors.CpuFanRpm));
        public int GpuFanRpm => RoundToNearest100(GetSensorReading(AcerWmi.Sensors.GpuFanRpm));

        public void ApplyOverlayScheme(int modeIndex)
        {
            try
            {
                Guid overlay = modeIndex switch
                {
                    0 => OVERLAY_EFFICIENCY,
                    2 => OVERLAY_PERFORMANCE,
                    _ => OVERLAY_BALANCED
                };
                PowerSetActiveOverlayScheme(overlay);
            }
            catch (Exception ex)
            {
                AppLogger.Log($"ApplyOverlayScheme failed: {ex.Message}");
            }
        }

        private void SyncWindowsPowerMode(byte acerMode)
        {
            int overlayIndex = PreySense.Mode.WindowsPowerPolicy.OverlaySchemeForMode(acerMode);
            ApplyOverlayScheme(overlayIndex);
            AppLogger.Log($"SyncWindowsPowerMode: mode 0x{acerMode:X2} -> overlay {overlayIndex}");
        }

        private ulong? SendApgeCommand(string method, ulong input)
        {
            lock (_lock)
            {
                try
                {
                    var obj = GetApgeObject();
                    if (obj == null) return null;

                    ManagementBaseObject inParams;
                    string paramName;
                    if (string.Equals(method, AcerWmi.ApgeMethods.SetFunction, StringComparison.Ordinal))
                    {
                        if (_cachedApgeSetParams == null)
                        {
                            _cachedApgeSetParams = obj.GetMethodParameters(method);
                            _cachedApgeSetParamName = "uiInput";
                            foreach (var prop in _cachedApgeSetParams.Properties.Cast<PropertyData>())
                            {
                                _cachedApgeSetParamName = prop.Name;
                                break;
                            }
                        }
                        inParams = _cachedApgeSetParams;
                        paramName = _cachedApgeSetParamName!;
                    }
                    else
                    {
                        if (_cachedApgeGetParams == null)
                        {
                            _cachedApgeGetParams = obj.GetMethodParameters(method);
                            _cachedApgeGetParamName = "uiInput";
                            foreach (var prop in _cachedApgeGetParams.Properties.Cast<PropertyData>())
                            {
                                _cachedApgeGetParamName = prop.Name;
                                break;
                            }
                        }
                        inParams = _cachedApgeGetParams;
                        paramName = _cachedApgeGetParamName!;
                    }

                    inParams[paramName] = input;
                    using var outParams = obj.InvokeMethod(method, inParams, null);
                    string outName = "uiOutput";
                    foreach (var prop in outParams.Properties.Cast<PropertyData>())
                    {
                        if (prop.Name != "ReturnValue")
                        {
                            outName = prop.Name;
                            break;
                        }
                    }
                    return Convert.ToUInt64(outParams[outName]);
                }
                catch
                {
                    InvalidateCache();
                    return null;
                }
            }
        }



        public void SetLedTimeoutSeconds(int seconds)
        {
            ulong val = seconds > 0 ? (((ulong)seconds << 40) | 0x088402ul) : 0x88402ul;
            if (!SendApgeCommand(AcerWmi.ApgeMethods.SetFunction, val).HasValue)
                throw new InvalidOperationException("Acer firmware setting could not be applied.");
            AppLogger.Log($"Keyboard LED backlight timeout: {(seconds > 0 ? $"{seconds}s" : "Always On (Disabled)")}");
        }

        public int GetUsbChargingLevel()
        {
            ulong? status = SendApgeCommand(AcerWmi.ApgeMethods.GetFunction, 0x04ul);
            return DecodeUsbChargingLevel(status);
        }

        internal static int DecodeUsbChargingLevel(ulong? status)
        {
            if (!status.HasValue) return -1;
            // AcerControl.ps1 identifies bit 12 as disabled. Require a recognized
            // response payload as well, so errors/unknown packets cannot mean Off.
            int cutoff = (status.Value & ~0x1000ul) switch
            {
                659200ul => 10,
                1314560ul => 20,
                1969920ul => 30,
                _ => -1
            };
            return cutoff < 0 ? -1 : (status.Value & 0x1000ul) != 0 ? 0 : cutoff;
        }

        internal static ulong EncodeUsbChargingLevel(int percent) => percent switch
        {
            0 => 663300ul, // 0xA1F04
            10 => 659204ul, // 0xA0F04
            20 => 1314564ul, // 0x140F04
            30 => 1969924ul, // 0x1E0F04
            _ => throw new ArgumentOutOfRangeException(nameof(percent))
        };

        public void SetUsbChargingLevel(int percent)
        {
            if (!SendApgeCommand(AcerWmi.ApgeMethods.SetFunction, EncodeUsbChargingLevel(percent)).HasValue)
                throw new InvalidOperationException("USB power-off charging: —.");
            AppLogger.Log($"USB power-off charging cutoff: {percent}%");
        }

        public bool SetBatteryMode(int mode)
        {
            lock (_lock)
            {
                try
                {
                    var obj = GetBatteryObject();
                    if (obj == null)
                    {
                        AppLogger.Log("WmiController: BatteryControl WMI object is not available.");
                        return false;
                    }

                    byte normalizedMode = (byte)(mode == 1 ? 1 : 0);
                    using var inParams = obj.GetMethodParameters(AcerWmi.BatteryMethods.SetHealthControl);
                    inParams["uBatteryNo"] = (byte)1;
                    inParams["uFunctionMask"] = (byte)1;
                    inParams["uFunctionStatus"] = normalizedMode; // 1 = Limited, 0 = Full
                    inParams["uReservedIn"] = new byte[] { 0, 0, 0, 0, 0 };
                    
                    using var outParams = obj.InvokeMethod(AcerWmi.BatteryMethods.SetHealthControl, inParams, null);
                    return true;
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"WmiController: Failed to set battery mode: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"Failed to set battery mode: {ex.Message}");
                    InvalidateCache();
                    return false;
                }
            }
        }

        public bool SetLcdOverdrive(bool enable)
        {
            return new LcdOverdriveControl(SendCommand).Set(enable);
        }

        public bool TryGetLcdOverdrive(out bool enabled) =>
            new LcdOverdriveControl(SendCommand).TryGet(out enabled);

        #region Direct Hardware & WMI First Control

        public bool SetFanControl(int mode, int cpuSpeed = 50, int gpuSpeed = 50)
        {
            return SetFanControlWmi(mode, cpuSpeed, gpuSpeed);
        }

        public const int GpuMuxOptimus = 0;
        public const int GpuMuxAuto = 1;
        public const int GpuMuxDiscrete = 2;

        /// <summary>
        /// Stages the Acer hardware display mux via AcerGamingFunction.SetGamingMiscSetting.
        /// Predator Sense GPU_MODE is 0 Optimus, 1 NVIDIA GPU only, 2 Automatic.
        /// The WMI value is that mode plus one in the high byte, with function id 0x02.
        /// PreySense stores Automatic=1 and Discrete=2, so those two are swapped before the call.
        /// Used only for dGPU Only, which then reboots.
        /// </summary>
        public static ulong GpuMuxPayload(int mode)
        {
            mode = Math.Clamp(mode, 0, 2);
            int predatorMode = mode switch
            {
                GpuMuxOptimus => 0,
                GpuMuxAuto => 2,
                _ => 1
            };
            return (ulong)(((predatorMode + 1) << 8) | 0x02);
        }

        public static int? GpuMuxFromFirmware(ulong gmOutput)
        {
            if ((gmOutput & 0xFF) != 0)
                return null;

            return (gmOutput >> 8) switch
            {
                1 => GpuMuxOptimus,
                2 => GpuMuxDiscrete,
                3 => GpuMuxAuto,
                _ => null
            };
        }

        public bool SetGpuMuxMode(int mode)
        {
            ulong payload = GpuMuxPayload(mode);
            var (acpiOk, _) = SendCommand(AcerWmi.GamingMethods.SetMiscSetting, payload);
            if (acpiOk)
            {
                AppLogger.Log($"SetGpuMuxMode: staged hardware GPU mux {mode} via AcerGamingFunction.SetGamingMiscSetting(0x{payload:X}).");
                return true;
            }

            AppLogger.Log("SetGpuMuxMode: ACPI-WMI failed to apply GPU mode.");
            return false;
        }

        /// <summary>
        /// Reads the Acer hardware GPU mux via GetGamingMiscSetting(2).
        /// Firmware byte 1 is Optimus, 2 is NVIDIA GPU only, 3 is Automatic.
        /// </summary>
        public int? GetGpuMuxModeWmi()
        {
            var (success, result) = SendCommand(AcerWmi.GamingMethods.GetMiscSetting, 0x02ul);
            return success ? GpuMuxFromFirmware(result) : null;
        }

        #endregion

        public void Dispose()
        {
            InvalidateCache();
            if (_customEffects.IsValueCreated)
                _customEffects.Value.Dispose();
            if (_enekRgbClient.IsValueCreated)
                EnekRgbClient.Dispose();
            if (_oemLighting.IsValueCreated)
                OemLighting.Dispose();
        }
    }
}
