using System;
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
    }
}
