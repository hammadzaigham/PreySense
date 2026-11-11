using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PreySense.Core.Services;
using PreySense.Helpers;
using PreySense.Mode;

namespace PreySense.Tests
{
    [TestClass]
    public class PowerPolicyAndFanTests
    {
        [TestMethod]
        public void OverlayScheme_MapsEcoAndQuietToEfficiency()
        {
            Assert.AreEqual(WindowsPowerPolicy.OverlayEfficiency, WindowsPowerPolicy.OverlaySchemeForMode(0x00));
            Assert.AreEqual(WindowsPowerPolicy.OverlayEfficiency, WindowsPowerPolicy.OverlaySchemeForMode(0x06));
        }

        [TestMethod]
        public void OverlayScheme_MapsBalancedToBalanced()
        {
            Assert.AreEqual(WindowsPowerPolicy.OverlayBalanced, WindowsPowerPolicy.OverlaySchemeForMode(0x01));
        }

        [TestMethod]
        public void OverlayScheme_MapsPerformanceAndTurboToPerformance()
        {
            Assert.AreEqual(WindowsPowerPolicy.OverlayPerformance, WindowsPowerPolicy.OverlaySchemeForMode(0x04));
            Assert.AreEqual(WindowsPowerPolicy.OverlayPerformance, WindowsPowerPolicy.OverlaySchemeForMode(0x05));
        }

        [TestMethod]
        public void ProfileManager_UsesQuietNameAndDropsOverlayValues()
        {
            Assert.AreEqual("Quiet", ProfileManager.ModeToProfileName(0x00));
            string source = File.ReadAllText(Path.Combine(GetSolutionRoot(), "PreySense.Core", "Mode", "PerformanceProfile.cs"));
            Assert.DoesNotContain("WindowsPowerMode", source);
        }

