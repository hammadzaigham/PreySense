using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PreySense.Core.Configuration;

namespace PreySense.Tests
{
    [TestClass]
    public class ConfigurationServiceTests
    {
        private string _testDir = null!;
        private string _testConfigPath = null!;

        [TestInitialize]
        public void Setup()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "PreySenseConfigTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
            _testConfigPath = Path.Combine(_testDir, ConfigurationService.ConfigFileName);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try
            {
                if (Directory.Exists(_testDir))
                {
                    Directory.Delete(_testDir, true);
                }
            }
            catch { }
        }

        [TestMethod]
        public void RemovedOverlayMetrics_AreClearedAndNvapiClockIsRetained()
        {
            var service = new ConfigurationService(_testConfigPath);
            service.Update(cfg => cfg.Overlay.Slots = new()
            {
                new() { CpuMetric = "P-Core Usage", GpuMetric = "Effective Clock" },
                new() { CpuMetric = "E-Core", GpuMetric = "Active Clock" },
                new() { CpuMetric = "Effective Clock", GpuMetric = "Clock" }
            });
            var slots = service.Reload().Overlay.Slots;
            Assert.AreEqual("None", slots[0].CpuMetric);
            Assert.AreEqual("None", slots[0].GpuMetric);
            Assert.AreEqual("None", slots[1].CpuMetric);
            Assert.AreEqual("Clock", slots[1].GpuMetric);
            Assert.AreEqual("Effective Clock", slots[2].CpuMetric);
            Assert.AreEqual("Clock", slots[2].GpuMetric);
        }

        [TestMethod]
        public void LoadOrInitialize_WhenFileDoesNotExist_ReturnsDocumentedDefaults()
        {
            var service = new ConfigurationService(_testConfigPath);
            var config = service.GetCurrent();

            Assert.IsNotNull(config);
            Assert.AreEqual(PreySenseConfig.CurrentSchemaVersion, config.SchemaVersion);
            Assert.AreEqual("en-US", config.General.Language);
            Assert.IsFalse(config.General.LightTheme);
            Assert.IsNull(config.Tuning.Quiet.CpuPl1Watts);
            Assert.IsNull(config.Tuning.Quiet.CpuPl2Watts);
            Assert.IsNull(config.Tuning.Turbo.CpuPl1Watts);
            Assert.IsNull(config.Tuning.Turbo.GpuWatts);
            Assert.IsNotNull(config.Overlay.Slots);
            Assert.IsNotEmpty(config.Overlay.Slots);
            Assert.IsFalse(File.Exists(_testConfigPath), "Loading defaults without save should not write to disk yet");
        }

        [TestMethod]
        public void Save_AtomicallyPersistsJson_AndRoundTripsCleanly()
        {
            var service = new ConfigurationService(_testConfigPath);
            var config = service.GetCurrent();

            config.General.Language = "fr-FR";
            config.General.LightTheme = true;
            config.Tuning.Balanced.CpuPl1Watts = 60;
            config.Tuning.Balanced.CpuPl2Watts = 85;
            config.Tuning.GpuBasePowerWatts = 115;
            config.General.AcerServicesDebloated = true;
            config.Overlay.ScalePercent = 125;
            config.Overlay.PositionHorizontal = "Right";

            service.Save(config);

            Assert.IsTrue(File.Exists(_testConfigPath), "File must exist after save");

            // Verify a separate service instance reading the file gets exact values
            var roundTripService = new ConfigurationService(_testConfigPath);
            var loaded = roundTripService.GetCurrent();

            Assert.AreEqual("fr-FR", loaded.General.Language);
            Assert.IsTrue(loaded.General.LightTheme);
            Assert.AreEqual(60, loaded.Tuning.Balanced.CpuPl1Watts);
            Assert.AreEqual(85, loaded.Tuning.Balanced.CpuPl2Watts);
            Assert.AreEqual(115, loaded.Tuning.GpuBasePowerWatts);
            Assert.IsTrue(loaded.General.AcerServicesDebloated);
            Assert.AreEqual(125, loaded.Overlay.ScalePercent);
            Assert.AreEqual("Right", loaded.Overlay.PositionHorizontal);
        }

