using System.Drawing;
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
    public class WmiController : IDisposable
    {

        private ManagementObject? _cachedObj;
        private ManagementObject? _cachedApgeObj;
        private ManagementObject? _cachedBatteryObj;
        private ManagementEventWatcher? _powerSourceWatcher;
        private readonly object _lock = new();
        private int _pluggedSource = -1;

        public event EventHandler? PowerSourceChanged;

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
        private Color[] _zoneColors = new Color[4] { 
            Color.FromArgb(0, 150, 255), 
            Color.FromArgb(0, 150, 255), 
            Color.FromArgb(0, 150, 255), 
            Color.FromArgb(0, 150, 255) 
        };

        public byte LastR => _lastR;
        public byte LastG => _lastG;
        public byte LastB => _lastB;
        public byte Brightness => _brightness;
        public byte Speed => _speed;
        public byte Direction => _direction;
        public int LastRgbMode => _lastMode;
        public Color[] ZoneColors => _zoneColors;

        public ManagementObject? GetWmiObject()
        {
            lock (_lock)
            {
                if (_cachedObj != null) return _cachedObj;
                try
                {
                    using var searcher = new ManagementObjectSearcher(AcerWmi.Namespace, $"SELECT * FROM {AcerWmi.Classes.GamingFunction}");
                    _cachedObj = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
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
                    _cachedApgeObj = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
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
                    _cachedBatteryObj = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
                }
                catch { _cachedBatteryObj = null; }
                return _cachedBatteryObj;
            }
        }

        public void InvalidateCache()
        {
            lock (_lock)
            {
                _cachedObj = null;
                _cachedApgeObj = null;
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

                    using var inParams = obj.GetMethodParameters(method);
                    // Dynamically discover the input parameter name (not always "gmInput")
                    string inputName = "gmInput";
                    foreach (var inProp in inParams.Properties.Cast<PropertyData>())
                    {
                        if (!inProp.Name.StartsWith("__"))
                        {
                            inputName = inProp.Name;
                            break;
                        }
                    }
                    var inputProp = inParams.Properties[inputName];
                    object typedInput = inputProp.Type switch
                    {
                        System.Management.CimType.UInt32 => Convert.ToUInt32(input),
                        System.Management.CimType.UInt16 => Convert.ToUInt16(input),
                        System.Management.CimType.SInt32 => Convert.ToInt32(input),
                        System.Management.CimType.SInt64 => Convert.ToInt64(input),
                        System.Management.CimType.UInt64 => Convert.ToUInt64(input),
                        _ => input
                    };
                    inParams[inputName] = typedInput;
                    using var outParams = obj.InvokeMethod(method, inParams, null);
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
        private readonly Lazy<AcerCoverLogoHidClient> _coverLogoHidClient = new(() => new AcerCoverLogoHidClient(), true);
        private readonly Lazy<CustomLightingEffectsEngine> _customEffects;

        private EnekRgbClient EnekRgbClient => _enekRgbClient.Value;
        private AcerCoverLogoHidClient CoverLogoHid => _coverLogoHidClient.Value;
        private CustomLightingEffectsEngine Effects => _customEffects.Value;

        public bool IsEnekRgbAvailable => EnekRgbClient.IsAvailable;
        public bool IsCoverLogoHidAvailable => CoverLogoHid.IsAvailable;

        public EnekRgbClient EnekRgb => EnekRgbClient;
        public AcerCoverLogoHidClient CoverLogoHidClient => CoverLogoHid;
        public CustomLightingEffectsEngine CustomEffects => Effects;

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
            if (GetSystemPowerStatus(out var status))
            {
                onAc = status.ACLineStatus == 1;
                batteryPercent = status.BatteryLifePercent == 255 ? 80f : status.BatteryLifePercent;
                return true;
            }
            onAc = true;
            batteryPercent = 80f;
            return false;
        }

        public PowerSourceKind ReadPowerSource()
        {
            TryGetPowerStatus(out bool onAc, out _);
            if (!onAc)
                return PowerSourceKind.Battery;

            return Volatile.Read(ref _pluggedSource) == (int)PowerSourceKind.UsbC
                ? PowerSourceKind.UsbC
                : PowerSourceKind.Ac;
        }

        public void NotePluggedSource(PowerSourceKind kind)
        {
            if (kind is not (PowerSourceKind.Ac or PowerSourceKind.UsbC))
                return;

            Interlocked.CompareExchange(ref _pluggedSource, (int)kind, -1);
        }

        public void StartPowerSourceWatch()
        {
            try
            {
                var watcher = new ManagementEventWatcher(new ManagementScope(@"root\wmi"), new WqlEventQuery("SELECT * FROM APGeEvent"));
                watcher.EventArrived += OnAcerPowerEvent;
                watcher.Start();
                _powerSourceWatcher = watcher;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Power source watch: {ex.Message}");
            }
        }

        public static bool TryParseAcerPowerEvent(byte[]? detail, out PowerSourceKind source)
        {
            source = default;
            if (detail == null || detail.Length < 2 || detail[0] != 8)
                return false;

            switch (detail[1])
            {
                case 0:
                    source = PowerSourceKind.Battery;
                    return true;
                case 1:
                    source = PowerSourceKind.Ac;
                    return true;
                case 4:
                    source = PowerSourceKind.UsbC;
                    return true;
                default:
                    return false;
            }
        }

        private void OnAcerPowerEvent(object sender, EventArrivedEventArgs e)
        {
            try
            {
                if (e.NewEvent?["EventDetail"] is not byte[] detail ||
                    !TryParseAcerPowerEvent(detail, out PowerSourceKind source))
                    return;

                if (source is PowerSourceKind.Ac or PowerSourceKind.UsbC)
                    Volatile.Write(ref _pluggedSource, (int)source);

                AppLogger.Log($"Acer power source event: {source}.");
                PowerSourceChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Acer power source event: {ex.Message}");
            }
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

        public void SetCachedRgbState(int mode, byte r, byte g, byte b, byte brightness, byte speed, byte direction, Color[] colors)
        {
            _lastMode = mode;
            _lastR = r; _lastG = g; _lastB = b;
            _brightness = brightness;
            _speed = speed;
            _direction = direction;
            if (colors != null && colors.Length >= 4)
                _zoneColors = colors;
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

        public void SetDirection(byte direction)
        {
            _direction = direction;
            ApplyLightingMode(_lastMode);
        }

        public void SetStaticColor(byte r, byte g, byte b, byte brightness)
        {
            _lastR = r; _lastG = g; _lastB = b;
            _brightness = (byte)Math.Clamp((int)brightness, 0, 100);
            _lastMode = 0;
            Color c = Color.FromArgb(r, g, b);
            for (int i = 0; i < 4; i++)
                _zoneColors[i] = c;
            ApplyZoneLighting();
        }

        public void SetLogoLighting(int mode, byte r, byte g, byte b, byte brightness, byte speed, bool enabled = true)
        {
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

            if (EnekRgbClient.IsAvailable)
            {
                if (EnekRgbClient.SetZoneColors(_zoneColors, scaledBrightness))
                {
                    AppLogger.Log($"Keyboard zone colors set via ENEK5130 Direct HID: Zone0=#{_zoneColors[0].R:X2}{_zoneColors[0].G:X2}{_zoneColors[0].B:X2}, Zone1=#{_zoneColors[1].R:X2}{_zoneColors[1].G:X2}{_zoneColors[1].B:X2}, Zone2=#{_zoneColors[2].R:X2}{_zoneColors[2].G:X2}{_zoneColors[2].B:X2}, Zone3=#{_zoneColors[3].R:X2}{_zoneColors[3].G:X2}{_zoneColors[3].B:X2} (brightness={_brightness})");
                    return;
                }
            }

            AppLogger.Log("ApplyZoneLighting: no supported RGB transport is available.");
        }

        private void ApplyLightingMode(int mode)
        {
            if (mode == 0)
            {
                Effects.Stop();
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
            Effects.Stop();

            float factor = Math.Clamp(_brightness / 100.0f, 0f, 1f);
            byte dimR = (byte)Math.Clamp((int)Math.Round(_lastR * factor), 0, 255);
            byte dimG = (byte)Math.Clamp((int)Math.Round(_lastG * factor), 0, 255);
            byte dimB = (byte)Math.Clamp((int)Math.Round(_lastB * factor), 0, 255);
            byte scaledBrightness = (byte)Math.Clamp((int)_brightness, 0, 100);
            byte scaledSpeed = (byte)Math.Clamp((int)_speed, 1, 10);

            // Hardware animations key off color luminance; send dimmed RGB plus the 0–100 brightness byte.
            if (EnekRgbClient.IsAvailable)
            {
                if (EnekRgbClient.SetDynamicEffect(mode, Color.FromArgb(dimR, dimG, dimB), scaledBrightness, scaledSpeed, _direction))
                {
                    string modeName = mode >= 0 && mode < RgbProfile.UiModeNames.Length ? RgbProfile.UiModeNames[mode] : $"Unknown (0x{mode:X})";
                    AppLogger.Log($"Keyboard lighting mode set via ENEK5130 Direct HID: {modeName} (brightness={_brightness}, speed={_speed}, direction={_direction})");
                    return;
                }
            }

            AppLogger.Log($"ApplyLightingMode: no supported RGB transport is available (mode={mode}).");
        }



        public readonly record struct ThermalSnapshot(int CpuTemp, int GpuTemp, int CpuRpm, int GpuRpm);

        /// <summary>
        /// One WMI parameter object and one lock for the four live thermal sensors.
        /// </summary>
        public ThermalSnapshot ReadThermalSensors()
        {
            lock (_lock)
            {
                try
                {
                    var obj = GetWmiObject();
                    if (obj == null)
                        return default;

                    using var inParams = obj.GetMethodParameters(AcerWmi.GamingMethods.GetSystemInfo);
                    int cpuTemp = ReadSensor(obj, inParams, AcerWmi.Sensors.CpuTemperature);
                    int gpuTemp = ReadSensor(obj, inParams, AcerWmi.Sensors.GpuTemperature);
                    int cpuRpm = RoundToNearest100(ReadSensor(obj, inParams, AcerWmi.Sensors.CpuFanRpm));
                    int gpuRpm = RoundToNearest100(ReadSensor(obj, inParams, AcerWmi.Sensors.GpuFanRpm));
                    return new ThermalSnapshot(cpuTemp, gpuTemp, cpuRpm, gpuRpm);
                }
                catch
                {
                    InvalidateCache();
                    return default;
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
                    if (obj == null) return 0;

                    using var inParams = obj.GetMethodParameters(AcerWmi.GamingMethods.GetSystemInfo);
                    return ReadSensor(obj, inParams, sensorId);
                }
                catch
                {
                    InvalidateCache();
                    return 0;
                }
            }
        }

        private int ReadSensor(ManagementObject obj, ManagementBaseObject inParams, ulong sensorId)
        {
            try
            {
                inParams["gmInput"] = (ulong)(0x0001 | (sensorId << 8));
                using var outParams = obj.InvokeMethod(AcerWmi.GamingMethods.GetSystemInfo, inParams, null);
                var raw = (ulong)outParams["gmOutput"];
                if ((raw & 0xFF) == 0) return (int)((raw >> 8) & 0xFFFF);
            }
            catch
            {
                InvalidateCache();
            }
            return 0;
        }


        public bool SetPowerMode(byte mode)
        {
            ulong payload = (ulong)0x0B | ((ulong)mode << 8);
            AppLogger.Log($"[SetPowerMode-Diag] START: Requesting mode 0x{mode:X2} (Payload: 0x{payload:X8})");

            var (success, rawOutput) = SendCommand(AcerWmi.GamingMethods.SetMiscSetting, payload);
            AppLogger.Log($"[SetPowerMode-Diag] SendCommand(SetGamingMiscSetting): success={success}, rawOutput=0x{rawOutput:X}");
            if (!success)
                return false;

            lock (_lock) { _lastSetFanMode = -1; }
            SyncWindowsPowerMode(mode);
            if (TryGetPowerProfileWmi(out byte postMode) && postMode == mode)
                AppLogger.Log($"[SetPowerMode-Diag] SUCCESS via WMI verified: mode=0x{mode:X2}");
            else
                AppLogger.Log($"[SetPowerMode-Diag] Command sent for 0x{mode:X2}; hardware readback pending.");
            return true;
        }

        public byte GetPowerProfile()
        {
            if (TryGetPowerProfileWmi(out byte wmiMode))
                return wmiMode;

            return 0x01;
        }

        public bool TryGetPowerProfile(out byte mode)
        {
            if (TryGetPowerProfileWmi(out mode))
                return true;

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

        public bool SetFanBehavior(byte mode)
        {
            ulong value = mode switch
            {
                0 => 0x410009ul, // Auto
                1 => 0x820009ul, // Max
                2 => 0xC30009ul, // Custom
                _ => 0x410009ul
            };

            var (success, _) = SendCommand(AcerWmi.GamingMethods.SetFanBehavior, value);
            return success;
        }

        public static int ScalePercentToWmi(int percent)
        {
            return EcFanPolicy.ClampDuty(percent);
        }

        public void InvalidateFanTargets()
        {
            lock (_lock) { _lastSetFanMode = -1; }
        }

        public bool SetCpuFanSpeed(int percent) => SetFanDuty(1, percent);
        public bool SetGpuFanSpeed(int percent) => SetFanDuty(4, percent);

        private bool SetFanDuty(int fan, int percent)
        {
            int duty = EcFanPolicy.ClampDuty(percent);
            ulong payload = (ulong)(((duty * 25600) / 100) & 0xFF00) + (uint)fan;
            return SendCommand(AcerWmi.GamingMethods.SetFanSpeed, payload).success;
        }

        public bool SetFanControlWmi(int mode, int cpuSpeed = 50, int gpuSpeed = 50)
        {
            lock (_lock)
            {
                cpuSpeed = EcFanPolicy.ClampDuty(cpuSpeed);
                gpuSpeed = EcFanPolicy.ClampDuty(gpuSpeed);
                bool force = mode != _lastSetFanMode;
                if (!force && (mode != 2 || cpuSpeed == _lastSetCpuSpeed && gpuSpeed == _lastSetGpuSpeed))
                    return true;
                // Only enter Custom once. Re-entering it for every band change can
                // restart the EC's fan control cycle and produce audible pulses.
                if (force && !SetFanBehavior((byte)mode)) return false;
                if (mode == 2)
                {
                    if ((force || cpuSpeed != _lastSetCpuSpeed) && !SetCpuFanSpeed(cpuSpeed)) return false;
                    if ((force || gpuSpeed != _lastSetGpuSpeed) && !SetGpuFanSpeed(gpuSpeed)) return false;
                }
                _lastSetFanMode = mode;
                _lastSetCpuSpeed = cpuSpeed;
                _lastSetGpuSpeed = gpuSpeed;
                return true;
            }
        }

        private static int RoundToNearest100(int value) => (int)(Math.Round(value / 100.0) * 100);

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

        private ulong SendApgeCommand(string method, ulong input)
        {
            lock (_lock)
            {
                try
                {
                    var obj = GetApgeObject();
                    if (obj == null) return 0;
                    using var inParams = obj.GetMethodParameters(method);
                    string paramName = "uiInput";
                    foreach (var prop in inParams.Properties.Cast<PropertyData>())
                    {
                        paramName = prop.Name;
                        break;
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
                    return 0;
                }
            }
        }



        public bool GetLedTimeout()
        {
            ulong status = SendApgeCommand(AcerWmi.ApgeMethods.GetFunction, 0x88401ul);
            return status != 0x80000ul && status != 0;
        }

        public void SetLedTimeout(bool enabled)
        {
            SetLedTimeoutSeconds(enabled ? 30 : 0);
        }

        public void SetLedTimeoutSeconds(int seconds)
        {
            ulong val = seconds > 0 ? (((ulong)seconds << 40) | 0x088402ul) : 0x88402ul;
            SendApgeCommand(AcerWmi.ApgeMethods.SetFunction, val);
            AppLogger.Log($"Keyboard LED backlight timeout: {(seconds > 0 ? $"{seconds}s" : "Always On (Disabled)")}");
        }

        public bool GetBootSound()
        {
            var (success, result) = SendCommand(AcerWmi.GamingMethods.GetMiscSetting, 0x06ul);
            if (!success) return false;
            return result == 0x100ul;
        }

        public void SetBootSound(bool enabled)
        {
            ulong val = enabled ? 0x106ul : 0x06ul;
            SendCommand(AcerWmi.GamingMethods.SetMiscSetting, val);
            AppLogger.Log($"Boot animation and sound: {(enabled ? "Enabled" : "Disabled")}");
        }

        public int GetUsbChargingLevel()
        {
            ulong status = SendApgeCommand(AcerWmi.ApgeMethods.GetFunction, 0x04ul);
            return status switch
            {
                659200ul => 10,
                1314560ul => 20,
                1969920ul => 30,
                _ => 0
            };
        }

        public void SetUsbChargingLevel(int percent)
        {
            ulong val = percent switch
            {
                10 => 659204ul, // 0xA0F04
                20 => 1314564ul, // 0x140F04
                30 => 1969924ul, // 0x1E0F04
                _ => 663300ul   // 0xA1F04 (off)
            };
            SendApgeCommand(AcerWmi.ApgeMethods.SetFunction, val);
            AppLogger.Log($"USB power-off charging level: {percent}%");
        }

        public int GetBatteryMode()
        {
            lock (_lock)
            {
                try
                {
                    var obj = GetBatteryObject();
                    if (obj == null) return 0;
                    using var inParams = obj.GetMethodParameters(AcerWmi.BatteryMethods.GetHealthControlStatus);
                    inParams["uBatteryNo"] = (byte)1;
                    inParams["uFunctionQuery"] = (byte)1;
                    inParams["uReserved"] = new byte[] { 0, 0 };
                    
                    using var outParams = obj.InvokeMethod(AcerWmi.BatteryMethods.GetHealthControlStatus, inParams, null);
                    
                    var status = outParams["uFunctionStatus"];
                    if (status is byte[] bytes && bytes.Length > 0)
                    {
                        return bytes[0];
                    }
                    else if (status is IEnumerable<object> list)
                    {
                        var first = list.FirstOrDefault();
                        if (first != null) return Convert.ToInt32(first);
                    }
                    else if (status is System.Collections.IEnumerable list2)
                    {
                        foreach (var val in list2)
                        {
                            return Convert.ToInt32(val);
                        }
                    }
                    else if (status != null)
                    {
                        return Convert.ToInt32(status);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to get battery mode: {ex.Message}");
                    InvalidateCache();
                }
                return 0;
            }
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
            var (success, _) = SendCommand(AcerWmi.GamingMethods.SetProfile, enable ? 0x1000000000010ul : 0x10ul);
            return success;
        }

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
            if (_powerSourceWatcher != null)
            {
                _powerSourceWatcher.EventArrived -= OnAcerPowerEvent;
                try { _powerSourceWatcher.Stop(); } catch { }
                _powerSourceWatcher.Dispose();
                _powerSourceWatcher = null;
            }

            lock (_lock)
            {
                _cachedObj?.Dispose();
                _cachedObj = null;
                _cachedApgeObj?.Dispose();
                _cachedApgeObj = null;
                _cachedBatteryObj?.Dispose();
                _cachedBatteryObj = null;
            }
            if (_customEffects.IsValueCreated)
                Effects.Dispose();
            if (_enekRgbClient.IsValueCreated)
                EnekRgbClient.Dispose();
            if (_coverLogoHidClient.IsValueCreated)
                CoverLogoHid.Dispose();
        }
    }
}
