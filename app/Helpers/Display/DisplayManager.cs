using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PreySense.Helpers
{
    public class DisplayColorProfile
    {
        public int OverrideReference { get; set; } = 0;
        public int BrightnessR { get; set; } = 100;
        public int BrightnessG { get; set; } = 100;
        public int BrightnessB { get; set; } = 100;
        public int ContrastR { get; set; } = 100;
        public int ContrastG { get; set; } = 100;
        public int ContrastB { get; set; } = 100;
        public double GammaR { get; set; } = 1.0;
        public double GammaG { get; set; } = 1.0;
        public double GammaB { get; set; } = 1.0;
        public int Saturation { get; set; } = 50;
        public int Hue { get; set; } = 0;
        public int BlueLight { get; set; } = 0;
    }

    public static class DisplayManager
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern bool SetDeviceGammaRamp(IntPtr hDC, ref GammaRamp lpRamp);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct GammaRamp
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
            public ushort[] Red;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
            public ushort[] Green;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
            public ushort[] Blue;
        }

        public static DisplayColorProfile LoadProfile(int hz)
        {
            var profile = new DisplayColorProfile();
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey($@"SOFTWARE\PreySense\Display\ColorProfiles\{hz}");
                if (key != null)
                {
                    profile.OverrideReference = (int)key.GetValue("OverrideReference", 0);
                    profile.BrightnessR = (int)key.GetValue("BrightnessR", 100);
                    profile.BrightnessG = (int)key.GetValue("BrightnessG", 100);
                    profile.BrightnessB = (int)key.GetValue("BrightnessB", 100);
                    profile.ContrastR = (int)key.GetValue("ContrastR", 100);
                    profile.ContrastG = (int)key.GetValue("ContrastG", 100);
                    profile.ContrastB = (int)key.GetValue("ContrastB", 100);
                    profile.GammaR = Convert.ToDouble(key.GetValue("GammaR", "1.0"));
                    profile.GammaG = Convert.ToDouble(key.GetValue("GammaG", "1.0"));
                    profile.GammaB = Convert.ToDouble(key.GetValue("GammaB", "1.0"));
                    profile.Saturation = (int)key.GetValue("Saturation", 50);
                    profile.Hue = (int)key.GetValue("Hue", 0);
                    profile.BlueLight = (int)key.GetValue("BlueLight", 0);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Error loading display profile for {hz}Hz: {ex.Message}");
            }
            return profile;
        }

        public static void SaveProfile(int hz, DisplayColorProfile profile)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey($@"SOFTWARE\PreySense\Display\ColorProfiles\{hz}");
                if (key != null)
                {
                    key.SetValue("OverrideReference", profile.OverrideReference);
                    key.SetValue("BrightnessR", profile.BrightnessR);
                    key.SetValue("BrightnessG", profile.BrightnessG);
                    key.SetValue("BrightnessB", profile.BrightnessB);
                    key.SetValue("ContrastR", profile.ContrastR);
                    key.SetValue("ContrastG", profile.ContrastG);
                    key.SetValue("ContrastB", profile.ContrastB);
                    key.SetValue("GammaR", profile.GammaR.ToString("0.00"));
                    key.SetValue("GammaG", profile.GammaG.ToString("0.00"));
                    key.SetValue("GammaB", profile.GammaB.ToString("0.00"));
                    key.SetValue("Saturation", profile.Saturation);
                    key.SetValue("Hue", profile.Hue);
                    key.SetValue("BlueLight", profile.BlueLight);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Error saving display profile for {hz}Hz: {ex.Message}");
            }
        }

        public static bool ApplyProfile(DisplayColorProfile profile)
        {
            var ramp = new GammaRamp
            {
                Red = new ushort[256],
                Green = new ushort[256],
                Blue = new ushort[256]
            };

            if (profile.OverrideReference == 1)
            {
                for (int i = 0; i < 256; i++)
                {
                    ushort val = (ushort)(i * 257);
                    ramp.Red[i] = val;
                    ramp.Green[i] = val;
                    ramp.Blue[i] = val;
                }
            }
            else
            {
                double bR = profile.BrightnessR / 100.0;
                double bG = profile.BrightnessG / 100.0;
                double bB = profile.BrightnessB / 100.0;

                double cR = profile.ContrastR / 100.0;
                double cG = profile.ContrastG / 100.0;
                double cB = profile.ContrastB / 100.0;

                double sat = profile.Saturation / 50.0;
                double hueRad = (profile.Hue * Math.PI) / 180.0;

                double cosH = Math.Cos(hueRad);
                double sinH = Math.Sin(hueRad);

                // Hue rotation diagonal approximations
                double hR = 0.213 + 0.787 * cosH - 0.213 * sinH;
                double hG = 0.715 + 0.285 * cosH + 0.140 * sinH;
                double hB = 0.072 + 0.928 * cosH - 0.283 * sinH;

                // Saturation diagonal approximations
                double sR = 0.213 + 0.787 * sat;
                double sG = 0.715 + 0.285 * sat;
                double sB = 0.072 + 0.928 * sat;

                double blueScale = profile.BlueLight switch
                {
                    1 => 0.82,  // Low Reduction (18%)
                    2 => 0.64,  // High Reduction (36%)
                    _ => 1.0    // Off
                };
                double greenScale = profile.BlueLight switch
                {
                    1 => 0.95,  // Low Reduction (5% shift)
                    2 => 0.90,  // High Reduction (10% shift)
                    _ => 1.0    // Off
                };

                for (int i = 0; i < 256; i++)
                {
                    double v = i / 255.0;

                    double vR = Math.Pow(v, 1.0 / profile.GammaR);
                    double vG = Math.Pow(v, 1.0 / profile.GammaG);
                    double vB = Math.Pow(v, 1.0 / profile.GammaB);

                    vR = (vR - 0.5) * cR + 0.5;
                    vG = (vG - 0.5) * cG + 0.5;
                    vB = (vB - 0.5) * cB + 0.5;

                    vR = vR * bR * hR * sR;
                    vG = vG * bG * hG * sG * greenScale;
                    vB = vB * bB * hB * sB * blueScale;

                    ramp.Red[i] = (ushort)Math.Clamp(vR * 65535.0, 0.0, 65535.0);
                    ramp.Green[i] = (ushort)Math.Clamp(vG * 65535.0, 0.0, 65535.0);
                    ramp.Blue[i] = (ushort)Math.Clamp(vB * 65535.0, 0.0, 65535.0);
                }
            }

            IntPtr hDC = GetDC(IntPtr.Zero);
            if (hDC == IntPtr.Zero) return false;

            bool result = SetDeviceGammaRamp(hDC, ref ramp);
            ReleaseDC(IntPtr.Zero, hDC);
            return result;
        }

        public class MonitorRegistryEntry
        {
            public string RegistryPath { get; set; } = "";
            public string Name { get; set; } = "";
            public byte[] OriginalEdid { get; set; } = Array.Empty<byte>();
        }

        public static string GetMonitorName(byte[] edid)
        {
            if (edid == null || edid.Length < 128) return "Unknown Monitor";
            int[] offsets = { 54, 72, 90, 108 };
            foreach (int offset in offsets)
            {
                if (offset + 18 > edid.Length) break;
                if (edid[offset] == 0x00 && edid[offset + 1] == 0x00 && edid[offset + 2] == 0x00 && edid[offset + 3] == 0xFC)
                {
                    string name = System.Text.Encoding.ASCII.GetString(edid, offset + 5, 13).Trim('\0', '\r', '\n', ' ');
                    if (!string.IsNullOrEmpty(name)) return name;
                }
            }
            if (edid.Length >= 12)
            {
                int mfgId = (edid[8] << 8) | edid[9];
                char c1 = (char)('A' + ((mfgId >> 10) & 0x1F) - 1);
                char c2 = (char)('A' + ((mfgId >> 5) & 0x1F) - 1);
                char c3 = (char)('A' + (mfgId & 0x1F) - 1);
                string code = $"{c1}{c2}{c3}";
                int prodCode = edid[10] | (edid[11] << 8);
                return $"{code} {prodCode:X4}";
            }
            return "Generic Monitor";
        }

        public static System.Collections.Generic.List<MonitorRegistryEntry> ScanMonitors()
        {
            var entries = new System.Collections.Generic.List<MonitorRegistryEntry>();
            try
            {
                using var displayKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\DISPLAY");
                if (displayKey != null)
                {
                    foreach (var modelName in displayKey.GetSubKeyNames())
                    {
                        using var modelKey = displayKey.OpenSubKey(modelName);
                        if (modelKey == null) continue;
                        foreach (var instanceName in modelKey.GetSubKeyNames())
                        {
                            using var instanceKey = modelKey.OpenSubKey(instanceName);
                            if (instanceKey == null) continue;
                            using var paramsKey = instanceKey.OpenSubKey("Device Parameters");
                            if (paramsKey == null) continue;
                            var edid = paramsKey.GetValue("EDID") as byte[];
                            if (edid != null && edid.Length >= 128)
                            {
                                entries.Add(new MonitorRegistryEntry
                                {
                                    RegistryPath = $@"SYSTEM\CurrentControlSet\Enum\DISPLAY\{modelName}\{instanceName}\Device Parameters",
                                    Name = GetMonitorName(edid),
                                    OriginalEdid = edid
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Error scanning monitors: {ex.Message}");
            }
            return entries;
        }

        public static byte[] GenerateDisplayId20Block()
        {
            byte[] block = new byte[128];

            // --- DISPLAYID 2.0 EXTENSION BLOCK HEADER ---
            block[0] = 0x70; // DisplayID Extension Tag
            block[1] = 0x20; // DisplayID Version 2.0
            block[2] = 124;  // Section payload length (remaining bytes up to checksum)
            block[3] = 0x00; // Product Use Case (0 = Same as base EDID)

            // --- DATA BLOCK HEADER: Type VII Detailed Timing (0x22) ---
            block[4] = 0x22; // Tag for Type VII Detailed Timing
            block[5] = 0x00; // Revision 0
            block[6] = 20;   // Payload length for 1 descriptor (20 bytes)

            // --- 20-BYTE DETAILED TIMING DESCRIPTOR ---
            // Pixel Clock (562637 kHz - 1 = 562636 -> 0x0895CC)
            block[7] = 0xCC; 
            block[8] = 0x95; 
            block[9] = 0x08; 
            
            // Timing Options (Preferred, Progressive, 16:10 Aspect Ratio)
            block[10] = 0x85; 
            
            // Horizontal Active (2560 -> 0x0A00)
            block[11] = 0x00; 
            block[12] = 0x0A; 
            
            // Horizontal Blanking (80 -> 0x0050)
            block[13] = 0x50; 
            block[14] = 0x00; 
            
            // Horizontal Front Porch (8 -> 0x0008, Negative Polarity)
            block[15] = 0x08; 
            block[16] = 0x00; 
            
            // Horizontal Sync Width (32 -> 0x0020)
            block[17] = 0x20; 
            block[18] = 0x00; 
            
            // Vertical Active (1600 -> 0x0640)
            block[19] = 0x40; 
            block[20] = 0x06; 
            
            // Vertical Blanking (176 -> 0x00B0)
            block[21] = 0xB0; 
            block[22] = 0x00; 
            
            // Vertical Front Porch (169 -> 0x00A9, Negative Polarity)
            block[23] = 0xA9; 
            block[24] = 0x00; 
            
            // Vertical Sync Width (3 -> 0x0003)
            block[25] = 0x03; 
            block[26] = 0x00;

            // Bytes 27 to 126 will remain 0x00 (padding)

            // --- CALCULATE CHECKSUM FOR THE EXTENSION BLOCK ---
            int sum = 0;
            for (int i = 0; i < 127; i++)
            {
                sum += block[i];
            }
            block[127] = (byte)((256 - (sum % 256)) % 256);

            return block;
        }

        public static bool IsCustomResInjected(byte[] edid)
        {
            if (edid == null || edid.Length < 256) return false;
            if (edid[126] != 0x01) return false;
            if (edid[128] != 0x70) return false;
            if (edid[129] != 0x20) return false;
            if (edid[132] != 0x22) return false;
            if (edid[135] != 0xCC || edid[136] != 0x95 || edid[137] != 0x08) return false;
            return true;
        }

        public static bool InjectEdid(string registryPath, byte[] originalEdid)
        {
            try
            {
                byte[] newEdid = new byte[256];
                Array.Copy(originalEdid, 0, newEdid, 0, Math.Min(originalEdid.Length, 128));

                newEdid[126] = 0x01;

                int sum = 0;
                for (int i = 0; i < 127; i++)
                {
                    sum += newEdid[i];
                }
                newEdid[127] = (byte)((256 - (sum % 256)) % 256);

                byte[] extBlock = GenerateDisplayId20Block();
                Array.Copy(extBlock, 0, newEdid, 128, 128);

                using var key = Registry.LocalMachine.OpenSubKey(registryPath, true);
                if (key != null)
                {
                    key.SetValue("EDID", newEdid, RegistryValueKind.Binary);
                    AppLogger.Log($"Successfully injected 256-byte custom resolution EDID into registry path: {registryPath}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Failed to inject custom EDID: {ex.Message}");
            }
            return false;
        }
    }
}