        [TestMethod]
        public void WindowFooters_UseSharedBottomMargin()
        {
            string root = GetSolutionRoot();
            string spacing = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "Styles", "Spacing.xaml"));
            string controls = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "Styles", "Controls.xaml"));
            string mainWindow = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "MainWindow.xaml"));
            Assert.Contains("x:Key=\"WindowFooterMargin\"", spacing);
            Assert.Contains("14,10,14,16", spacing);
            Assert.Contains("{StaticResource WindowFooterMargin}", controls);
            Assert.DoesNotContain("Margin=\"0,10,0,0\"", mainWindow);
        }

        [TestMethod]
        public void FansFooterOpensPerModeEditor()
        {
            string root = GetSolutionRoot();
            string mainWindow = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "MainWindow.xaml"));
            Assert.Contains("FansButton_Click", mainWindow);
            Assert.DoesNotContain("CustomCpuFanPercent", mainWindow);
            Assert.IsTrue(File.Exists(Path.Combine(root, "PreySense.Frontend", "Views", "FanCurveWindow.xaml")));
            Assert.IsTrue(File.Exists(Path.Combine(root, "PreySense.Frontend", "Views", "FanCurveWindow.xaml.cs")));
        }

        [TestMethod]
        public void ModeSettingsWindow_UsesCpuBoostDropdownWithoutLegacyExtras()
        {
            string root = GetSolutionRoot();
            string xaml = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "Views", "ModeSettingsWindow.xaml"));
            Assert.Contains("DropdownCpuBoost", xaml);
            Assert.DoesNotContain("SwitchCpuBoost", xaml);
            Assert.DoesNotContain("ComboCpuBoost", xaml);
            Assert.DoesNotContain("CPU Turbo Boost", xaml);
            Assert.DoesNotContain("Windows Power", xaml);
            Assert.DoesNotContain("ApplyOverlayScheme", xaml);
            Assert.DoesNotContain("GpuCoreOffset", xaml);
            Assert.DoesNotContain("GpuMemoryOffset", xaml);
        }

        [TestMethod]
        public void SettingsWindow_RemovesIgpuOnBatteryAndCpuBoost()
        {
            string root = GetSolutionRoot();
            string xaml = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "Views", "SettingsWindow.xaml"));
            Assert.DoesNotContain("iGPU on Battery", xaml);
            Assert.DoesNotContain("CPU Boost", xaml);
            Assert.DoesNotContain("IsGpuBatteryAutoEnabled", xaml);
            Assert.DoesNotContain("CpuBoostModes", xaml);
            Assert.DoesNotContain("ResetAdvancedSettingsCommand", xaml);
            Assert.DoesNotContain("ApplyAdvancedSettingsCommand", xaml);
            Assert.DoesNotContain("SaveAdvancedSettingsCommand", xaml);
            Assert.DoesNotContain("Factory Defaults", xaml);
            Assert.DoesNotContain("ViewFactoryDefaultsCommand", xaml);
        }

        [TestMethod]
        public void DisplayManager_NoLongerExposesColorProfiles()
        {
            string root = GetSolutionRoot();
            string source = File.ReadAllText(Path.Combine(root, "PreySense.Core", "Display", "DisplayManager.cs"));
            Assert.DoesNotContain("class DisplayColorProfile", source);
            Assert.DoesNotContain("LoadProfile(int hz)", source);
        }

        [TestMethod]
        public void EcFanPolicy_ClampsDutyToZeroThrough100()
        {
            Assert.AreEqual(0, EcFanPolicy.ClampDuty(-10));
            Assert.AreEqual(0, EcFanPolicy.ClampDuty(0));
            Assert.AreEqual(40, EcFanPolicy.ClampDuty(40));
            Assert.AreEqual(100, EcFanPolicy.ClampDuty(120));
        }

        [TestMethod]
        public void PowerEvents_NormalizeExternalPowerToAc()
        {
            Assert.IsTrue(WmiController.TryParseAcerPowerEvent(new byte[] { 8, 0 }, out var battery));
            Assert.AreEqual(PowerSourceKind.Battery, battery);
            Assert.IsTrue(WmiController.TryParseAcerPowerEvent(new byte[] { 8, 1 }, out var ac));
            Assert.AreEqual(PowerSourceKind.Ac, ac);
            Assert.IsTrue(WmiController.TryParseAcerPowerEvent(new byte[] { 8, 4 }, out var external));
            Assert.AreEqual(PowerSourceKind.Ac, external);
        }

        [TestMethod]
        public void FanCurveWindow_ShowsStageEditorAsDisabled_InAutoOrMaxMode()
        {
            string root = GetSolutionRoot();
            string fanWindowXaml = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "Views", "FanCurveWindow.xaml"));
            string fanWindowCs = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "Views", "FanCurveWindow.xaml.cs"));

            Assert.Contains("x:Name=\"FanTabs\"", fanWindowXaml);
            Assert.Contains("x:Name=\"Curve\"", fanWindowXaml);
            Assert.Contains("x:Name=\"CurveEditor\"", fanWindowXaml);
            Assert.Contains("WpfFanCurveControl", fanWindowXaml);

            Assert.Contains("CurveEditor.IsEnabled = isCustom && !_applying;", fanWindowCs);
            Assert.Contains("CurveEditor.Opacity = isCustom ? 1.0 : 0.45;", fanWindowCs);
            Assert.Contains("FooterPanel.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;", fanWindowCs);
        }

        [TestMethod]
        public void FanCurveWindow_Reset_SwitchesToAutoAndResetsToDefault()
        {
            string root = GetSolutionRoot();
            string fanWindowCs = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "Views", "FanCurveWindow.xaml.cs"));

            // Reset creates default EC stages, updates draft, persists and applies
            Assert.Contains("var defaultProfile = FanCurveProfile.CreateDefault(_mode);", fanWindowCs);
            Assert.Contains("await SaveProfileAsync(Vm, _mode, defaultProfile);", fanWindowCs);
            Assert.Contains("await Vm.ApplyFanProfileAsync();", fanWindowCs);

            var def = FanCurveProfile.CreateDefault(1);
            Assert.AreEqual(FanControlMode.Auto, def.ControlMode);
            Assert.AreEqual(0, def.RampUpSeconds);
            Assert.HasCount(FanCurveProfile.StageCount, def.Stages);
            Assert.AreEqual(1800, def.Stages[1].CpuTargetRpm);
        }

        [TestMethod]
        public void PowerAndFanPolicy_DebounceCancellation_And_DraftSynchronization()
        {
            string root = GetSolutionRoot();
            string powerModesCs = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "ViewModels", "MainViewModel.PowerModes.cs"));
            string fanWindowCs = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "Views", "FanCurveWindow.xaml.cs"));
            string modeSettingsCs = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "Views", "ModeSettingsWindow.xaml.cs"));

            // ApplyModeSwitchAsync cancels pending trailing mode apply immediately
            Assert.Contains("CancelPendingModeSwitch();\n            if (!await TryEnterModeSwitchGateAsync()) return;", powerModesCs.Replace("\r\n", "\n"));
            Assert.Contains("AcerOperatingModeTiming.TransitionDelay", powerModesCs);
            Assert.DoesNotContain("ModeSwitchDebounceMilliseconds", powerModesCs);

            // Fan mode toggle synchronizes draft profile immediately
            Assert.Contains("SaveDraft();\n        UpdateEditor();", fanWindowCs.Replace("\r\n", "\n"));

            // Fan window Apply and Reset switch hardware mode immediately when not active
            Assert.Contains("await vm.ApplyModeSwitchAsync(_mode);", fanWindowCs);

            // Mode settings window applies tuning profile via driver transaction backend
            Assert.Contains("await vm.ApplyTuningProfileAsync", modeSettingsCs);
        }

        private static string GetSolutionRoot()
        {
            DirectoryInfo? dir = new(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PreySense.slnx")))
                dir = dir.Parent;
            return dir?.FullName ?? AppDomain.CurrentDomain.BaseDirectory;
        }
    }
}
