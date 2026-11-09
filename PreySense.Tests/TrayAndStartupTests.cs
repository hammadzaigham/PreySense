using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PreySense.Frontend.Services;

namespace PreySense.Tests
{
    [TestClass]
    public class TrayAndStartupTests
    {
        [TestMethod]
        public void StartupTask_LogonXml_RunsOnBattery()
        {
            string xml = StartupTaskService.BuildLogonTaskXml(
                @"D:\Applications\Prey Sense\PreySense.exe",
                "S-1-5-21-1-2-3-1001");

            StringAssert.Contains(xml, "<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>");
            StringAssert.Contains(xml, "<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>");
            StringAssert.Contains(xml, "<StartWhenAvailable>true</StartWhenAvailable>");
            StringAssert.Contains(xml, "<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>");
            StringAssert.Contains(xml, "<LogonType>InteractiveToken</LogonType>");
            StringAssert.Contains(xml, "<Arguments>--minimized</Arguments>");
            StringAssert.Contains(xml, @"<WorkingDirectory>D:\Applications\Prey Sense</WorkingDirectory>");
            StringAssert.Contains(xml, @"D:\Applications\Prey Sense\PreySense.exe");
            Assert.DoesNotContain("<DisallowStartIfOnBatteries>true</DisallowStartIfOnBatteries>", xml);
        }

        [TestMethod]
        public void StartupTask_WorkingDirectory_IsValidXmlWithSpecialCharacters()
        {
            string xml = StartupTaskService.BuildLogonTaskXml(
                @"D:\Apps & Tools\PreySense.exe", "S-1-5-21-1-2-3-1001");
            var document = System.Xml.Linq.XDocument.Parse(xml);
            System.Xml.Linq.XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
            Assert.AreEqual(@"D:\Apps & Tools", document.Root!
                .Element(ns + "Actions")!.Element(ns + "Exec")!.Element(ns + "WorkingDirectory")!.Value);
        }

        [TestMethod]
        public void StartupTask_UsesSchtasksOnly_NoRunKey()
        {
            string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            string startup = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "PreySense.Frontend", "Services", "StartupTaskService.cs"));
            Assert.Contains("schtasks.exe", startup);
            Assert.DoesNotContain(@"CurrentVersion\Run", startup);
            Assert.DoesNotContain("Registry.CurrentUser", startup);
            Assert.DoesNotContain(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", startup);
        }

