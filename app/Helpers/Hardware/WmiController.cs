using System.Drawing;
using System.Management;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.ServiceProcess;
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

        private ManagementObject? GetWmiObject()
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

        private void InvalidateCache()
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
                    foreach (var prop in inParams.Properties.Cast<PropertyData>())
                    {
                        inputName = prop.Name;
                        break;
                    }
                    inParams[inputName] = input;
                    using var outParams = obj.InvokeMethod(method, inParams, null);
                    // Dynamically discover the output parameter name
                    string outputName = "gmOutput";
                    foreach (var prop in outParams.Properties.Cast<PropertyData>())
                    {
                        if (prop.Name != "ReturnValue") { outputName = prop.Name; break; }
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

        private TelemetryData? _telemetryCache;
        private DateTime _telemetryFetched = DateTime.MinValue;
        private static readonly TimeSpan TelemetryCacheTtl = TimeSpan.FromMilliseconds(500);

        private TelemetryData? GetTelemetryCached()
        {
            if (!EnsureAcerService()) return null;

            if (DateTime.UtcNow - _telemetryFetched < TelemetryCacheTtl && _telemetryCache != null)
                return _telemetryCache;

            _telemetryFetched = DateTime.UtcNow;
            string? json = _serviceClient.GetTelemetryData();
            _telemetryCache = json != null ? TelemetryData.Parse(json) : null;
            return _telemetryCache;
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

        private bool? _acConnectedCache;
        private DateTime _acStatusFetched = DateTime.MinValue;
        private static readonly TimeSpan AcStatusCacheTtl = TimeSpan.FromSeconds(2);

        public void InvalidateAcStatusCache()
        {
            _acConnectedCache = null;
            _acStatusFetched = DateTime.MinValue;
        }

        /// <summary>True when on AC adapter. Uses AcerService AC_STATUS when available.</summary>
        public bool TryGetAcConnected(out bool onAc)
        {
            onAc = false;
            if (_acConnectedCache.HasValue && DateTime.UtcNow - _acStatusFetched < AcStatusCacheTtl)
            {
                onAc = _acConnectedCache.Value;
                return true;
            }

            if (!EnsureAcerService()) return false;

            string? json = _serviceClient.QueryUpdatedData(AcerWmi.Service.AcStatus, log: false);
            if (string.IsNullOrEmpty(json)) return false;

            var status = Regex.Match(json, @"""status""\s*:\s*(\d+)");
            if (status.Success)
            {
                onAc = status.Groups[1].Value != "0";
                _acConnectedCache = onAc;
                _acStatusFetched = DateTime.UtcNow;
                return true;
            }

            var connected = Regex.Match(json, @"""connected""\s*:\s*(\d+)");
            if (connected.Success)
            {
                onAc = connected.Groups[1].Value != "0";
                _acConnectedCache = onAc;
                _acStatusFetched = DateTime.UtcNow;
                return true;
            }

            var result = Regex.Match(json, @"""result""\s*:\s*""?(\d+)""?");
            if (result.Success)
            {
                onAc = result.Groups[1].Value != "0";
                _acConnectedCache = onAc;
                _acStatusFetched = DateTime.UtcNow;
                return true;
            }

            return false;
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

        private bool _acerServiceBroken = false;

        public bool SetPowerMode(byte mode)
        {
            if (!_acerServiceBroken && EnsureAcerService() && _serviceClient.SetOperatingMode(mode))
            {
                // Verify if it actually applied by reading it back
                if (TryGetPowerProfileAcerService(out byte appliedMode) && appliedMode == mode)
                {
                    SyncWindowsPowerMode(mode);
                    return true;
                }
                else
                {
                    AppLogger.Log("SetPowerMode: Acer Service failed to verify/apply the mode. Falling back to WMI permanently.");
                    _acerServiceBroken = true;
                }
            }

            var (success, _) = SendCommand(AcerWmi.GamingMethods.SetMiscSetting, (ulong)0x0B | ((ulong)mode << 8));
            SyncWindowsPowerMode(mode);
            return success;
        }

        public byte GetPowerProfile()
        {
            if (!_acerServiceBroken && TryGetPowerProfileAcerService(out byte serviceMode))
                return serviceMode;

            if (TryGetPowerProfileWmi(out byte wmiMode))
                return wmiMode;

            return 0x01;
        }

        public bool TryGetPowerProfile(out byte mode)
        {
            if (!_acerServiceBroken && TryGetPowerProfileAcerService(out mode))
                return true;

            if (TryGetPowerProfileWmi(out mode))
                return true;

            mode = 0;
            return false;
        }

        public bool TryGetPowerProfileAcerService(out byte mode)
        {
            mode = 0;
            if (_acerServiceBroken || !EnsureAcerService())
                return false;

            string? json = _serviceClient.QueryUpdatedData(AcerWmi.Service.OperatingMode);
            if (TryParseOperatingMode(json, out mode))
                return true;

            AppLogger.Log($"TryGetPowerProfileAcerService: invalid OPERATING_MODE response: {json ?? "null"}. Marking service as broken.");
            _acerServiceBroken = true;
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

        public void SetCpuFanSpeed(int percent)
        {
            lock (_lock)
            {
                try
                {
                    var obj = GetWmiObject();
                    if (obj == null) return;
                    using var inParams = obj.GetMethodParameters(AcerWmi.GamingMethods.SetFanSpeed);
                    ulong val = (ulong)(((percent * 25600) / 100) & 0xFF00) + 1;
                    inParams["gmInput"] = val;
                    obj.InvokeMethod(AcerWmi.GamingMethods.SetFanSpeed, inParams, null);
                }
                catch
                {
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
                    ulong val = (ulong)(((percent * 25600) / 100) & 0xFF00) + 4;
                    inParams["gmInput"] = val;
                    obj.InvokeMethod(AcerWmi.GamingMethods.SetFanSpeed, inParams, null);
                }
                catch
                {
                    InvalidateCache();
                }
            }
        }

        public bool SetFanControlWmi(int mode, int cpuSpeed = 50, int gpuSpeed = 50)
        {
            bool success = SetFanBehavior((byte)mode);
            if (!success)
            {
                return false;
            }

            if (mode == 2)
            {
                SetCpuFanSpeed(Math.Clamp(cpuSpeed, 0, 100));
                SetGpuFanSpeed(Math.Clamp(gpuSpeed, 0, 100));
            }

            return true;
        }

        public int CpuTemp
        {
            get
            {
                var t = GetTelemetryCached();
                if (t != null && t.CpuTemp > 0) return t.CpuTemp;
                return GetSensorReading(AcerWmi.Sensors.CpuTemperature);
            }
        }

        public int GpuTemp
        {
            get
            {
                var t = GetTelemetryCached();
                if (t != null && t.GpuTemp > 0) return t.GpuTemp;
                return GetSensorReading(AcerWmi.Sensors.GpuTemperature);
            }
        }

        public int CpuFanRpm
        {
            get
            {
                var t = GetTelemetryCached();
                if (t != null && t.CpuFanSpeed > 0) return t.CpuFanSpeed;
                return GetSensorReading(AcerWmi.Sensors.CpuFanRpm);
            }
        }

        public int GpuFanRpm
        {
            get
            {
                var t = GetTelemetryCached();
                if (t != null && t.GpuFanSpeed > 0) return t.GpuFanSpeed;
                return GetSensorReading(AcerWmi.Sensors.GpuFanRpm);
            }
        }

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

        public bool GetBootAnimation()
        {
            var (_, output) = SendCommand(AcerWmi.GamingMethods.GetMiscSetting, 0x06ul);
            return output == 0x100;
        }

        public void SetBootAnimation(bool enabled) => SetBootSound(enabled);

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
            if (EnsureAcerService() && _serviceClient.SetLcdOverdrive(enable))
            {
                return;
            }

            SendCommand(AcerWmi.GamingMethods.SetProfile, enable ? 0x1000000000010ul : 0x10ul);
        }

        public void SetScreenBrightness(byte brightness)
        {
            try
            {
                using var searcher = new ManagementClass(AcerWmi.Namespace, AcerWmi.Classes.MonitorBrightnessMethods, null);
                using var instances = searcher.GetInstances();
                foreach (ManagementObject instance in instances.Cast<ManagementObject>())
                {
                    instance.InvokeMethod("WmiSetBrightness", new object[] { uint.MaxValue, brightness });
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"SetScreenBrightness FAILED: {ex.Message}");
            }
        }

        public byte GetScreenBrightness()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(AcerWmi.Namespace, $"SELECT CurrentBrightness FROM {AcerWmi.Classes.MonitorBrightness}");
                using var collection = searcher.Get();
                foreach (ManagementObject obj in collection.Cast<ManagementObject>())
                {
                    return (byte)obj["CurrentBrightness"];
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"GetScreenBrightness FAILED: {ex.Message}");
            }
            return 100; // Default fallback
        }

        public int GetUsbCharging()
        {
            ulong status = SendApgeCommand(AcerWmi.ApgeMethods.GetFunction, 0x4ul);
            if ((status & 4096) != 0)
            {
                return 0;
            }
            else
            {
                ulong percent = ((status >> 17) & 0x6Ful) * 2;
                return (int)percent;
            }
        }

        public void SetUsbCharging(int minBattery)
        {
            ulong val = minBattery switch
            {
                10 => 659204ul,
                20 => 1314564ul,
                30 => 1969924ul,
                _ => 663300ul
            };
            SendApgeCommand(AcerWmi.ApgeMethods.SetFunction, val);
        }

        #region AcerService-first (WMI fallback where noted)

        public bool SetFanControl(int mode, int cpuSpeed = 50, int gpuSpeed = 50)
        {
            if (EnsureAcerService() && _serviceClient.SetFanControl(mode, cpuSpeed, gpuSpeed))
            {
                return true;
            }

            return SetFanControlWmi(mode, cpuSpeed, gpuSpeed);
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

            RefreshAcerService();

            if (_serviceClient.IsAvailable && _serviceClient.SetGpuMode(mode))
            {
                return true;
            }

            AppLogger.Log($"SetGpuMuxMode: AcerService set failed or unavailable (mode={mode}). Trying direct WMI BIOS offset fallback.");
            return TrySetGpuMuxModeWmi(mode);
        }

        private bool TrySetGpuMuxModeWmi(int mode)
        {
            try
            {
                // Map AcerService MUX values (1 = discrete, 2 = hybrid) to BIOS offset 80 values:
                // AcerService mode 1 (Discrete) -> BIOS value 3 (Discrete GPU Only / dGPU)
                // AcerService mode 2 (Hybrid) -> BIOS value 2 (Optimus / Hybrid)
                byte biosVal = mode switch
                {
                    1 => 3, // Discrete
                    2 => 2, // Optimus
                    _ => 2
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

                // Update GPU mode at offset 80
                data[80] = biosVal;

                // Write it back
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

        public bool SetSoundMode(int mode)
        {
            if (!EnsureAcerService()) return false;
            return _serviceClient.SetSoundMode(mode);
        }

        public bool SetWinKeyLock(bool locked)
        {
            if (!EnsureAcerService()) return false;
            return _serviceClient.SetWinKeyLock(locked);
        }

        public bool SetStickyKeys(bool enabled)
        {
            if (!EnsureAcerService()) return false;
            return _serviceClient.SetStickyKeys(enabled);
        }

        public bool SetBootSound(bool enabled)
        {
            if (EnsureAcerService() && _serviceClient.SetBootSound(enabled))
            {
                return true;
            }

            var (success, _) = SendCommand(AcerWmi.GamingMethods.SetMiscSetting, enabled ? 0x106ul : 0x06ul);
            return success;
        }

        public bool SetPanelDfrMode(int mode)
        {
            if (!EnsureAcerService()) return false;
            return _serviceClient.SetPanelDfrMode(mode);
        }

        public TelemetryData? GetRichTelemetry() => GetTelemetryCached();

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
