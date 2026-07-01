using System.Drawing;
using System.Management;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.ServiceProcess;
using System.Threading;
using PreySense;

namespace PreySense.Helpers
{
    [SupportedOSPlatform("windows")]
    public class WmiController : IDisposable
    {

        private ManagementObject? _cachedObj;
        private ManagementObject? _cachedApgeObj;
        private ManagementObject? _cachedBatteryObj;
        private readonly object _lock = new();

        [DllImport("powrprof.dll")]
        private static extern uint PowerSetActiveOverlayScheme(Guid scheme);

        private static readonly Guid OVERLAY_EFFICIENCY = AcerWmi.WindowsPowerOverlayEfficiency;
        private static readonly Guid OVERLAY_BALANCED = AcerWmi.WindowsPowerOverlayBalanced;
        private static readonly Guid OVERLAY_PERFORMANCE = AcerWmi.WindowsPowerOverlayPerformance;

        private byte _lastR = 0, _lastG = 150, _lastB = 255;
        private byte _brightness = 5;
        private byte _speed = 5;       
        private byte _direction = 1;   
        private int _lastMode = 0;     
        private int _lastSetFanMode = -1;
        private int _lastSetCpuSpeed = -1;
        private int _lastSetGpuSpeed = -1;
        private long _lastSetFanTick = 0;
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

        private readonly AcerServiceClient _serviceClient = new();

        public bool IsAcerServiceAvailable => _serviceClient.IsAvailable;

        public void RefreshAcerService() => _serviceClient.Refresh();

        public AcerServiceClient ServiceClient => _serviceClient;

        private static void TryStartService(string serviceName)
        {
            try
            {
                using var sc = new ServiceController(serviceName);
                if (sc.Status == ServiceControllerStatus.Stopped || sc.Status == ServiceControllerStatus.StopPending)
                {
                    AppLogger.Log($"TryStartService: starting service {serviceName}...");
                    sc.Start();
                    sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(5));
                    AppLogger.Log($"TryStartService: service {serviceName} status is now {sc.Status}");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"TryStartService: failed to start service {serviceName}: {ex.Message}");
            }
        }

        private bool EnsureAcerService()
        {
            if (_serviceClient.IsAvailable) return true;

            TryStartService("AcerServiceSvc");
            TryStartService("AcerLightingService");

            _serviceClient.Refresh();
            if (_serviceClient.IsAvailable) return true;
            return false;
        }


        private bool TryParseOperatingMode(string? json, out byte mode)
        {
            mode = 0;
            if (string.IsNullOrEmpty(json)) return false;

            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                if (TryReadOperatingMode(doc.RootElement, out mode))
                    return true;
            }
            catch
            {
                // Fall through to regex parsing for non-standard service payloads.
            }

