using System;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using PreySense;

namespace PreySense.Helpers
{
    public static class DeviceHelper
    {
        private const uint DIF_PROPERTYCHANGE = 0x00000012;
        private const uint DICS_ENABLE = 0x00000001;
        private const uint DICS_DISABLE = 0x00000002;
        private const uint DICS_PROPCHANGE = 0x00000003;
        private const uint DICS_START = 0x00000004;
        private const uint DICS_STOP = 0x00000005;
        private const uint DICS_FLAG_GLOBAL = 0x00000001;
        private const uint DIGCF_PRESENT = 0x00000002;

        private static readonly Guid DisplayClassGuid = new Guid("4d36e968-e325-11ce-bfc1-08002be10318");

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVINFO_DATA
        {
            public int cbSize;
            public Guid classGuid;
            public int devInst;
            public IntPtr reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_CLASSINSTALL_HEADER
        {
            public int cbSize;
            public int installFunction;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_PROPCHANGE_PARAMS
        {
            public SP_CLASSINSTALL_HEADER classInstallHeader;
            public int stateChange;
            public int scope;
            public int hwProfile;
        }

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, string? enumerator, IntPtr hwndParent, uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, int memberIndex, ref SP_DEVINFO_DATA deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiSetClassInstallParams(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, ref SP_PROPCHANGE_PARAMS classInstallParams, int classInstallParamsSize);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiCallClassInstaller(uint installFunction, IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SetupDiGetDeviceRegistryProperty(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, int property, out int propertyRegDataType, StringBuilder propertyBuffer, int propertyBufferSize, out int requiredSize);

        /// <summary>
        /// Disables or Enables any display device matching the hardware ID substring (e.g. "VEN_10DE" for NVIDIA dGPU).
        /// </summary>
        public static bool SetDeviceState(string hardwareIdSubstring, bool enable)
        {
            Guid displayGuid = DisplayClassGuid;
            IntPtr devInfoSet = SetupDiGetClassDevs(ref displayGuid, null, IntPtr.Zero, DIGCF_PRESENT);
            if (devInfoSet == IntPtr.Zero || devInfoSet.ToInt64() == -1)
                return false;

            bool success = false;
            try
            {
                SP_DEVINFO_DATA devInfoData = new SP_DEVINFO_DATA();
                devInfoData.cbSize = Marshal.SizeOf(devInfoData);

                int memberIndex = 0;
                while (SetupDiEnumDeviceInfo(devInfoSet, memberIndex, ref devInfoData))
                {
                    memberIndex++;

                    StringBuilder buffer = new StringBuilder(1000);
                    // 1 = SPDRP_HARDWAREID
                    if (SetupDiGetDeviceRegistryProperty(devInfoSet, ref devInfoData, 1, out _, buffer, buffer.Capacity, out _))
                    {
                        string hwId = buffer.ToString().ToUpper();
                        if (hwId.Contains(hardwareIdSubstring.ToUpper()))
                        {
                            SP_PROPCHANGE_PARAMS pcp = new SP_PROPCHANGE_PARAMS();
                            pcp.classInstallHeader.cbSize = Marshal.SizeOf(typeof(SP_CLASSINSTALL_HEADER));
                            pcp.classInstallHeader.installFunction = (int)DIF_PROPERTYCHANGE;
                            pcp.stateChange = enable ? (int)DICS_ENABLE : (int)DICS_DISABLE;
                            pcp.scope = (int)DICS_FLAG_GLOBAL;
                            pcp.hwProfile = 0;

                            if (SetupDiSetClassInstallParams(devInfoSet, ref devInfoData, ref pcp, Marshal.SizeOf(pcp)))
                            {
                                if (SetupDiCallClassInstaller(DIF_PROPERTYCHANGE, devInfoSet, ref devInfoData))
                                {
                                    success = true;
                                    AppLogger.Log($"PnP Device {(enable ? "Enabled" : "Disabled")} successfully: {hwId}");
                                }
                            }
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"SetDeviceState failed: {ex.Message}");
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(devInfoSet);
            }
            return success;
        }

        /// <summary>
        /// Restarts / power-cycles a display device node matching the hardware ID substring (e.g. "VEN_10DE")
        /// without requiring a full system reboot, matching Acer hardware service behavior.
        /// </summary>
        public static bool RestartDevice(string hardwareIdSubstring)
        {
            Guid displayGuid = DisplayClassGuid;
            IntPtr devInfoSet = SetupDiGetClassDevs(ref displayGuid, null, IntPtr.Zero, DIGCF_PRESENT);
            if (devInfoSet == IntPtr.Zero || devInfoSet.ToInt64() == -1)
                return false;

            bool success = false;
            try
            {
                SP_DEVINFO_DATA devInfoData = new SP_DEVINFO_DATA();
                devInfoData.cbSize = Marshal.SizeOf(devInfoData);

                int memberIndex = 0;
                while (SetupDiEnumDeviceInfo(devInfoSet, memberIndex, ref devInfoData))
                {
                    memberIndex++;

                    StringBuilder buffer = new StringBuilder(1000);
                    if (SetupDiGetDeviceRegistryProperty(devInfoSet, ref devInfoData, 1, out _, buffer, buffer.Capacity, out _))
                    {
                        string hwId = buffer.ToString().ToUpper();
                        if (hwId.Contains(hardwareIdSubstring.ToUpper()))
                        {
                            // First attempt: DICS_PROPCHANGE (PnP driver restart without disabling DevNode)
                            SP_PROPCHANGE_PARAMS pcp = new SP_PROPCHANGE_PARAMS();
                            pcp.classInstallHeader.cbSize = Marshal.SizeOf(typeof(SP_CLASSINSTALL_HEADER));
                            pcp.classInstallHeader.installFunction = (int)DIF_PROPERTYCHANGE;
                            pcp.stateChange = (int)DICS_PROPCHANGE;
                            pcp.scope = (int)DICS_FLAG_GLOBAL;
                            pcp.hwProfile = 0;

                            if (SetupDiSetClassInstallParams(devInfoSet, ref devInfoData, ref pcp, Marshal.SizeOf(pcp)))
                            {
                                if (SetupDiCallClassInstaller(DIF_PROPERTYCHANGE, devInfoSet, ref devInfoData))
                                {
                                    success = true;
                                    AppLogger.Log($"PnP Device Restarted (DICS_PROPCHANGE) successfully: {hwId}");
                                }
                            }

                            // Second attempt fallback: Quick Disable-then-Enable cycle
                            if (!success)
                            {
                                AppLogger.Log($"DICS_PROPCHANGE fallback: cycling Disable -> Enable for {hwId}");
                                SetDeviceState(hardwareIdSubstring, false);
                                System.Threading.Thread.Sleep(500);
                                success = SetDeviceState(hardwareIdSubstring, true);
                            }
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"RestartDevice failed: {ex.Message}");
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(devInfoSet);
            }
            return success;
        }

        /// <summary>
        /// True when NVIDIA is the running display adapter and the Intel or AMD GPU is not.
        /// A BIOS dGPU-only boot leaves the iGPU in Device Manager with Availability Off Line.
        /// </summary>
        public static bool IsDiscreteGpuOnlyDisplay()
        {
            try
            {
                bool integratedRunning = false;
                bool discreteRunning = false;
                using var searcher = new ManagementObjectSearcher("SELECT PNPDeviceID, Availability FROM Win32_VideoController");
                foreach (ManagementObject gpu in searcher.Get())
                {
                    string id = (gpu["PNPDeviceID"] as string ?? "").ToUpperInvariant();
                    bool running = Convert.ToInt32(gpu["Availability"] ?? 0) == 3;
                    if (id.Contains("VEN_8086") || id.Contains("VEN_1002"))
                        integratedRunning |= running;
                    else if (id.Contains("VEN_10DE"))
                        discreteRunning |= running;
                }

                if (discreteRunning || integratedRunning)
                    return discreteRunning && !integratedRunning;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"IsDiscreteGpuOnlyDisplay failed: {ex.Message}");
            }

            bool hasIntegrated = HasPresentDisplayDevice("VEN_8086") || HasPresentDisplayDevice("VEN_1002");
            bool hasDiscrete = HasPresentDisplayDevice("VEN_10DE");
            return hasDiscrete && !hasIntegrated;
        }

        public static bool HasPresentDisplayDevice(string hardwareIdSubstring)
        {
            Guid displayGuid = DisplayClassGuid;
            IntPtr devInfoSet = SetupDiGetClassDevs(ref displayGuid, null, IntPtr.Zero, DIGCF_PRESENT);
            if (devInfoSet == IntPtr.Zero || devInfoSet.ToInt64() == -1)
                return false;

            try
            {
                SP_DEVINFO_DATA devInfoData = new SP_DEVINFO_DATA();
                devInfoData.cbSize = Marshal.SizeOf(devInfoData);

                int memberIndex = 0;
                while (SetupDiEnumDeviceInfo(devInfoSet, memberIndex, ref devInfoData))
                {
                    memberIndex++;

                    StringBuilder buffer = new StringBuilder(1000);
                    if (SetupDiGetDeviceRegistryProperty(devInfoSet, ref devInfoData, 1, out _, buffer, buffer.Capacity, out _))
                    {
                        string hwId = buffer.ToString().ToUpperInvariant();
                        if (hwId.Contains(hardwareIdSubstring.ToUpperInvariant()))
                            return true;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"HasPresentDisplayDevice failed: {ex.Message}");
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(devInfoSet);
            }

            return false;
        }

        /// <summary>
        /// Returns true when Windows reports the matching display adapter as
        /// disabled in its PnP configuration. This distinguishes true iGPU-only
        /// mode from normal Optimus/Hybrid mode, where the NVIDIA adapter remains
        /// enabled but may be idle.
        /// </summary>
        public static bool? IsDisplayDeviceDisabled(string hardwareIdSubstring)
        {
            try
            {
                // Query video controllers specifically. Win32_PnPEntity also
                // contains NVIDIA HDMI audio and other devices, which could make
                // a disabled GPU look enabled if the wrong matching device wins.
                using var searcher = new ManagementObjectSearcher(
                    "SELECT PNPDeviceID, ConfigManagerErrorCode FROM Win32_VideoController");
                foreach (ManagementObject device in searcher.Get())
                {
                    string id = device["PNPDeviceID"] as string ?? string.Empty;
                    if (id.Contains(hardwareIdSubstring, StringComparison.OrdinalIgnoreCase))
                        return Convert.ToInt32(device["ConfigManagerErrorCode"] ?? 0) == 22;
                }
                return null;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"IsDisplayDeviceDisabled failed: {ex.Message}");
                return null;
            }
        }
    }
}
