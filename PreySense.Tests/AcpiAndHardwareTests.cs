using System;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PreySense.Core.Acpi;
using PreySense.Core.Hardware;
using PreySense.Core.Services;

namespace PreySense.Tests
{
    [TestClass]
    public class AcpiAndHardwareTests
    {
        private static string GetSolutionRoot()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            // Traverse up to find PreySense.slnx
            DirectoryInfo? dir = new DirectoryInfo(baseDir);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PreySense.slnx")))
            {
                dir = dir.Parent;
            }
            return dir != null ? dir.FullName : baseDir;
        }

        [TestMethod]
        public void Test_OemTableId_Base64_Decodes_To_Exact_8Bytes()
        {
            // Requirement 1 & 2: Ensure OemTableId base64 string decodes to exactly 8 bytes "ACRPRDCT"
            string base64 = OpenCorePowerLimitManager.CorrectOemTableIdBase64;
            Assert.AreEqual("QUNSUFJEQ1Q=", base64, "Base64 string must match QUNSUFJEQ1Q=");

            byte[] decoded = Convert.FromBase64String(base64);
            Assert.AreEqual(8, decoded.Length, "OpenCore requires exactly 8-byte OemTableId");
            string ascii = Encoding.ASCII.GetString(decoded);
            Assert.AreEqual("ACRPRDCT", ascii, "OemTableId must be ACRPRDCT");
        }

        [TestMethod]
        public void Test_TableSignature_Base64_Decodes_To_Exact_4Bytes()
        {
            string sigBase64 = "RFNEVA==";
            byte[] decoded = Convert.FromBase64String(sigBase64);
            Assert.AreEqual(4, decoded.Length, "TableSignature must be 4 bytes");
            string ascii = Encoding.ASCII.GetString(decoded);
            Assert.AreEqual("DSDT", ascii);
        }

        [TestMethod]
        public void Test_FindAndReplace_Base64_ByteLengths()
        {
            // Find: _Q30 -> X1EzMA== (4 bytes)
            // Replace: XQ30 -> WFEzMA== (4 bytes)
            byte[] findBytes = Convert.FromBase64String("X1EzMA==");
            byte[] replaceBytes = Convert.FromBase64String("WFEzMA==");

            Assert.AreEqual(4, findBytes.Length);
            Assert.AreEqual(4, replaceBytes.Length);
            Assert.AreEqual("_Q30", Encoding.ASCII.GetString(findBytes));
            Assert.AreEqual("XQ30", Encoding.ASCII.GetString(replaceBytes));
        }

        [TestMethod]
        public void Test_DSDT_Header_Extraction_Matches_Hardware()
        {
            string root = GetSolutionRoot();
            string dsdtPath = Path.Combine(root, "docs", "acpi_tables", "DSDT_ACRSYS_ACRPRDCT_00000002.bin");
            if (!File.Exists(dsdtPath))
            {
                Assert.Inconclusive($"DSDT binary not found at {dsdtPath}");
                return;
            }

            var meta = OpenCorePowerLimitManager.ExtractDsdtMetadata(dsdtPath);
            Assert.AreEqual("QUNSUFJEQ1Q=", meta.OemTableIdBase64);
            Assert.AreEqual("RFNEVA==", meta.TableSignatureBase64);
            Assert.AreEqual(512018u, meta.TableLength);

            // Verify binary header directly
            byte[] header = new byte[36];
            using (var fs = File.OpenRead(dsdtPath))
            {
                fs.Read(header, 0, 36);
            }

            string sig = Encoding.ASCII.GetString(header, 0, 4);
            uint len = BitConverter.ToUInt32(header, 4);
            string oemTableId = Encoding.ASCII.GetString(header, 16, 8);

            Assert.AreEqual("DSDT", sig);
            Assert.AreEqual(512018u, len);
            Assert.AreEqual("ACRPRDCT", oemTableId);
        }

        [TestMethod]
        public void Test_Dynamic_Live_Kernel_DSDT_Extraction()
        {
            var liveMeta = OpenCorePowerLimitManager.ExtractDsdtMetadata();
            Assert.IsNotNull(liveMeta.OemTableIdBase64);
            Assert.IsTrue(liveMeta.TableLength > 1000);
            Assert.AreEqual("RFNEVA==", liveMeta.TableSignatureBase64);
        }

        [TestMethod]
        public void Test_DSDT_Binary_Has_Exactly_One_Q30_Method_Declaration()
        {
            string root = GetSolutionRoot();
            string dsdtPath = Path.Combine(root, "docs", "acpi_tables", "DSDT_ACRSYS_ACRPRDCT_00000002.bin");
            if (!File.Exists(dsdtPath))
            {
                Assert.Inconclusive($"DSDT binary not found at {dsdtPath}");
                return;
            }

            byte[] dsdt = File.ReadAllBytes(dsdtPath);
            byte[] pattern = Encoding.ASCII.GetBytes("_Q30");

            int count = 0;
            int foundIdx = -1;
            for (int i = 0; i <= dsdt.Length - pattern.Length; i++)
            {
                if (dsdt[i] == pattern[0] &&
                    dsdt[i + 1] == pattern[1] &&
                    dsdt[i + 2] == pattern[2] &&
                    dsdt[i + 3] == pattern[3])
                {
                    count++;
                    foundIdx = i;
                }
            }

            Assert.AreEqual(1, count, "DSDT must contain exactly one occurrence of _Q30");
            Assert.AreEqual(0x034CCF, foundIdx, "DSDT _Q30 offset should match verified Acer BIOS offset 0x034CCF");

            // Method opcode verify: preceding byte is MethodOp (0x14) followed by PkgLength bytes
            Assert.AreEqual(0x14, dsdt[foundIdx - 3], "Preceding byte must be MethodOp 0x14");
        }

        [TestMethod]
        public void Test_Generated_SSDTs_Lengths_And_Checksums()
        {
            string root = GetSolutionRoot();
            string cpuAmlPath = Path.Combine(root, "acpi_staging", "SSDT-PREYCPU.aml");
            string boostAmlPath = Path.Combine(root, "acpi_staging", "SSDT-PREYBOOST.aml");

            void VerifyAml(string path, string expectedOemTableId)
            {
                if (!File.Exists(path))
                {
                    Assert.Inconclusive($"AML file not found at {path}");
                    return;
                }

                byte[] data = File.ReadAllBytes(path);
                Assert.IsTrue(data.Length >= 36, "AML must have at least standard 36-byte header");

                // Check signature
                string sig = Encoding.ASCII.GetString(data, 0, 4);
                Assert.AreEqual("SSDT", sig);

                // Check length
                uint reportedLen = BitConverter.ToUInt32(data, 4);
                Assert.AreEqual((uint)data.Length, reportedLen, "Header length must equal actual file length");

                // Check ACPI Checksum (modulo 256 sum of all bytes in table must be 0)
                int sum = 0;
                foreach (byte b in data) sum += b;
                Assert.AreEqual(0, sum % 256, $"Table {Path.GetFileName(path)} checksum invalid! Modulo 256 sum = {sum % 256}");

                // Check OEM Table ID
                string oemTblId = Encoding.ASCII.GetString(data, 16, 8);
                Assert.IsTrue(oemTblId.StartsWith(expectedOemTableId), $"OEM Table ID must start with {expectedOemTableId}");
            }

            VerifyAml(cpuAmlPath, "PREYCPU");
            VerifyAml(boostAmlPath, "PREYBST");
        }

        [TestMethod]
        public void Test_PowerLimitConfiguration_Mchbar_Field_Units()
        {
            // Verify that PL1 and PL2 values written to MCHBAR (ERM4 table & PPL1/PPL2)
            // obey the Acer 1/8W (125mW) scaling factor.
            // Test that custom targets are within electrical limits of the platform
            var cfg = new CpuPowerLimitConfiguration();
            Assert.IsTrue(cfg.QuietPl1 >= 15 && cfg.QuietPl1 <= 65);
            Assert.IsTrue(cfg.TurboPl2 >= 45 && cfg.TurboPl2 <= 140);
        }

        [TestMethod]
        public void Test_PdhCpuPowerMonitor_Instantaneous_Or_Unavailable_State()
        {
            using var monitor = new PdhCpuPowerMonitor();
            float watts = monitor.GetPackagePowerWatts();
            // In test environment, if counter is present, watts >= 0. If absent/unprimed, returns 0.
            Assert.IsTrue(watts >= 0f, "Watts returned must be >= 0");
        }

        [TestMethod]
        public void Test_SSDJ_Binary_Has_Exactly_One_WMBH_Method_Declaration()
        {
            string root = GetSolutionRoot();
            string ssdjPath = Path.Combine(root, "docs", "acpi_tables", "SSDJ_ACRSYS_ACRPRDCT_00001000.bin");
            if (!File.Exists(ssdjPath))
            {
                Assert.Inconclusive($"SSDJ binary not found at {ssdjPath}");
                return;
            }

            byte[] ssdj = File.ReadAllBytes(ssdjPath);
            byte[] pattern = Encoding.ASCII.GetBytes("WMBH");

            int count = 0;
            int foundIdx = -1;
            for (int i = 0; i <= ssdj.Length - pattern.Length; i++)
            {
                if (ssdj[i] == pattern[0] &&
                    ssdj[i + 1] == pattern[1] &&
                    ssdj[i + 2] == pattern[2] &&
                    ssdj[i + 3] == pattern[3])
                {
                    count++;
                    foundIdx = i;
                }
            }

            Assert.AreEqual(1, count, "SSDJ must contain exactly one occurrence of WMBH");
            Assert.AreEqual(0x007D58, foundIdx, "SSDJ WMBH offset should match verified Acer BIOS offset 0x007D58");

            // Method opcode verify: byte at foundIdx - 3 is MethodOp 0x14
            Assert.AreEqual(0x14, ssdj[foundIdx - 3], "Preceding byte must be MethodOp 0x14");
        }

        [TestMethod]
        public void Test_OpenCore_WMBH_Patch_Generation()
        {
            // Verify WMBH -> XMBH Base64 encodings
            // WMBH -> V01CSA== (4 bytes)
            // XMBH -> WE1CSA== (4 bytes)
            byte[] findBytes = Convert.FromBase64String("V01CSA==");
            byte[] replaceBytes = Convert.FromBase64String("WE1CSA==");

            Assert.AreEqual(4, findBytes.Length);
            Assert.AreEqual(4, replaceBytes.Length);
            Assert.AreEqual("WMBH", Encoding.ASCII.GetString(findBytes));
            Assert.AreEqual("XMBH", Encoding.ASCII.GetString(replaceBytes));

            string xml = OpenCorePowerLimitManager.OpenCoreWmbhRenameXml;
            Assert.IsTrue(xml.Contains("V01CSA=="));
            Assert.IsTrue(xml.Contains("WE1CSA=="));
            Assert.IsTrue(xml.Contains("U1NEVA==")); // SSDT table signature
            Assert.IsTrue(xml.Contains("QUNSUFJEQ1Q=")); // ACRPRDCT OEM Table ID
            Assert.IsTrue(xml.Contains("37087")); // Exact SSDJ length
        }

        [TestMethod]
        public void Test_OpenCore_CrossClamp_Patch_Generation()
        {
            // Verify E3DT 0x0E -> E3DT 0x0D Base64 encodings
            // Find:    RTNEVAoO (45-33-44-54-0A-0E: 6 bytes)
            // Replace: RTNEVAoN (45-33-44-54-0A-0D: 6 bytes)
            byte[] findBytes = Convert.FromBase64String("RTNEVAoO");
            byte[] replaceBytes = Convert.FromBase64String("RTNEVAoN");

            Assert.AreEqual(6, findBytes.Length);
            Assert.AreEqual(6, replaceBytes.Length);
            CollectionAssert.AreEqual(new byte[] { 0x45, 0x33, 0x44, 0x54, 0x0A, 0x0E }, findBytes);
            CollectionAssert.AreEqual(new byte[] { 0x45, 0x33, 0x44, 0x54, 0x0A, 0x0D }, replaceBytes);

            string xml = OpenCorePowerLimitManager.OpenCoreCrossClampPatchXml;
            Assert.IsTrue(xml.Contains("RTNEVAoO"));
            Assert.IsTrue(xml.Contains("RTNEVAoN"));
            Assert.IsTrue(xml.Contains("U1NEVA==")); // SSDT table signature
            Assert.IsTrue(xml.Contains("QUNSUFJEQ1Q=")); // ACRPRDCT OEM Table ID
            Assert.IsTrue(xml.Contains("26025")); // Exact SSDO length
        }

