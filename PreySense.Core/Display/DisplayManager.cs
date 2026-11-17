using System;
using System.Runtime.InteropServices;

namespace PreySense.Helpers
{
    public static class DisplayManager
    {
        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);

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

        private const int DM_DISPLAYFREQUENCY = 0x400000;

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

        /// <summary>Windows' active primary-display adapter; this does not open a vendor GPU client.</summary>
        public static string? GetPrimaryDisplayGpuName()
        {
            for (uint index = 0; ; index++)
            {
                var device = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
                if (!EnumDisplayDevices(null, index, ref device, 0)) return null;
                // DISPLAY_DEVICE_ATTACHED_TO_DESKTOP | DISPLAY_DEVICE_PRIMARY_DEVICE
                if ((device.StateFlags & 5) == 5)
                    return string.IsNullOrWhiteSpace(device.DeviceString) ? null : device.DeviceString.Trim();
            }
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
                DEVMODE fallbackDm = new();
                bool foundExact = false;
                bool foundFallback = false;
                int modeNum = 0;
                while (EnumDisplaySettings(devName, modeNum, ref targetDm))
                {
                    if (targetDm.dmPelsWidth == currentDm.dmPelsWidth &&
                        targetDm.dmPelsHeight == currentDm.dmPelsHeight &&
                        targetDm.dmDisplayFrequency == hz)
                    {
                        if (!foundFallback)
                        {
                            fallbackDm = targetDm;
                            foundFallback = true;
                        }
                        if (targetDm.dmBitsPerPel == currentDm.dmBitsPerPel &&
                            targetDm.dmDisplayOrientation == currentDm.dmDisplayOrientation &&
                            targetDm.dmDisplayFixedOutput == currentDm.dmDisplayFixedOutput)
                        {
                            foundExact = true;
                            break;
                        }
                    }
                    modeNum++;
                }

                if (!foundExact)
                {
                    if (foundFallback)
                    {
                        targetDm = fallbackDm;
                    }
                    else
                    {
                        targetDm = currentDm;
                        targetDm.dmDisplayFrequency = hz;
                        targetDm.dmFields = DM_DISPLAYFREQUENCY | 0x80000 | 0x100000;
                    }
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
    }
}
