using PreySense;
using PreySense.Frontend.Services;

namespace PreySense.Tests;

[TestClass]
public class HotkeyModeFilterTests
{
    [TestMethod]
    public void OrdinaryNumberOneAndLettersAreNotBrightnessEvents()
    {
        Assert.IsFalse(KeyboardHook.IsBrightnessKey(0x31, 0x02));
        Assert.IsFalse(KeyboardHook.IsBrightnessKey(0x52, 0x13));
        Assert.IsFalse(KeyboardHook.IsBrightnessKey(0x57, 0x11));
        Assert.IsTrue(KeyboardHook.IsBrightnessKey(0xFF, 0x02));
    }

    [TestMethod]
    public void PredatorOneComboConsumesKeysAndInvokesOnceWithoutOpeningApp()
    {
        var hook = (KeyboardHook)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(KeyboardHook));
        int selected = 0, opened = 0, calls = 0;
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        typeof(KeyboardHook).GetField("_onPredatorNumberPressed", flags)!.SetValue(hook,
            (Action<int>)(number => { selected = number; calls++; }));
        typeof(KeyboardHook).GetField("_onPredatorSensePressed", flags)!.SetValue(hook, (Action)(() => opened++));
        var callback = typeof(KeyboardHook).GetMethod("HookCallback", flags)!;
        IntPtr buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(24);
        try
        {
            for (int i = 0; i < 24; i++) System.Runtime.InteropServices.Marshal.WriteByte(buffer, i, 0);
            void Key(uint vk, uint scan, bool down)
            {
                System.Runtime.InteropServices.Marshal.WriteInt32(buffer, 0, (int)vk);
                System.Runtime.InteropServices.Marshal.WriteInt32(buffer, 4, (int)scan);
                Assert.AreEqual((IntPtr)1, callback.Invoke(hook, new object[] { 0, (IntPtr)(down ? 0x100 : 0x101), buffer }));
            }
            Key(0xFF, 0x75, true);
            Key(0x31, 0x02, true);
            Key(0x31, 0x02, true); // auto-repeat is suppressed
            Key(0x31, 0x02, false);
            Key(0xFF, 0x75, false);
            Assert.AreEqual(1, selected);
            Assert.AreEqual(1, calls);
            Assert.AreEqual(0, opened);
        }
        finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer); }
    }

    [TestMethod]
    public void TurboKeyWatcher_IsPhysicalModeKey_RecognizesModeKey()
    {
        // PHN16S standard Mode key: Category 0x07, SubEvent 0x05
        Assert.IsTrue(TurboKeyWatcher.IsPhysicalModeKey(0x07, 0x05, 0x0001));
        // Param is ModeKeyId
        Assert.IsTrue(TurboKeyWatcher.IsPhysicalModeKey(0x07, 0x00, 0x0005));
        // Legacy Turbo button event without subevent/param
        Assert.IsTrue(TurboKeyWatcher.IsPhysicalModeKey(0x07, 0x00, 0x0000));
        // GHKE stuffed as first byte
        Assert.IsTrue(TurboKeyWatcher.IsPhysicalModeKey(0x05, 0x01, 0x0000));
        Assert.IsTrue(TurboKeyWatcher.IsPhysicalModeKey(0x05, 0x00, 0x0000));
        // Category 0 or 1 with subEventId 0x05
        Assert.IsTrue(TurboKeyWatcher.IsPhysicalModeKey(0x01, 0x05, 0x0000));
        Assert.IsTrue(TurboKeyWatcher.IsPhysicalModeKey(0x00, 0x05, 0x0000));
        // Category 0x03 with param ModeKeyCategory
        Assert.IsTrue(TurboKeyWatcher.IsPhysicalModeKey(0x03, 0x01, 0x0007));
    }

    [TestMethod]
    public void TurboKeyWatcher_IsPhysicalModeKey_StrictlyRejectsBrightnessAndLightingEvents()
    {
        // Category 0x07 with Fn+F11 brightness down (0x11) and Fn+F12 brightness up (0x12)
        Assert.IsFalse(TurboKeyWatcher.IsPhysicalModeKey(0x07, 0x11, 0x0000), "Fn+F11 (0x11) must not trigger mode key");
        Assert.IsFalse(TurboKeyWatcher.IsPhysicalModeKey(0x07, 0x12, 0x0000), "Fn+F12 (0x12) must not trigger mode key");
        // Backlight toggle / status
        Assert.IsFalse(TurboKeyWatcher.IsPhysicalModeKey(0x07, 0x02, 0x0000), "Backlight toggle (0x02) must not trigger mode key");
        Assert.IsFalse(TurboKeyWatcher.IsPhysicalModeKey(0x07, 0x84, 0x0000), "Backlight toggle (0x84) must not trigger mode key");

        // Other keyboard backlight event IDs under Category 0x07
        byte[] brightnessSubEvents = { 0x1A, 0x1B, 0x44, 0x45, 0x57, 0x58, 0x78, 0x79, 0x7A, 0x7C, 0x7D, 0x8B, 0x8C, 0xC4, 0xC5 };
        foreach (byte sub in brightnessSubEvents)
        {
            Assert.IsFalse(TurboKeyWatcher.IsPhysicalModeKey(0x07, sub, 0x0000), $"SubEvent 0x{sub:X2} must not trigger mode key");
            Assert.IsFalse(TurboKeyWatcher.IsPhysicalModeKey(0x01, sub, 0x0000), $"Category 0x01 SubEvent 0x{sub:X2} must not trigger mode key");
        }

        // Arbitrary non-mode events under Category 0x07 must not trigger mode switch
        Assert.IsFalse(TurboKeyWatcher.IsPhysicalModeKey(0x07, 0x20, 0x0000));
        Assert.IsFalse(TurboKeyWatcher.IsPhysicalModeKey(0x07, 0x30, 0x0000));
        Assert.IsFalse(TurboKeyWatcher.IsPhysicalModeKey(0x07, 0xFF, 0x0000));
    }

    [TestMethod]
    public void KeyboardHook_IsBrightnessKey_DetectsAllBrightnessKeys()
    {
        // VK_F11 and VK_F12
        Assert.IsTrue(KeyboardHook.IsBrightnessKey(0x7A, 0));
        Assert.IsTrue(KeyboardHook.IsBrightnessKey(0x7B, 0));

        // Scancodes
        uint[] scancodes = { 0x11, 0x12, 0x02, 0x1A, 0x1B, 0x44, 0x45, 0x57, 0x58, 0x78, 0x07, 0x79, 0x7A, 0x7C, 0x7D, 0x84, 0x8B, 0x8C, 0xC4, 0xC5 };
        foreach (uint sc in scancodes)
        {
            Assert.IsTrue(KeyboardHook.IsBrightnessKey(0x00, sc), $"Scancode 0x{sc:X2} must be identified as brightness key");
            Assert.IsTrue(KeyboardHook.IsBrightnessKey(0xFF, sc), $"Scancode 0x{sc:X2} with vk 0xFF must be identified as brightness key");
        }
    }

    [TestMethod]
    public void KeyboardHook_IsChassisModeKey_AcceptsOnlyChassisKeysAndRejectsBrightness()
    {
        // Recognized chassis mode key scancodes
        Assert.IsTrue(KeyboardHook.IsChassisModeKey(0x00, 0x85));
        Assert.IsTrue(KeyboardHook.IsChassisModeKey(0x00, 0x6A));
        Assert.IsTrue(KeyboardHook.IsChassisModeKey(0x00, 0x6B));
        Assert.IsTrue(KeyboardHook.IsChassisModeKey(0x00, 0x7E));

        // Rejected keys: Predator key, Copilot key
        Assert.IsFalse(KeyboardHook.IsChassisModeKey(0x00, 0x75)); // SC_PREDATOR
        Assert.IsFalse(KeyboardHook.IsChassisModeKey(0x00, 0x6E)); // Copilot

        // Brightness scancodes must never be chassis mode keys
        uint[] brightnessScancodes = { 0x11, 0x12, 0x02, 0x1A, 0x1B, 0x44, 0x45, 0x57, 0x58, 0x78, 0x07, 0x79, 0x7A, 0x7C, 0x7D, 0x84, 0x8B, 0x8C, 0xC4, 0xC5 };
        foreach (uint sc in brightnessScancodes)
        {
            Assert.IsFalse(KeyboardHook.IsChassisModeKey(0x00, sc), $"Brightness scancode 0x{sc:X2} must not be chassis mode key");
            Assert.IsFalse(KeyboardHook.IsChassisModeKey(0xFF, sc), $"Brightness scancode 0x{sc:X2} with vk 0xFF must not be chassis mode key");
        }
    }

    [TestMethod]
    public void KeyboardHook_TryGetBrightnessDelta_CalculatesExpectedDeltas()
    {
        // Decrement (-1)
        uint[] downScans = { 0x11, 0x1A, 0x44, 0x57, 0x78, 0x7A, 0x7D, 0x8B, 0xC5 };
        foreach (uint sc in downScans)
        {
            Assert.IsTrue(KeyboardHook.TryGetBrightnessDelta(0xFF, sc, out int delta));
            Assert.AreEqual(-1, delta);
        }

        // Increment (+1)
        uint[] upScans = { 0x12, 0x1B, 0x45, 0x58, 0x07, 0x79, 0x7C, 0x8C, 0xC4 };
        foreach (uint sc in upScans)
        {
            Assert.IsTrue(KeyboardHook.TryGetBrightnessDelta(0xFF, sc, out int delta));
            Assert.AreEqual(1, delta);
        }
    }

    [TestMethod]
    public void HidModeKeyWatcher_ClassifyReport_PrioritizesBrightnessOverMode()
    {
        // Mode reports
        Assert.AreEqual(HidModeKeyWatcher.HotkeyKind.Mode,
            HidModeKeyWatcher.ClassifyReport(new byte[] { 0x04, 0x85, 0xFF }, 3));
        Assert.AreEqual(HidModeKeyWatcher.HotkeyKind.Mode,
            HidModeKeyWatcher.ClassifyReport(new byte[] { 0x04, 0x05, 0x01 }, 3));

        // Brightness down reports
        Assert.AreEqual(HidModeKeyWatcher.HotkeyKind.BrightnessDown,
            HidModeKeyWatcher.ClassifyReport(new byte[] { 0x04, 0x11, 0xFF }, 3));
        Assert.AreEqual(HidModeKeyWatcher.HotkeyKind.BrightnessDown,
            HidModeKeyWatcher.ClassifyReport(new byte[] { 0x04, 0x57, 0xFF }, 3));
        Assert.AreEqual(HidModeKeyWatcher.HotkeyKind.BrightnessDown,
            HidModeKeyWatcher.ClassifyReport(new byte[] { 0x04, 0x7D, 0xFF }, 3));
        Assert.AreEqual(HidModeKeyWatcher.HotkeyKind.BrightnessDown,
            HidModeKeyWatcher.ClassifyReport(new byte[] { 0x04, 0x7A, 0xFF }, 3));

        // Brightness up reports
        Assert.AreEqual(HidModeKeyWatcher.HotkeyKind.BrightnessUp,
            HidModeKeyWatcher.ClassifyReport(new byte[] { 0x04, 0x12, 0xFF }, 3));
        Assert.AreEqual(HidModeKeyWatcher.HotkeyKind.BrightnessUp,
            HidModeKeyWatcher.ClassifyReport(new byte[] { 0x04, 0x58, 0xFF }, 3));
        Assert.AreEqual(HidModeKeyWatcher.HotkeyKind.BrightnessUp,
            HidModeKeyWatcher.ClassifyReport(new byte[] { 0x04, 0x7C, 0xFF }, 3));
        Assert.AreEqual(HidModeKeyWatcher.HotkeyKind.BrightnessUp,
            HidModeKeyWatcher.ClassifyReport(new byte[] { 0x04, 0x79, 0xFF }, 3));

        // Mode report release (buffer[2] == 0) must not be treated as Mode press
        Assert.AreEqual(HidModeKeyWatcher.HotkeyKind.None,
            HidModeKeyWatcher.ClassifyReport(new byte[] { 0x04, 0x05, 0x00 }, 3));
    }
}
