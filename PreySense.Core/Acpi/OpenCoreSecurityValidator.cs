using System;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PreySense.Core.Acpi
{
    public static class OpenCoreSecurityValidator
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint GetFirmwareEnvironmentVariable(
            string lpName,
            string lpGuid,
            byte[] pBuffer,
            uint nSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint EnumSystemFirmwareTables(
            uint firmwareTableProviderSignature,
            IntPtr pFirmwareTableEnumBuffer,
            uint bufferSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint GetSystemFirmwareTable(
            uint firmwareTableProviderSignature,
            uint firmwareTableId,
            byte[]? pFirmwareTableBuffer,
            uint bufferSize);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(
            IntPtr ProcessHandle,
            uint DesiredAccess,
            out IntPtr TokenHandle);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool LookupPrivilegeValue(
            string? lpSystemName,
            string lpName,
            out long lpLuid);

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct TOKEN_PRIVILEGES
        {
            public int PrivilegeCount;
            public long Luid;
            public int Attributes;
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(
            IntPtr TokenHandle,
            bool DisableAllPrivileges,
            ref TOKEN_PRIVILEGES NewState,
            int BufferLength,
            IntPtr PreviousState,
            IntPtr ReturnLength);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        private static void TryEnableSystemEnvironmentPrivilege()
        {
            try
            {
                if (OpenProcessToken(GetCurrentProcess(), 0x0020 /* TOKEN_ADJUST_PRIVILEGES */ | 0x0008 /* TOKEN_QUERY */, out IntPtr token))
                {
                    try
                    {
                        if (LookupPrivilegeValue(null, "SeSystemEnvironmentPrivilege", out long luid))
                        {
                            var tp = new TOKEN_PRIVILEGES
                            {
                                PrivilegeCount = 1,
                                Luid = luid,
                                Attributes = 0x00000002 // SE_PRIVILEGE_ENABLED
                            };
                            AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
                        }
                    }
                    finally
                    {
                        CloseHandle(token);
                    }
                }
            }
            catch { }
        }

        public enum OpenCoreIntegrationState
        {
            NotLoaded,
            Active,
            RestartRequired,
            UpdateRequired
        }

        /// <summary>
        /// Reads the OEM revision from a PREYDEV SSDT image.
        /// </summary>
        public static bool TryReadPreyDevOemRevision(ReadOnlySpan<byte> table, out uint oemRevision)
        {
            oemRevision = 0;
            if (table.Length < 28)
                return false;
            if (table[0] != (byte)'S' || table[1] != (byte)'S' || table[2] != (byte)'D' || table[3] != (byte)'T')
                return false;

            string oemId = System.Text.Encoding.ASCII.GetString(table.Slice(10, 6));
            string oemTableId = System.Text.Encoding.ASCII.GetString(table.Slice(16, 8));
            if (!oemId.StartsWith("PREY", StringComparison.OrdinalIgnoreCase) ||
                !oemTableId.StartsWith("PREYDEV", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            oemRevision = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(table.Slice(24, 4));
            return true;
        }

        /// <summary>
        /// Reads the OEM revision of the PREYDEV SSDT loaded for this boot.
        /// The firmware-table API returns only the first SSDT when many share that
        /// signature, so the per-boot ACPI registry is checked as well.
        /// </summary>
        public static bool TryGetPreyDevOemRevision(out uint oemRevision)
        {
            if (TryGetPreyDevOemRevisionFromFirmware(out oemRevision))
                return true;
            return TryGetPreyDevOemRevisionFromRegistry(out oemRevision);
        }

        private static bool TryGetPreyDevOemRevisionFromFirmware(out uint oemRevision)
        {
            oemRevision = 0;
            try
            {
                const uint acpiProvider = 0x41435049; // 'ACPI'
                uint size = EnumSystemFirmwareTables(acpiProvider, IntPtr.Zero, 0);
                if (size < 4)
                    return false;

                IntPtr pBuffer = Marshal.AllocHGlobal((int)size);
                try
                {
                    uint bytesEnum = EnumSystemFirmwareTables(acpiProvider, pBuffer, size);
                    int count = (int)bytesEnum / 4;
                    for (int i = 0; i < count; i++)
                    {
                        uint tableId = (uint)Marshal.ReadInt32(pBuffer, i * 4);
                        uint tableSize = GetSystemFirmwareTable(acpiProvider, tableId, null, 0);
                        if (tableSize < 28)
                            continue;

                        byte[] tableBuf = new byte[tableSize];
                        if (GetSystemFirmwareTable(acpiProvider, tableId, tableBuf, tableSize) < 28)
                            continue;
                        if (TryReadPreyDevOemRevision(tableBuf, out oemRevision))
                            return true;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(pBuffer);
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool TryGetPreyDevOemRevisionFromRegistry(out uint oemRevision)
        {
            oemRevision = 0;
            try
            {
                using RegistryKey? acpiKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\ACPI");
                if (acpiKey == null)
                    return false;

                foreach (string tableName in acpiKey.GetSubKeyNames())
                {
                    using RegistryKey? tableKey = acpiKey.OpenSubKey(tableName);
                    if (tableKey == null)
                        continue;

                    foreach (string oemId in tableKey.GetSubKeyNames())
                    {
                        if (!oemId.Contains("PREY", StringComparison.OrdinalIgnoreCase))
                            continue;

                        using RegistryKey? oemKey = tableKey.OpenSubKey(oemId);
                        if (oemKey == null)
                            continue;

                        foreach (string tableId in oemKey.GetSubKeyNames())
                        {
                            if (!tableId.Contains("PREYDEV", StringComparison.OrdinalIgnoreCase))
                                continue;

                            using RegistryKey? idKey = oemKey.OpenSubKey(tableId);
                            if (idKey == null)
                                continue;

                            if (idKey.GetValue("00000000") is byte[] direct &&
                                TryReadPreyDevOemRevision(direct, out oemRevision))
                            {
                                return true;
                            }

                            foreach (string revision in idKey.GetSubKeyNames())
                            {
                                using RegistryKey? revisionKey = idKey.OpenSubKey(revision);
                                if (revisionKey?.GetValue("00000000") is byte[] nested &&
                                    TryReadPreyDevOemRevision(nested, out oemRevision))
                                {
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        /// <summary>
        /// Reads the SSDT stored on the PREYSENSE volume without changing the partition.
        /// </summary>
        public static bool TryReadInstalledPreyDevRevision(out uint oemRevision)
        {
            oemRevision = 0;
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Label, Name, DeviceID FROM Win32_Volume");
                using var volumes = searcher.Get();
                foreach (ManagementObject volume in volumes)
                {
                    string label = Convert.ToString(volume["Label"]) ?? string.Empty;
                    if (!label.Equals("PREYSENSE", StringComparison.OrdinalIgnoreCase))
                        continue;

                    foreach (string? root in new[] { Convert.ToString(volume["Name"]), Convert.ToString(volume["DeviceID"]) })
                    {
                        if (string.IsNullOrWhiteSpace(root))
                            continue;

                        string amlPath = Path.Combine(root, "EFI", "OC", "ACPI", "SSDT-PREYDEV.aml");
                        if (!File.Exists(amlPath))
                            continue;

                        return TryReadPreyDevOemRevision(File.ReadAllBytes(amlPath), out oemRevision);
                    }

                    return false;
                }
            }
            catch
            {
            }

            return false;
        }

        public static OpenCoreIntegrationState DecideIntegrationState(
            bool loadedSsdtMatches,
            bool wmiReady,
            bool currentPackageInstalled,
            bool integrationPresent,
            bool installedPackageKnownOutdated = false)
        {
            if (installedPackageKnownOutdated)
                return OpenCoreIntegrationState.UpdateRequired;
            if (loadedSsdtMatches && wmiReady)
                return OpenCoreIntegrationState.Active;
            if (!loadedSsdtMatches && currentPackageInstalled)
                return OpenCoreIntegrationState.RestartRequired;
            if (integrationPresent && !loadedSsdtMatches)
                return OpenCoreIntegrationState.UpdateRequired;
            return OpenCoreIntegrationState.NotLoaded;
        }

        public static bool DetectIsOpenCoreLoaded()
        {
            // 1. Query UEFI NVRAM opencore-version variable (GUID: {4D1FDA02-38C7-4A6A-9CC6-4BCCA8B30102})
            // Requires SeSystemEnvironmentPrivilege, which we attempt to enable in the process token.
            try
            {
                TryEnableSystemEnvironmentPrivilege();
                byte[] buf = new byte[256];
                uint read = GetFirmwareEnvironmentVariable("opencore-version", "{4D1FDA02-38C7-4A6A-9CC6-4BCCA8B30102}", buf, (uint)buf.Length);
                if (read > 0)
                {
                    return true;
                }
            }
            catch { }

            // 2. Query Windows registry ACPI table registry map (HKLM\HARDWARE\ACPI)
            // When OpenCore boots, injected tables (e.g. SSDT-PREYCPU, SSDT-PREYBST, SSDT-PREYDEV)
            // and hotpatched tables (e.g. SSDJ containing RTNEVAoN) are loaded into HKLM\HARDWARE\ACPI.
            // This registry check is unprivileged and accessible to all user processes.
            try
            {
                using var acpiKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\ACPI");
                if (acpiKey != null)
                {
                    foreach (var tableName in acpiKey.GetSubKeyNames())
                    {
                        using var tableKey = acpiKey.OpenSubKey(tableName);
                        if (tableKey == null) continue;

                        foreach (var oemId in tableKey.GetSubKeyNames())
                        {
                            if (oemId.Equals("PREY", StringComparison.OrdinalIgnoreCase))
                            {
                                return true;
                            }

                            using var oemKey = tableKey.OpenSubKey(oemId);
                            if (oemKey == null) continue;

                            foreach (var tableId in oemKey.GetSubKeyNames())
                            {
                                if (tableId.Contains("PREY", StringComparison.OrdinalIgnoreCase))
                                {
                                    return true;
                                }

                                using var revKey = oemKey.OpenSubKey(tableId);
                                if (revKey == null) continue;

                                foreach (var rev in revKey.GetSubKeyNames())
                                {
                                    using var leafKey = revKey.OpenSubKey(rev);
                                    if (leafKey?.GetValue("00000000") is byte[] data && data.Length > 0)
                                    {
                                        string s = System.Text.Encoding.ASCII.GetString(data);
                                        if (s.Contains("RTNEVAoN") || s.Contains("PREY") || s.Contains("PBOO"))
                                        {
                                            return true;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            // 3. Query all live ACPI tables via Win32 EnumSystemFirmwareTables / GetSystemFirmwareTable
            try
            {
                uint acpiProvider = 0x41435049; // 'ACPI'
                uint size = EnumSystemFirmwareTables(acpiProvider, IntPtr.Zero, 0);
                if (size > 0)
                {
                    IntPtr pBuffer = Marshal.AllocHGlobal((int)size);
                    try
                    {
                        uint bytesEnum = EnumSystemFirmwareTables(acpiProvider, pBuffer, size);
                        int count = (int)bytesEnum / 4;
                        for (int i = 0; i < count; i++)
                        {
                            uint tableId = (uint)Marshal.ReadInt32(pBuffer, i * 4);
                            uint tableSize = GetSystemFirmwareTable(acpiProvider, tableId, null, 0);
                            if (tableSize > 0)
                            {
                                byte[] tableBuf = new byte[tableSize];
                                if (GetSystemFirmwareTable(acpiProvider, tableId, tableBuf, tableSize) > 0)
                                {
                                    if (tableBuf.Length >= 24)
                                    {
                                        string oemId = System.Text.Encoding.ASCII.GetString(tableBuf, 10, 6);
                                        string oemTableId = System.Text.Encoding.ASCII.GetString(tableBuf, 16, 8);
                                        if (oemId.Contains("PREY", StringComparison.OrdinalIgnoreCase) ||
                                            oemTableId.Contains("PREY", StringComparison.OrdinalIgnoreCase))
                                        {
                                            return true;
                                        }
                                    }

                                    string tableStr = System.Text.Encoding.ASCII.GetString(tableBuf);
                                    if (tableStr.Contains("RTNEVAoN") || tableStr.Contains("PREY") || tableStr.Contains("PBOO"))
                                    {
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(pBuffer);
                    }
                }
            }
            catch { }

            // 4. Query Windows registry cached firmware variables for OpenCore GUID
            try
            {
                using var nvramKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\FirmwareEnvironmentVariables\{4d1fda02-38c7-4a6a-9cc6-4bcca8b30102}");
                if (nvramKey != null)
                {
                    return true;
                }
            }
            catch { }

            return false;
        }

        public static string GetAcpiOverrideLabel(OpenCoreIntegrationState state) => state switch
        {
            OpenCoreIntegrationState.Active => "OpenCore Active",
            OpenCoreIntegrationState.RestartRequired => "Restart Required",
            OpenCoreIntegrationState.UpdateRequired => "OpenCore Update Required",
            _ => "OpenCore Not Loaded"
        };

        /// <summary>
        /// True when the hidden PREYSENSE partition is on a disk. This is independent
        /// of whether the current boot actually loaded OpenCore.
        /// </summary>
        public static bool IsPreySensePartitionPresent()
        {
            if (VolumeLabelExists("PREYSENSE"))
                return true;

            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage", "SELECT GptType, Size FROM MSFT_Partition");
                using var partitions = searcher.Get();
                foreach (ManagementObject partition in partitions)
                {
                    string gptType = Convert.ToString(partition["GptType"]) ?? string.Empty;
                    ulong size = Convert.ToUInt64(partition["Size"] ?? 0UL);
                    if (gptType.Contains("c0de0001-5052-4559-534e-4f4330310001", StringComparison.OrdinalIgnoreCase))
                        return true;
                    if (gptType.Contains("c12a7328-f81f-11d2-ba4b-00a0c93ec93b", StringComparison.OrdinalIgnoreCase)
                        && size is >= 8UL * 1024 * 1024 and <= 32UL * 1024 * 1024)
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool VolumeLabelExists(string label)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Label FROM Win32_Volume");
                using var volumes = searcher.Get();
                foreach (ManagementObject volume in volumes)
                {
                    string volumeLabel = Convert.ToString(volume["Label"]) ?? string.Empty;
                    if (volumeLabel.Equals(label, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }
    }
}