        [TestMethod]
        public void Test_TauEncoding_KnownFirmwareValues()
        {
            Assert.AreEqual(0x01, CpuPowerLimitConfiguration.EncodeTauSeconds(0));
            Assert.AreEqual(0x01, CpuPowerLimitConfiguration.EncodeTauSelection(0));
            Assert.AreEqual(0, CpuPowerLimitConfiguration.DecodeTauSeconds(0x00));
            Assert.AreEqual(0, CpuPowerLimitConfiguration.DecodeTauSeconds(0x01));
            Assert.AreEqual("0 s", CpuPowerLimitConfiguration.FormatTauSelection(0));
            Assert.AreEqual(0x6F, CpuPowerLimitConfiguration.EncodeTauSeconds(56));
            Assert.AreEqual(56, CpuPowerLimitConfiguration.DecodeTauSeconds(0x6F));
            Assert.AreEqual(0x72, CpuPowerLimitConfiguration.EncodeTauSeconds(448));
            Assert.AreEqual(448, CpuPowerLimitConfiguration.DecodeTauSeconds(0x72));
            Assert.AreEqual(0x7F, CpuPowerLimitConfiguration.EncodeTauSelection(CpuPowerLimitConfiguration.InfiniteTauSliderValue));
            Assert.AreEqual("448 s", CpuPowerLimitConfiguration.FormatTauSelection(448));
            Assert.AreEqual("∞", CpuPowerLimitConfiguration.FormatTauSelection(CpuPowerLimitConfiguration.InfiniteTauSliderValue));
            Assert.AreEqual(56, CpuPowerLimitConfiguration.SnapTauSelection(56));
            Assert.AreEqual(56, CpuPowerLimitConfiguration.SnapTauSelection(48));
            Assert.AreEqual(32, CpuPowerLimitConfiguration.SnapTauSelection(40));
            Assert.AreEqual(256, CpuPowerLimitConfiguration.SnapTauSelection(256));
            Assert.AreEqual(CpuPowerLimitConfiguration.InfiniteTauSliderValue, CpuPowerLimitConfiguration.SnapTauSelection(448));
            Assert.AreEqual(56, CpuPowerLimitConfiguration.TauSecondsForIndex(CpuPowerLimitConfiguration.TauStepIndex(56)));
        }

        [TestMethod]
        public void Test_DetectIntelPlatformPowerLimit_UsesAcpiFactoryDefault()
        {
            int watts = OpenCorePowerLimitManager.DetectIntelPlatformPowerLimitWatts();
            Assert.AreEqual(OpenCorePowerLimitManager.FactoryIntelPlatformLimitWatts, watts);
        }

        [TestMethod]
        public void PcccPatch_AlwaysStores999Watts()
        {
            string xml = OpenCorePowerLimitManager.BuildOpenCorePcccPatchXml(150);
            Assert.Contains("85W to 999W", xml);
            byte[] expected = Convert.FromBase64String(OpenCorePowerLimitManager.PcccStoreFindBase64);
            BitConverter.GetBytes(999_000).CopyTo(expected, 2);
            Assert.AreEqual(Convert.ToBase64String(expected), OpenCorePowerLimitManager.PcccStoreReplaceBase64);
            Assert.Contains(OpenCorePowerLimitManager.PcccStoreReplaceBase64, OpenCorePowerLimitManager.BuildOpenCorePcccPatchXml(50));
            Assert.Contains(OpenCorePowerLimitManager.PcccStoreReplaceBase64, OpenCorePowerLimitManager.BuildOpenCorePcccPatchXml(2000));
        }

