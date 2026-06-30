using System.Drawing;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using PreySense.Helpers;

namespace PreySense
{
    /// <summary>
    /// TCP socket client for AcerService (port 46933).
    /// Uses the documented ACER binary packet protocol to send commands
    /// like LIGHTING, OPERATING_MODE, etc. AcerService handles routing
    /// to the correct hardware (EC-controlled or USB HID keyboards).
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class AcerServiceClient : IDisposable
    {
        private static readonly byte[] MAGIC = "ACER"u8.ToArray();

        /// <summary>SET_DEVICE_DATA functions exposed by AcerService (port 46933).</summary>
        public static readonly IReadOnlyList<string> SetFunctions =
        [
            AcerWmi.Service.Lighting, AcerWmi.Service.OperatingMode, AcerWmi.Service.FanControl,
            AcerWmi.Service.SoundMode, AcerWmi.Service.WinKey, AcerWmi.Service.StickyKey,
            AcerWmi.Service.BootSound, AcerWmi.Service.LcdOverdrive, AcerWmi.Service.GpuMode,
            AcerWmi.Service.PanelDfrMode
        ];

        /// <summary>GET_UPDATED_DATA query functions (packet 20).</summary>
        public static readonly IReadOnlyList<string> QueryFunctions =
        [
            AcerWmi.Service.AcStatus, AcerWmi.Service.AdaptorStatus, AcerWmi.Service.BatteryBoost,
            AcerWmi.Service.OperatingMode, AcerWmi.Service.FanControl, AcerWmi.Service.Lighting,
            AcerWmi.Service.SoundMode
        ];

        private byte[]? _aesKey;
        private bool _serviceAvailable;

        public bool IsAvailable => _serviceAvailable;

        private static readonly string[] EffectNames =
        {
            "STATIC", "BREATHING", "WAVE", "SNAKE", "RIPPLE", "NEON", "RAIN_DROP", "LIGHTNING", "SPOT", "STAR",
            "FIREBALL", "SNOW", "HEARTBEAT", "SHIFTING", "ZOOM", "METEOR", "TWINKLING", "MUSIC", "SCREEN",
            "RAINBOW", "SLASH", "BLASTING", "STACK", "MOTION_POINT", "DAZZLING", "MATRIX", "SNAKE_RAINBOW",
            "ZOOM_IN", "DYNAMIC_LIGHTING", "SWIPING", "RACING", "SPROUTING", "DISCO", "PINGPONG", "LIGHT_SHOW",
            "PULSAR_DEMO", "ROWWAVE"
        };

        public AcerServiceClient()
        {
            LoadAesKey();
        }

        private void LoadAesKey()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Acer\XSense");
                var aesKeyStr = key?.GetValue("AESkey") as string;
                if (!string.IsNullOrEmpty(aesKeyStr) && aesKeyStr.Length == 32)
                {
                    _aesKey = Encoding.ASCII.GetBytes(aesKeyStr);
                }
            }
            catch { }
        }

        private DateTime _lastCheckTime = DateTime.MinValue;

        private bool CheckServiceAvailable()
        {
            if (!_serviceAvailable && (DateTime.Now - _lastCheckTime).TotalSeconds < 5)
            {
                return false;
            }
            _lastCheckTime = DateTime.Now;
            try
            {
                using var client = new TcpClient();
                var result = client.BeginConnect("127.0.0.1", AcerWmi.Service.CommandPort, null, null);
                bool success = result.AsyncWaitHandle.WaitOne(100);
                if (success)
                {
                    client.EndConnect(result);
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Recheck if AcerService is running (e.g., after user restarts it).
        /// </summary>
        public void Refresh()
        {
            _serviceAvailable = CheckServiceAvailable();
        }

        #region Packet Building

        private byte[] BuildPacket(uint packetId, string jsonPayload)
        {
            byte[] payloadBytes = _aesKey != null
                ? EncryptAes(jsonPayload)
                : Encoding.UTF8.GetBytes(jsonPayload);

            byte[] packet = new byte[8 + payloadBytes.Length];
            MAGIC.CopyTo(packet, 0);
            BitConverter.GetBytes(packetId).CopyTo(packet, 4);
            payloadBytes.CopyTo(packet, 8);
            return packet;
        }

        private byte[] EncryptAes(string text)
        {
            using var aes = Aes.Create();
            aes.Key = _aesKey!;
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.PKCS7;
            using var enc = aes.CreateEncryptor();
            byte[] plain = Encoding.UTF8.GetBytes(text);
            return enc.TransformFinalBlock(plain, 0, plain.Length);
        }

        private string DecryptAes(byte[] data)
        {
            using var aes = Aes.Create();
            aes.Key = _aesKey!;
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.PKCS7;
            using var dec = aes.CreateDecryptor();
            byte[] plain = dec.TransformFinalBlock(data, 0, data.Length);
            return Encoding.UTF8.GetString(plain);
        }

        #endregion

        #region TCP Communication

        private string? SendCommand(uint packetId, string json)
        {
            try
            {
                using var client = new TcpClient();
                client.Connect("127.0.0.1", AcerWmi.Service.CommandPort);
                client.SendTimeout = 2000;
                client.ReceiveTimeout = 2000;

                var stream = client.GetStream();
                byte[] packet = BuildPacket(packetId, json);
                stream.Write(packet, 0, packet.Length);

                // Read response
                byte[] buf = new byte[16384];
                int read = stream.Read(buf, 0, buf.Length);
                if (read == 0) return null;

                // Check for ACER header in response
                int jsonStart = 0;
                if (read >= 8 && buf[0] == 'A' && buf[1] == 'C' && buf[2] == 'E' && buf[3] == 'R')
                {
                    jsonStart = 8;
                }

                byte[] payload = new byte[read - jsonStart];
                Array.Copy(buf, jsonStart, payload, 0, payload.Length);

                string response = _aesKey != null
                    ? DecryptAes(payload)
                    : Encoding.UTF8.GetString(payload);

                return response;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AcerService TCP failed: {ex.Message}");
                _serviceAvailable = false;
                return null;
            }
        }

        #endregion

        #region Public API

        /// <summary>
        /// Set keyboard/lightbar RGB lighting via AcerService.
        /// This routes through AcerLightingService which handles both
        /// EC-controlled and USB HID keyboards.
        /// </summary>
        public bool SetLighting(int mode, byte r, byte g, byte b, int brightness, int speed, int direction)
        {
            if (!_serviceAvailable) return false;
            if (mode < 0 || mode >= RgbProfile.ModeCount) return false;

            string effect = RgbProfile.GetServiceEffectName(mode, EffectNames);
            string color = $"#{r:X2}{g:X2}{b:X2}";
            int scaledBrightness = RgbProfile.ScaleBrightnessToService(brightness);
            int scaledSpeed = Math.Clamp(RgbProfile.NormalizeStepLevel(speed), 1, RgbProfile.StepCount);
            string sub1 = effect;
            string sub2 = RgbProfile.GetSubindexSecondary(effect);

            // Predator Sense on this hardware: device 0, duration 3, colortype 1, subindex zones 1+2.
            string json = $"{{\"Function\":\"{AcerWmi.Service.Lighting}\",\"Parameter\":{{\"device\":0,\"effect\":\"{effect}\",\"speed\":{scaledSpeed},\"duration\":3,\"direction\":{direction},\"brightness\":{scaledBrightness},\"color\":\"{color.ToLower()}\",\"colortype\":1,\"subindex\":{{\"1\":\"{sub1}\",\"2\":\"{sub2}\"}}}}}}";

            var response = SendCommand(AcerWmi.Service.SetDeviceDataPacket, json);

            if (response == null)
            {
                _serviceAvailable = false;
                return false;
            }

            _serviceAvailable = true;
            return response.Contains("\"result\"") && (response.Contains("\"0\"") || response.Contains(": 0") || response.Contains(":0"));
        }

        /// <summary>
        /// Set cover logo lighting via AcerService targeting device 4.
        /// </summary>
        public bool SetLogoLighting(int mode, byte r, byte g, byte b, int brightness, int speed)
        {
            if (!_serviceAvailable) return false;

            string effect = mode switch
            {
                2 => "BREATHING",
                3 => "NEON",
                _ => "STATIC"
            };

            int scaledBrightness = mode == 0 ? 0 : RgbProfile.ScaleBrightnessToService(brightness);
            string color = $"#{r:X2}{g:X2}{b:X2}";

            string json = $"{{\"Function\":\"{AcerWmi.Service.Lighting}\",\"Parameter\":{{\"device\":4,\"effect\":\"{effect}\",\"speed\":{speed},\"duration\":3,\"direction\":0,\"brightness\":{scaledBrightness},\"color\":\"{color.ToLower()}\",\"random\":true,\"LEDs\":[],\"dyEffect\":\"WAVE\",\"music_effect\":1,\"UserDynamicEffect\":8,\"Notification\":[{{\"name\":\"Number\",\"enable\":true}},{{\"name\":\"NewMail\",\"enable\":true}},{{\"name\":\"LowBattery\",\"enable\":true}}],\"subindex\":{{\"1\":\"{effect}\",\"2\":\"{effect}\",\"3\":\"{effect}\",\"4\":\"{effect}\"}},\"deviceName\":\"Logo Device\",\"colortype\":1}}}}";

            var response = SendCommand(AcerWmi.Service.SetDeviceDataPacket, json);

            if (response == null)
            {
                _serviceAvailable = false;
                return false;
            }

            _serviceAvailable = true;
            return response.Contains("\"result\"") && (response.Contains("\"0\"") || response.Contains(": 0") || response.Contains(":0"));
        }

        /// <summary>
        /// Send a VERSION handshake to verify communication.
        /// </summary>
        public string? Handshake()
        {
            string json = "{\"Function\":\"VERSION\"}";
            var response = SendCommand(AcerWmi.Service.InitializationPacket, json);
            return response;
        }

        /// <summary>
        /// Set keyboard static lighting colors for 4 individual zones.
        /// </summary>
        public bool SetLightingZones(Color[] zoneColors, int brightness)
        {
            if (!_serviceAvailable) return false;
            if (zoneColors == null || zoneColors.Length < 4) return false;

            int scaledBrightness = RgbProfile.ScaleBrightnessToService(brightness);

            // Format each color as hex lowercase
            string[] hexColors = new string[4];
            for (int i = 0; i < 4; i++)
            {
                hexColors[i] = $"#{zoneColors[i].R:X2}{zoneColors[i].G:X2}{zoneColors[i].B:X2}".ToLower();
            }

            // Construct LEDs array JSON
            string ledsJson = $"{{\"LED_id\":0,\"color\":\"{hexColors[0]}\",\"status\":1}}," +
                              $"{{\"LED_id\":1,\"color\":\"{hexColors[1]}\",\"status\":1}}," +
                              $"{{\"LED_id\":2,\"color\":\"{hexColors[2]}\",\"status\":1}}," +
                              $"{{\"LED_id\":3,\"color\":\"{hexColors[3]}\",\"status\":1}}";

            string json = $"{{\"Function\":\"{AcerWmi.Service.Lighting}\",\"Parameter\":{{\"device\":1,\"effect\":\"STATIC\",\"speed\":5,\"duration\":3,\"direction\":0,\"brightness\":{scaledBrightness},\"color\":\"#ffffff\",\"random\":true,\"LEDs\":[{ledsJson}],\"dyEffect\":\"WAVE\",\"music_effect\":1,\"UserDynamicEffect\":8,\"Notification\":[{{\"name\":\"Number\",\"enable\":true}},{{\"name\":\"NewMail\",\"enable\":true}},{{\"name\":\"LowBattery\",\"enable\":true}}],\"subindex\":{{\"1\":\"STATIC\",\"2\":\"STATIC\"}},\"deviceName\":\"AcerECKeyboard Device\",\"colortype\":1}}}}";

            var response = SendCommand(AcerWmi.Service.SetDeviceDataPacket, json);

            if (response == null)
            {
                _serviceAvailable = false;
                return false;
            }
            _serviceAvailable = true;
            return response.Contains("\"result\"") && (response.Contains("\"0\"") || response.Contains(": 0") || response.Contains(":0"));
        }

        /// <summary>
        /// Set GPU Mux mode via AcerService.
        /// </summary>
        public bool SetGpuMode(int mode)
        {
            if (!_serviceAvailable) return false;
            // mode = 1 (Discrete/Ultimate), mode = 2 (Hybrid/Optimus/Standard/Endurance)
            string json = $"{{\"Function\":\"{AcerWmi.Service.GpuMode}\",\"Parameter\":{{\"mode\":{mode}}}}}";
            var response = SendCommand(AcerWmi.Service.SetDeviceDataPacket, json);
            return CheckResponse(response);
        }

        public bool SetOperatingMode(int mode)
        {
            if (!_serviceAvailable) return false;
            string json = $"{{\"Function\":\"{AcerWmi.Service.OperatingMode}\",\"Parameter\":{{\"mode\":{mode}}}}}";
            var response = SendCommand(AcerWmi.Service.SetDeviceDataPacket, json);
            return CheckResponse(response);
        }

        public bool SetLcdOverdrive(bool enabled)
        {
            if (!_serviceAvailable) return false;
            int status = enabled ? 1 : 0;
            string json = $"{{\"Function\":\"{AcerWmi.Service.LcdOverdrive}\",\"Parameter\":{{\"status\":{status}}}}}";
            var response = SendCommand(AcerWmi.Service.SetDeviceDataPacket, json);
            return CheckResponse(response);
        }

        public bool SetFanControl(int mode, int cpuSpeed = 50, int gpuSpeed = 50)
        {
            if (!_serviceAvailable) return false;
            cpuSpeed = Math.Clamp(cpuSpeed, 0, 100);
            gpuSpeed = Math.Clamp(gpuSpeed, 0, 100);
            string customData = mode switch
            {
                2 => $",\"custom_fan_data\":[{{\"fan_custom_auto\":0,\"fan_custom_speed\":{cpuSpeed},\"fan_name\":\"CPU\"}},{{\"fan_custom_auto\":0,\"fan_custom_speed\":{gpuSpeed},\"fan_name\":\"GPU\"}}]",
                0 => $",\"custom_fan_data\":[{{\"fan_custom_auto\":1,\"fan_custom_speed\":{cpuSpeed},\"fan_name\":\"CPU\"}},{{\"fan_custom_auto\":1,\"fan_custom_speed\":{gpuSpeed},\"fan_name\":\"GPU\"}}]",
                _ => ""
            };
            string json = $"{{\"Function\":\"{AcerWmi.Service.FanControl}\",\"Parameter\":{{\"mode\":{mode}{customData}}}}}";
            var response = SendCommand(AcerWmi.Service.SetDeviceDataPacket, json);
            return CheckResponse(response);
        }



        /// <summary>
        /// Query current device state (packet 20 GET_UPDATED_DATA).
        /// Example: QueryUpdatedData("LIGHTING") returns active keyboard RGB JSON.
        /// </summary>
        public string? QueryUpdatedData(string function, bool log = true)
        {
            string json = $"{{\"Function\":\"{function}\"}}";
            var response = SendCommand(AcerWmi.Service.GetUpdatedDataPacket, json);
            return response;
        }

        #endregion

        #region Response Helpers

        private bool CheckResponse(string? response)
        {
            if (response == null)
            {
                _serviceAvailable = false;
                return false;
            }

            _serviceAvailable = true;
            return response.Contains("\"result\"") && (response.Contains("\"0\"") || response.Contains(": 0") || response.Contains(":0"));
        }

        #endregion

        public void Dispose() { }
    }
}
