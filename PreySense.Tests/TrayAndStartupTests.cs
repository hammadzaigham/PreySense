using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PreySense.Frontend.Services;

namespace PreySense.Tests
{
    [TestClass]
    public class TrayAndStartupTests
    {
        [TestMethod]
        public void Test_StartupTaskService_TaskName_And_Query()
        {
            Assert.AreEqual("PreySense", StartupTaskService.TaskName);
            // Verify IsStartupEnabled does not throw
            bool isEnabled = StartupTaskService.IsStartupEnabled();
            Assert.IsTrue(isEnabled || !isEnabled);
        }

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
            StringAssert.Contains(xml, @"D:\Applications\Prey Sense\PreySense.exe");
            Assert.DoesNotContain("<DisallowStartIfOnBatteries>true</DisallowStartIfOnBatteries>", xml);
        }

        [TestMethod]
        public void Test_TrayIconManager_MessageHandling_And_Dispose()
        {
            bool openCalled = false;
            bool quitCalled = false;

            using var tray = new TrayIconManager(
                IntPtr.Zero,
                onOpen: () => openCalled = true,
                onQuit: () => quitCalled = true
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
            string cs = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "PreySense.Frontend", "MainWindow.xaml.cs"));
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
    }
}
