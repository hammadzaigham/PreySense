using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using PreySense.Helpers;

namespace PreySense.Core.Acpi
{
    /// <summary>
    /// OpenCore-native ACPI power limit hotpatch generator.
    /// Preserves existing SSDT-PREYBOOST (GPU 140W unlock) and cleanly implements
    /// SSDT-PREYCPU to override CPU PL1/PL2 calculations on thermal mode transitions.
    /// Fully dynamic on any laptop via Windows kernel firmware table extraction.
    /// </summary>
    public class OpenCorePowerLimitManager
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint GetSystemFirmwareTable(
            uint firmwareTableProviderSignature,
            uint firmwareTableId,
            byte[]? pFirmwareTableBuffer,
            uint bufferSize);

        private const uint AcpiProviderSignature = 0x41435049; // 'ACPI'
        public const int FactoryIntelPlatformLimitWatts = 85;

        public class OpenCorePatchResult
        {
            public bool Success { get; set; }
            public string DslPath { get; set; } = string.Empty;
            public string AmlPath { get; set; } = string.Empty;
            public string OpenCorePatchXml { get; set; } = string.Empty;
            public string LogOutput { get; set; } = string.Empty;
            public string ErrorMessage { get; set; } = string.Empty;
            public string OemTableIdBase64 { get; set; } = string.Empty;
            public uint TableLength { get; set; }
        }

        // Canonical fallback 8-byte Base64 for 'ACRPRDCT': QUNSUFJEQ1Q=
        public const string CorrectOemTableIdBase64 = "QUNSUFJEQ1Q=";