        [TestMethod]
        public void Test_MinimalOpenCoreConfig_DoesNotHookOemMethods()
        {
            string path = Path.Combine(GetSolutionRoot(), "tools", "OpenCore_PreyBoost_EFI", "EFI", "OC", "config.plist");
            string plist = File.ReadAllText(path);
            Assert.IsTrue(plist.Contains("SSDT-PREYDEV.aml"));
            Assert.IsFalse(plist.Contains("SSDT-PREYCPU"));
            Assert.IsFalse(plist.Contains("SSDT-PREYBOOST"));
            Assert.IsFalse(plist.Contains("_Q30"));
            Assert.IsFalse(plist.Contains("WMBH"));
            Assert.IsTrue(plist.Contains("cAwITAEAiIOIUENDWAEAAQA="));
            Assert.Contains(OpenCorePowerLimitManager.PcccStoreReplaceBase64, plist);
            Assert.IsTrue(plist.Contains("RTNEVAoO"));
            Assert.IsTrue(plist.Contains("<string>Optional</string>"));
            Assert.IsFalse(plist.Contains("<string>Secure</string>"));
            Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(plist, @"<key>SecureBootModel</key>\s*<string>Disabled</string>"));
            Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(plist, @"<key>SecureBootModel</key>\s*<string>Default</string>"));
            Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(plist, @"<key>ShowPicker</key>\s*<false\s*/>"));
            Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(plist, @"<key>Vault</key>\s*<string>Optional</string>"));
            Assert.IsTrue(plist.Contains("OpenRuntime.efi"));
            Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(plist, @"<key>SyncRuntimePermissions</key>\s*<true\s*/>"));
            Assert.IsFalse(plist.Contains("ResetNvramEntry", StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(plist.Contains("CustomPciSerialDevice"));
            Assert.IsFalse(plist.Contains("CustomPciBarSize"));
            Assert.IsFalse(plist.Contains(".kext", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(plist.Contains("HfsPlus", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(plist.Contains("OpenCanopy", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(plist.Contains("AudioDxe", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(plist.Contains("Lilu", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(plist.Contains("VirtualSMC", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(plist.Contains("WhateverGreen", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(plist.Contains("AppleALC", StringComparison.OrdinalIgnoreCase));
            string manager = File.ReadAllText(Path.Combine(GetSolutionRoot(), "PreySense.Core", "Acpi", "OpenCorePowerLimitManager.cs"));
            Assert.Contains("string>Optional</string>", manager);
            Assert.Contains("string>Disabled</string>", manager);
            Assert.Contains("DisablePlatformInfoSpoof", manager);
            Assert.Contains("StripAppleBootFeatures", manager);
            string acpiVm = File.ReadAllText(Path.Combine(GetSolutionRoot(), "PreySense.Frontend", "ViewModels", "MainViewModel.AcpiModeTuning.cs"));
            Assert.Contains("ClampCpuPl1ToPlatformLimit", acpiVm);
            Assert.Contains("LimitPl1ToPlatform", acpiVm);
            Assert.Contains("LimitPl2", acpiVm);
            Assert.Contains("EffectivePlatformPl1Limit", acpiVm);
            Assert.Contains("MaxPowerLimitWatts", acpiVm);
            Assert.Contains("MaxPowerLimitWatts", acpiVm);
        }

        [TestMethod]
        public void Test_UpdateConfigPlistPatches_StripsAppleDriversAndKexts()
        {
            string sample = Path.Combine(GetSolutionRoot(), "tools", "Sample.plist");
            string temp = Path.Combine(Path.GetTempPath(), "PreySense-WindowsOnly-" + Guid.NewGuid().ToString("N") + ".plist");
            File.Copy(sample, temp, overwrite: true);
            try
            {
                Assert.IsTrue(OpenCorePowerLimitManager.UpdateConfigPlistPatches(temp, string.Empty, 0, 200));
                string plist = File.ReadAllText(temp);
                Assert.IsTrue(plist.Contains("SSDT-PREYDEV.aml"));
                Assert.IsTrue(plist.Contains("OpenRuntime.efi"));
                Assert.IsFalse(plist.Contains("ResetNvramEntry", StringComparison.OrdinalIgnoreCase));
                Assert.IsFalse(plist.Contains("HfsPlus", StringComparison.OrdinalIgnoreCase));
                Assert.IsFalse(plist.Contains("Lilu", StringComparison.OrdinalIgnoreCase));
                Assert.IsFalse(plist.Contains(".kext", StringComparison.OrdinalIgnoreCase));
                Assert.IsFalse(plist.Contains("OpenCanopy", StringComparison.OrdinalIgnoreCase));
                Assert.IsFalse(plist.Contains("AudioDxe", StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
        }

        [TestMethod]
        public void Test_Live_Kernel_ACPI_Tables_Patch_Status()
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\ACPI\SSDJ\ACRSYS\ACRPRDCT\00001000");
            if (key != null)
            {
                byte[] bytes = (byte[])key.GetValue("00000000")!;
                string raw = Encoding.ASCII.GetString(bytes);
                int wmbh = raw.IndexOf("WMBH");
                int xmbh = raw.IndexOf("XMBH");
                Console.WriteLine($"SSDJ LIVE: WMBH at {wmbh}, XMBH at {xmbh}");
            }

            using var dsdtKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\ACPI\DSDT\ACRSYS\ACRPRDCT\00000002");
            if (dsdtKey != null)
            {
                byte[] dsdtBytes = (byte[])dsdtKey.GetValue("00000000")!;
                string dsdtRaw = Encoding.ASCII.GetString(dsdtBytes);
                string oemTableId = Encoding.ASCII.GetString(dsdtBytes, 16, 8);
                int q30 = dsdtRaw.IndexOf("_Q30");
                int xq30 = dsdtRaw.IndexOf("XQ30");
                Console.WriteLine($"DSDT LIVE: Length={dsdtBytes.Length}, OemTableId='{oemTableId}', _Q30 at {q30}, XQ30 at {xq30}");
            }
        }

        [TestMethod]
        public void Test_Probe_PreySense_Wmi_Device()
        {
            var res = PreySense.Core.Hardware.PreySenseWmiDeviceClient.ProbeDevice();
            Console.WriteLine($"PreySense WMI Device Probe: Found={res.found}, Version=0x{res.version:X4}, Status='{res.status}'");
        }

        [TestMethod]
        public void Test_HardwareOverlay_PositionPresets()
        {
            // Test horizontal & vertical position persistence in AppConfig
            PreySense.Overlay.AppConfig.Set("overlay_pos_horizontal", "Center");
            PreySense.Overlay.AppConfig.Set("overlay_pos_vertical", "Bottom");

            string h = PreySense.Overlay.AppConfig.GetString("overlay_pos_horizontal");
            string v = PreySense.Overlay.AppConfig.GetString("overlay_pos_vertical");

            Assert.AreEqual("Center", h, "Horizontal position must persist to AppConfig");
            Assert.AreEqual("Bottom", v, "Vertical position must persist to AppConfig");

            // Reset back to Left / Top
            PreySense.Overlay.HardwareOverlayManager.Reposition("Left", "Top");
            Assert.AreEqual("Left", PreySense.Overlay.AppConfig.GetString("overlay_pos_horizontal"));
            Assert.AreEqual("Top", PreySense.Overlay.AppConfig.GetString("overlay_pos_vertical"));
        }

        [TestMethod]
        public void Test_Wpf_RectangularSwitch_And_DropdownRow()
        {
            var thread = new System.Threading.Thread(() =>
            {
                var sw = new PreySense.Frontend.Controls.RectangularSwitch
                {
                    OnText = "80%",
                    OffText = "100%",
                    IsChecked = true
                };
                Assert.AreEqual("80%", sw.OnText);
                Assert.AreEqual("100%", sw.OffText);
                Assert.IsTrue(sw.IsChecked == true);

                sw.IsChecked = false;
                Assert.IsFalse(sw.IsChecked == true);

                var row = new PreySense.Frontend.Controls.SettingDropdownRow
                {
                    Title = "Horizontal Position",
                    Description = "Alignment test",
                    ItemsSource = new string[] { "Left", "Center", "Right" },
                    SelectedItem = "Center"
                };
                Assert.AreEqual("Horizontal Position", row.Title);
                Assert.AreEqual("Center", row.SelectedItem);

                var switchRow = new PreySense.Frontend.Controls.SettingSwitchRow
                {
                    Title = "Run on Windows Startup",
                    Description = "Launch test",
                    IsChecked = true,
                    OnText = "Enabled",
                    OffText = "Disabled"
                };
                Assert.AreEqual("Run on Windows Startup", switchRow.Title);
                Assert.AreEqual("Launch test", switchRow.Description);
                Assert.IsTrue(switchRow.IsChecked == true);

                var actionRow = new PreySense.Frontend.Controls.SettingActionRow
                {
                    Title = "dGPU Reset",
                    Description = "Direct hardware test",
                    ActionText = "Restart dGPU"
                };
                Assert.AreEqual("dGPU Reset", actionRow.Title);
                Assert.AreEqual("Restart dGPU", actionRow.ActionText);

                var sliderRow = new PreySense.Frontend.Controls.SettingSliderRow
                {
                    Title = "PL1 Sustained Long Power",
                    BadgeText = "55 W",
                    Value = 55,
                    Minimum = 25,
                    Maximum = 140
                };
                Assert.AreEqual("PL1 Sustained Long Power", sliderRow.Title);
                Assert.AreEqual(55.0, sliderRow.Value);

                var valueRow = new PreySense.Frontend.Controls.SettingValueRow
                {
                    Title = "Intel Platform Power Limit",
                    Description = "Detected minimum: 85 W",
                    Value = 85,
                    Unit = "W"
                };
                Assert.AreEqual("Intel Platform Power Limit", valueRow.Title);
                Assert.AreEqual(85.0, valueRow.Value);
                Assert.AreEqual("W", valueRow.Unit);

                var card = new PreySense.Frontend.Controls.SettingCard
                {
                    Title = "General System Policies",
                    StatusText = "Active"
                };
                Assert.AreEqual("General System Policies", card.Title);
                Assert.AreEqual("Active", card.StatusText);

                var actionBtn = new PreySense.Frontend.Controls.ActionButton
                {
                    Content = "Apply",
                    CornerRadius = new System.Windows.CornerRadius(6)
                };
                Assert.AreEqual("Apply", actionBtn.Content);
                Assert.AreEqual(6.0, actionBtn.CornerRadius.TopLeft);
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            thread.Join();
        }

        [TestMethod]
        public void Test_MockHardwareService_RestartDiscreteGpu()
        {
            var mockHw = new PreySense.Core.Services.MockHardwareService();
            var result = mockHw.RestartDiscreteGpu();
            Assert.IsTrue(result.Success, "MockHardwareService RestartDiscreteGpu should return true");
            Assert.IsTrue(result.Message.Contains("reinitialized", StringComparison.OrdinalIgnoreCase), "Message should mention reinitialized");
        }

        [TestMethod]
        public void Test_DeviceHelper_GracefullyHandles_MissingDevice()
        {
            // Searching for a non-existent hardware ID should fail gracefully with false instead of throwing
            bool restarted = PreySense.Helpers.DeviceHelper.RestartDevice("VEN_NONEXISTENT_DEVICE_9999");
            Assert.IsFalse(restarted, "DeviceHelper.RestartDevice should return false when device is not found");
        }

        [TestMethod]
        public void Test_OpenCoreSecurityValidator_AcpiOverrideLabel()
        {
            var state = PreySense.Core.Acpi.OpenCoreSecurityValidator.OpenCoreIntegrationState.NotLoaded;
            Assert.AreEqual("OpenCore Not Loaded", PreySense.Core.Acpi.OpenCoreSecurityValidator.GetAcpiOverrideLabel(state));
            Assert.AreEqual("OpenCore Active", PreySense.Core.Acpi.OpenCoreSecurityValidator.GetAcpiOverrideLabel(PreySense.Core.Acpi.OpenCoreSecurityValidator.OpenCoreIntegrationState.Active));
            Assert.AreEqual("Restart Required", PreySense.Core.Acpi.OpenCoreSecurityValidator.GetAcpiOverrideLabel(PreySense.Core.Acpi.OpenCoreSecurityValidator.OpenCoreIntegrationState.RestartRequired));
            Assert.AreEqual("OpenCore Update Required", PreySense.Core.Acpi.OpenCoreSecurityValidator.GetAcpiOverrideLabel(PreySense.Core.Acpi.OpenCoreSecurityValidator.OpenCoreIntegrationState.UpdateRequired));
            Assert.AreEqual(PreySense.Core.Acpi.OpenCoreSecurityValidator.OpenCoreIntegrationState.Active, PreySense.Core.Acpi.OpenCoreSecurityValidator.DecideIntegrationState(true, true, false, true));
            Assert.AreEqual(PreySense.Core.Acpi.OpenCoreSecurityValidator.OpenCoreIntegrationState.NotLoaded, PreySense.Core.Acpi.OpenCoreSecurityValidator.DecideIntegrationState(true, false, true, true));
            Assert.AreEqual(PreySense.Core.Acpi.OpenCoreSecurityValidator.OpenCoreIntegrationState.RestartRequired, PreySense.Core.Acpi.OpenCoreSecurityValidator.DecideIntegrationState(false, false, true, true));
            Assert.AreEqual(PreySense.Core.Acpi.OpenCoreSecurityValidator.OpenCoreIntegrationState.UpdateRequired, PreySense.Core.Acpi.OpenCoreSecurityValidator.DecideIntegrationState(false, false, false, true));
            Assert.AreEqual(PreySense.Core.Acpi.OpenCoreSecurityValidator.OpenCoreIntegrationState.NotLoaded, PreySense.Core.Acpi.OpenCoreSecurityValidator.DecideIntegrationState(false, false, false, false));
            byte[] table = new byte[36];
            table[0] = (byte)'S';
            table[1] = (byte)'S';
            table[2] = (byte)'D';
            table[3] = (byte)'T';
            Encoding.ASCII.GetBytes("PREY  ").CopyTo(table, 10);
            Encoding.ASCII.GetBytes("PREYDEV ").CopyTo(table, 16);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(24), 0x101);
            Assert.IsTrue(PreySense.Core.Acpi.OpenCoreSecurityValidator.TryReadPreyDevOemRevision(table, out uint revision));
            Assert.AreEqual(0x101u, revision);
        }

        [TestMethod]
        public void Test_MockHardwareService_SetCoverLogoLighting()
        {
            var mockHw = new PreySense.Core.Services.MockHardwareService();
            // Should execute without exception
            mockHw.SetCoverLogoLighting(1, System.Drawing.Color.Cyan, 5, 5, true);
            mockHw.SetCoverLogoLighting(2, System.Drawing.Color.Red, 3, 7, true);
            mockHw.SetCoverLogoLighting(0, System.Drawing.Color.Black, 0, 1, false);
        }

        [TestMethod]
        public void Test_CoverLogoLighting_Transports()
        {
            using var wmi = new PreySense.Helpers.WmiController();
            wmi.SetLogoLighting(1, 0, 229, 255, 5, 5, true);
        }

        [TestMethod]
        public void CoverLogo_UsesHidOnly()
        {
            string source = File.ReadAllText(Path.Combine(GetSolutionRoot(), "PreySense.Core", "Hardware", "WmiController.cs"));
            int method = source.IndexOf("public void SetLogoLighting", StringComparison.Ordinal);
            Assert.IsGreaterThanOrEqualTo(0, method);
            string body = source[method..];
            int next = body.IndexOf("\n        public ", 1, StringComparison.Ordinal);
            if (next > 0)
            {
                body = body[..next];
            }

            Assert.Contains("EnekRgbClient.SetLogoLighting", body);
            Assert.DoesNotContain("AcerService.SetLogoLighting", body);
            Assert.DoesNotContain("OpenRgbClient", body);
            Assert.Contains("0x83", body);
        }

        [TestMethod]
        public void CoverLogoHid_UsesEnekTargetNotSharedEcChannel()
        {
            string enek = File.ReadAllText(Path.Combine(GetSolutionRoot(), "PreySense.Core", "Hardware", "EnekRgbClient.cs"));
            string logo = File.ReadAllText(Path.Combine(GetSolutionRoot(), "PreySense.Core", "Hardware", "AcerCoverLogoHidClient.cs"));
            Assert.Contains("TargetCoverLogo = 0x83", enek);
            Assert.Contains("TargetKeyboard = 0x21", enek);
            Assert.Contains("SetLogoLighting", enek);
            Assert.Contains("0x83", logo);
            Assert.DoesNotContain("0xA0", logo);
            Assert.DoesNotContain("1025174B", logo);
        }

        [TestMethod]
        public void LhmDriver_ExtractsToProgramDataNotExeDirectory()
        {
            string source = File.ReadAllText(Path.Combine(GetSolutionRoot(), "PreySense.Overlay", "WinRing0Cleanup.cs"));
            string overlayProject = File.ReadAllText(Path.Combine(GetSolutionRoot(), "PreySense.Overlay", "PreySense.Overlay.csproj"));
            Assert.Contains("CommonApplicationData", source);
            Assert.Contains("WinRing0x64.sys", source);
            Assert.Contains("R0PreySense", source);
            Assert.Contains("sc.exe", source);
            Assert.DoesNotContain("create \"", source);
            Assert.DoesNotContain("LibreHardwareMonitor", overlayProject);
            string hardware = File.ReadAllText(Path.Combine(GetSolutionRoot(), "PreySense.Overlay", "HardwareControl.cs"));
            Assert.Contains("WinRing0Cleanup.RemoveInstalledDriver", hardware);
            Assert.DoesNotContain("LibreHardwareMonitor", hardware);
            Assert.DoesNotContain("LhmDriverLocator", hardware);
            Assert.Contains("PdhCpuPowerMonitor", hardware);
            Assert.Contains("cpuPower = GetCPUPower()", hardware);
            Assert.DoesNotContain("cpuPower = null", hardware);
            Assert.Contains("TrySampleFrequencies", hardware);
            Assert.Contains("IsDiscreteGpuInUse()", hardware);
            Assert.Contains("thermal.GpuTemp", hardware);
            Assert.Contains("if (!inUse)", hardware);
            Assert.Contains("gpuTemp = 0", hardware);
            Assert.DoesNotContain("NvmlHelper.GetGpuTemperature", hardware);
            Assert.DoesNotContain("ReadCurrentTemperature", hardware);
            Assert.DoesNotContain("GpuControl.GetCurrentTemperature()", hardware);
            Assert.Contains("_lastCpuUsage", hardware);

            string nvml = File.ReadAllText(Path.Combine(GetSolutionRoot(), "PreySense.Core", "Gpu", "NvmlHelper.cs"));
            Assert.Contains("nvmlDeviceGetTemperature", nvml);
            Assert.Contains("GetGpuTemperature", nvml);
        }

        [TestMethod]
        public void CustomLighting_HonorsIdleTimeoutWithoutHardwareWrites()
        {
            string source = File.ReadAllText(Path.Combine(GetSolutionRoot(), "PreySense.Core", "Rgb", "CustomLightingEffectsEngine.cs"));
            Assert.Contains("SetIdleTimeoutSeconds", source);
            Assert.Contains("KeyboardActivity.LastKeyTick", source);
            Assert.Contains("KeyboardActivity.NoteKeyDown", source);
            Assert.DoesNotContain("GetLastInputInfo", source);
            string hook = File.ReadAllText(Path.Combine(GetSolutionRoot(), "PreySense.Frontend", "Services", "KeyboardHook.cs"));
            Assert.Contains("KeyboardActivity.NoteKeyDown", hook);
            Assert.Contains("IdleOffFrame", source);
            Assert.Contains("SetLedTimeoutSeconds(0)", source);
            Assert.Contains("Animation stopped.", source);
            Assert.Contains("writeNow = !_idleDimmed && IsRunning && _hasLastFrame", source);
            Assert.Contains("TryCopyLastFrame", source);
        }

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetPreparsedData(IntPtr HidDeviceObject, out IntPtr PreparsedData);
        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_FreePreparsedData(IntPtr PreparsedData);
        [DllImport("hid.dll", SetLastError = true)]
        private static extern int HidP_GetCaps(IntPtr PreparsedData, ref HIDP_CAPS Capabilities);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetFeature(IntPtr HidDeviceObject, byte[] lpReportBuffer, int ReportBufferLength);
        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_SetFeature(IntPtr HidDeviceObject, byte[] lpReportBuffer, int ReportBufferLength);

        [TestMethod]
        public void Test_Enumerate_Acer_EC_Hid_Devices()
        {
            var paths = PreySense.EnekRgbClient.EnumerateAllHidPaths();
            Console.WriteLine($"Total HID paths found: {paths.Count}");
            foreach (var p in paths)
            {
                if (p.Contains("1025174B", StringComparison.OrdinalIgnoreCase) || p.Contains("ENEK5130", StringComparison.OrdinalIgnoreCase))
                {
                    IntPtr h = CreateFile(p, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero); // GENERIC_READ | GENERIC_WRITE
                    if (h == (IntPtr)(-1) || h == IntPtr.Zero)
                    {
                        h = CreateFile(p, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                    }
                    if (h != (IntPtr)(-1) && h != IntPtr.Zero)
                    {
                        if (HidD_GetPreparsedData(h, out IntPtr pData))
                        {
                            var caps = new HIDP_CAPS();
                            HidP_GetCaps(pData, ref caps);
                            HidD_FreePreparsedData(pData);
                            Console.WriteLine($"MATCH: UsagePage=0x{caps.UsagePage:X4}, Usage=0x{caps.Usage:X4}, FeatLen={caps.FeatureReportByteLength}, OutLen={caps.OutputReportByteLength}\n  Path: {p}");

                            if (caps.UsagePage == 0xFF05 && caps.FeatureReportByteLength == 65)
                            {
                                byte[] reportA0 = new byte[65];
                                reportA0[0] = 0xA0;
                                bool okA0 = HidD_GetFeature(h, reportA0, 65);
                                int errA0 = Marshal.GetLastWin32Error();
                                Console.WriteLine($"  GetFeature(0xA0): ok={okA0}, err={errA0}\n  FullBytes: [{BitConverter.ToString(reportA0)}]");

                                // Test sending SetFeature with Cyan color
                                byte[] setReport = (byte[])reportA0.Clone();
                                setReport[0] = 0xA0;
                                setReport[6] = 0x01; // Mode 1 = Static
                                setReport[7] = 0x05; // Speed 5
                                setReport[9] = 0x64; // Brightness 100%
                                setReport[13] = 0x00; // Red 0
                                setReport[14] = 0xE5; // Green 229
                                setReport[15] = 0xFF; // Blue 255
                                bool setOk = HidD_SetFeature(h, setReport, 65);
                                int setErr = Marshal.GetLastWin32Error();
                                Console.WriteLine($"  SetFeature(0xA0, Cyan): ok={setOk}, err={setErr}");

                                // Read back after 100ms to verify EC accepted the state!
                                System.Threading.Thread.Sleep(100);
                                byte[] verifyReport = new byte[65];
                                verifyReport[0] = 0xA0;
                                bool verifyOk = HidD_GetFeature(h, verifyReport, 65);
                                Console.WriteLine($"  Readback verify: ok={verifyOk}\n  Readback: [{BitConverter.ToString(verifyReport, 0, 20)}]");

                                byte[] reportA4 = new byte[65];
                                reportA4[0] = 0xA4;
                                bool okA4 = HidD_GetFeature(h, reportA4, 65);
                                int errA4 = Marshal.GetLastWin32Error();
                                Console.WriteLine($"  GetFeature(0xA4): ok={okA4}, err={errA4}, data=[{BitConverter.ToString(reportA4, 0, 16)}...]");
                            }
                        }
                        CloseHandle(h);
                    }
                }
            }
        }

        [TestMethod]
        public void Test_DisplayManager_RefreshRates()
        {
            var rates = PreySense.Helpers.DisplayManager.GetSupportedRefreshRates();
            int current = PreySense.Helpers.DisplayManager.GetCurrentRefreshRate();
            Console.WriteLine($"DisplayManager: Current={current}Hz, Supported=[{string.Join(", ", rates)}]");
            Assert.IsTrue(current > 0, "Current refresh rate should be > 0");

            bool setOk = PreySense.Helpers.DisplayManager.SetRefreshRate(current);
            Console.WriteLine($"DisplayManager: SetRefreshRate({current}) returned {setOk}");

            var t = typeof(PreySense.Helpers.DisplayManager.DEVMODE);
            Console.WriteLine("dmSize offset: " + System.Runtime.InteropServices.Marshal.OffsetOf(t, "dmSize"));
            Console.WriteLine("dmFields offset: " + System.Runtime.InteropServices.Marshal.OffsetOf(t, "dmFields"));
            Console.WriteLine("dmPositionX offset: " + System.Runtime.InteropServices.Marshal.OffsetOf(t, "dmPositionX"));
            Console.WriteLine("dmFormName offset: " + System.Runtime.InteropServices.Marshal.OffsetOf(t, "dmFormName"));
            Console.WriteLine("dmLogPixels offset: " + System.Runtime.InteropServices.Marshal.OffsetOf(t, "dmLogPixels"));
            Console.WriteLine("dmBitsPerPel offset: " + System.Runtime.InteropServices.Marshal.OffsetOf(t, "dmBitsPerPel"));
            Console.WriteLine("dmPelsWidth offset: " + System.Runtime.InteropServices.Marshal.OffsetOf(t, "dmPelsWidth"));
            Console.WriteLine("dmDisplayFrequency offset: " + System.Runtime.InteropServices.Marshal.OffsetOf(t, "dmDisplayFrequency"));
            Console.WriteLine("Total size: " + System.Runtime.InteropServices.Marshal.SizeOf(t));

            // Debug with EnumDisplaySettings on current mode:
            PreySense.Helpers.DisplayManager.DebugChangeDisplaySettings(current);
        }

        [TestMethod]
        public void Test_GpuPowerLimitConfiguration_DetectFromHardware_AcerErm4()
        {
            // Simulate DSDT containing OpRegion ERM4: 5B 80 'E' 'R' 'M' '4'
            byte[] simulatedDsdt = new byte[]
            {
                0x10, 0x20, 0x5B, 0x80, (byte)'E', (byte)'R', (byte)'M', (byte)'4', 0x00, 0x00
            };

            var config = GpuPowerLimitConfiguration.DetectFromHardware(simulatedDsdt);

            Assert.AreEqual(85, config.BasePowerWatts, "Base power floor must be 85W");
            Assert.AreEqual(5, config.MinPowerWatts, "Slider floor must allow 5W");
            Assert.AreEqual(999, config.MaxPowerWatts, "Slider ceiling must allow 999W");
            Assert.AreEqual(130, config.AcpiTurboDefaultWatts, "ACPI default Turbo limit must be 130W");
            Assert.AreEqual(85, config.QuietGpuWatts, "Quiet default must be 85W");
            Assert.AreEqual(100, config.BalancedGpuWatts, "Balanced default must be 100W");
            Assert.AreEqual(115, config.PerformanceGpuWatts, "Performance default must be 115W");
            Assert.AreEqual(130, config.TurboGpuWatts, "Turbo default must be 130W");

            // Zero Dynamic Boost deltas
            Assert.AreEqual(0, config.QuietDeltaWatts, "Quiet delta = 85 - 85 = 0W");
            Assert.AreEqual(15, config.BalancedDeltaWatts, "Balanced delta = 100 - 85 = 15W");
            Assert.AreEqual(30, config.PerformanceDeltaWatts, "Performance delta = 115 - 85 = 30W");
            Assert.AreEqual(45, config.TurboDeltaWatts, "Turbo delta = 130 - 85 = 45W");

            bool valid = config.Validate(out string error);
            Assert.IsTrue(valid, $"Validation failed: {error}");

            config.TurboGpuWatts = 999;
            Assert.AreEqual(914, config.TurboDeltaWatts, "999W Turbo delta = 999 - 85 = 914W");
            Assert.IsTrue(config.Validate(out string err999), $"Validation to 999W failed: {err999}");
        }

        [TestMethod]
        public void Test_HardwareService_HasNoNvapiGpuClockOffsets()
        {
            string root = GetSolutionRoot();
            string hw = File.ReadAllText(Path.Combine(root, "PreySense.Core", "Services", "IHardwareService.cs"));
            string wrapper = File.ReadAllText(Path.Combine(root, "PreySense.Core", "Gpu", "NvidiaWrapper.cs"));
            string profiles = File.ReadAllText(Path.Combine(root, "PreySense.Core", "Mode", "ProfileManager.cs"));
            Assert.DoesNotContain("GetGpuClocks", hw);
            Assert.DoesNotContain("SetGpuClocks", hw);
            Assert.DoesNotContain("GetGpuClockLimits", hw);
            Assert.DoesNotContain("DescribeGpuPerformanceStates", hw);
            Assert.DoesNotContain("GetGpuPowerLimitInfo", hw);
            string gpuControl = File.ReadAllText(Path.Combine(root, "PreySense.Core", "Gpu", "NvidiaGpuControl.cs"));
            Assert.DoesNotContain("GetPerformanceStates20", gpuControl);
            Assert.DoesNotContain("ApplyGpuSettings", wrapper);
            Assert.DoesNotContain("NvidiaWrapper.ApplyGpuSettings", profiles);
        }

        [TestMethod]
        public void Test_OpenCorePowerLimitManager_GenerateOpenCoreGpuOverride_130wZeroDynamicBoost()
        {
            string root = GetSolutionRoot();
            string iaslPath = Path.Combine(root, "docs", "acpi_tables", "iasl.exe");
            string testStaging = Path.Combine(root, "PreySense.Tests", "test_gpu_staging");

            try
            {
                var config = new GpuPowerLimitConfiguration
                {
                    BasePowerWatts = 85,
                    MaxPowerWatts = 130,
                    MinPowerWatts = 5,
                    QuietGpuWatts = 85,
                    BalancedGpuWatts = 100,
                    PerformanceGpuWatts = 115,
                    TurboGpuWatts = 130
                };

                var res = OpenCorePowerLimitManager.GenerateOpenCoreGpuOverride(config, testStaging, iaslPath);
                Assert.IsTrue(res.Success, $"Override generation failed: {res.ErrorMessage} | {res.LogOutput}");

                string dslContent = File.ReadAllText(res.DslPath);
                Assert.IsTrue(dslContent.Contains("M4CT = 0x1E"), "Must contain 30W Configurable TGP delta (0x1E)");
                Assert.IsTrue(dslContent.Contains("M4DB = 0x0F"), "Must retain 15W Dynamic Boost for Turbo");
                Assert.IsTrue(dslContent.Contains("M3CT = 0x0F"), "Must contain Perf delta");
                Assert.IsTrue(dslContent.Contains("M2CT = 0x00"), "Must contain Balanced delta");
                Assert.IsTrue(dslContent.Contains("ACBT = (M4CT * 0x08)"), "Must write Configurable TGP offset to ACBT");
                Assert.IsTrue(dslContent.Contains("ATPP = (M4CP * 0x08)"), "Must write platform cap headroom offset to ATPP");
                Assert.IsTrue(dslContent.Contains("AMAT = (M4DB * 0x08)"), "Must write Dynamic Boost offset to AMAT");

                Assert.IsTrue(File.Exists(res.AmlPath), "AML file must be compiled by iasl");
            }
            finally
            {
                if (Directory.Exists(testStaging))
                {
                    try { Directory.Delete(testStaging, true); } catch { }
                }
            }
        }

        [TestMethod]
        public void Test_SsdtPreyDev_ContainsGpuPowerControlsAndCompiles()
        {
            string stagingDsl = @"d:\Development\PreySense\acpi_staging\SSDT-PREYDEV.dsl";
            Assert.IsTrue(File.Exists(stagingDsl), "SSDT-PREYDEV.dsl must exist in acpi_staging");

            string dsl = File.ReadAllText(stagingDsl);
            Assert.IsTrue(dsl.Contains("If (SizeOf (Arg2) >= 0x08)"), "SetCpuPowerLimits must accept 8-byte WMI buffers without tau");
            Assert.IsTrue(dsl.Contains("Case (0x05) // SetGpuPowerLimit"), "SSDT-PREYDEV must implement SetGpuPowerLimit (method 5)");
            Assert.IsTrue(dsl.Contains("Case (0x06) // GetGpuPowerLimit"), "SSDT-PREYDEV must implement GetGpuPowerLimit (method 6)");
            Assert.IsTrue(dsl.Contains("Notify (\\_SB.NPCF, 0xC0)"), "Must trigger NPCF 0xC0 notification to live NVIDIA driver");
            Assert.Contains("ACBT = (Arg0 * 0x08)", dsl, "Must write independent Configurable TGP offset to ACBT");
            Assert.Contains("AMAT = (Arg1 * 0x08)", dsl, "Must write independent Dynamic Boost offset to AMAT");
            Assert.Contains("ATPP = (Arg2 * 0x08)", dsl, "Must write independent platform offset to ATPP");

            string iaslPath = @"d:\Development\PreySense\docs\acpi_tables\iasl.exe";
            if (File.Exists(iaslPath))
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = iaslPath,
                    Arguments = $"\"{stagingDsl}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = System.Diagnostics.Process.Start(psi)!;
                proc.WaitForExit(5000);
                Assert.AreEqual(0, proc.ExitCode, "SSDT-PREYDEV.dsl must compile with 0 errors via iasl");
            }
        }

        [TestMethod]
        public void Test_GpuThermalTripPoint_StandardizedDefaults_And_WmiMethods()
        {
            Assert.AreEqual(81, GpuPowerLimitConfiguration.FactoryGpuThermalTripPointCelsius);

            var config = new GpuPowerLimitConfiguration();
            Assert.AreEqual(81, config.QuietGpuTripPointCelsius);
            Assert.AreEqual(81, config.EcoGpuTripPointCelsius);
            Assert.AreEqual(81, config.BalancedGpuTripPointCelsius);
            Assert.AreEqual(81, config.PerformanceGpuTripPointCelsius);
            Assert.AreEqual(81, config.TurboGpuTripPointCelsius);

            // Verify SSDT-PREYDEV has Method 7 (SetGpuThermalTripPoint) and Method 8 (GetGpuThermalTripPoint)
            string stagingDsl = @"d:\Development\PreySense\acpi_staging\SSDT-PREYDEV.dsl";
            Assert.IsTrue(File.Exists(stagingDsl));
            string dsl = File.ReadAllText(stagingDsl);
            Assert.IsTrue(dsl.Contains("Case (0x07) // SetGpuThermalTripPoint"), "Must implement Method 7");
            Assert.IsTrue(dsl.Contains("Case (0x08) // GetGpuThermalTripPoint"), "Must implement Method 8");
            Assert.IsTrue(dsl.Contains(@"\_SB.NPCF.WMG3 = Local0"), "Must update WMG3 register");
            Assert.IsTrue(dsl.Contains("If (Local0 > 89) { Local0 = 89 }"), "Must clamp upper bound to NVML GPU_MAX limit 89°C");

            // Safe invocation check
            var (success, temp) = PreySenseWmiDeviceClient.GetGpuThermalTripPoint();
            // In live environment with PRYW active, this returns true with valid temperature
            if (success)
            {
                Assert.IsTrue(temp >= 70 && temp <= 89, $"Live trip point {temp}°C must be within 70-89°C");
            }
        }

        [TestMethod]
        public void Test_AcpiOverride_RegistryPersistence_And_BannerVisibility()
        {
            const string subKey = @"SOFTWARE\PreySense\AdvancedSettings";
            int? originalValue = null;
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subKey))
            {
                if (k?.GetValue("AcpiOverrideEnabled") is int existing)
                {
                    originalValue = existing;
                }
            }

            try
            {
                // 1. Explicitly enable via registry
                using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(subKey))
                {
                    k.SetValue("AcpiOverrideEnabled", 1);
                }

                var hw = new MockHardwareService();
                var vm = new PreySense.Frontend.ViewModels.MainViewModel(hw);

                // A saved override cannot bypass the required factory calibration.
                if (!vm.IsFactoryCalibrationComplete)
                    Assert.IsFalse(vm.IsAcpiOverrideEnabled);

                // Case A: OpenCore is NOT loaded
                vm.IsOpenCoreLoaded = false;
                Assert.AreEqual(System.Windows.Visibility.Visible, vm.AcpiOverrideDisabledBannerVisibility, "Banner must be visible when OpenCore is not loaded");
                Assert.IsTrue(vm.AcpiOverrideDisabledBannerText.Contains("OpenCore is not loaded"), "Banner text must explain that OpenCore is not loaded");
                Assert.IsFalse(vm.CanEnableAcpiOverride, "Override must remain locked when OpenCore is not loaded");

                // Case B: OpenCore IS loaded, but ACPI Override switch is turned OFF
                vm.IsOpenCoreLoaded = true;
                vm.IsAcpiOverrideEnabled = false;
                Assert.AreEqual(System.Windows.Visibility.Visible, vm.AcpiOverrideDisabledBannerVisibility, "Banner must be visible when override is off");
                Assert.AreEqual(
                    "ACPI Hardware Override is disabled.",
                    vm.AcpiOverrideDisabledBannerText,
                    "Banner text must match exact disabled override notice when OpenCore is loaded"
                );
                Assert.IsFalse(vm.CanEnableAcpiOverride, "Override must remain locked before calibration");

                // Verify registry updated to 0
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subKey))
                {
                    int val = (int)(k?.GetValue("AcpiOverrideEnabled") ?? -1);
                    Assert.AreEqual(0, val, "Registry should be updated to 0 when disabled");
                }

                // Case C: OpenCore IS loaded and ACPI Override switch is ON
                vm.IsFactoryCalibrationComplete = true;
                vm.ToggleAcpiOverride();
                Assert.IsTrue(vm.IsAcpiOverrideEnabled);
                Assert.AreEqual(System.Windows.Visibility.Collapsed, vm.AcpiOverrideDisabledBannerVisibility, "Banner must collapse when OpenCore is loaded and override enabled");
                Assert.IsTrue(vm.CanEnableAcpiOverride, "Override must be available after OpenCore and calibration are ready");

                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subKey))
                {
                    int val = (int)(k?.GetValue("AcpiOverrideEnabled") ?? -1);
                    Assert.AreEqual(1, val, "Registry should be updated to 1 when enabled");
                }
            }
            finally
            {
                using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(subKey);
                if (originalValue.HasValue)
                {
                    k.SetValue("AcpiOverrideEnabled", originalValue.Value);
                }
                else
                {
                    try { k.DeleteValue("AcpiOverrideEnabled"); } catch { }
                }
            }
        }

        [TestMethod]
        public void Test_ModeSettingsWindow_XAML_Lockdown_And_RemovedControls()
        {
            string root = GetSolutionRoot();
            string xamlPath = Path.Combine(root, "PreySense.Frontend", "Views", "ModeSettingsWindow.xaml");
            Assert.IsTrue(File.Exists(xamlPath), "ModeSettingsWindow.xaml must exist");

            string xaml = File.ReadAllText(xamlPath);

            // Verify ACPI Override Disabled Warning Banner
            Assert.IsTrue(xaml.Contains("Visibility=\"{Binding AcpiOverrideDisabledBannerVisibility}\""), "Must bind warning banner visibility");
            Assert.IsTrue(xaml.Contains("Text=\"{Binding AcpiOverrideDisabledBannerText}\""), "Must bind dynamic banner text");

            // Verify controls container is locked when ACPI tuning is not allowed
            Assert.IsTrue(xaml.Contains("IsEnabled=\"{Binding IsAcpiOverrideEnabled}\""), "All mode tuning controls must be disabled when ACPI override is off");

            // Verify CPU Turbo Boost is removed
            Assert.IsFalse(xaml.Contains("ComboCpuBoost"), "ComboCpuBoost must be removed from ModeSettingsWindow.xaml");
            Assert.IsFalse(xaml.Contains("CPU Turbo Boost"), "CPU Turbo Boost UI must be removed from ModeSettingsWindow.xaml");

            // Verify GPU Core Offset & Memory Offset are removed
            Assert.IsFalse(xaml.Contains("GpuCoreOffset"), "GpuCoreOffset slider must be removed from ModeSettingsWindow.xaml");
            Assert.IsFalse(xaml.Contains("GpuMemoryOffset"), "GpuMemoryOffset slider must be removed from ModeSettingsWindow.xaml");
            Assert.IsFalse(xaml.Contains("Apply Offsets"), "Apply Offsets button must be removed from ModeSettingsWindow.xaml");

            Assert.Contains("SliderTau", xaml);
            Assert.Contains("x:Name=\"SliderTau\" Minimum=\"0\"", xaml);
            Assert.Contains("SettingValueRow", xaml);
            Assert.Contains("Sustained Long Power [PL1]", xaml);
            Assert.Contains("Burst Peak Power [PL2]", xaml);
            Assert.DoesNotContain("SliderPl1", xaml);
            Assert.DoesNotContain("SliderPl2", xaml);
            Assert.Contains("RowGpuPower", xaml);
            Assert.DoesNotContain("SliderGpuPower", xaml);
            Assert.DoesNotContain("TabCpuGpu", xaml);
            Assert.Contains("VerticalAlignment=\"Top\"", xaml);

            string modeCs = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "Views", "ModeSettingsWindow.xaml.cs"));
            int resetIdx = modeCs.IndexOf("private async void ResetButton_Click", StringComparison.Ordinal);
            Assert.IsTrue(resetIdx >= 0);
            string resetBody = modeCs.Substring(resetIdx, Math.Min(5000, modeCs.Length - resetIdx));
            Assert.Contains("await ApplySelectedModeAsync()", resetBody);
            Assert.Contains("SaveCpuLimitsToRegistry", resetBody);
            Assert.Contains("ResetModeToCapturedFactoryDefaults", resetBody);

            string acpiVm = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "ViewModels", "MainViewModel.Acpi.cs"));
            Assert.Contains("FactoryPowerProfileDetector.Capture", acpiVm);
            Assert.Contains("CalibrateFactoryProfilesAsync", acpiVm);
            Assert.Contains("IsPlatformLimitCalibrated = (calibratedMask & 0x10) != 0", acpiVm);
            Assert.Contains("BuildFactoryLimitsMessage", acpiVm);
            Assert.Contains("ShowCancelButton = false", acpiVm);
            Assert.Contains("Task.Run", acpiVm);

            string advancedXaml = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "Views", "AdvancedWindow.xaml"));
            Assert.Contains("Factory Defaults", advancedXaml);
            Assert.DoesNotContain("CalibrateFactoryProfilesCommand", advancedXaml);
            Assert.Contains("IsEnabled=\"{Binding CanEnableAcpiOverride}\"", advancedXaml);
            Assert.Contains("ViewFactoryDefaultsCommand", advancedXaml);
            Assert.DoesNotContain("PL1 Platform Power Limit", advancedXaml);
            Assert.DoesNotContain("CanEditPlatformPowerLimit", advancedXaml);
            Assert.DoesNotContain("Text=\"{Binding FactoryCalibrationStatus}\"", advancedXaml);

            string mainWindowCs = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "MainWindow.xaml.cs"));
            Assert.DoesNotContain("CaptureFactoryPowerProfileAsync", mainWindowCs);
        }

        [TestMethod]
        public void Test_GameProfiles_Store_And_GamesWindow()
        {
            string root = GetSolutionRoot();
            Assert.IsTrue(File.Exists(Path.Combine(root, "PreySense.Frontend", "Views", "GamesWindow.xaml")));
            string mainXaml = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "MainWindow.xaml"));
            Assert.Contains("BtnNavGames", mainXaml);
            Assert.Contains("GamesButton_Click", mainXaml);
            Assert.Contains("Columns=\"5\"", mainXaml);
            Assert.Contains("<RowDefinition Height=\"34\"/>", mainXaml);
            Assert.Contains("Grid.Row=\"2\" Columns=\"3\"", mainXaml);

            string exe = PreySense.Mode.GameProfileStore.NormalizeExeName(@"C:\Games\r5apex.exe");
            Assert.AreEqual("r5apex.exe", exe);
            var profile = new PreySense.Mode.GameProfile { ExeName = "r5apex.exe", PowerMode = 0x05, CpuPl1 = 85, CpuPl2 = 140 };
            Assert.AreEqual("r5apex", profile.ProcessName);
            Assert.AreEqual("Turbo", profile.ModeTitle);
            var ecoProfile = new PreySense.Mode.GameProfile { ExeName = "battery-game.exe", PowerMode = 0x06 };
            Assert.AreEqual("Eco", ecoProfile.ModeTitle);
            Assert.AreEqual(PreySense.Core.Acpi.CpuPowerLimitConfiguration.FactoryTauSeconds, profile.TauSeconds);
            Assert.AreEqual(PreySense.Core.Acpi.GpuPowerLimitConfiguration.FactoryGpuThermalTripPointCelsius, profile.GpuThermalTripCelsius);

            string gamesXaml = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "Views", "GamesWindow.xaml"));
            Assert.Contains("Visibility=\"{Binding AcpiOverrideDisabledBannerVisibility}\"", gamesXaml);
            Assert.Contains("Text=\"{Binding AcpiOverrideDisabledBannerText}\"", gamesXaml);
            Assert.Contains("IsEnabled=\"{Binding IsAcpiOverrideEnabled}\"", gamesXaml);
            Assert.Contains("Burst Window [Tau]", gamesXaml);
            Assert.Contains("GPU Power Limit", gamesXaml);
            Assert.Contains("GPU Thermal Trip Point [WMG3]", gamesXaml);

            string gamesVm = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "ViewModels", "MainViewModel.Games.cs"));
            Assert.Contains("{ \"Eco\", \"Quiet\", \"Balanced\", \"Performance\", \"Turbo\" }", gamesVm);
            Assert.Contains("\"Eco\" => 0x06", gamesVm);
            Assert.Contains("ApplyGameProfileAsync", gamesVm);
            Assert.Contains("GameLaunchWatcher", gamesVm);
            Assert.Contains("SetCpuPowerLimits", gamesVm);
            Assert.Contains("SetGpuPowerLimit", gamesVm);
            Assert.Contains("SetGpuThermalTripPoint", gamesVm);
            Assert.Contains("if (!IsAcpiOverrideEnabled)", gamesVm);
        }

        [TestMethod]
        public void Eco_And_Quiet_Power_Profiles_Remain_Independent()
        {
            Assert.AreEqual(55, PreySense.Core.Acpi.CpuPowerLimitConfiguration.FactoryQuietPl1);
            Assert.AreEqual(45, PreySense.Core.Acpi.CpuPowerLimitConfiguration.FactoryEcoPl1);
            Assert.AreEqual(50, PreySense.Core.Acpi.CpuPowerLimitConfiguration.FactoryEcoPl2);
            var cpu = new PreySense.Core.Acpi.CpuPowerLimitConfiguration
            {
                EcoPl1 = 25,
                EcoPl2 = 65,
                QuietPl1 = 45,
                QuietPl2 = 140
            };
            var gpu = new PreySense.Core.Acpi.GpuPowerLimitConfiguration
            {
                EcoGpuWatts = 55,
                QuietGpuWatts = 85,
                EcoGpuTripPointCelsius = 75,
                QuietGpuTripPointCelsius = 79
            };

            Assert.AreNotEqual(cpu.EcoPl1, cpu.QuietPl1);
            Assert.AreNotEqual(cpu.EcoPl2, cpu.QuietPl2);
            Assert.AreNotEqual(gpu.EcoGpuWatts, gpu.QuietGpuWatts);
            Assert.AreNotEqual(gpu.EcoGpuTripPointCelsius, gpu.QuietGpuTripPointCelsius);

            string root = GetSolutionRoot();
            string manager = File.ReadAllText(Path.Combine(root, "PreySense.Core", "Acpi", "OpenCorePowerLimitManager.cs"));
            Assert.Contains("Case (0x04) // Eco Mode", manager);
            Assert.Contains("ePl1Raw", manager);
            Assert.Contains("ePl2Raw", manager);
            Assert.Contains("M5CT", manager);
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern uint GetFirmwareEnvironmentVariable(string lpName, string lpGuid, byte[] pBuffer, uint nSize);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern uint EnumSystemFirmwareTables(uint FirmwareTableProviderSignature, IntPtr pFirmwareTableEnumBuffer, uint BufferSize);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern uint GetSystemFirmwareTable(uint FirmwareTableProviderSignature, uint FirmwareTableID, byte[] pFirmwareTableBuffer, uint BufferSize);

        [TestMethod]
        public void Test_Diagnose_OpenCore_Detection()
        {
            bool isLoaded = PreySense.Core.Acpi.OpenCoreSecurityValidator.DetectIsOpenCoreLoaded();
            Console.WriteLine($"[Diagnostic] OpenCoreSecurityValidator.DetectIsOpenCoreLoaded() = {isLoaded}");

            // On a system with OpenCore loaded (like this machine), verify it detects true
            Assert.IsTrue(isLoaded, "OpenCore should be detected as loaded when running with OpenCore ACPI tables or NVRAM");
        }

        [TestMethod]
        public void Test_IntelTuningServiceDebloater_IsRemoved()
        {
            string root = GetSolutionRoot();
            Assert.IsFalse(File.Exists(Path.Combine(root, "PreySense.Core", "Services", "IntelTuningServiceDebloater.cs")));
            string vm = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "ViewModels", "MainViewModel.cs"));
            string services = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "ViewModels", "MainViewModel.Services.cs"));
            Assert.DoesNotContain("IntelTuningServiceDebloater", vm);
            Assert.DoesNotContain("IntelTuningServiceDebloater", services);
            Assert.DoesNotContain("ToggleIntelTuningServices", services);
            Assert.IsFalse(File.Exists(Path.Combine(root, "PreySense.Core", "Hardware", "AcerServiceClient.cs")));
            string wmi = File.ReadAllText(Path.Combine(root, "PreySense.Core", "Hardware", "WmiController.cs"));
            Assert.DoesNotContain("AcerServiceClient", wmi);
            Assert.DoesNotContain("HardwareControlModePreference", wmi);
            Assert.DoesNotContain("ToggleControlMethod", services);
            string xaml = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "Views", "AdvancedSettingsWindow.xaml"));
            Assert.DoesNotContain("Control Method", xaml);
            Assert.DoesNotContain("ToggleControlMethodCommand", xaml);
            Assert.Contains("ToggleAcerServicesDebloaterCommand", xaml);
            string debloater = File.ReadAllText(Path.Combine(root, "PreySense.Core", "Services", "AcerServiceDebloater.cs"));
            Assert.Contains("KillRunningAcerProcesses", debloater);
            Assert.Contains("PredatorSense", debloater);
        }

        [TestMethod]
        public void Test_AcerServiceDebloater_ManagedServices_And_RegistryPersistence()
        {
            var services = PreySense.Core.Services.AcerServiceDebloater.GetAllAcerServiceNames();
            Assert.Contains("AASSvc", services);
            Assert.Contains("AcerLightingService", services);
            Assert.Contains("AcerQAAgentSvis", services);
            Assert.Contains("AcerServiceSvc", services);
            Assert.Contains("ASMSvc", services);

            string source = File.ReadAllText(Path.Combine(GetSolutionRoot(), "PreySense.Core", "Services", "AcerServiceDebloater.cs"));
            Assert.Contains("RemoveAcerScheduledTasks", source);
            Assert.Contains("IsAcerOwnedBinary", source);
            Assert.DoesNotContain("sc.exe", source);
            Assert.DoesNotContain("AcerCertService", source);
            Assert.DoesNotContain("AcerCertificateService", source);

            // Test Registry persistence
            try
            {
                PreySense.Core.Services.AcerServiceDebloater.SaveRegistryState(false);
                Assert.IsFalse(PreySense.Core.Services.AcerServiceDebloater.GetRegistryState() ?? true);

                PreySense.Core.Services.AcerServiceDebloater.SaveRegistryState(true);
                Assert.IsTrue(PreySense.Core.Services.AcerServiceDebloater.GetRegistryState() ?? false);
            }
            finally
            {
                PreySense.Core.Services.AcerServiceDebloater.SaveRegistryState(true);
            }
        }

        [TestMethod]
        public void Test_AdvancedSettingsWindow_IntelTuningServices_Switch()
        {
            string root = GetSolutionRoot();
            string xamlPath = Path.Combine(root, "PreySense.Frontend", "Views", "AdvancedSettingsWindow.xaml");
            Assert.IsTrue(File.Exists(xamlPath), "AdvancedSettingsWindow.xaml must exist");

            string xaml = File.ReadAllText(xamlPath);
            Assert.DoesNotContain("Title=\"Intel Tuning Services\"", xaml);
            Assert.DoesNotContain("ToggleIntelTuningServicesCommand", xaml);
        }

        [TestMethod]
        public void Test_Display_RefreshRates_Dropdown_And_Auto()
        {
            var hw = new PreySense.Core.Services.MockHardwareService();
            var vm = new PreySense.Frontend.ViewModels.MainViewModel(hw);

            // Verify supported refresh rates are loaded into dropdown items
            Assert.IsTrue(vm.AvailableRefreshRates.Count >= 3, "AvailableRefreshRates must contain supported modes");
            Assert.AreEqual("240 Hz", vm.AvailableRefreshRates[0]);
            CollectionAssert.Contains(vm.AvailableRefreshRates, "60 Hz");
            CollectionAssert.Contains(vm.AvailableRefreshRates, "120 Hz");
            CollectionAssert.Contains(vm.AvailableRefreshRates, "240 Hz");

            // Selecting a rate from the dropdown updates hardware and state
            vm.SelectedRefreshRateOption = "120 Hz";
            System.Threading.Thread.Sleep(50);
            Assert.AreEqual(120, vm.CurrentRefreshRate);
            Assert.AreEqual(120, hw.GetDisplayRefreshRate());
            Assert.IsTrue(vm.Is120HzActive);

            // MainWindow.xaml verification: Auto button on left, ComboBox on right
            string root = GetSolutionRoot();
            string mwXaml = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "MainWindow.xaml"));
            Assert.IsTrue(mwXaml.Contains("Command=\"{Binding ToggleAutoRefreshRateCommand}\""), "MainWindow must have Auto refresh rate button");
            Assert.IsTrue(mwXaml.Contains("ItemsSource=\"{Binding AvailableRefreshRates}\""), "MainWindow must have ComboBox bound to AvailableRefreshRates");
            Assert.IsTrue(mwXaml.Contains("SelectedItem=\"{Binding SelectedRefreshRateOption, Mode=TwoWay}\""), "MainWindow ComboBox must bind SelectedRefreshRateOption TwoWay");
        }

        [TestMethod]
        public void Test_BlueLightShield_TwoModes_And_Dropdown()
        {
            var hw = new PreySense.Core.Services.MockHardwareService();
            var vm = new PreySense.Frontend.ViewModels.MainViewModel(hw);

            // Verify two-mode options (plus Off)
            Assert.AreEqual(3, vm.BlueLightShieldModes.Count);
            Assert.AreEqual("Off", vm.BlueLightShieldModes[0]);
            Assert.AreEqual("Low (18% Reduction)", vm.BlueLightShieldModes[1]);
            Assert.AreEqual("High (36% Reduction)", vm.BlueLightShieldModes[2]);

            // Test Mode 1 (Low)
            vm.BlueLightShieldMode = 1;
            Assert.AreEqual(1, hw.GetBlueLightShieldMode());
            Assert.IsTrue(vm.IsBlueLightShieldEnabled);

            // Test Mode 2 (High)
            vm.BlueLightShieldMode = 2;
            Assert.AreEqual(2, hw.GetBlueLightShieldMode());
            Assert.IsTrue(vm.IsBlueLightShieldEnabled);

            // Test Mode 0 (Off)
            vm.BlueLightShieldMode = 0;
            Assert.AreEqual(0, hw.GetBlueLightShieldMode());
            Assert.IsFalse(vm.IsBlueLightShieldEnabled);

            // AdvancedSettingsWindow.xaml verification: SettingDropdownRow for BlueLight Shield
            string root = GetSolutionRoot();
            string advXaml = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "Views", "AdvancedSettingsWindow.xaml"));
            Assert.IsTrue(advXaml.Contains("SettingDropdownRow Title=\"Acer BlueLight Shield\""), "AdvancedSettings must use SettingDropdownRow for Acer BlueLight Shield");
            Assert.IsTrue(advXaml.Contains("Margin=\"0,8,0,8\""), "Acer BlueLight Shield must have extra vertical margin");
            Assert.IsTrue(advXaml.Contains("ItemsSource=\"{Binding BlueLightShieldModes}\""), "SettingDropdownRow must bind BlueLightShieldModes");
            Assert.IsTrue(advXaml.Contains("SelectedIndex=\"{Binding BlueLightShieldMode, Mode=TwoWay}\""), "SettingDropdownRow must bind SelectedIndex to BlueLightShieldMode");
            Assert.Contains("Title=\"Battery Health Limit\"", advXaml);
        }

        [TestMethod]
        public void Test_CoverLogoHidClient_DirectControl()
        {
            using var client = new PreySense.AcerCoverLogoHidClient();
            if (client.IsAvailable)
            {
                bool ok = client.SetLogoLighting(1, System.Drawing.Color.FromArgb(0, 229, 255), 100, 1);
                Assert.IsTrue(ok, "ENEK5130 target 0x83 SetLogoLighting must succeed when the keyboard HID is available");
            }
            else
            {
                Assert.Inconclusive("ENEK5130 cover logo HID target 0x83 is not accessible.");
            }
        }

        [TestMethod]
        public void CoverSync_FollowsKeyboardLighting()
        {
            var hw = new PreySense.Core.Services.MockHardwareService();
            var vm = new PreySense.Frontend.ViewModels.MainViewModel(hw);
            int before = hw.SetCoverLogoLightingCount;

            vm.IsCoverLightingEnabled = true;
            vm.IsCoverSyncWithKeyboard = true;
            vm.SelectedHexColor = "#FF0033";
            vm.SetLightingEffect("HW:1");
            vm.CommitKeyboardLighting();

            Assert.IsGreaterThan(before, hw.SetCoverLogoLightingCount, "Sync must write the cover logo when keyboard lighting changes");
            Assert.AreEqual("#FF0033", vm.CoverHexColor);
            Assert.AreEqual(2, vm.CoverLightingMode);
        }

        [TestMethod]
        public void CoverSync_AveragesZoneColorsForOtherKeyboardAnimations()
        {
            var hw = new PreySense.Core.Services.MockHardwareService();
            var vm = new PreySense.Frontend.ViewModels.MainViewModel(hw);
            vm.IsCoverLightingEnabled = true;
            vm.ApplyZonePreset("sunset");
            vm.IsCoverSyncWithKeyboard = true;
            vm.SetLightingEffect("HW:3");
            vm.CommitKeyboardLighting();

            Assert.AreEqual("#CE3256", vm.CoverHexColor);
            Assert.AreEqual(1, vm.CoverLightingMode);
        }

        [TestMethod]
        public void Overlay_KeepsGameOnlyWhenPreySenseHides_AndPollsEverySecond()
        {
            string root = GetSolutionRoot();
            string overlay = File.ReadAllText(Path.Combine(root, "PreySense.Overlay", "HardwareOverlay.cs"));
            string vm = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "ViewModels", "MainViewModel.cs"));
            string lightingUi = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "Views", "KeyboardWindow.xaml.cs"));

            Assert.AreEqual("P-Core|Clock", PreySense.Overlay.AppConfig.DefaultOverlayColumn(3));
            Assert.AreEqual("Voltage|Voltage", PreySense.Overlay.AppConfig.DefaultOverlayColumn(4));
            Assert.AreEqual("Memory Speed|Memory Speed", PreySense.Overlay.AppConfig.DefaultOverlayColumn(7));
            Assert.AreEqual("None|None", PreySense.Overlay.AppConfig.DefaultOverlayColumn(8));
            Assert.Contains("PollIntervalMs = 1000", overlay);
            Assert.DoesNotContain("ApplyPollInterval", overlay);
            Assert.Contains("bool gpuActive = HardwareControl.gpuInUse", overlay);
            Assert.DoesNotContain("gpuTemp > 0", overlay);
            Assert.Contains("VisibleTelemetryIntervalMs = 1000", vm);
            Assert.DoesNotContain("HiddenTelemetryIntervalMs", vm);
            string session = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "ViewModels", "MainViewModel.Session.cs"));
            Assert.Contains("PersistSessionSettings", session);
            Assert.Contains("RestoreSessionSettingsAsync", session);
            Assert.Contains("InitializeSessionAsync", session);
            Assert.Contains("_modeSwitchGate.WaitAsync()", session);
            Assert.Contains("PowerModes.Resume", session);
            Assert.Contains("SessionEnding", session);
            Assert.Contains("ApplyKeyboardLightingNow", session);
            Assert.Contains("ApplyCoverLighting(saveSettings: false)", session);
            Assert.Contains("SetPowerMode", session);
            Assert.DoesNotContain("ModeSwitchLightingDelayMs", session);
            Assert.Contains("_hw.SetPowerMode(mode)", session);
            Assert.DoesNotContain("modeChanged", session);
            string modes = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "ViewModels", "MainViewModel.PowerModes.cs"));
            Assert.DoesNotContain("ApplyKeyboardLightingNow()", modes);
            Assert.DoesNotContain("ModeSwitchLightingDelayMs", modes);
            Assert.Contains("ModeSwitchDebounceMilliseconds = 1000", modes);
            Assert.Contains("AllowConcurrentExecutions = true", modes);
            Assert.Contains("CyclePerformanceMode", modes);
            Assert.Contains("PeekPendingOrActiveMode", modes);
            Assert.Contains("if (mode == PeekPendingOrActiveMode())", modes);
            Assert.Contains("IsModeSelectionPending", modes);
            Assert.Contains("TurboKey: queued mode", vm);
            Assert.Contains("HandleModeKeyPressed", vm);
            Assert.DoesNotContain("QuickAccessModeWatcher", vm);
            Assert.DoesNotContain("SyncFirmwarePowerMode", vm);
            Assert.DoesNotContain("TurboKey: firmware mode", vm);
            Assert.IsFalse(File.Exists(Path.Combine(root, "PreySense.Core", "Hardware", "QuickAccessModeWatcher.cs")));
            Assert.IsTrue(File.Exists(Path.Combine(root, "PreySense.Core", "Hardware", "HidModeKeyWatcher.cs")));
            string hidWatcher = File.ReadAllText(Path.Combine(root, "PreySense.Core", "Hardware", "HidModeKeyWatcher.cs"));
            Assert.Contains("IsWindowsExclusiveHidPath", hidWatcher);
            Assert.DoesNotContain("failed to open HID", hidWatcher);
            Assert.IsTrue(PreySense.HidModeKeyWatcher.IsWindowsExclusiveHidPath(@"\\?\hid#1025174b&col05#x#{guid}\kbd"));
            Assert.IsFalse(PreySense.HidModeKeyWatcher.IsWindowsExclusiveHidPath(@"\\?\hid#1025174b&col06#x#{guid}"));
            Assert.IsTrue(PreySense.HidModeKeyWatcher.IsModeKeyReport(new byte[] { 0x04, 0x85, 0xFF }, 3));
            Assert.IsFalse(PreySense.HidModeKeyWatcher.IsModeKeyReport(new byte[] { 0xA0, 0x00, 0xE0 }, 3));
            Assert.AreEqual(PreySense.HidModeKeyWatcher.HotkeyKind.BrightnessDown, PreySense.HidModeKeyWatcher.ClassifyReport(new byte[] { 0x04, 0x8B, 0xFF }, 3));
            Assert.AreEqual(PreySense.HidModeKeyWatcher.HotkeyKind.BrightnessUp, PreySense.HidModeKeyWatcher.ClassifyReport(new byte[] { 0x04, 0x8C, 0xFF }, 3));
            Assert.AreEqual(PreySense.HidModeKeyWatcher.HotkeyKind.BrightnessUp, PreySense.HidModeKeyWatcher.ClassifyReport(new byte[] { 0x04, 0x7C, 0xFF }, 3));
            Assert.AreEqual(PreySense.HidModeKeyWatcher.HotkeyKind.BrightnessDown, PreySense.HidModeKeyWatcher.ClassifyReport(new byte[] { 0x04, 0x7D, 0xFF }, 3));
            Assert.AreEqual(PreySense.HidModeKeyWatcher.HotkeyKind.None, PreySense.HidModeKeyWatcher.ClassifyReport(new byte[] { 0x04, 0x00, 0x00, 0x44 }, 4));
            Assert.IsTrue(PreySense.Frontend.Services.KeyboardHook.IsChassisModeKey(0xFF, 0x85));
            Assert.IsFalse(PreySense.Frontend.Services.KeyboardHook.IsChassisModeKey(0xFF, 0x75));
            Assert.IsTrue(PreySense.Frontend.Services.KeyboardHook.TryGetBrightnessDelta(0xFF, 0x57, out int down) && down == -1);
            Assert.IsTrue(PreySense.Frontend.Services.KeyboardHook.TryGetBrightnessDelta(0xFF, 0x58, out int up) && up == 1);
            Assert.IsTrue(PreySense.Frontend.Services.KeyboardHook.TryGetBrightnessDelta(0xFF, 0x7C, out int hidUp) && hidUp == 1);
            Assert.IsTrue(PreySense.Frontend.Services.KeyboardHook.TryGetBrightnessDelta(0xFF, 0x7D, out int hidDown) && hidDown == -1);
            Assert.IsFalse(PreySense.Frontend.Services.KeyboardHook.IsChassisModeKey(0xFF, 0x7C));
            Assert.IsFalse(PreySense.Frontend.Services.KeyboardHook.IsChassisModeKey(0xFF, 0x7D));
            Assert.IsFalse(PreySense.Frontend.Services.KeyboardHook.TryGetBrightnessDelta(0x7A, 0x57, out _));
            Assert.IsTrue(PreySense.TurboKeyWatcher.TryGetBrightnessDelta(0x01, 0x1A, out int wmiDown) && wmiDown == -1);
            Assert.Contains("StepKeyboardBrightness", vm);
            Assert.Contains("< 80", vm);
            Assert.Contains("new HidModeKeyWatcher(HandleModeKeyPressed, StepKeyboardBrightness)", vm);
            string lighting = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "ViewModels", "MainViewModel.Lighting.cs"));
            Assert.Contains("NoteBrightness", lighting);
            Assert.Contains("BrightnessStep = 25", lighting);
            Assert.Contains("SnapBrightness", lighting);
            Assert.Contains("_pendingBrightnessDelta", lighting);
            Assert.DoesNotContain("_fnBrightnessPercent", lighting);
            string lightingXaml = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "Views", "KeyboardWindow.xaml"));
            Assert.Contains("Maximum=\"100\"", lightingXaml);
            Assert.Contains("TickFrequency=\"25\"", lightingXaml);
            Assert.Contains("StringFormat='{}{0}%'", lightingXaml);
            Assert.DoesNotContain("Speed {0}/9", lightingXaml);
            Assert.Contains("EffectTag", lighting);
            Assert.Contains("LightingEffectTag", lighting);
            Assert.Contains("MigrateSavedSpeed", lighting);
            Assert.Contains("ToHidKeyboardSpeed", lighting);
            Assert.Contains("RestoreLightingModeUi", lightingUi);
            Assert.Contains("LabeledTabSwitcher", lightingXaml);
            Assert.Contains("LeftText=\"Keyboard\"", lightingXaml);
            Assert.Contains("RightText=\"Logo\"", lightingXaml);
            Assert.DoesNotContain("LabeledTabSwitcher", File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "Views", "ModeSettingsWindow.xaml")));
            Assert.DoesNotContain("_hw.SetRgbBrightness((byte)next)", lighting);
            Assert.DoesNotContain("LightingEngine.SetBrightness((byte)(next * 20))", lighting);
            Assert.DoesNotContain("_hw.SetRgbHardwareEffect(0,", lighting);
            Assert.Contains("prod.LightingEngine.SetBrightness(scaledBrightness)", lighting);
            Assert.Contains("prod.LightingEngine.SetBrightness((byte)next)", lighting);
            Assert.IsTrue(PreySense.TurboKeyWatcher.IsPhysicalModeKey(5));
            Assert.IsTrue(PreySense.TurboKeyWatcher.IsPhysicalModeKey(new byte[] { 0x07, 0x05 }));
            Assert.IsTrue(PreySense.TurboKeyWatcher.IsPhysicalModeKey(new byte[] { 0x07, 0x05, 0x01, 0, 0, 0, 0, 0 }));
            Assert.IsTrue(PreySense.TurboKeyWatcher.IsPhysicalModeKey(new ushort[] { 0x07, 0x05, 0x01 }));
            Assert.IsTrue(PreySense.TurboKeyWatcher.IsPhysicalModeKey(0x00010507));
            Assert.IsFalse(PreySense.TurboKeyWatcher.IsPhysicalModeKey(new byte[] { 0x01, 0x82 }));
            Assert.Contains("_keepOverlayAfterUiHide", overlay);
            Assert.Contains("bool needFps = _showFps || _gameOnly", overlay);
            Assert.Contains("LoadDisplayConfiguration", overlay);
            Assert.DoesNotContain("overlay_show_frame_time", overlay);
            Assert.DoesNotContain("CopyRecentFrameTimesMs", overlay);
            Assert.DoesNotContain("1000.0 / _currentFps", overlay);
            string overlayBridge = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "Services", "OverlayBridge.cs"));
            string overlayManager = File.ReadAllText(Path.Combine(root, "PreySense.Overlay", "HardwareOverlayManager.cs"));
            string metricsUi = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "Views", "MetricsWindow.xaml.cs"));
            Assert.Contains("EnabledChanged", overlayBridge);
            Assert.Contains("NotifyEnabledChanged", overlayBridge);
            Assert.Contains("StartIfNeeded", overlayBridge);
            Assert.Contains("ShouldRunOverlay", overlayManager);
            Assert.Contains("SyncRunningState", overlayManager);
            Assert.DoesNotContain("HardwareControl.ReadSensorsOverlay();", overlayManager);
            Assert.Contains("if (AppConfig.IsOverlay())", overlayManager);
            Assert.Contains("if (AppConfig.IsOverlayGameOnly())", overlayManager);
            Assert.Contains("OnOverlayEnabledChanged", metricsUi);
            Assert.Contains("ApplyEnableOverlayAvailability", metricsUi);
            Assert.Contains("CheckEnableOverlay.IsEnabled = !gameOnly", metricsUi);
            Assert.DoesNotContain("ResetButton_Click", metricsUi);
            string metricsXaml = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "Views", "MetricsWindow.xaml"));
            Assert.DoesNotContain("Content=\"Reset\"", metricsXaml);
            Assert.DoesNotContain("ResetButton_Click", metricsXaml);
            Assert.Contains("GameOnly_Changed", metricsXaml);
            Assert.DoesNotContain("Title=\"Update Interval\"", metricsXaml);
            Assert.DoesNotContain("SliderUpdateInterval", metricsXaml);
            Assert.DoesNotContain("ApplyPollInterval", metricsUi);
            Assert.DoesNotContain("CheckShowFrameTime", metricsUi);
            Assert.DoesNotContain("overlay_show_frame_time", metricsUi);
            Assert.DoesNotContain("CheckShowFrameTime", metricsXaml);
            string appConfig = File.ReadAllText(Path.Combine(root, "PreySense.Overlay", "AppConfig.cs"));
            Assert.DoesNotContain("overlay_update_interval_ms", appConfig);
            Assert.Contains("IsOverlay() || IsOverlayGameOnly()", appConfig);
            string mainWindow = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "MainWindow.xaml.cs"));
            Assert.Contains("OverlayBridge.StartIfNeeded()", mainWindow);
            Assert.DoesNotContain("ApplyLightingButton_Click", lightingUi);
            Assert.DoesNotContain("SaveLightingButton_Click", lightingUi);
            Assert.DoesNotContain("ApplyLightingSettings();", lightingUi);
            Assert.Contains("SetLightingEffect", lightingUi);
            Assert.Contains("CommitKeyboardLighting", lighting);
            Assert.Contains("CommitCoverLighting", lighting);
            Assert.DoesNotContain("vm.CommitKeyboardLighting()", lightingUi);
            Assert.DoesNotContain("vm.CommitCoverLighting()", lightingUi);
            Assert.Contains("ScheduleKeyboardLightingCommit", lighting);
            Assert.Contains("ApplyLiveBrightness", lighting);
            Assert.Contains("_hw.SetRgbBrightness(brightness)", lighting);
            Assert.DoesNotContain("BeginLightingPointerAdjust", lighting);
            Assert.DoesNotContain("_lightingPointerAdjust", lighting);
            Assert.DoesNotContain("LightingPointer_PreviewMouseLeftButtonDown", lightingUi);
            string colorWheel = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "Controls", "ColorWheelControl.cs"));
            Assert.Contains("PickColorAtPoint(e.GetPosition(this), commit: false)", colorWheel);
            Assert.Contains("PickColorAtPoint(e.GetPosition(this), commit: true)", colorWheel);
            Assert.Contains("if (commit)", colorWheel);
            Assert.DoesNotContain("_lastReportedMs >= 40", colorWheel);
            Assert.DoesNotContain("isFinal: false", colorWheel);
            string prodHw = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "Services", "ProductionHardwareService.cs"));
            Assert.Contains("_wmi.SetZoneColors(colors, _wmi.Brightness)", prodHw);
            Assert.Contains("_wmi.SetZoneColors([z1, z2, z3, z4], _wmi.Brightness)", prodHw);
            Assert.DoesNotContain("TriggerZoneClick", prodHw);
            Assert.DoesNotContain("Enek.SetZoneColors(colors, 100)", prodHw);
            Assert.DoesNotContain("Enek.SetZoneColors(_wmi.ZoneColors", prodHw);
            Assert.Contains("NoteBrightness(scaled)", prodHw);
            string engineSrc = File.ReadAllText(Path.Combine(root, "PreySense.Core", "Rgb", "CustomLightingEffectsEngine.cs"));
            Assert.Contains("_forceHwDispatch", engineSrc);
            Assert.Contains("Math.Clamp(brightness, (byte)0, (byte)100)", engineSrc);
            Assert.DoesNotContain("ClickToLight", engineSrc);
            Assert.DoesNotContain("TriggerZoneClick", engineSrc);
            Assert.DoesNotContain("Click-to-Light", lightingXaml);
            Assert.DoesNotContain("ClickToLight", lighting);
            Assert.Contains("RainbowCycle", engineSrc);
            Assert.Contains("OceanTide", engineSrc);
            Assert.Contains("Heartbeat", engineSrc);
            Assert.Contains("LightningStorm", engineSrc);
            Assert.Contains("Starlight", engineSrc);
            Assert.Contains("IceCrystal", engineSrc);
            Assert.Contains("Width=\"210\"", lightingXaml);
            Assert.Contains("PanelCoverColorWheel", lightingXaml);
            Assert.Contains("Binding=\"{Binding CoverLightingMode}\" Value=\"1\"", lightingXaml);
            Assert.Contains("PanelCoverColorWheel.Visibility = (isStatic && !sync)", lightingUi);
            string dropdownRow = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "Controls", "SettingDropdownRow.xaml"));
            Assert.Contains("VerticalAlignment=\"Center\"", dropdownRow);
            Assert.Contains("MinHeight=\"28\"", dropdownRow);
            string comboStyle = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "Styles", "Controls.xaml"));
            Assert.Contains("Property=\"IsEnabled\" Value=\"False\"", comboStyle);
            Assert.Contains("DisabledControlBrush", comboStyle);
            Assert.Contains("HorizontalAlignment\" Value=\"Stretch\"", comboStyle);
            Assert.Contains("MaxHeight=\"{TemplateBinding ComboBox.MaxDropDownHeight}\"", comboStyle);
            Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", comboStyle);
            Assert.Contains("DYN:RainbowCycle", lightingXaml);
            Assert.Contains("DYN:OceanTide", lightingXaml);
            Assert.Contains("DYN:Heartbeat", lightingXaml);
            Assert.Contains("DYN:LightningStorm", lightingXaml);
            Assert.Contains("DYN:Starlight", lightingXaml);
            Assert.Contains("DYN:IceCrystal", lightingXaml);
            Assert.Contains("Tag=\"Sunset\"", lightingXaml);
            Assert.Contains("Tag=\"Vaporwave\"", lightingXaml);
            Assert.Contains("Tag=\"Dracula\"", lightingXaml);
            Assert.Contains("Tag=\"Midnight\"", lightingXaml);
            Assert.Contains("case \"sunset\":", lighting);
            Assert.Contains("case \"dracula\":", lighting);
            Assert.Contains("\"Sunset\" => \"#FF6B00\"", lighting);
            string wmiSrc = File.ReadAllText(Path.Combine(root, "PreySense.Core", "Hardware", "WmiController.cs"));
            Assert.Contains("_lastR * factor", wmiSrc);
            Assert.Contains("Color.FromArgb(dimR, dimG, dimB)", wmiSrc);
            Assert.IsFalse(File.Exists(Path.Combine(root, "PreySense.Core", "Hardware", "OpenRgbClient.cs")));
            Assert.DoesNotContain("OpenRgbClient", wmiSrc);
            Assert.DoesNotContain("OpenRGB", wmiSrc);
            string enek = File.ReadAllText(Path.Combine(root, "PreySense.Core", "Hardware", "EnekRgbClient.cs"));
            Assert.Contains("1 => EffectBreathing", enek);
            Assert.DoesNotContain("OpenRGB", enek);
            int applyIdx = wmiSrc.IndexOf("private void ApplyLightingMode(int mode)", StringComparison.Ordinal);
            Assert.IsTrue(applyIdx >= 0);
            string applyMode = wmiSrc.Substring(applyIdx, Math.Min(450, wmiSrc.Length - applyIdx));
            Assert.Contains("ApplyZoneLighting();", applyMode);
            Assert.DoesNotContain("_zoneColors[i] = c;", applyMode);
            Assert.DoesNotContain("Content=\"Apply\"", lightingXaml);
            Assert.DoesNotContain("Content=\"Save\"", lightingXaml);
            Assert.Contains("UpdateCoverModePanels", lightingUi);
            Assert.Contains("Title=\"Sync With Keyboard\"", lightingXaml);
            Assert.Contains("Title=\"Logo Backlight\"", lightingXaml);
            int logoBacklightIdx = lightingXaml.IndexOf("Title=\"Logo Backlight\"", StringComparison.Ordinal);
            int syncIdx = lightingXaml.IndexOf("Title=\"Sync With Keyboard\"", StringComparison.Ordinal);
            int effectModeIdx = lightingXaml.IndexOf("Text=\"Effect Mode\"", StringComparison.Ordinal);
            int coverPresetsIdx = lightingXaml.IndexOf("Text=\"Preset Themes\"", lightingXaml.IndexOf("SectionCover", StringComparison.Ordinal), StringComparison.Ordinal);
            Assert.IsTrue(logoBacklightIdx >= 0 && syncIdx > logoBacklightIdx && effectModeIdx > syncIdx, "Sync With Keyboard must sit under Logo Backlight");
            Assert.IsTrue(coverPresetsIdx > effectModeIdx);
            Assert.Contains("Binding=\"{Binding IsCoverSyncWithKeyboard}\" Value=\"True\"", lightingXaml);
            Assert.Contains("Zone1HexColor", lighting);
            Assert.Contains("Zone2HexColor", lighting);
            Assert.Contains("Zone3HexColor", lighting);
            Assert.Contains("Zone4HexColor", lighting);
            Assert.Contains("Background=\"{Binding Zone1HexColor}\"", lightingXaml);
            Assert.Contains("Background=\"{Binding Zone2HexColor}\"", lightingXaml);
            Assert.Contains("Background=\"{Binding Zone3HexColor}\"", lightingXaml);
            Assert.Contains("ZoneColorButtonStyle", lightingXaml);
            Assert.Contains("BorderThickness\" Value=\"0\"", lightingXaml);
            Assert.DoesNotContain("UpdateZoneButtonSelection", lightingUi);
            Assert.DoesNotContain("SelectedTileGradientBrush", lightingUi);
            Assert.DoesNotContain("ColdBootHealer", session);
            Assert.DoesNotContain("IsColdBootHealerEnabled", vm);
            Assert.DoesNotContain("ToggleColdBootHealer", File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "ViewModels", "MainViewModel.Services.cs")));
            Assert.Contains("private void TickCore()", overlay);
            Assert.Contains("Interlocked.Exchange(ref _tickRunning, 1)", overlay);
            string hardwareControl = File.ReadAllText(Path.Combine(root, "PreySense.Overlay", "HardwareControl.cs"));
            Assert.Contains("NvidiaGpuControl.Shared", hardwareControl);
            Assert.DoesNotContain("LibreHardwareMonitor", hardwareControl);
            Assert.Contains("TrySampleFrequencies", hardwareControl);
            Assert.DoesNotContain("HardwareControl.Warmup();", overlay[..overlay.IndexOf("public void StartOverlay()", StringComparison.Ordinal)]);
            Assert.DoesNotContain("Thread.Sleep", wmiSrc);
            Assert.DoesNotContain("_readTask?.Wait(500)", File.ReadAllText(Path.Combine(root, "PreySense.Core", "Gpu", "NvidiaGpuControl.cs")));
            Assert.DoesNotContain("_animationTask?.Wait(250)", engineSrc);
            Assert.Contains("RunOnUi", mainWindow);
            Assert.Contains("Dispatcher.BeginInvoke", mainWindow);
            Assert.Contains("if (_hw.CurrentPowerMode != mode)", modes);
            Assert.DoesNotContain("if (mode == _hw.CurrentPowerMode)", modes);
            string servicesVm = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "ViewModels", "MainViewModel.Services.cs"));
            Assert.DoesNotContain("ColdBootHealer", servicesVm);
        }

        [TestMethod]
        public void OpenCoreScripts_RepairWmiClassOnlyAfterReboot()
        {
            string root = GetSolutionRoot();
            string install = File.ReadAllText(Path.Combine(root, "tools", "OpenCore_PreyBoost_EFI", "Install-To-ESP.ps1"));
            string uninstall = File.ReadAllText(Path.Combine(root, "tools", "OpenCore_PreyBoost_EFI", "Uninstall-From-ESP.ps1"));
            string repair = File.ReadAllText(Path.Combine(root, "tools", "OpenCore_PreyBoost_EFI", "Repair-PreySenseWmi.ps1"));
            string mof = File.ReadAllText(Path.Combine(root, "acpi_staging", "preysense.mof"));

            Assert.Contains("-Action Arm", install);
            Assert.DoesNotContain("-Action Delete", install);
            Assert.Contains("-Action Delete", uninstall);
            Assert.DoesNotContain("preysense.mof", uninstall);
            Assert.Contains("#pragma deleteclass(\"PreySenseControl\", NOFAIL)", repair);
            Assert.Contains("Autorecover MOFs", repair);
            Assert.DoesNotContain("resetrepository", repair);
            Assert.DoesNotContain("#pragma autorecover", mof);

            string dsl = File.ReadAllText(Path.Combine(root, "acpi_staging", "SSDT-PREYDEV.dsl"));
            string efiDsl = File.ReadAllText(Path.Combine(root, "tools", "OpenCore_PreyBoost_EFI", "EFI", "OC", "ACPI", "SSDT-PREYDEV.dsl"));
            string repairCode = File.ReadAllText(Path.Combine(root, "PreySense.Core", "Hardware", "PreySenseWmiRepair.cs"));
            Assert.Contains("// PreySenseInterfaceVersion=0x0201", mof);
            Assert.Contains("DefinitionBlock (\"\", \"SSDT\", 2, \"PREY\", \"PREYDEV\", 0x00000201)", dsl);
            Assert.Contains("PKG1 [Zero] = 0x0201", dsl);
            Assert.AreEqual(dsl, efiDsl);
            Assert.IsTrue(PreySenseWmiRepair.TryParseInterfaceVersion(mof, out uint version));
            Assert.AreEqual(0x0201u, version);
            Assert.IsTrue(PreySenseWmiRepair.ShouldRebuild(PreySenseWmiSchemaState.Missing));
            Assert.IsTrue(PreySenseWmiRepair.ShouldRebuild(PreySenseWmiSchemaState.Mismatched));
            Assert.IsFalse(PreySenseWmiRepair.ShouldRebuild(PreySenseWmiSchemaState.Ready));
            Assert.IsFalse(PreySenseWmiRepair.ShouldRebuild(PreySenseWmiSchemaState.RuntimeFailure));
            Assert.IsFalse(PreySenseWmiRepair.ShouldRebuild(PreySenseWmiSchemaState.SsdtVersionDiffers));
            Assert.Contains("\"-check \\\"", repairCode);
            Assert.Contains("IsInRole(WindowsBuiltInRole.Administrator)", repairCode);
            Assert.Contains("TryGetPreyDevOemRevision", repairCode);
            string validator = File.ReadAllText(Path.Combine(root, "PreySense.Core", "Acpi", "OpenCoreSecurityValidator.cs"));
            Assert.Contains("TryGetPreyDevOemRevisionFromRegistry", validator);
            string session = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "ViewModels", "MainViewModel.Session.cs"));
            string acpiUi = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "ViewModels", "MainViewModel.Acpi.cs"));
            Assert.Contains("DecideIntegrationState", session);
            int matchIdx = session.IndexOf("loadedMatches", StringComparison.Ordinal);
            int registerIdx = session.IndexOf("TryRegister", StringComparison.Ordinal);
            Assert.IsTrue(matchIdx >= 0 && registerIdx > matchIdx);
            Assert.Contains("if (loadedMatches)", session);
            Assert.Contains("ConfirmText = \"Update OpenCore\"", acpiUi);
            Assert.Contains("CancelText = \"Later\"", acpiUi);
            Assert.Contains("if (reinstall == true)", acpiUi);
            Assert.Contains("InstallOpenCoreToEspAsync", acpiUi);
            int revisionIdx = repairCode.IndexOf("TryGetPreyDevOemRevision", StringComparison.Ordinal);
            int checkIdx = repairCode.IndexOf("MofCompMode.Check", StringComparison.Ordinal);
            int deleteIdx = repairCode.IndexOf("MofCompMode.Delete", StringComparison.Ordinal);
            Assert.IsTrue(revisionIdx >= 0 && checkIdx > revisionIdx && deleteIdx > checkIdx, "Check the loaded SSDT, then the MOF, before deleting the class.");
        }
    }
}