        [TestMethod]
        public void Sources_HaveNoPreySenseHkcuAppKeys()
        {
            string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            string[] projects = ["PreySense.Core", "PreySense.Frontend", "PreySense.Overlay"];
            foreach (string project in projects)
            {
                string projectRoot = System.IO.Path.Combine(root, project);
                foreach (string path in System.IO.Directory.EnumerateFiles(projectRoot, "*.cs", System.IO.SearchOption.AllDirectories))
                {
                    if (path.Contains($"{System.IO.Path.DirectorySeparatorChar}obj{System.IO.Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                        || path.Contains($"{System.IO.Path.DirectorySeparatorChar}bin{System.IO.Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string source = System.IO.File.ReadAllText(path);
                    Assert.IsFalse(source.Contains("SOFTWARE" + "\\PreySense", StringComparison.Ordinal), path);
                    Assert.IsFalse(source.Contains("SOFTWARE" + "\\\\PreySense", StringComparison.Ordinal), path);
                }
            }
        }

        [TestMethod]
        public void Test_TrayIconManager_MessageHandling_And_Dispose()
        {
            bool openCalled = false;
            using var tray = new TrayIconManager(
                IntPtr.Zero,
                onOpen: () => openCalled = true,
                onQuit: static () => { }
            );

            // Simulate double click message
            bool handled = false;
            const int WM_TRAY_CALLBACK = 0x0400 + 2048;
            const int WM_LBUTTONDBLCLK = 0x0203;

            tray.WndProc(IntPtr.Zero, WM_TRAY_CALLBACK, IntPtr.Zero, (IntPtr)WM_LBUTTONDBLCLK, ref handled);
            Assert.IsTrue(handled, "Double-click message must be handled");
            Assert.IsTrue(openCalled, "onOpen callback must be invoked on double-click");
        }

        [TestMethod]
        public void MainWindow_QuitButton_CallsQuitApplication()
        {
            string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            string xaml = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "PreySense.Frontend", "MainWindow.xaml"));
            string cs = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "PreySense.Frontend", "MainWindow.xaml.cs"))
                + System.IO.File.ReadAllText(System.IO.Path.Combine(root, "PreySense.Frontend", "MainWindow.Visibility.cs"));
            Assert.Contains("BtnNavQuit", xaml);
            Assert.Contains("Click=\"QuitButton_Click\"", xaml);
            Assert.Contains("QuitButton_Click", cs);
            Assert.Contains("QuitApplication()", cs);
            Assert.Contains("_isExplicitExit = true", cs);
        }

        [TestMethod]
        [DoNotParallelize]
        public void TrayMenuFollowsThemeChangesWithoutPersistingTestSettings()
        {
            bool original = ThemeManager.IsLightTheme;
            try
            {
                ThemeManager.Apply(true, persist: false);
                Assert.IsTrue(ThemeManager.IsLightTheme);
                ThemeManager.Apply(false, persist: false);
                Assert.IsFalse(ThemeManager.IsLightTheme);
            }
            finally { ThemeManager.Apply(original, persist: false); }
            string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            string tray = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "PreySense.Frontend", "Services", "TrayIconManager.cs"));
            Assert.Contains("SetPreferredAppMode(isLight ? 3 : 2)", tray);
            Assert.Contains("FlushMenuThemes();", tray);
            Assert.DoesNotContain("SetPreferredAppMode(2);", tray);
        }

        [TestMethod]
        public void MainWindow_PredatorKeyActivation_UsesCleanForegroundAndFocusArchitecture()
        {
            string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            string cs = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "PreySense.Frontend", "MainWindow.xaml.cs"))
                + System.IO.File.ReadAllText(System.IO.Path.Combine(root, "PreySense.Frontend", "MainWindow.Visibility.cs"));
            Assert.Contains("SetForegroundWindow(hwnd)", cs);
            Assert.Contains("window.Activate()", cs);
            Assert.Contains("AttachThreadInput(targetThread, foregroundThread, true)", cs);
            Assert.Contains("AttachThreadInput(targetThread, foregroundThread, false)", cs);
            Assert.Contains("IsApplicationForeground(GetForegroundWindow())", cs);
            Assert.Contains("processId == (uint)Environment.ProcessId", cs);

            // Verify no forced Keyboard.Focus or window.Focus overriding control focus scopes
            Assert.DoesNotContain("Keyboard.Focus", cs);
            Assert.DoesNotContain("window.Focus()", cs);

            // Verify no topmost toggling or Z-order manipulation
            Assert.DoesNotContain("HwndTopmost", cs);
            Assert.DoesNotContain("SetWindowPos", cs);

            // Verify no fragile synthetic keystrokes or foreground lock hacks
            Assert.DoesNotContain("keybd_event", cs);
            Assert.DoesNotContain("VK_MENU", cs);
            Assert.DoesNotContain("AllowSetForegroundWindow", cs);
            Assert.DoesNotContain("LockSetForegroundWindow", cs);
        }

        [TestMethod]
        public void MainWindow_EcoModeTile_UsesModeEcoBrushOutline()
        {
            string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            string xaml = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "PreySense.Frontend", "MainWindow.xaml"));
            int ecoIndex = xaml.IndexOf("AutomationProperties.AutomationId=\"BtnModeEco\"", StringComparison.Ordinal);
            Assert.IsGreaterThanOrEqualTo(0, ecoIndex, "BtnModeEco must exist");
            int quietIndex = xaml.IndexOf("AutomationProperties.AutomationId=\"BtnModeQuiet\"", StringComparison.Ordinal);
            string ecoBlock = xaml.Substring(ecoIndex, quietIndex - ecoIndex);
            Assert.Contains("Property=\"BorderBrush\"", ecoBlock);
            Assert.Contains("Value=\"{DynamicResource ModeEcoBrush}\"", ecoBlock);
        }

        [TestMethod]
        public void KeyboardWindow_LightingModeDropdown_SeparatesDynamicAndSoftware()
        {
            string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            string xaml = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "PreySense.Frontend", "Views", "KeyboardWindow.xaml"));

            // Mode dropdown contains Static, Dynamic, Software
            Assert.Contains("Content=\"Static\"", xaml);
            Assert.Contains("Content=\"Dynamic\"", xaml);
            Assert.Contains("Content=\"Software\"", xaml);

            // Separate dynamic and animation panels exist
            Assert.Contains("x:Name=\"PanelDynamicControls\"", xaml);
            Assert.Contains("x:Name=\"PanelSoftwareControls\"", xaml);
            Assert.Contains("x:Name=\"ComboDynamicSelect\"", xaml);
            Assert.Contains("x:Name=\"ComboSoftwareSelect\"", xaml);

            // Dynamic section only contains hardware effects, not DYN:
            int dynPanelStart = xaml.IndexOf("x:Name=\"PanelDynamicControls\"", StringComparison.Ordinal);
            int softPanelStart = xaml.IndexOf("x:Name=\"PanelSoftwareControls\"", StringComparison.Ordinal);
            string dynBlock = xaml.Substring(dynPanelStart, softPanelStart - dynPanelStart);
            Assert.Contains("Tag=\"HW:1\"", dynBlock);
            Assert.Contains("Tag=\"HW:3\"", dynBlock);
            Assert.DoesNotContain("Tag=\"DYN:", dynBlock);

            // Preset Themes combo defaults to Predator (index 1 after Custom)
            Assert.Contains("x:Name=\"ComboPresetStatic\" SelectedIndex=\"1\"", xaml);
        }

        [TestMethod]
        public void MetricsWindow_HudScaleAndTextOpacity_FollowConfiguredBehavior()
        {
            string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            string xaml = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "PreySense.Frontend", "Views", "MetricsWindow.xaml"));
            string cs = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "PreySense.Frontend", "Views", "MetricsWindow.xaml.cs"));
            string overlayCs = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "PreySense.Overlay", "HardwareOverlay.cs"));

            // HUD scale increments by 5%
            Assert.Contains("SliderHudScale", xaml);
            Assert.Contains("SmallChange=\"5\" LargeChange=\"5\" TickFrequency=\"5\"", xaml);
            Assert.Contains("Math.Round(SliderHudScale.Value / 5.0) * 5", cs);

            // Separate text opacity slider exists
            Assert.Contains("x:Name=\"SliderTextOpacity\"", xaml);
            Assert.Contains("x:Name=\"LabelTextOpacity\"", xaml);
            Assert.Contains("Text=\"Text Opacity\"", xaml);
            Assert.Contains("SliderTextOpacity_ValueChanged", cs);
            Assert.Contains("overlay_text_alpha", cs);

            // Overlay reads the shared text opacity preference and snaps scale to 5% increments
            Assert.Contains("config.TextAlphaPercent * 2.55", overlayCs);
            Assert.Contains("Math.Round(next / 5.0) * 5", overlayCs);
            Assert.Contains("ScaleStepPercent = 5", overlayCs);
            Assert.DoesNotContain("Math.Round(next / 25.0) * 25", overlayCs);
        }
    }
}
