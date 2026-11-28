using PreySense;

namespace PreySense.Tests;

[TestClass]
public class ChiconySpecialKeyTests
{
    [TestMethod]
    public void IsChiconySpecialKeyPath_MatchesSupportedPidsAndRejectsOthers()
    {
        Assert.IsTrue(HidModeKeyWatcher.IsChiconySpecialKeyPath(
            @"\\?\hid#vid_04f2&pid_0117&mi_02&col03#7&123456&0&0002#{4d1e55b2-f16f-11cf-88cb-001111000030}"));
        Assert.IsTrue(HidModeKeyWatcher.IsChiconySpecialKeyPath(
            @"\\?\HID#VID_04F2&PID_011A&MI_02&COL03#7&123456&0&0002#{4d1e55b2-f16f-11cf-88cb-001111000030}"));
        Assert.IsTrue(HidModeKeyWatcher.IsChiconySpecialKeyPath(
            @"\\?\hid#vid_04f2&pid_0119&mi_02&col03#7&123456&0&0002#{4d1e55b2-f16f-11cf-88cb-001111000030}"));

        // Wrong VID / PID / Windows-exclusive keyboard interface
        Assert.IsFalse(HidModeKeyWatcher.IsChiconySpecialKeyPath(
            @"\\?\hid#vid_04f2&pid_0118&mi_02&col03#7&123456&0&0002#{4d1e55b2-f16f-11cf-88cb-001111000030}"));
        Assert.IsFalse(HidModeKeyWatcher.IsChiconySpecialKeyPath(
            @"\\?\hid#vid_0d62&pid_0117&mi_02&col03#7&123456&0&0002#{4d1e55b2-f16f-11cf-88cb-001111000030}"));
        Assert.IsFalse(HidModeKeyWatcher.IsChiconySpecialKeyPath(
            @"\\?\hid#vid_04f2&pid_0117&mi_00#7&123456&0&0000#{884b96c3-56ef-11d1-bc8c-00a0c91405dd}\kbd"));
        Assert.IsFalse(HidModeKeyWatcher.IsChiconySpecialKeyPath(""));
    }

    [TestMethod]
    public void IsChiconySpecialKeyCaps_RequiresUsagePage0088AndAtLeast3ByteInputReport()
    {
        Assert.IsTrue(HidModeKeyWatcher.IsChiconySpecialKeyCaps(0x0088, 3));
        Assert.IsTrue(HidModeKeyWatcher.IsChiconySpecialKeyCaps(0x0088, 8));

        Assert.IsFalse(HidModeKeyWatcher.IsChiconySpecialKeyCaps(0xFF02, 65));
        Assert.IsFalse(HidModeKeyWatcher.IsChiconySpecialKeyCaps(0x0001, 8));
        Assert.IsFalse(HidModeKeyWatcher.IsChiconySpecialKeyCaps(0x0088, 0));
        Assert.IsFalse(HidModeKeyWatcher.IsChiconySpecialKeyCaps(0x0088, 2));
    }

    [TestMethod]
    public void ClassifyReport_RecognizesPh16ChiconySpecialKeyReportsAndIgnoresRelease()
    {
        Assert.AreEqual(HidModeKeyWatcher.HotkeyKind.BrightnessDown,
            HidModeKeyWatcher.ClassifyReport(new byte[] { 0x04, 0x7D, 0xFF }, 3));
        Assert.AreEqual(HidModeKeyWatcher.HotkeyKind.BrightnessUp,
            HidModeKeyWatcher.ClassifyReport(new byte[] { 0x04, 0x7C, 0xFF }, 3));
        Assert.AreEqual(HidModeKeyWatcher.HotkeyKind.Mode,
            HidModeKeyWatcher.ClassifyReport(new byte[] { 0x04, 0x81, 0xFF }, 3));

        // Release and invalid/truncated reports
        Assert.AreEqual(HidModeKeyWatcher.HotkeyKind.None,
            HidModeKeyWatcher.ClassifyReport(new byte[] { 0x04, 0x00, 0x00 }, 3));
        Assert.AreEqual(HidModeKeyWatcher.HotkeyKind.None,
            HidModeKeyWatcher.ClassifyReport(new byte[] { 0x04, 0x81, 0x00 }, 3));
        Assert.AreEqual(HidModeKeyWatcher.HotkeyKind.None,
            HidModeKeyWatcher.ClassifyReport(new byte[] { 0x04, 0x81 }, 2));
    }
}
