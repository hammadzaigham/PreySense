using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

namespace PreySense.Frontend.Services
{
    [SupportedOSPlatform("windows")]
    public class KeyboardHook : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYUP = 0x0105;
        private const uint SC_PREDATOR = 0x75;
        private const uint SC_MODE_KEY = 0x85;

        private readonly Action _onPredatorSensePressed;
        private readonly Action<int>? _onPredatorNumberPressed;
        private readonly Action? _onPredatorRPressed;
        private readonly Action? _onModeKeyPressed;
        private readonly Action<int>? _onBrightnessDelta;
        private readonly LowLevelKeyboardProc _proc;
        private IntPtr _hookId = IntPtr.Zero;
        private Thread? _hookThread;
        private uint _hookThreadId;
        private readonly ManualResetEventSlim _hookReady = new(false);
        private bool _isDisposed;

        private bool _isPredatorKeyPressed;
        private bool _modeKeyDown;
        private bool _brightnessKeyDown;
        private bool _predatorUsedInCombo;
        private bool _predatorRComboDown;
        private bool _suppressPredatorReleaseAction;
        private int? _activePredatorNumber;

        public KeyboardHook(
            Action onPredatorSensePressed,
            Action<int>? onPredatorNumberPressed = null,
            Action? onPredatorRPressed = null,
            Action? onModeKeyPressed = null,
            Action<int>? onBrightnessDelta = null)
        {
            _onPredatorSensePressed = onPredatorSensePressed ?? throw new ArgumentNullException(nameof(onPredatorSensePressed));
            _onPredatorNumberPressed = onPredatorNumberPressed;
            _onPredatorRPressed = onPredatorRPressed;
            _onModeKeyPressed = onModeKeyPressed;
            _onBrightnessDelta = onBrightnessDelta;
            _proc = HookCallback;
            _hookThread = new Thread(HookThreadMain)
            {
                IsBackground = true,
                Name = "PreySenseKeyboardHook"
            };
            _hookThread.Start();
            _hookReady.Wait(TimeSpan.FromSeconds(2));
        }

        private void HookThreadMain()
        {
            _hookThreadId = GetCurrentThreadId();
            try
            {
                _hookId = SetHook(_proc);
            }
            catch
            {
            }

            _hookReady.Set();
            while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
        }