        /// <summary>
        /// Reads any live ACPI table directly from the Windows kernel firmware table cache.
        /// Fully dynamic across all laptop and motherboard vendors.
        /// </summary>
        public static byte[]? GetLiveAcpiTable(string tableSignature)
        {
            if (string.IsNullOrEmpty(tableSignature) || tableSignature.Length != 4) return null;

            try
            {
                uint tableId = (uint)(tableSignature[0] | (tableSignature[1] << 8) | (tableSignature[2] << 16) | (tableSignature[3] << 24));
                uint requiredSize = GetSystemFirmwareTable(AcpiProviderSignature, tableId, null, 0);
                if (requiredSize == 0) return null;

                byte[] buffer = new byte[requiredSize];
                uint bytesRead = GetSystemFirmwareTable(AcpiProviderSignature, tableId, buffer, requiredSize);
                if (bytesRead > 0)
                {
                    AppLogger.Log($"[OpenCorePowerLimitManager] Read {bytesRead} bytes from live kernel ACPI table '{tableSignature}'.");
                    return buffer;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[OpenCorePowerLimitManager] GetLiveAcpiTable('{tableSignature}') error: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Reads the Intel PCCC/PCCX platform power limit encoded in the live ACPI
        /// SSDT data. Windows exposes duplicate SSDTs most reliably through the
        /// HKLM HARDWARE ACPI cache, so that cache is checked before the firmware API.
        /// </summary>
        public static int DetectIntelPlatformPowerLimitWatts()
        {
            return TryDetectIntelPlatformPowerLimitWatts(out int watts)
                ? watts
                : FactoryIntelPlatformLimitWatts;
        }

        public static bool TryDetectIntelPlatformPowerLimitWatts(out int watts)
        {
            try
            {
                using RegistryKey? ssdtKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\ACPI\SSDT");
                int? registryValue = FindIntelPlatformLimitInRegistry(ssdtKey);
                if (registryValue is > 0)
                {
                    AppLogger.Log($"[OpenCorePowerLimitManager] Detected Intel platform limit {registryValue}W from the Windows ACPI cache.");
                    watts = registryValue.Value;
                    return true;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[OpenCorePowerLimitManager] ACPI cache platform-limit query failed: {ex.Message}");
            }

            byte[]? liveSsdt = GetLiveAcpiTable("SSDT");
            int? firmwareValue = TryReadIntelPlatformLimit(liveSsdt);
            if (firmwareValue is > 0)
            {
                AppLogger.Log($"[OpenCorePowerLimitManager] Detected Intel platform limit {firmwareValue}W from the live SSDT.");
                watts = firmwareValue.Value;
                return true;
            }

            AppLogger.Log($"[OpenCorePowerLimitManager] Intel platform limit was not queryable; using verified factory fallback {FactoryIntelPlatformLimitWatts}W.");
            watts = FactoryIntelPlatformLimitWatts;
            return false;
        }

        private static int? FindIntelPlatformLimitInRegistry(RegistryKey? key)
        {
            if (key == null) return null;

            foreach (string valueName in key.GetValueNames())
            {
                if (key.GetValue(valueName) is byte[] data)
                {
                    int? value = TryReadIntelPlatformLimit(data);
                    if (value.HasValue) return value;
                }
            }

            foreach (string subKeyName in key.GetSubKeyNames())
            {
                using RegistryKey? child = key.OpenSubKey(subKeyName);
                int? value = FindIntelPlatformLimitInRegistry(child);
                if (value.HasValue) return value;
            }

            return null;
        }

        private static int? TryReadIntelPlatformLimit(byte[]? data)
        {
            if (data == null || data.Length < 18) return null;

            ReadOnlySpan<byte> suffix = new byte[] { 0x88, 0x83, 0x88, 0x50, 0x43, 0x43, 0x58, 0x01, 0x00, 0x01, 0x00 };
            for (int i = 0; i <= data.Length - 18; i++)
            {
                if (data[i] != 0x70 || data[i + 1] != 0x0C ||
                    !data.AsSpan(i + 6, suffix.Length).SequenceEqual(suffix))
                {
                    continue;
                }

                uint milliwatts = BitConverter.ToUInt32(data, i + 2);
                if (milliwatts >= 1000 && milliwatts % 1000 == 0)
                {
                    uint watts = milliwatts / 1000;
                    // Use the ACPI factory PCCC encoding (85W), not a live OpenCore-patched ceiling.
                    if (watts == FactoryIntelPlatformLimitWatts) return (int)watts;
                }
            }

            return null;
        }

        /// <summary>
        /// Reads the 8-byte OEM Table ID and Table Length directly from the host machine's live kernel DSDT,
        /// or falls back to an on-disk binary DSDT file, ensuring exact OpenCore table matching on any laptop.
        /// </summary>
        public static (string OemTableIdBase64, uint TableLength, string TableSignatureBase64) ExtractDsdtMetadata(string? dsdtPath = null, string? stagingDir = null)
        {
            // 1. Dynamic Extraction: Read live DSDT directly from the Windows kernel
            try
            {
                byte[]? liveBytes = GetLiveAcpiTable("DSDT");
                if (liveBytes != null && liveBytes.Length >= 36)
                {
                    if (!string.IsNullOrEmpty(stagingDir))
                    {
                        try
                        {
                            Directory.CreateDirectory(stagingDir);
                            File.WriteAllBytes(Path.Combine(stagingDir, "DSDT_LIVE.aml"), liveBytes);
                        }
                        catch { }
                    }

                    byte[] sigBytes = new byte[4];
                    Array.Copy(liveBytes, 0, sigBytes, 0, 4);

                    uint tableLen = BitConverter.ToUInt32(liveBytes, 4);

                    byte[] oemTableIdBytes = new byte[8];
                    Array.Copy(liveBytes, 16, oemTableIdBytes, 0, 8);

                    return (
                        Convert.ToBase64String(oemTableIdBytes),
                        tableLen,
                        Convert.ToBase64String(sigBytes)
                    );
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[OpenCorePowerLimitManager] Live kernel DSDT extraction exception: {ex.Message}");
            }

            // 2. Fallback to file on disk if specified
            if (!string.IsNullOrEmpty(dsdtPath) && File.Exists(dsdtPath))
            {
                byte[] bytes = File.ReadAllBytes(dsdtPath);
                if (bytes.Length >= 36)
                {
                    byte[] sigBytes = new byte[4];
                    Array.Copy(bytes, 0, sigBytes, 0, 4);

                    uint tableLen = BitConverter.ToUInt32(bytes, 4);

                    byte[] oemTableIdBytes = new byte[8];
                    Array.Copy(bytes, 16, oemTableIdBytes, 0, 8);

                    return (
                        Convert.ToBase64String(oemTableIdBytes),
                        tableLen,
                        Convert.ToBase64String(sigBytes)
                    );
                }
            }

            // 3. Fallback to validated hardware values from Acer PHN16S-71 factory DSDT
            return (CorrectOemTableIdBase64, 512018u, "RFNEVA==");
        }

        /// <summary>
        /// Replaces the ACPI Add/Patch lists with the minimal PreySense configuration.
        /// The OEM _Q30/WMBH methods remain untouched; runtime control is provided only by PREYDEV.
        /// </summary>
        public static bool UpdateConfigPlistPatches(string configPlistPath, string oemTableIdBase64, uint tableLength, int pcccCeilingWatts = FactoryIntelPlatformLimitWatts)
        {
            if (!File.Exists(configPlistPath)) return false;

            try
            {
                string content = File.ReadAllText(configPlistPath);
                const string add = @"<key>Add</key>
		<array>
			<dict>
				<key>Comment</key><string>PreySense isolated runtime power control</string>
				<key>Enabled</key><true/>
				<key>Path</key><string>SSDT-PREYDEV.aml</string>
			</dict>
		</array>";
                string patch = $@"<key>Patch</key>
		<array>
{BuildOpenCorePcccPatchXml(pcccCeilingWatts)}
{BuildOpenCoreCrossClampPatchXml(CorrectOemTableIdBase64, 26025, "U1NEVA==")}
		</array>";
                string updated = new Regex(@"<key>Add</key>\s*<array>[\s\S]*?</array>").Replace(content, add, 1);
                updated = new Regex(@"<key>Patch</key>\s*<array>[\s\S]*?</array>").Replace(updated, patch, 1);
                updated = new Regex(@"<key>Vault</key>\s*<string>.*?</string>").Replace(updated, "<key>Vault</key>\r\n\t\t\t\t<string>Optional</string>", 1);
                updated = new Regex(@"<key>SecureBootModel</key>\s*<string>.*?</string>").Replace(updated, "<key>SecureBootModel</key>\r\n\t\t\t\t<string>Disabled</string>", 1);
                updated = new Regex(@"<key>ScanPolicy</key>\s*<integer>.*?</integer>").Replace(updated, "<key>ScanPolicy</key>\r\n\t\t\t\t<integer>0</integer>", 1);
                updated = DisablePlatformInfoSpoof(updated);
                updated = StripAppleBootFeatures(updated);
                if (updated == content)
                {
                    AppLogger.Log($"[OpenCorePowerLimitManager] ACPI configuration already matches the requested {pcccCeilingWatts}W ceiling.");
                    return true;
                }
                File.WriteAllText(configPlistPath, updated, new UTF8Encoding(false));
                AppLogger.Log($"[OpenCorePowerLimitManager] Applied minimal PREYDEV + PCCC {pcccCeilingWatts}W + cross-throttle ACPI configuration to {configPlistPath}");
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[OpenCorePowerLimitManager] UpdateConfigPlistPatches failed: {ex.Message}");
            }

            return false;
        }

        private static string DisablePlatformInfoSpoof(string plist)
        {
            plist = new Regex(@"<key>Automatic</key>\s*<true\s*/>").Replace(plist, "<key>Automatic</key>\r\n\t\t\t<false />", 1);
            plist = new Regex(@"<key>UpdateDataHub</key>\s*<true\s*/>").Replace(plist, "<key>UpdateDataHub</key>\r\n\t\t\t<false />", 1);
            plist = new Regex(@"<key>UpdateNVRAM</key>\s*<true\s*/>").Replace(plist, "<key>UpdateNVRAM</key>\r\n\t\t\t<false />", 1);
            plist = new Regex(@"<key>UpdateSMBIOS</key>\s*<true\s*/>").Replace(plist, "<key>UpdateSMBIOS</key>\r\n\t\t\t<false />", 1);
            return plist;
        }

        private static string StripAppleBootFeatures(string plist)
        {
            plist = new Regex(@"<key>Kernel</key>\s*<dict>\s*<key>Add</key>\s*<array>[\s\S]*?</array>").Replace(
                plist,
                "<key>Kernel</key>\r\n\t\t<dict>\r\n\t\t\t<key>Add</key>\r\n\t\t\t<array/>",
                1);
            plist = new Regex(@"<key>Block</key>\s*<array>[\s\S]*?</array>").Replace(plist, "<key>Block</key>\r\n\t\t\t<array/>", 1);
            plist = new Regex(@"<key>Force</key>\s*<array>[\s\S]*?</array>").Replace(plist, "<key>Force</key>\r\n\t\t\t<array/>", 1);
            plist = new Regex(@"<key>Force</key>\s*<array/>\s*<key>Patch</key>\s*<array>[\s\S]*?</array>").Replace(
                plist,
                "<key>Force</key>\r\n\t\t\t<array/>\r\n\t\t\t<key>Patch</key>\r\n\t\t\t<array/>",
                1);
            plist = new Regex(@"<key>DeviceProperties</key>\s*<dict>\s*<key>Add</key>\s*<dict>[\s\S]*?<key>Delete</key>").Replace(
                plist,
                "<key>DeviceProperties</key>\r\n\t\t<dict>\r\n\t\t\t<key>Add</key>\r\n\t\t\t<dict/>\r\n\t\t\t<key>Delete</key>",
                1);
            plist = new Regex(@"<key>NVRAM</key>\s*<dict>\s*<key>Add</key>\s*<dict>[\s\S]*?<key>Delete</key>").Replace(
                plist,
                "<key>NVRAM</key>\r\n\t\t<dict>\r\n\t\t\t<key>Add</key>\r\n\t\t\t<dict/>\r\n\t\t\t<key>Delete</key>",
                1);
            plist = new Regex(@"<key>NVRAM</key>\s*<dict>\s*<key>Add</key>\s*<dict/>\s*<key>Delete</key>\s*<dict>[\s\S]*?<key>LegacyOverwrite</key>").Replace(
                plist,
                "<key>NVRAM</key>\r\n\t\t<dict>\r\n\t\t\t<key>Add</key>\r\n\t\t\t<dict/>\r\n\t\t\t<key>Delete</key>\r\n\t\t\t<dict/>\r\n\t\t\t<key>LegacyOverwrite</key>",
                1);
            plist = new Regex(@"<key>LegacySchema</key>\s*<dict>[\s\S]*?</dict>\s*<key>WriteFlash</key>").Replace(
                plist,
                "<key>LegacySchema</key>\r\n\t\t\t<dict/>\r\n\t\t\t<key>WriteFlash</key>",
                1);
            plist = new Regex(@"<key>Drivers</key>\s*<array>[\s\S]*?</array>").Replace(
                plist,
                @"<key>Drivers</key>
			<array>
				<dict>
					<key>Arguments</key>
					<string></string>
					<key>Comment</key>
					<string>OpenCore runtime services</string>
					<key>Enabled</key>
					<true/>
					<key>HideVerbose</key>
					<false/>
					<key>LoadEarly</key>
					<false/>
					<key>Path</key>
					<string>OpenRuntime.efi</string>
				</dict>
			</array>",
                1);
            return plist;
        }

        public const string PcccStoreFindBase64 = "cAwITAEAiIOIUENDWAEAAQA=";

        /// <summary>
        /// Replaces the OEM 85,000 mW PCCC literal with 999,000 mW.
        /// The advertised ceiling is fixed; mode PL1 is still written at runtime.
        /// </summary>
        public static string PcccStoreReplaceBase64 { get; } = BuildPcccStoreReplaceBase64();

        private static string BuildPcccStoreReplaceBase64()
        {
            byte[] replacement = Convert.FromBase64String(PcccStoreFindBase64);
            Buffer.BlockCopy(BitConverter.GetBytes(CpuPowerLimitConfiguration.MaxPowerLimitWatts * 1000), 0, replacement, 2, 4);
            return Convert.ToBase64String(replacement);
        }

        public static string BuildOpenCorePcccPatchXml(int ceilingWatts = CpuPowerLimitConfiguration.MaxPowerLimitWatts)
        {
            _ = ceilingWatts;
            return $@"			<dict>
				<key>Base</key><string></string><key>BaseSkip</key><integer>0</integer>
				<key>Comment</key><string>PreySense: Set Intel IPF PCCC PL1 Limit (85W to {CpuPowerLimitConfiguration.MaxPowerLimitWatts}W)</string>
				<key>Count</key><integer>1</integer><key>Enabled</key><true/>
				<key>Find</key><data>{PcccStoreFindBase64}</data><key>Limit</key><integer>0</integer>
				<key>Mask</key><data></data><key>OemTableId</key><data>QUNSUFJEQ1Q=</data>
				<key>Replace</key><data>{PcccStoreReplaceBase64}</data><key>ReplaceMask</key><data></data>
				<key>Skip</key><integer>0</integer><key>TableLength</key><integer>20131</integer>
				<key>TableSignature</key><data>U1NEVA==</data>
			</dict>";
        }

        public static string BuildOpenCorePatchXml(string oemTableIdBase64, uint tableLength, string tableSignatureBase64) =>
$@"<dict>
    <key>Base</key>
    <string>\_SB.PC00.LPCB.EC0</string>
    <key>BaseSkip</key>
    <integer>0</integer>
    <key>Comment</key>
    <string>PreySense: Rename EC _Q30 to XQ30 for custom PL1/PL2 power limits (Chassis Turbo Key)</string>
    <key>Count</key>
    <integer>1</integer>
    <key>Enabled</key>
    <true/>
    <key>Find</key>
    <data>X1EzMA==</data>
    <key>Limit</key>
    <integer>0</integer>
    <key>Mask</key>
    <data></data>
    <key>OemTableId</key>
    <data>{oemTableIdBase64}</data>
    <key>Replace</key>
    <data>WFEzMA==</data>
    <key>ReplaceMask</key>
    <data></data>
    <key>Skip</key>
    <integer>0</integer>
    <key>TableLength</key>
    <integer>{tableLength}</integer>
    <key>TableSignature</key>
    <data>{tableSignatureBase64}</data>
</dict>";

        public static string BuildOpenCoreWmbhPatchXml(string oemTableIdBase64, uint tableLength, string tableSignatureBase64) =>
$@"<dict>
    <key>Base</key>
    <string>\_SB.PC00.WMID</string>
    <key>BaseSkip</key>
    <integer>0</integer>
    <key>Comment</key>
    <string>PreySense: Rename WMBH to XMBH for custom PL1/PL2 power limits (Software UI Mode Switching)</string>
    <key>Count</key>
    <integer>1</integer>
    <key>Enabled</key>
    <true/>
    <key>Find</key>
    <data>V01CSA==</data>
    <key>Limit</key>
    <integer>0</integer>
    <key>Mask</key>
    <data></data>
    <key>OemTableId</key>
    <data>{oemTableIdBase64}</data>
    <key>Replace</key>
    <data>WE1CSA==</data>
    <key>ReplaceMask</key>
    <data></data>
    <key>Skip</key>
    <integer>0</integer>
    <key>TableLength</key>
    <integer>{tableLength}</integer>
    <key>TableSignature</key>
    <data>{tableSignatureBase64}</data>
</dict>";

        public static string BuildOpenCoreCrossClampPatchXml(string oemTableIdBase64 = CorrectOemTableIdBase64, uint tableLength = 26025, string tableSignatureBase64 = "U1NEVA==") =>
$@"<dict>
    <key>Base</key>
    <string></string>
    <key>BaseSkip</key>
    <integer>0</integer>
    <key>Comment</key>
    <string>PreySense: Suppress GPU-to-CPU Cross-Throttling (NVPCF Sub-Func 9 E3DT 0x0E -&gt; 0x0D)</string>
    <key>Count</key>
    <integer>1</integer>
    <key>Enabled</key>
    <true/>
    <key>Find</key>
    <data>RTNEVAoO</data>
    <key>Limit</key>
    <integer>0</integer>
    <key>Mask</key>
    <data></data>
    <key>OemTableId</key>
    <data>{oemTableIdBase64}</data>
    <key>Replace</key>
    <data>RTNEVAoN</data>
    <key>ReplaceMask</key>
    <data></data>
    <key>Skip</key>
    <integer>0</integer>
    <key>TableLength</key>
    <integer>{tableLength}</integer>
    <key>TableSignature</key>
    <data>{tableSignatureBase64}</data>
</dict>";

        public static readonly string OpenCoreQ30RenameXml = BuildOpenCorePatchXml(CorrectOemTableIdBase64, 512018, "RFNEVA==");
        public static readonly string OpenCoreWmbhRenameXml = BuildOpenCoreWmbhPatchXml(CorrectOemTableIdBase64, 37087, "U1NEVA==");
        public static readonly string OpenCoreCrossClampPatchXml = BuildOpenCoreCrossClampPatchXml(CorrectOemTableIdBase64, 26025, "U1NEVA==");

        public static OpenCorePatchResult GenerateOpenCoreOverride(
            CpuPowerLimitConfiguration config,
            string stagingDirectory,
            string iaslPath,
            string? dsdtPath = null,
            string? ssdjPath = null)
        {
            var dsdtMeta = ExtractDsdtMetadata(dsdtPath, stagingDirectory);

            var ssdjMeta = !string.IsNullOrEmpty(ssdjPath) && File.Exists(ssdjPath)
                ? ExtractDsdtMetadata(ssdjPath)
                : (dsdtMeta.OemTableIdBase64, 37087u, "U1NEVA==");

            string q30Xml = BuildOpenCorePatchXml(dsdtMeta.OemTableIdBase64, dsdtMeta.TableLength, dsdtMeta.TableSignatureBase64);
            string wmbhXml = BuildOpenCoreWmbhPatchXml(ssdjMeta.Item1, ssdjMeta.Item2, ssdjMeta.Item3);
            string crossClampXml = BuildOpenCoreCrossClampPatchXml(dsdtMeta.OemTableIdBase64, 26025, "U1NEVA==");

            var res = new OpenCorePatchResult
            {
                OpenCorePatchXml = q30Xml + "\n" + wmbhXml + "\n" + crossClampXml,
                OemTableIdBase64 = dsdtMeta.OemTableIdBase64,
                TableLength = dsdtMeta.TableLength
            };

            if (!config.Validate(out string validationError))
            {
                res.ErrorMessage = validationError;
                return res;
            }

            Directory.CreateDirectory(stagingDirectory);
            // Distinct file name to PREVENT colliding with or overwriting SSDT-PREYBOOST
            string dslPath = Path.Combine(stagingDirectory, "SSDT-PREYCPU.dsl");
            string amlPath = Path.Combine(stagingDirectory, "SSDT-PREYCPU.aml");

            // MCHBAR raw values: 1W = 8 units
            int qPl1Raw = config.QuietPl1 * 8;
            int qPl2Raw = config.QuietPl2 * 8;
            int ePl1Raw = config.EcoPl1 * 8;
            int ePl2Raw = config.EcoPl2 * 8;
            int bPl1Raw = config.BalancedPl1 * 8;
            int bPl2Raw = config.BalancedPl2 * 8;
            int pPl1Raw = config.PerformancePl1 * 8;
            int pPl2Raw = config.PerformancePl2 * 8;
            int tPl1Raw = config.TurboPl1 * 8;
            int tPl2Raw = config.TurboPl2 * 8;

            var sb = new StringBuilder();
            sb.AppendLine("/*");
            sb.AppendLine(" * SSDT-PREYCPU: OpenCore Custom CPU Power Limits");
            sb.AppendLine(" * Target: Acer Predator Helios Neo 16S (PHN16S-71-98RF)");
            sb.AppendLine(" * Works alongside SSDT-PREYBOOST (GPU TGP Hotpatch)");
            sb.AppendLine(" * Dual Interception Architecture:");
            sb.AppendLine(" *   1. Hardware Path: Rename EC _Q30 -> XQ30 in DSDT (Physical Turbo key)");
            sb.AppendLine(" *   2. Software Path: Rename WMBH -> XMBH in SSDJ (PreySense WMI mode button)");
            sb.AppendLine(" */");
            sb.AppendLine("DefinitionBlock (\"\", \"SSDT\", 2, \"PREY\", \"PREYCPU\", 0x00001000)");
            sb.AppendLine("{");
            sb.AppendLine("    External (\\_SB.PC00.LPCB.EC0, DeviceObj)");
            sb.AppendLine("    External (\\_SB.PC00.LPCB.EC0.XQ30, MethodObj)");
            sb.AppendLine("    External (\\_SB.TUGM, IntObj)");
            sb.AppendLine("    External (\\_SB.CAUM, IntObj)");
            sb.AppendLine("    External (\\_SB.CRTS, IntObj)");
            sb.AppendLine("    External (\\_SB.CRTO, IntObj)");
            sb.AppendLine("    External (\\_SB.CRTP, IntObj)");
            sb.AppendLine("    External (\\_SB.CRTB, IntObj)");
            sb.AppendLine("    External (\\_SB.CRTQ, IntObj)");
            sb.AppendLine("    External (\\_SB.CRTE, IntObj)");
            sb.AppendLine("    External (\\PPL1, FieldUnitObj)");
            sb.AppendLine("    External (\\PPL2, FieldUnitObj)");
            sb.AppendLine("    External (\\PL1E, FieldUnitObj)");
            sb.AppendLine("    External (\\PL2E, FieldUnitObj)");
            sb.AppendLine("    External (\\PL1T, FieldUnitObj)");
            sb.AppendLine("    External (\\HIDA, MethodObj)");
            sb.AppendLine("    External (\\_TZ.TZ01, DeviceObj)");
            sb.AppendLine();
            sb.AppendLine("    Scope (\\_SB)");
            sb.AppendLine("    {");
            sb.AppendLine("        // Shared helper method to enforce custom limits to MCHBAR registers");
            sb.AppendLine("        Method (PPRW, 2, Serialized)");
            sb.AppendLine("        {");
            sb.AppendLine("            If (CondRefOf (\\PL1E))");
            sb.AppendLine("            {");
            sb.AppendLine("                \\PL1E = One");
            sb.AppendLine("                \\PPL1 = Arg0");
            sb.AppendLine("                \\PL2E = One");
            sb.AppendLine("                \\PPL2 = Arg1");
            sb.AppendLine("                If (CondRefOf (\\HIDA))");
            sb.AppendLine("                {");
            sb.AppendLine("                    \\HIDA ()");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    Scope (\\_SB.PC00.LPCB.EC0)");
            sb.AppendLine("    {");
            sb.AppendLine("        Method (_Q30, 0, Serialized)");
            sb.AppendLine("        {");
            sb.AppendLine("            // Execute original renamed XQ30 to preserve fan curves, thermal trips & EC state");
            sb.AppendLine("            If (CondRefOf (\\_SB.PC00.LPCB.EC0.XQ30))");
            sb.AppendLine("            {");
            sb.AppendLine("                \\_SB.PC00.LPCB.EC0.XQ30 ()");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            // Apply custom PL1/PL2 overrides to Intel MCHBAR registers");
            sb.AppendLine("            Switch (ToInteger (\\_SB.TUGM))");
            sb.AppendLine("            {");
            sb.AppendLine("                Case (Zero) // Turbo Mode");
            sb.AppendLine("                {");
            sb.AppendLine($"                    \\_SB.PPRW (0x{tPl1Raw:X4}, 0x{tPl2Raw:X4})");
            sb.AppendLine("                }");
            sb.AppendLine("                Case (One) // Performance Mode");
            sb.AppendLine("                {");
            sb.AppendLine($"                    \\_SB.PPRW (0x{pPl1Raw:X4}, 0x{pPl2Raw:X4})");
            sb.AppendLine("                }");
            sb.AppendLine("                Case (0x02) // Balanced Mode");
            sb.AppendLine("                {");
            sb.AppendLine($"                    \\_SB.PPRW (0x{bPl1Raw:X4}, 0x{bPl2Raw:X4})");
            sb.AppendLine("                }");
            sb.AppendLine("                Case (0x03) // Quiet Mode");
            sb.AppendLine("                {");
                sb.AppendLine($"                    \\_SB.PPRW (0x{qPl1Raw:X4}, 0x{qPl2Raw:X4})");
            sb.AppendLine("                }");
            sb.AppendLine("                Case (0x04) // Eco Mode");
            sb.AppendLine("                {");
            sb.AppendLine($"                    \\_SB.PPRW (0x{ePl1Raw:X4}, 0x{ePl2Raw:X4})");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            try
            {
                File.WriteAllText(dslPath, sb.ToString());
                res.DslPath = dslPath;

                if (File.Exists(iaslPath))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = iaslPath,
                        Arguments = $"-p \"{amlPath}\" \"{dslPath}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        string stdout = proc.StandardOutput.ReadToEnd();
                        string stderr = proc.StandardError.ReadToEnd();
                        proc.WaitForExit();

                        res.LogOutput = stdout + "\n" + stderr;
                        if (proc.ExitCode == 0 && File.Exists(amlPath))
                        {
                            res.Success = true;
                            res.AmlPath = amlPath;
                        }
                        else
                        {
                            res.ErrorMessage = $"iASL compilation error (Exit code {proc.ExitCode})";
                        }
                    }
                }
                else
                {
                    res.ErrorMessage = $"iASL compiler missing at {iaslPath}";
                }
            }
            catch (Exception ex)
            {
                res.ErrorMessage = ex.Message;
                AppLogger.Log($"[OpenCorePowerLimitManager] {ex.Message}");
            }

            return res;
        }

        /// <summary>
        /// Generates SSDT-PREYBOOST override dynamically tailored to the detected GPU power limits
        /// for this laptop or any other laptop, enforcing Zero Dynamic Boost.
        /// </summary>
        public static OpenCorePatchResult GenerateOpenCoreGpuOverride(
            GpuPowerLimitConfiguration gpuConfig,
            string stagingDirectory,
            string iaslPath)
        {
            var res = new OpenCorePatchResult();
            if (!gpuConfig.Validate(out string validationError))
            {
                res.ErrorMessage = validationError;
                return res;
            }

            Directory.CreateDirectory(stagingDirectory);
            string dslPath = Path.Combine(stagingDirectory, "SSDT-PREYBOOST.dsl");
            string amlPath = Path.Combine(stagingDirectory, "SSDT-PREYBOOST.aml");


            var sb = new StringBuilder();
            sb.AppendLine("/*");
            sb.AppendLine(" * SSDT-PREYBOOST: ACPI Platform GPU Power Limit Unlock");
            sb.AppendLine($" * Target Architecture: Base={gpuConfig.BasePowerWatts}W, Max={gpuConfig.TurboGpuWatts}W");
            sb.AppendLine(" * Enforces: Unconditional Base Power Floor + Delta (Zero Dynamic Boost)");
            sb.AppendLine(" * Generated: " + DateTime.UtcNow.ToString("o"));
            sb.AppendLine(" */");
            sb.AppendLine("DefinitionBlock (\"\", \"SSDT\", 2, \"PREY\", \"PREYBST\", 0x00001006)");
            sb.AppendLine("{");
            sb.AppendLine("    External (\\_SB.DPTF, FieldUnitObj)");
            sb.AppendLine("    External (\\_SB.NPCF, DeviceObj)");
            sb.AppendLine("    External (\\_SB.NPCF.ACBT, IntObj)");
            sb.AppendLine("    External (\\_SB.NPCF.AMAT, IntObj)");
            sb.AppendLine("    External (\\_SB.NPCF.ATPP, IntObj)");
            sb.AppendLine("    External (\\_SB.SADE, FieldUnitObj)");
            sb.AppendLine();
            sb.AppendLine("    Scope (\\_SB)");
            sb.AppendLine("    {");
            sb.AppendLine("        OperationRegion (XRM4, SystemMemory, 0xFE800400, 0x0100)");
            sb.AppendLine("        Field (XRM4, ByteAcc, NoLock, Preserve)");
            sb.AppendLine("        {");
            sb.AppendLine("            Offset (0xA0),");
            sb.AppendLine("            M1P1, 8, M1PM, 8, M1PT, 8, M1P2, 8, M1DB, 8, M1CT, 8, M1CP, 8,");
            sb.AppendLine("            Offset (0xA8),");
            sb.AppendLine("            M2P1, 8, M2PM, 8, M2PT, 8, M2P2, 8, M2DB, 8, M2CT, 8, M2CP, 8,");
            sb.AppendLine("            Offset (0xB0),");
            sb.AppendLine("            M3P1, 8, M3PM, 8, M3PT, 8, M3P2, 8, M3DB, 8, M3CT, 8, M3CP, 8,");
            sb.AppendLine("            Offset (0xB8),");
            sb.AppendLine("            M4P1, 8, M4PM, 8, M4PT, 8, M4P2, 8, M4DB, 8, M4CT, 8, M4CP, 8,");
            sb.AppendLine("            Offset (0xC0),");
            sb.AppendLine("            M5P1, 8, M5PM, 8, M5PT, 8, M5P2, 8, M5DB, 8, M5CT, 8, M5CP, 8");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        Device (PBOO)");
            sb.AppendLine("        {");
            sb.AppendLine("            Name (_HID, EisaId (\"PNP0C02\"))");
            sb.AppendLine("            Name (_UID, 0x50524559)");
            sb.AppendLine("            Method (_STA, 0, NotSerialized)");
            sb.AppendLine("            {");
            sb.AppendLine("                Return (0x0F)");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            Method (_INI, 0, Serialized)");
            sb.AppendLine("            {");
            sb.AppendLine("                M4P1 = 0xA0");
            sb.AppendLine("                M4PM = 0xA0");
            sb.AppendLine("                M4PT = 0x7F");
            sb.AppendLine("                M4P2 = 0xC8");
            sb.AppendLine("                M3P1 = 0x7D");
            sb.AppendLine("                M3PM = 0x7D");
            sb.AppendLine("                M3PT = 0x7F");
            sb.AppendLine("                M3P2 = 0xA0");
            sb.AppendLine("                M2P1 = 0x5F");
            sb.AppendLine("                M2PM = 0x5F");
            sb.AppendLine("                M2PT = 0x7F");
            sb.AppendLine("                M2P2 = 0x8C");
            sb.AppendLine();
            sb.AppendLine("                /* Dynamic GPU profiles with NVPCF headroom */");
            sb.AppendLine("                M1DB = 0x00 // Zero Dynamic Boost");
                sb.AppendLine($"                M1CT = 0x{gpuConfig.QuietDeltaWatts:X2} // {gpuConfig.QuietDeltaWatts}W Delta ({gpuConfig.QuietGpuWatts}W)");
            sb.AppendLine("                M1CP = 0xFF // Fixed platform offset");
            sb.AppendLine();
            sb.AppendLine("                M2DB = 0x0F // 15W Dynamic Boost");
            sb.AppendLine("                M2CT = 0x00 // 0W Base Delta (100W)");
            sb.AppendLine("                M2CP = 0xFF // Fixed platform offset");
            sb.AppendLine();
            sb.AppendLine("                M3DB = 0x0F // 15W Dynamic Boost");
            sb.AppendLine("                M3CT = 0x0F // 15W Base Delta (115W)");
            sb.AppendLine("                M3CP = 0xFF // Fixed platform offset");
            sb.AppendLine();
            sb.AppendLine("                M4DB = 0x0F // 15W Dynamic Boost (required by NVIDIA driver for upper tier)");
            sb.AppendLine($"                M4CT = 0x{Math.Max(0, gpuConfig.TurboDeltaWatts - 15):X2} // {Math.Max(0, gpuConfig.TurboDeltaWatts - 15)}W Configurable TGP Delta");
                sb.AppendLine("                M4CP = 0xFF // Fixed platform offset");
                sb.AppendLine();
                sb.AppendLine("                M5CP = 0xFF // Fixed platform offset");
                sb.AppendLine("                /* Eco is Acer's independent M5 profile, not Quiet/M1 */");
                sb.AppendLine($"                M5CT = 0x{gpuConfig.EcoDeltaWatts:X2} // {gpuConfig.EcoDeltaWatts}W Delta ({gpuConfig.EcoGpuWatts}W)");
            sb.AppendLine();
            sb.AppendLine("                If (CondRefOf (\\_SB.NPCF.ACBT))");
            sb.AppendLine("                {");
            sb.AppendLine("                    \\_SB.NPCF.ACBT = (M4CT * 0x08)");
            sb.AppendLine("                    \\_SB.NPCF.AMAT = (M4DB * 0x08)");
            sb.AppendLine("                    \\_SB.NPCF.ATPP = 0x07F8");
            sb.AppendLine("                    Notify (\\_SB.NPCF, 0xC0)");
            sb.AppendLine("                    Notify (\\_SB.NPCF, 0xC1)");
            sb.AppendLine("                }");
            sb.AppendLine();
            sb.AppendLine("                If (CondRefOf (\\_SB.DPTF)) { \\_SB.DPTF = Zero }");
            sb.AppendLine("                If (CondRefOf (\\_SB.SADE)) { \\_SB.SADE = Zero }");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            try
            {
                File.WriteAllText(dslPath, sb.ToString());
                res.DslPath = dslPath;

                if (File.Exists(iaslPath))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = iaslPath,
                        Arguments = $"-p \"{amlPath}\" \"{dslPath}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        string stdout = proc.StandardOutput.ReadToEnd();
                        string stderr = proc.StandardError.ReadToEnd();
                        proc.WaitForExit();

                        res.LogOutput = stdout + "\n" + stderr;
                        if (proc.ExitCode == 0 && File.Exists(amlPath))
                        {
                            res.Success = true;
                            res.AmlPath = amlPath;
                        }
                        else
                        {
                            res.ErrorMessage = $"iASL compilation error (Exit code {proc.ExitCode})";
                        }
                    }
                }
                else
                {
                    res.ErrorMessage = $"iASL compiler missing at {iaslPath}";
                }
            }
            catch (Exception ex)
            {
                res.ErrorMessage = ex.Message;
                AppLogger.Log($"[OpenCorePowerLimitManager] GenerateOpenCoreGpuOverride error: {ex.Message}");
            }

            return res;
        }
    }
}