            var match = Regex.Match(json, @"""(?:mode|result)""\s*:\s*""?(\d+)""?");
            return match.Success &&
                   byte.TryParse(match.Groups[1].Value, out mode) &&
                   IsKnownOperatingMode(mode);
        }

        private static bool TryReadOperatingMode(JsonElement element, out byte mode)
        {
            mode = 0;

            if (TryReadByte(element, out mode))
                return IsKnownOperatingMode(mode);

            if (element.ValueKind != JsonValueKind.Object)
                return false;

            if (element.TryGetProperty("mode", out JsonElement modeElement) &&
                TryReadByte(modeElement, out mode))
            {
                return IsKnownOperatingMode(mode);
            }

            if (element.TryGetProperty("result", out JsonElement resultElement))
            {
                if (TryReadByte(resultElement, out mode))
                    return IsKnownOperatingMode(mode);

                if (resultElement.ValueKind == JsonValueKind.Object &&
                    resultElement.TryGetProperty("mode", out JsonElement resultModeElement) &&
                    TryReadByte(resultModeElement, out mode))
                {
                    return IsKnownOperatingMode(mode);
                }
            }

            return false;
        }

        private static bool TryReadByte(JsonElement element, out byte value)
        {
            value = 0;
            return element.ValueKind switch
            {
                JsonValueKind.Number => element.TryGetByte(out value),
                JsonValueKind.String => byte.TryParse(element.GetString(), out value),
                _ => false
            };
        }

        private static bool IsKnownOperatingMode(byte mode)
        {
            return mode is 0x00 or 0x01 or 0x04 or 0x05 or 0x06;
        }

        public bool TryGetAcConnected(out bool onAc)
        {
            onAc = System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
            return true;
        }

        public void SetRgbMode(int mode, byte r, byte g, byte b, byte brightness, byte speed, byte direction)
        {
            _lastR = r; _lastG = g; _lastB = b;
            _brightness = brightness;
            _speed = speed;
            _direction = direction;
            _lastMode = mode;
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




        public void SetBrightness(byte brightness)
        {
            _brightness = brightness;
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
            _brightness = brightness;
            _lastMode = 0;
            ApplyLightingMode(0);
        }

        public void SetLogoLighting(int mode, byte r, byte g, byte b, byte brightness, byte speed)
        {
            if (!EnsureAcerService()) return;
            bool ok = _serviceClient.SetLogoLighting(mode, r, g, b, brightness, speed);
            if (!ok) AppLogger.Log("SetLogoLighting: AcerService call failed.");
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
            if (!EnsureAcerService())
            {
                return;
            }
            bool ok = _serviceClient.SetLightingZones(_zoneColors, _brightness);
            if (!ok)
            {
                AppLogger.Log("ApplyZoneLighting: AcerService SetLightingZones failed.");
            }
            else
            {
                AppLogger.Log($"Keyboard zone colors set: Zone0=#{_zoneColors[0].R:X2}{_zoneColors[0].G:X2}{_zoneColors[0].B:X2}, Zone1=#{_zoneColors[1].R:X2}{_zoneColors[1].G:X2}{_zoneColors[1].B:X2}, Zone2=#{_zoneColors[2].R:X2}{_zoneColors[2].G:X2}{_zoneColors[2].B:X2}, Zone3=#{_zoneColors[3].R:X2}{_zoneColors[3].G:X2}{_zoneColors[3].B:X2} (brightness={_brightness})");
            }
        }

        private void ApplyLightingMode(int mode)
        {
            if (mode == 0)
            {
                Color c = Color.FromArgb(_lastR, _lastG, _lastB);
                for (int i = 0; i < 4; i++) _zoneColors[i] = c;
                ApplyZoneLighting();
                return;
            }

            float factor = _brightness / 5.0f;
            byte r = (byte)Math.Min(255, Math.Max(0, Math.Round(_lastR * factor)));
            byte g = (byte)Math.Min(255, Math.Max(0, Math.Round(_lastG * factor)));
            byte b = (byte)Math.Min(255, Math.Max(0, Math.Round(_lastB * factor)));

            if (!EnsureAcerService())
            {
                return;
            }

            bool ok = _serviceClient.SetLighting(mode, r, g, b, _brightness, _speed, _direction);
            if (!ok)
            {
                AppLogger.Log($"ApplyLightingMode: AcerService SetLighting failed (mode={mode}).");
            }
            else
            {
                string modeName = mode >= 0 && mode < RgbProfile.UiModeNames.Length ? RgbProfile.UiModeNames[mode] : $"Unknown (0x{mode:X})";
                AppLogger.Log($"Keyboard lighting mode set to: {modeName} (brightness={_brightness}, speed={_speed}, direction={_direction})");
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
                    inParams["gmInput"] = (ulong)(0x0001 | (sensorId << 8));
                    using var outParams = obj.InvokeMethod(AcerWmi.GamingMethods.GetSystemInfo, inParams, null);
                    var raw = (ulong)outParams["gmOutput"];
                    if ((raw & 0xFF) == 0) return (int)((raw >> 8) & 0xFFFF);
                }
                catch { InvalidateCache(); }
                return 0;
            }
        }


        public bool SetPowerMode(byte mode)
        {
            string modePref = PreySense.Overlay.AppConfig.GetString("HardwareControlMode", "service");
            if (modePref == "wmi")
            {
                var (success, _) = SendCommand(AcerWmi.GamingMethods.SetMiscSetting, (ulong)0x0B | ((ulong)mode << 8));
                if (success)
                {
                    for (int attempt = 0; attempt < 4; attempt++)
                    {
                        if (TryGetPowerProfileWmi(out byte appliedMode) && appliedMode == mode)
                        {
                            SyncWindowsPowerMode(mode);
                            return true;
                        }
                        Thread.Sleep(80);
                    }
                }

                if (EnsureAcerService() && _serviceClient.SetOperatingMode(mode))
                {
                    if (TryGetPowerProfileAcerService(out byte appliedMode) && appliedMode == mode)
                    {
                        SyncWindowsPowerMode(mode);
                        return true;
                    }
                }
            }
            else
            {
                if (EnsureAcerService() && _serviceClient.SetOperatingMode(mode))
                {
                    if (TryGetPowerProfileAcerService(out byte appliedMode) && appliedMode == mode)
                    {
                        SyncWindowsPowerMode(mode);
                        return true;
                    }
                }

                var (success, _) = SendCommand(AcerWmi.GamingMethods.SetMiscSetting, (ulong)0x0B | ((ulong)mode << 8));
                if (success)
                {
                    for (int attempt = 0; attempt < 4; attempt++)
                    {
                        if (TryGetPowerProfileWmi(out byte appliedMode) && appliedMode == mode)
                        {
                            SyncWindowsPowerMode(mode);
                            return true;
                        }
                        Thread.Sleep(80);
                    }
                }
            }

            return false;
        }

        public byte GetPowerProfile()
        {
            if (TryGetPowerProfileWmi(out byte wmiMode))
                return wmiMode;

            if (TryGetPowerProfileAcerService(out byte serviceMode))
                return serviceMode;

            return 0x01;
        }

        public bool TryGetPowerProfile(out byte mode)
        {
            string modePref = PreySense.Overlay.AppConfig.GetString("HardwareControlMode", "service");
            if (modePref == "wmi")
            {
                if (TryGetPowerProfileWmi(out mode))
                    return true;
                if (TryGetPowerProfileAcerService(out mode))
                    return true;
            }
            else
            {
                if (TryGetPowerProfileAcerService(out mode))
                    return true;
                if (TryGetPowerProfileWmi(out mode))
                    return true;
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

        public bool TryGetPowerProfileAcerService(out byte mode)
        {
            mode = 0;
            if (!EnsureAcerService())
                return false;

            string? json = _serviceClient.QueryUpdatedData(AcerWmi.Service.OperatingMode);
            if (TryParseOperatingMode(json, out mode))
                return true;

            AppLogger.Log($"TryGetPowerProfileAcerService: invalid OPERATING_MODE response: {json ?? "null"}");
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

        public int GetCpuFanSpeed()
        {
            lock (_lock)
            {
                try
                {
                    var obj = GetWmiObject();
                    if (obj == null) return 0;
                    using var inParams = obj.GetMethodParameters(AcerWmi.GamingMethods.GetFanSpeed);
                    inParams["gmInput"] = 1ul;
                    using var outParams = obj.InvokeMethod(AcerWmi.GamingMethods.GetFanSpeed, inParams, null);
                    ulong result = Convert.ToUInt64(outParams["gmOutput"]);
                    return (int)(result / 256);
                }
                catch
                {
                    InvalidateCache();
                    return 0;
                }
            }
        }

        public int GetGpuFanSpeed()
        {
            lock (_lock)
            {
                try
                {
                    var obj = GetWmiObject();
                    if (obj == null) return 0;
                    using var inParams = obj.GetMethodParameters(AcerWmi.GamingMethods.GetFanSpeed);
                    inParams["gmInput"] = 4ul;
                    using var outParams = obj.InvokeMethod(AcerWmi.GamingMethods.GetFanSpeed, inParams, null);
                    ulong result = Convert.ToUInt64(outParams["gmOutput"]);
                    return (int)(result / 256);
                }
                catch
                {
                    InvalidateCache();
                    return 0;
                }
            }
        }

        public static int ScalePercentToWmi(int percent)
        {
            return Math.Clamp(percent, 0, 100);
        }

        public void SetCpuFanSpeed(int percent)
        {
            lock (_lock)
            {
                try
                {
                    var obj = GetWmiObject();
                    if (obj == null) return;
                    using var inParams = obj.GetMethodParameters(AcerWmi.GamingMethods.SetFanSpeed);
                    int wmiPercent = ScalePercentToWmi(percent);
                    ulong val = (ulong)(((wmiPercent * 25600) / 100) & 0xFF00) + 1;
                    inParams["gmInput"] = val;
                    obj.InvokeMethod(AcerWmi.GamingMethods.SetFanSpeed, inParams, null);
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"[FanControl] CPU SetFanSpeed Exception: {ex.Message}");
                    InvalidateCache();
                }
            }
        }

        public void SetGpuFanSpeed(int percent)
        {
            lock (_lock)
            {
                try
                {
                    var obj = GetWmiObject();
                    if (obj == null) return;
                    using var inParams = obj.GetMethodParameters(AcerWmi.GamingMethods.SetFanSpeed);
                    int wmiPercent = ScalePercentToWmi(percent);
                    ulong val = (ulong)(((wmiPercent * 25600) / 100) & 0xFF00) + 4;
                    inParams["gmInput"] = val;
                    obj.InvokeMethod(AcerWmi.GamingMethods.SetFanSpeed, inParams, null);
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"[FanControl] GPU SetFanSpeed Exception: {ex.Message}");
                    InvalidateCache();
                }
            }
        }

        public bool SetFanControlWmi(int mode, int cpuSpeed = 50, int gpuSpeed = 50)
        {
            long now = Environment.TickCount64;
            bool force = (mode != _lastSetFanMode);
            
            if (mode == 2 && !force)
            {
                if (cpuSpeed == _lastSetCpuSpeed && gpuSpeed == _lastSetGpuSpeed && (now - _lastSetFanTick < 1000))
                {
                    return true; // Identical speeds, skip writing unless 1 second passed (watchdog keep-alive)
                }

                int diffCpu = Math.Abs(cpuSpeed - _lastSetCpuSpeed);
                int diffGpu = Math.Abs(gpuSpeed - _lastSetGpuSpeed);
                
                // If the speed difference is tiny (<= 2%) and it's been less than 5 seconds, skip to avoid WMI spam
                if (diffCpu <= 2 && diffGpu <= 2 && (now - _lastSetFanTick < 5000))
                {
                    return true;
                }
            }
            else if (mode == _lastSetFanMode && mode != 2)
            {
                return true; // No mode change, not custom mode, skip
            }

            bool success = SetFanBehavior((byte)mode);
            if (!success)
            {
                AppLogger.Log($"[FanControl] SetFanBehavior failed for mode={mode}");
                return false;
            }

            if (mode == 2)
            {
                SetCpuFanSpeed(Math.Clamp(cpuSpeed, 0, 100));
                SetGpuFanSpeed(Math.Clamp(gpuSpeed, 0, 100));
            }

            _lastSetFanMode = mode;
            _lastSetCpuSpeed = cpuSpeed;
            _lastSetGpuSpeed = gpuSpeed;
            _lastSetFanTick = now;
            return true;
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
            try
            {
                var profile = PreySense.Mode.ProfileManager.LoadProfile(acerMode);
                ApplyOverlayScheme(profile.WindowsPowerMode);
            }
            catch
            {
                Guid overlay = acerMode switch
                {
                    0x00 or 0x06 => OVERLAY_EFFICIENCY,
                    0x04 or 0x05 => OVERLAY_PERFORMANCE,
                    _ => OVERLAY_BALANCED
                };
                try { PowerSetActiveOverlayScheme(overlay); } catch { }
            }
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
            return status == 0x1E0000080000ul;
        }

        public void SetLedTimeout(bool enabled)
        {
            ulong val = enabled ? 0x1E0000088402ul : 0x88402ul;
            SendApgeCommand(AcerWmi.ApgeMethods.SetFunction, val);
            AppLogger.Log($"Keyboard LED backlight timeout: {(enabled ? "Enabled" : "Disabled")}");
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

        public void SetLcdOverdrive(bool enable)
        {
            if (enable && DisplayManager.IsOledScreen())
            {
                AppLogger.Log("WmiController: OLED monitor detected. Skipping LCD overdrive enable under the hood.");
                return;
            }
            var (success, _) = SendCommand(AcerWmi.GamingMethods.SetProfile, enable ? 0x1000000000010ul : 0x10ul);
            if (!success)
            {
                if (EnsureAcerService())
                {
                    _serviceClient.SetLcdOverdrive(enable);
                }
            }
        }

        #region AcerService-first (WMI fallback where noted)

        public bool SetFanControl(int mode, int cpuSpeed = 50, int gpuSpeed = 50)
        {
            string modePref = PreySense.Overlay.AppConfig.GetString("HardwareControlMode", "service");
            if (modePref == "wmi")
            {
                if (SetFanControlWmi(mode, cpuSpeed, gpuSpeed))
                {
                    return true;
                }

                if (EnsureAcerService() && _serviceClient.SetFanControl(mode, cpuSpeed, gpuSpeed))
                {
                    return true;
                }
            }
            else
            {
                if (EnsureAcerService() && _serviceClient.SetFanControl(mode, cpuSpeed, gpuSpeed))
                {
                    return true;
                }

                if (SetFanControlWmi(mode, cpuSpeed, gpuSpeed))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>AcerService GPU_MODE mux values: 1 = discrete (Ultimate), 2 = hybrid (Endurance/Standard).</summary>
        public const int GpuMuxDiscrete = AcerWmi.GpuMux.Discrete;
        public const int GpuMuxHybrid = AcerWmi.GpuMux.Hybrid;

        /// <summary>Maps UI GPU mode (0=Endurance/iGPU only, 1=Standard, 2=Ultimate) to AcerService GPU_MODE mux byte.</summary>
        public static int MapUiGpuModeToMuxMode(int uiGpuMode) => uiGpuMode switch
        {
            2 => GpuMuxDiscrete,
            _ => GpuMuxHybrid
        };

        /// <summary>
        /// GPU MUX via AcerService TCP GPU_MODE only. Reboot required for hardware MUX change.
        /// Pass <see cref="GpuMuxDiscrete"/> or <see cref="GpuMuxHybrid"/>; 0 is a no-op (Endurance uses Windows dGPU disable).
        /// </summary>
        public bool SetGpuMuxMode(int mode)
        {
            if (mode == 0)
            {
                return true;
            }

            if (EnsureAcerService())
            {
                bool serviceOk = _serviceClient.SetGpuMode(mode);
                if (serviceOk)
                {
                    AppLogger.Log($"SetGpuMuxMode: successfully set GPU mode to {mode} via AcerService.");
                    return true;
                }
                AppLogger.Log("SetGpuMuxMode: AcerService call failed, falling back to WMI/BIOS offset.");
            }

            return TrySetGpuMuxModeWmi(mode);
        }

        private bool TrySetGpuMuxModeWmi(int mode)
        {
            try
            {
                byte biosVal = mode switch
                {
                    1 => 3, // Discrete
                    2 => 1, // Auto Select
                    _ => 1
                };

                using var searcher = new ManagementObjectSearcher(AcerWmi.Namespace, "SELECT * FROM AcerBiosConfigurationTool");
                using var collection = searcher.Get();
                var biosObj = collection.Cast<ManagementObject>().FirstOrDefault();
                if (biosObj == null)
                {
                    AppLogger.Log("TrySetGpuMuxModeWmi: AcerBiosConfigurationTool WMI class not found.");
                    return false;
                }

                byte[] pwBytes = new byte[128];
                using var inParams = biosObj.GetMethodParameters("GetBiosOptions");
                inParams["PasswordLen"] = (ushort)0;
                inParams["Password"] = pwBytes;

                using var outParams = biosObj.InvokeMethod("GetBiosOptions", inParams, null);
                if (outParams == null)
                {
                    AppLogger.Log("TrySetGpuMuxModeWmi: GetBiosOptions returned null outParams.");
                    return false;
                }

                int getRetCode = 0;
                if (outParams.Properties["ReturnCode"] != null)
                    getRetCode = Convert.ToInt32(outParams["ReturnCode"]);
                else if (outParams.Properties["ReturnValue"] != null)
                    getRetCode = Convert.ToInt32(outParams["ReturnValue"]);

                if (getRetCode != 0)
                {
                    AppLogger.Log($"TrySetGpuMuxModeWmi: GetBiosOptions failed with ReturnCode/ReturnValue={getRetCode}.");
                    return false;
                }

                byte[]? data = outParams["Data"] as byte[];
                if (data == null || data.Length <= 80)
                {
                    AppLogger.Log("TrySetGpuMuxModeWmi: Retrieved data buffer is invalid or too small.");
                    return false;
                }

                data[80] = biosVal;

                using var setParams = biosObj.GetMethodParameters("SetBiosOptions");
                setParams["PasswordLen"] = (ushort)0;
                setParams["Password"] = pwBytes;
                setParams["Data"] = data;

                using var setOut = biosObj.InvokeMethod("SetBiosOptions", setParams, null);
                if (setOut == null)
                {
                    AppLogger.Log("TrySetGpuMuxModeWmi: SetBiosOptions returned null outParams.");
                    return false;
                }

                int setRetCode = -1;
                if (setOut.Properties["ReturnCode"] != null)
                    setRetCode = Convert.ToInt32(setOut["ReturnCode"]);
                else if (setOut.Properties["ReturnValue"] != null)
                    setRetCode = Convert.ToInt32(setOut["ReturnValue"]);

                if (setRetCode == 0 || setRetCode == 8)
                {
                    AppLogger.Log($"TrySetGpuMuxModeWmi: Successfully updated GPU MUX mode at BIOS offset 80 to {biosVal}.");
                    return true;
                }

                AppLogger.Log($"TrySetGpuMuxModeWmi: SetBiosOptions failed with ReturnCode/ReturnValue={setRetCode}.");
                return false;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"TrySetGpuMuxModeWmi FAILED: {ex.Message}");
                return false;
            }
        }

        #endregion

        public void Dispose()
        {
            lock (_lock)
            {
                _cachedObj?.Dispose();
                _cachedObj = null;
                _cachedApgeObj?.Dispose();
                _cachedApgeObj = null;
                _cachedBatteryObj?.Dispose();
                _cachedBatteryObj = null;
            }
            _serviceClient.Dispose();
        }
    }
}