        private IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (var curProcess = System.Diagnostics.Process.GetCurrentProcess())
            using (var curModule = curProcess.MainModule)
            {
                if (curModule != null && curModule.ModuleName != null)
                {
                    IntPtr moduleHandle = GetModuleHandle(curModule.ModuleName);
                    IntPtr hook = SetWindowsHookEx(WH_KEYBOARD_LL, proc, moduleHandle, 0);
                    if (hook != IntPtr.Zero)
                    {
                        return hook;
                    }
                }
            }
            return SetWindowsHookEx(WH_KEYBOARD_LL, proc, IntPtr.Zero, 0);
        }

        public static bool IsChassisModeKey(uint vkCode, uint scanCode)
        {
            // Acer Copilot firmware may expose this as the unknown 0x6E
            // keyboard usage. It is not the chassis/Turbo key.
            if (scanCode == 0x6E)
                return false;
            if (scanCode == SC_PREDATOR)
                return false;
            if (TryGetBrightnessDelta(vkCode, scanCode, out _))
                return false;
            if (scanCode == SC_MODE_KEY || scanCode == 0x6E || scanCode == 0x6A || scanCode == 0x6B || scanCode == 0x7E)
                return true;
            return (vkCode == 0xFF || vkCode == 0x00) && scanCode != 0;
        }

        public static bool TryGetBrightnessDelta(uint vkCode, uint scanCode, out int delta)
        {
            delta = 0;
            // Plain F11/F12 (vk 0x7A/0x7B) stay with the app. Fn+F11/F12 on this
            // EC arrive as the same scancodes with vk 0xFF/0x00, or as HID
            // keyboard-illum usages 0x7A/0x79.
            if (vkCode is not (0xFF or 0x00 or 0x08))
                return false;
            if (scanCode is 0x57 or 0x44 or 0x7A or 0x7D or 0x8B)
            {
                delta = -1;
                return true;
            }
            if (scanCode is 0x58 or 0x45 or 0x79 or 0x7C or 0x8C)
            {
                delta = 1;
                return true;
            }
            return false;
        }

        private static bool IsChassisModeKey(KBDLLHOOKSTRUCT hookStruct) =>
            IsChassisModeKey(hookStruct.vkCode, hookStruct.scanCode);

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                bool isKeyDown = wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN;
                if (isKeyDown)
                    PreySense.Helpers.KeyboardActivity.NoteKeyDown();
                bool isKeyUp = wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP;

                if (isKeyDown || isKeyUp)
                {
                    var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

                    bool isPhysicalPredatorKey = hookStruct.scanCode == SC_PREDATOR;

                    if (TryGetBrightnessDelta(hookStruct.vkCode, hookStruct.scanCode, out int brightnessDelta) &&
                        _onBrightnessDelta != null)
                    {
                        if (isKeyDown && !_brightnessKeyDown)
                        {
                            _brightnessKeyDown = true;
                            PreySense.AppLogger.Log($"TurboKey: Fn brightness vk=0x{hookStruct.vkCode:X2} sc=0x{hookStruct.scanCode:X2} delta={brightnessDelta}");
                            _onBrightnessDelta(brightnessDelta);
                        }
                        else if (isKeyUp)
                        {
                            _brightnessKeyDown = false;
                        }
                        return (IntPtr)1;
                    }

                    if (IsChassisModeKey(hookStruct) && _onModeKeyPressed != null)
                    {
                        if (isKeyDown && !_modeKeyDown)
                        {
                            _modeKeyDown = true;
                            PreySense.AppLogger.Log($"TurboKey: keyboard Mode/Turbo vk=0x{hookStruct.vkCode:X2} sc=0x{hookStruct.scanCode:X2} flags=0x{hookStruct.flags:X2}");
                            _onModeKeyPressed();
                        }
                        else if (isKeyUp)
                        {
                            _modeKeyDown = false;
                        }
                        return (IntPtr)1;
                    }

                    bool isPredatorKey = hookStruct.scanCode == SC_PREDATOR;
                    if (isPredatorKey)
                    {
                        if (isKeyDown)
                        {
                            if (!_isPredatorKeyPressed)
                            {
                                _isPredatorKeyPressed = true;
                                _predatorUsedInCombo = false;
                            }
                        }
                        else if (isKeyUp)
                        {
                            if (!_predatorUsedInCombo && !_suppressPredatorReleaseAction)
                                SafeInvoke(_onPredatorSensePressed);
                            _isPredatorKeyPressed = false;
                            _predatorUsedInCombo = false;
                            _predatorRComboDown = false;
                            _suppressPredatorReleaseAction = false;
                            _activePredatorNumber = null;
                        }
                        return (IntPtr)1;
                    }

                    // Predator key + number row (1-5) selects a performance mode directly
                    if (_isPredatorKeyPressed && _onPredatorNumberPressed != null &&
                        ((hookStruct.vkCode >= 0x31 && hookStruct.vkCode <= 0x35) ||   // top-row 1-5
                         (hookStruct.vkCode >= 0x61 && hookStruct.vkCode <= 0x65)))    // numpad 1-5
                    {
                        int number = hookStruct.vkCode >= 0x61
                            ? (int)(hookStruct.vkCode - 0x60)
                            : (int)(hookStruct.vkCode - 0x30);

                        if (isKeyDown)
                        {
                            if (_activePredatorNumber != number)
                            {
                                _predatorUsedInCombo = true;
                                _suppressPredatorReleaseAction = true;
                                _activePredatorNumber = number;
                            _onPredatorNumberPressed(number);
                            }
                        }
                        else if (isKeyUp && _activePredatorNumber == number)
                        {
                            _activePredatorNumber = null;
                        }
                        return (IntPtr)1;
                    }

                    // Predator key + R: triggers refresh rate cycle or overlay
                    if (_isPredatorKeyPressed && hookStruct.vkCode == 0x52)
                    {
                        if (isKeyDown && _onPredatorRPressed != null && !_predatorRComboDown)
                        {
                            _predatorRComboDown = true;
                            _predatorUsedInCombo = true;
                            _suppressPredatorReleaseAction = true;
                            _onPredatorRPressed();
                        }
                        else if (isKeyUp)
                        {
                            _predatorRComboDown = false;
                        }
                        return (IntPtr)1;
                    }

                    return CallNextHookEx(_hookId, nCode, wParam, lParam);
                }
            }
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private static void SafeInvoke(Action? action)
        {
            try { action?.Invoke(); }
            catch (Exception ex) { PreySense.AppLogger.Error($"Keyboard shortcut callback failed: {ex}"); }
        }

        #region Win32 API Imports

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref MSG lpMsg);

        private const uint WM_QUIT = 0x0012;

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int x;
            public int y;
        }

        #endregion

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            if (_hookThreadId != 0)
                PostThreadMessage(_hookThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);

            _hookThread?.Join(TimeSpan.FromSeconds(1));
            _hookReady.Dispose();
        }
    }
}