        [TestMethod]
        public void Update_MutatesSpecificSection_AndPreservesUnrelatedSections()
        {
            var service = new ConfigurationService(_testConfigPath);
            var initial = service.GetCurrent();
            initial.General.Language = "es-ES";
            initial.Lighting.Keyboard.SelectedColorHex = "#FF00AA";
            service.Save(initial);

            // Update only Overlay settings
            service.Update(cfg =>
            {
                cfg.Overlay.Enabled = true;
                cfg.Overlay.BackgroundAlphaPercent = 80;
            });

            var reloaded = service.Reload();

            // Overlay updated
            Assert.IsTrue(reloaded.Overlay.Enabled);
            Assert.AreEqual(80, reloaded.Overlay.BackgroundAlphaPercent);

            // Unrelated sections untouched
            Assert.AreEqual("es-ES", reloaded.General.Language);
            Assert.AreEqual("#FF00AA", reloaded.Lighting.Keyboard.SelectedColorHex);
            Assert.IsNull(reloaded.Tuning.Balanced.CpuPl1Watts);
        }

        [TestMethod]
        public void Validate_RejectsOutOfRangeValues_WithClearErrors()
        {
            var service = new ConfigurationService(_testConfigPath);
            var config = service.GetCurrent();

            // PL1 out of range
            config.Tuning.Performance.CpuPl1Watts = 500;
            bool caughtPl1 = false;
            try
            {
                service.Save(config);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                caughtPl1 = true;
                Assert.Contains("PL1 for Performance must be between 5W and 300W", ex.Message, ex.Message);
            }
            Assert.IsTrue(caughtPl1, "Expected ArgumentOutOfRangeException for PL1 > 300W");

            config.Tuning.Performance.CpuPl1Watts = 70;

            // Reset and test invalid power mode
            config.Session.ActivePowerMode = 99;
            bool caughtMode = false;
            try
            {
                service.Save(config);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                caughtMode = true;
                Assert.Contains("ActivePowerMode must be 0, 1, 4, 5, or 6", ex.Message, ex.Message);
            }
            Assert.IsTrue(caughtMode, "Expected ArgumentOutOfRangeException for invalid power mode");
        }

        [TestMethod]
        public void Reload_OnMalformedJson_ThrowsClearInvalidOperationException()
        {
            File.WriteAllText(_testConfigPath, "{ \"General\": { \"Language\": \"en-US\" incomplete-json");

            bool caught = false;
            try
            {
                _ = new ConfigurationService(_testConfigPath);
            }
            catch (InvalidOperationException ex)
            {
                caught = true;
                Assert.Contains("Failed to load configuration file", ex.Message, ex.Message);
            }
            Assert.IsTrue(caught, "Expected InvalidOperationException for malformed JSON");
        }

        [TestMethod]
        public void MissingFields_ReceiveDocumentedDefaults()
        {
            // Minimal valid JSON containing only SchemaVersion and General
            string minimalJson = @"{
                ""SchemaVersion"": 1,
                ""General"": {
                    ""Language"": ""zh-CN""
                }
            }";
            File.WriteAllText(_testConfigPath, minimalJson);

            var service = new ConfigurationService(_testConfigPath);
            var config = service.GetCurrent();

            Assert.AreEqual("zh-CN", config.General.Language);
            Assert.IsNotNull(config.Tuning);
            Assert.IsNull(config.Tuning.Quiet.CpuPl1Watts);
            Assert.IsNotNull(config.Overlay);
            Assert.IsNotNull(config.Overlay.Slots);
            Assert.IsNotEmpty(config.Overlay.Slots);
            Assert.AreEqual("Usage", config.Overlay.Slots[0].CpuMetric);
        }

        [TestMethod]
        public void ResetToDefaults_PersistsDefaultValues()
        {
            var service = new ConfigurationService(_testConfigPath);
            service.Update(c =>
            {
                c.General.Language = "fr-FR";
                c.Tuning.Turbo.CpuPl1Watts = 120;
            });

            Assert.AreEqual(120, service.GetCurrent().Tuning.Turbo.CpuPl1Watts);

            var reset = service.ResetToDefaults();
            Assert.AreEqual("en-US", reset.General.Language);
            Assert.IsNull(reset.Tuning.Turbo.CpuPl1Watts);

            var reloaded = new ConfigurationService(_testConfigPath).GetCurrent();
            Assert.AreEqual("en-US", reloaded.General.Language);
            Assert.IsNull(reloaded.Tuning.Turbo.CpuPl1Watts);
        }
    }
}
