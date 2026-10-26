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
            Assert.IsFalse(source.Contains("WindowsPowerMode"));
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
            Assert.IsTrue(File.Exists(Path.Combine(root, "PreySense.Frontend", "Controls", "WpfFanCurveControl.cs")));
        }

        [TestMethod]
        public void ModeSettingsWindow_DoesNotAddOverlayOrBoostControls()
        {
            string root = GetSolutionRoot();
            string xaml = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "Views", "ModeSettingsWindow.xaml"));
            Assert.IsFalse(xaml.Contains("ComboCpuBoost"));
            Assert.IsFalse(xaml.Contains("CPU Turbo Boost"));
            Assert.IsFalse(xaml.Contains("Windows Power"));
            Assert.IsFalse(xaml.Contains("ApplyOverlayScheme"));
            Assert.IsFalse(xaml.Contains("GpuCoreOffset"));
            Assert.IsFalse(xaml.Contains("GpuMemoryOffset"));
        }

        [TestMethod]
        public void AdvancedSettingsWindow_RemovesIgpuOnBatteryAndCpuBoost()
        {
            string root = GetSolutionRoot();
            string xaml = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "Views", "AdvancedSettingsWindow.xaml"));
            Assert.DoesNotContain("iGPU on Battery", xaml);
            Assert.DoesNotContain("CPU Boost", xaml);
            Assert.DoesNotContain("IsGpuBatteryAutoEnabled", xaml);
            Assert.DoesNotContain("CpuBoostModes", xaml);
            Assert.DoesNotContain("ResetAdvancedSettingsCommand", xaml);
            Assert.DoesNotContain("ApplyAdvancedSettingsCommand", xaml);
            Assert.DoesNotContain("SaveAdvancedSettingsCommand", xaml);
        }

        [TestMethod]
        public void DisplayManager_NoLongerExposesColorProfiles()
        {
            string root = GetSolutionRoot();
            string source = File.ReadAllText(Path.Combine(root, "PreySense.Core", "Display", "DisplayManager.cs"));
            Assert.IsFalse(source.Contains("class DisplayColorProfile"));
            Assert.IsFalse(source.Contains("LoadProfile(int hz)"));
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
        public void EcFanPolicy_SnapsDutyToFivePercentSteps()
        {
            Assert.AreEqual(0, EcFanPolicy.SnapDuty(1));
            Assert.AreEqual(45, EcFanPolicy.SnapDuty(47));
            Assert.AreEqual(50, EcFanPolicy.SnapDuty(48));
            Assert.AreEqual(100, EcFanPolicy.SnapDuty(99));
        }

        [TestMethod]
        public void SourceProfiles_MapUsbCAndBatteryModes()
        {
            Assert.AreEqual(0x01, ProfileManager.SourceProfileMode(1));
            Assert.AreEqual(0x06, ProfileManager.SourceProfileMode(4));
            Assert.AreEqual(1, ProfileManager.SourceProfileIndex(0x01));
            Assert.AreEqual(4, ProfileManager.SourceProfileIndex(0x06));
            Assert.AreEqual("Eco", ProfileManager.SourceProfileNames[4]);

            string xaml = File.ReadAllText(Path.Combine(GetSolutionRoot(), "PreySense.Frontend", "Views", "AdvancedWindow.xaml"));
            Assert.Contains("Power Profile", xaml);
            Assert.Contains("Title=\"On AC\"", xaml);
            Assert.Contains("Title=\"On USB-C\"", xaml);
            Assert.Contains("Title=\"On Battery\"", xaml);
            Assert.Contains("Energy Saver on Battery", xaml);
            Assert.Contains("AcPowerModeIndex", xaml);
            Assert.Contains("UsbCPowerModeIndex", xaml);
            Assert.Contains("BatteryPowerModeIndex", xaml);
            Assert.DoesNotContain("IsPowerProfileSwitchEnabled", xaml);

            Assert.IsTrue(WmiController.TryParseAcerPowerEvent(new byte[] { 8, 0 }, out PowerSourceKind battery));
            Assert.AreEqual(PowerSourceKind.Battery, battery);
            Assert.IsTrue(WmiController.TryParseAcerPowerEvent(new byte[] { 8, 1 }, out PowerSourceKind ac));
            Assert.AreEqual(PowerSourceKind.Ac, ac);
            Assert.IsTrue(WmiController.TryParseAcerPowerEvent(new byte[] { 8, 4 }, out PowerSourceKind usbC));
            Assert.AreEqual(PowerSourceKind.UsbC, usbC);
            Assert.IsFalse(WmiController.TryParseAcerPowerEvent(new byte[] { 1, 1 }, out _));
            Assert.AreEqual(100, PowerLimitController.EnergySaverBatteryThreshold(true));
            Assert.AreEqual(0, PowerLimitController.EnergySaverBatteryThreshold(false));
        }

        [TestMethod]
        public void GameProfileAndPowerSource_SerializedThroughModeSwitchGate_AndReappliedOnExit()
        {
            string root = GetSolutionRoot();
            string powerModesCs = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "ViewModels", "MainViewModel.PowerModes.cs"));
            string gamesCs = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "ViewModels", "MainViewModel.Games.cs"));

            Assert.Contains("await _modeSwitchGate.WaitAsync()", powerModesCs);
            Assert.Contains("ApplyModeSwitchCoreAsync", powerModesCs);
            Assert.Contains("CancelPendingModeSwitch()", powerModesCs);
            Assert.Contains("ApplyModeSwitchAsync", powerModesCs);

            // Game profiles serialize under gate
            Assert.Contains("CancelPendingModeSwitch()", gamesCs);
            Assert.Contains("await _modeSwitchGate.WaitAsync()", gamesCs);
            Assert.Contains("ApplyModeSwitchCoreAsync", gamesCs);
            Assert.Contains("PowerLimitController.SetCpuPowerLimits", gamesCs);

            // Game profile exit reapplies settings through ApplyModeSwitchAsync even when mode is unchanged
            Assert.Contains("await ApplyModeSwitchAsync(restore)", gamesCs);
            Assert.DoesNotContain("await SetModeAsync(restore)", gamesCs);

            // Power source change updates _modeBeforeGame when game is active and cancels pending debounce otherwise
            Assert.Contains("if (_appliedGameExe != null)", powerModesCs);
            Assert.Contains("_modeBeforeGame = mode", powerModesCs);
            Assert.Contains("CancelPendingModeSwitch()", powerModesCs);
        }

        [TestMethod]
        public void FanCurveWindow_ShowsCurveEditorAsDisabled_InAutoOrMaxMode()
        {
            string root = GetSolutionRoot();
            string fanWindowXaml = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "Views", "FanCurveWindow.xaml"));
            string fanWindowCs = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "Views", "FanCurveWindow.xaml.cs"));
            string fanCurveControlCs = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "Controls", "WpfFanCurveControl.cs"));

            // FanTabs is outside CurveEditor so users can switch between CPU/GPU tabs even when disabled
            Assert.Contains("x:Name=\"FanTabs\"", fanWindowXaml);
            Assert.Contains("x:Name=\"CurveEditor\"", fanWindowXaml);
            int fanTabsIndex = fanWindowXaml.IndexOf("x:Name=\"FanTabs\"", StringComparison.Ordinal);
            int curveEditorIndex = fanWindowXaml.IndexOf("x:Name=\"CurveEditor\"", StringComparison.Ordinal);
            Assert.IsTrue(fanTabsIndex < curveEditorIndex, "FanTabs should be positioned outside and above CurveEditor");

            // Editor is kept visible and disabled with dimming opacity; footer buttons only show in Custom mode
            Assert.Contains("CurveEditor.IsEnabled = isCustom;", fanWindowCs);
            Assert.Contains("CurveEditor.Opacity = isCustom ? 1.0 : 0.45;", fanWindowCs);
            Assert.DoesNotContain("CurveEditor.Visibility", fanWindowCs);
            Assert.Contains("FooterPanel.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;", fanWindowCs);

            // Control guards against interaction when disabled
            Assert.Contains("if (!IsEnabled || !BeginPointEdit(e.GetPosition(this))) return;", fanCurveControlCs);
            Assert.Contains("if (!IsEnabled) { Cursor = Cursors.Arrow; return; }", fanCurveControlCs);
            Assert.Contains("if (!IsEnabled) return;", fanCurveControlCs);
        }

        [TestMethod]
        public void FanCurveWindow_Reset_SwitchesToAutoAndResetsToDefault()
        {
            string root = GetSolutionRoot();
            string fanWindowCs = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "Views", "FanCurveWindow.xaml.cs"));

            // Reset creates default profile with Auto control mode, updates draft, persists and applies
            Assert.Contains("var defaultProfile = FanCurveProfile.CreateDefault(_mode);", fanWindowCs);
            Assert.Contains("Vm?.SaveFanProfile(_mode, defaultProfile);", fanWindowCs);
            Assert.Contains("await Vm.ApplyFanProfileAsync();", fanWindowCs);

            var def = FanCurveProfile.CreateDefault(1);
            Assert.AreEqual(FanControlMode.Auto, def.ControlMode);
            Assert.AreEqual(2, def.RampUpSeconds);
            CollectionAssert.AreEqual(FanCurveProfile.DefaultCurve(1), def.Cpu);
            CollectionAssert.AreEqual(FanCurveProfile.DefaultCurve(1, gpu: true), def.Gpu);
        }

        [TestMethod]
        public void PowerAndFanPolicy_DebounceCancellation_And_DraftSynchronization()
        {
            string root = GetSolutionRoot();
            string powerModesCs = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "ViewModels", "MainViewModel.PowerModes.cs"));
            string fanWindowCs = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "Views", "FanCurveWindow.xaml.cs"));
            string modeSettingsCs = File.ReadAllText(Path.Combine(root, "PreySense.Frontend", "Views", "ModeSettingsWindow.xaml.cs"));

            // ApplyModeSwitchAsync cancels pending debounce timer immediately
            Assert.Contains("CancelPendingModeSwitch();\n            await _modeSwitchGate.WaitAsync();", powerModesCs.Replace("\r\n", "\n"));

            // Running game does not overwrite persistent source profiles
            Assert.Contains("_appliedGameExe != null", powerModesCs);

            // Fan mode toggle synchronizes draft profile immediately
            Assert.Contains("SaveDraft();\n        UpdateEditor();", fanWindowCs.Replace("\r\n", "\n"));

            // Fan window Apply and Reset switch hardware mode immediately when not active
            Assert.Contains("await Vm.ApplyModeSwitchAsync(_mode);", fanWindowCs);

            // Mode settings window Apply switches hardware mode immediately when not confirmed active
            Assert.Contains("await vm.ApplyModeSwitchAsync(selectedMode);", modeSettingsCs);
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
