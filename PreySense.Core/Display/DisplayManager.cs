using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PreySense.Helpers
{
    public static class DisplayManager
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern bool SetDeviceGammaRamp(IntPtr hDC, ref GammaRamp lpRamp);

        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);

        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        private static extern int ChangeDisplaySettings(ref DEVMODE devMode, int flags);

        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        private static extern int ChangeDisplaySettingsEx(string? lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, int dwflags, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }

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

        private const int DM_DISPLAYFREQUENCY = 0x400000;
        private const int CDS_UPDATEREGISTRY = 0x01;

        public static string? GetPrimaryDeviceName()
        {
            var dispDev = new DISPLAY_DEVICE();
            dispDev.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
            uint devIdx = 0;
            while (EnumDisplayDevices(null, devIdx, ref dispDev, 0))
            {
                if ((dispDev.StateFlags & 4) != 0) // DISPLAY_DEVICE_PRIMARY_DEVICE
                {
                    return dispDev.DeviceName;
                }
                devIdx++;
            }
            return null;
        }

        public static System.Collections.Generic.List<int> GetSupportedRefreshRates()
        {
            var rates = new System.Collections.Generic.List<int>();
            string? devName = GetPrimaryDeviceName();
            if (devName is null) return rates;
            DEVMODE dm = new();
            dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            int modeNum = 0;
            while (EnumDisplaySettings(devName, modeNum, ref dm))
            {
                if (dm.dmDisplayFrequency > 0 && !rates.Contains(dm.dmDisplayFrequency))
                {
                    rates.Add(dm.dmDisplayFrequency);
                }
                modeNum++;
            }
            rates.Sort();
            return rates;
        }

        public static int GetCurrentRefreshRate()
        {
            string? devName = GetPrimaryDeviceName();
            if (devName is null) return 0;
            DEVMODE dm = new();
            dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            if (EnumDisplaySettings(devName, -1, ref dm))
            {
                return dm.dmDisplayFrequency;
            }
            return 0;
        }

        public static bool SetRefreshRate(int hz)
        {
            try
            {
                string? devName = GetPrimaryDeviceName();
                if (devName is null) return false;
                DEVMODE currentDm = new();
                currentDm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
                if (!EnumDisplaySettings(devName, -1, ref currentDm))
                {
                    AppLogger.Log($"DisplayManager: Failed to get current display settings for {devName ?? "primary"}");
                    return false;
                }

                if (currentDm.dmDisplayFrequency == hz)
                {
                    AppLogger.Log($"DisplayManager: {hz}Hz is already active on {devName ?? "primary"}.");
                    return true;
                }

                DEVMODE targetDm = new();
                targetDm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
                int modeNum = 0;
                bool found = false;
                while (EnumDisplaySettings(devName, modeNum, ref targetDm))
                {
                    if (targetDm.dmPelsWidth == currentDm.dmPelsWidth &&
                        targetDm.dmPelsHeight == currentDm.dmPelsHeight &&
                        targetDm.dmDisplayFrequency == hz)
                    {
                        found = true;
                        break;
                    }
                    modeNum++;
                }

                if (!found)
                {
                    targetDm = currentDm;
                    targetDm.dmDisplayFrequency = hz;
                    targetDm.dmFields = DM_DISPLAYFREQUENCY | 0x80000 | 0x100000;
                }

                int result = ChangeDisplaySettingsEx(devName, ref targetDm, IntPtr.Zero, 0, IntPtr.Zero);

                AppLogger.Log($"DisplayManager: Set refresh rate on {devName} to {hz}Hz, result={result}");
                return result == 0;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"DisplayManager: Failed to set refresh rate to {hz}Hz: {ex.Message}");
            }
            return false;
        }

        public static void DebugChangeDisplaySettings(int hz)
        {
            DEVMODE dm = new();
            dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            Console.WriteLine($"DEVMODE size = {dm.dmSize}");
            if (EnumDisplaySettings(null, -1, ref dm))
            {
                Console.WriteLine($"EnumDisplaySettings SUCCESS: {dm.dmPelsWidth}x{dm.dmPelsHeight} @ {dm.dmDisplayFrequency}Hz, dmFields=0x{dm.dmFields:X}");
                
                // Trial 1: dmFields = DM_DISPLAYFREQUENCY only (Current code)
                var dm1 = dm;
                dm1.dmDisplayFrequency = hz;
                dm1.dmFields = DM_DISPLAYFREQUENCY;
                int res1 = ChangeDisplaySettings(ref dm1, CDS_UPDATEREGISTRY);
                Console.WriteLine($"Trial 1 (dmFields=DM_DISPLAYFREQUENCY only, CDS_UPDATEREGISTRY): result={res1}");

                // Trial 2: dmFields with width, height, freq, etc.
                var dm2 = dm;
                dm2.dmDisplayFrequency = hz;
                dm2.dmFields = 0x400000 | 0x80000 | 0x100000; // DM_DISPLAYFREQUENCY | DM_PELSWIDTH | DM_PELSHEIGHT
                int res2 = ChangeDisplaySettings(ref dm2, CDS_UPDATEREGISTRY);
                Console.WriteLine($"Trial 2 (dmFields with W+H+Freq, CDS_UPDATEREGISTRY): result={res2}");

                // Trial 3: Trial 2 with flags = 0 (dynamic test/apply)
                int res3 = ChangeDisplaySettings(ref dm2, 0);
                Console.WriteLine($"Trial 3 (flags=0 dynamic): result={res3}");

                // Trial 4: EnumDisplaySettings for the specific matching mode from list of modes
                DEVMODE modeDm = new();
                modeDm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
                int i = 0;
                while (EnumDisplaySettings(null, i, ref modeDm))
                {
                    if (modeDm.dmPelsWidth == dm.dmPelsWidth && modeDm.dmPelsHeight == dm.dmPelsHeight && modeDm.dmDisplayFrequency == hz)
                    {
                        break;
                    }
                    i++;
                }

                // Trial 5: EnumDisplayDevices + ChangeDisplaySettingsEx
                var dispDev = new DISPLAY_DEVICE();
                dispDev.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                uint devIdx = 0;
                while (EnumDisplayDevices(null, devIdx, ref dispDev, 0))
                {
                    Console.WriteLine($"DisplayDevice {devIdx}: Name={dispDev.DeviceName}, String={dispDev.DeviceString}, StateFlags=0x{dispDev.StateFlags:X}");
                    // StateFlags 1 = DISPLAY_DEVICE_ATTACHED_TO_DESKTOP, 4 = DISPLAY_DEVICE_PRIMARY_DEVICE
                    if ((dispDev.StateFlags & 1) != 0)
                    {
                        var targetDm = new DEVMODE();
                        targetDm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
                        int m = 0;
                        while (EnumDisplaySettings(dispDev.DeviceName, m, ref targetDm))
                        {
                            if (targetDm.dmPelsWidth == dm.dmPelsWidth && targetDm.dmPelsHeight == dm.dmPelsHeight && targetDm.dmDisplayFrequency == hz)
                            {
                                int res5 = ChangeDisplaySettingsEx(dispDev.DeviceName, ref targetDm, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
                                int res5Dyn = ChangeDisplaySettingsEx(dispDev.DeviceName, ref targetDm, IntPtr.Zero, 0, IntPtr.Zero);
                                Console.WriteLine($"Trial 5 ({dispDev.DeviceName} mode {m}): resultRegistry={res5}, resultDyn={res5Dyn}");
                                break;
                            }
                            m++;
                        }
                    }
                    devIdx++;
                }
            }
        }

        public static bool SetBlueLightShield(int mode)
        {
            var ramp = new GammaRamp
            {
                Red = new ushort[256],
                Green = new ushort[256],
                Blue = new ushort[256]
            };

            // Mode 0: Off
            // Mode 1: Low Reduction (18% reduction -> 0.82 blue scale)
            // Mode 2: High Reduction (36% reduction -> 0.64 blue scale)
            double blueScale = mode switch
            {
                1 => 0.82,
                2 => 0.64,
                _ => 1.0
            };
            double greenScale = mode switch
            {
                1 => 0.96,
                2 => 0.92,
                _ => 1.0
            };

            for (int i = 0; i < 256; i++)
            {
                ushort baseVal = (ushort)(i * 257);
                ramp.Red[i] = baseVal;
                ramp.Green[i] = (ushort)Math.Clamp(baseVal * greenScale, 0, 65535);
                ramp.Blue[i] = (ushort)Math.Clamp(baseVal * blueScale, 0, 65535);
            }

            IntPtr hDC = GetDC(IntPtr.Zero);
            if (hDC == IntPtr.Zero) return false;

            bool result = SetDeviceGammaRamp(hDC, ref ramp);
            ReleaseDC(IntPtr.Zero, hDC);
            string modeName = mode switch
            {
                1 => "Low (18% Reduction)",
                2 => "High (36% Reduction)",
                _ => "Off"
            };
            AppLogger.Log($"DisplayManager: BlueLight Shield set to {modeName}");
            return result;
        }

        public static bool SetBlueLightShield(bool enable) => SetBlueLightShield(enable ? 1 : 0);

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
