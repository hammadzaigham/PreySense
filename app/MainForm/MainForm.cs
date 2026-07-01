using System;
using System.IO;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using PreySense.Dialogs;
using PreySense.UI;
using PreySense.UI.Controls;
using PreySense.Display;
using PreySense.Fan;
using PreySense.Helpers;
using PreySense.Input;
using PreySense.Battery;
using PreySense.Mode;

namespace PreySense
{
    [SupportedOSPlatform("windows")]
    public partial class MainForm : RForm
    {
        #region Win32 Interop

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll")]
        private static extern bool ChangeWindowMessageFilter(uint message, uint dwFlag);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern IntPtr SetActiveWindow(IntPtr hWnd);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_TRANSITIONS_FORCEDISABLED = 3;

        private const int SW_RESTORE = 9;
        private static readonly uint WM_SHOWME = RegisterWindowMessage("PREY_SENSE_SHOW_INSTANCE");

        #endregion

        #region Fields

        private WmiController? _wmiInstance;
        private WmiController _wmi => _wmiInstance ??= new WmiController();
        public WmiController Wmi => _wmi;
        private KeyboardHook? _keyboardHook;
        private WmiHotkeyWatcher? _wmiHotkeyWatcher;
        private QuickAccessModeWatcher? _quickAccessModeWatcher;
        private Overlay.ModeToastNotification? _modeToast;
        private System.Windows.Forms.Timer _timer = new();
        private ColorDialog _colorPicker = new() { FullOpen = true };
        private NotifyIcon? _trayIcon;
        private ContextMenuStrip? _trayMenu;

        private int _cpuTemp, _gpuTemp, _cpuRpm, _gpuRpm;
        public int CpuTemp => _cpuTemp;
        public int GpuTemp => _gpuTemp;
        public int CpuRpm => _cpuRpm;
        public int GpuRpm => _gpuRpm;
        private int _gpuMode = 1;
        public int GpuMode => _gpuMode;
        private bool _gpuBatteryAuto = false;
        private byte _lastKnownProfile = 0xFF;
        private bool? _isPluggedIn = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;
        private bool _isLoaded = false;
        private bool _isApplyingSavedBatteryLimit = false;
        private bool _isApplyingSavedRgbState = false;
        private bool _isTelemetryUpdating = false;
        private int _fanRampUp = 1;
        private int _pendingBatteryMode = -1;
        private int _maxHz = 60;
        private int _lastRefreshRate = -1;
        private int _lastAppliedGammaRefreshRate = -1;
        private List<int>? _supportedRefreshRates;
        private int _currentRefreshRate = 60;
        private bool _showBatteryTelemetry = true;
        private byte _prevPowerMode = 0x01; // Balanced
        private DateTime _lastModeChangeTime = DateTime.MinValue;
        private DateTime _lastDirectModeShortcutTime = DateTime.MinValue;
        private DateTime _lastModeToastTime = DateTime.MinValue;
        private bool _allowExit = false;
        private const int DirectShortcutEchoSuppressMs = 5000;
        private const int ModeToastDebounceMs = 2000;

        private static readonly string[] RgbModeNames = RgbProfile.UiModeNames;

        private static readonly Color FormBg = Color.FromArgb(28, 28, 28);
        private static readonly Color SeparatorColor = Color.FromArgb(55, 55, 55);
        private static readonly Color AccentColor = Color.FromArgb(58, 174, 239);
        private static readonly Color PerformanceModeColor = Color.FromArgb(147, 51, 234);
        private static readonly Color TurboModeColor = Color.FromArgb(255, 32, 32);

        // Sub-forms active references
        public Fans? fansForm;
        private bool _applyCustomFans = false;
        private int _lastFanCpuPercent = 50;
        private int _lastFanGpuPercent = 50;
        private int _lastWrittenCpuSpeed = -1;
        private int _lastWrittenGpuSpeed = -1;
        private bool _maxFanEnabled = false;
        public bool MaxFanEnabled => _maxFanEnabled;
        private PointF[] _cpuCurve = Array.Empty<PointF>();
        private PointF[] _gpuCurve = Array.Empty<PointF>();
        private FooterQuickActionsControl _footerQuickActions = null!;

        // Advanced fan control algorithm state
        private float _filteredCpuTemp = 0f;
        private float _filteredGpuTemp = 0f;
        private int _cpuTargetPending = -1;
        private int _gpuTargetPending = -1;
        private int _cpuDelayTicks = 0;
        private int _gpuDelayTicks = 0;

        // Colors for active/inactive buttons matching G-Helper style
        private static readonly Color ColorActiveBorder = Color.FromArgb(58, 174, 239);

        #endregion

        #region Constructor & Init

        public MainForm()
        {
            ChangeWindowMessageFilter(WM_SHOWME, 1); // MSGFLT_ALLOW: bypass UAC UIPI for second instance activation

            InitializeComponent();
            InitTheme(true);
            ConfigureStartupWindow();
            BackColor = RForm.formBack;
            ForeColor = RForm.foreMain;
            Opacity = 0;
            _maxHz = 60;
            ConfigureStartupButtons();
            ConfigureStartupPanels();
            WireUpEvents();
            ConfigureStartupActions();
            ConfigureStartupInfrastructure();
            this.Shown += (s, e) =>
            {
                QueueStartupWork();
                StartDeferredStartupInitialization();
                _ = Task.Run(CheckForUpdatesAsync);

                if (!PreySense.Overlay.AppConfig.Exists("HardwareControlMode"))
                {
                    var choice = PreySense.Dialogs.ConfirmDialog.Show(
                        this,
                        "Prey Sense can communicate with your Acer notebook using WMI calls or the default Acer background services.\n\n" +
                        "Acer Services (Recommended): Maximum compatibility, relies on default Acer background services.\n" +
                        "WMI Interface (Faster): Direct WMI calls. Faster execution but may not function on all notebook models.",
                        "Hardware Control Mode",
                        "WMI Interface",
                        "Acer Services (Recommended)"
                    );

                    if (choice == DialogResult.Yes)
                    {
                        PreySense.Overlay.AppConfig.Set("HardwareControlMode", "wmi");
                    }
                    else
                    {
                        PreySense.Overlay.AppConfig.Set("HardwareControlMode", "service");
                    }
                }

                if (Environment.CommandLine.Contains("-hidden"))
                {
                    HideApp();
                }
                else
                {
                    Opacity = 1.0;
                }
            };

            _isLoaded = true;
        }

        protected override bool ShowWindowIcon => true;
        protected override bool ShowWindowInTaskbar => true;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int val = 1;
            DwmSetWindowAttribute(this.Handle, DWMWA_TRANSITIONS_FORCEDISABLED, ref val, sizeof(int));
        }
        // Removed StartFade and FadeTimer_Tick to prevent focus loss issues with transparent windows
        private void ConfigureFooterButtons()
        {
            _footerQuickActions = new FooterQuickActionsControl
            {
                Name = "footerQuickActions",
                Dock = DockStyle.Fill,
                Margin = Padding.Empty
            };
            _footerQuickActions.KeyboardIcon = GetThemeIcon(ResizeImageToSize(Properties.Resources.icons8_keyboard_32, 18, 18));
            _footerQuickActions.MetricsIcon = GetThemeIcon(ResizeImageToSize(Properties.Resources.icons8_soonvibes_32, 18, 18));
            _footerQuickActions.ApplyTheme(buttonSecond, foreMain, borderSecond);
            _footerQuickActions.KeyboardClicked += (_, _) => OpenRgbForm();
            _footerQuickActions.MetricsClicked += (_, _) => OpenMetricsOverlay();

            tableButtons.SuspendLayout();
            tableButtons.Controls.Clear();
            tableButtons.ColumnCount = 3;
            tableButtons.ColumnStyles.Clear();
            tableButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33333F));
            tableButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33333F));
            tableButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33333F));
            tableButtons.Controls.Add(_footerQuickActions, 0, 0);
            tableButtons.SetColumnSpan(_footerQuickActions, 2);

            buttonQuit.Text = "Quit";
            buttonQuit.Image = GetThemeIcon(ResizeImageToSize(Properties.Resources.icons8_quit_32, 18, 18));
            buttonQuit.ImageAlign = ContentAlignment.MiddleLeft;
            buttonQuit.TextAlign = ContentAlignment.MiddleCenter;
            buttonQuit.TextImageRelation = TextImageRelation.ImageBeforeText;
            buttonQuit.Padding = new Padding(10, 0, 10, 0);
            buttonQuit.Margin = new Padding(4, 2, 4, 2);
            buttonQuit.Activated = false;
            buttonQuit.Secondary = true;
            buttonQuit.BorderRadius = 2;
            buttonQuit.BorderColor = Color.Transparent;
            buttonQuit.Dock = DockStyle.Fill;
            buttonQuit.FlatStyle = FlatStyle.Flat;
            buttonQuit.FlatAppearance.BorderColor = borderSecond;
            buttonQuit.BackColor = buttonSecond;
            buttonQuit.ForeColor = foreMain;
            buttonQuit.UseVisualStyleBackColor = false;
            buttonQuit.Click += (s, e) => QuitApplication();
            tableButtons.Controls.Add(buttonQuit, 2, 0);
            tableButtons.ResumeLayout();
        }

        private const uint WM_DISPLAYCHANGE = 0x007E;

        protected override void WndProc(ref Message m)
        {
            if (WM_SHOWME != 0 && (uint)m.Msg == WM_SHOWME) ShowApp();
            base.WndProc(ref m);

            if ((uint)m.Msg == WM_DISPLAYCHANGE)
            {
                // Give the display driver and OS a moment to finish resetting the LUT
                System.Threading.Thread.Sleep(250);
                int hz = GetCurrentRefreshRate();
                _currentRefreshRate = hz;
                ApplyGammaForRefreshRate(hz, force: true);
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Alt | Keys.F4))
            {
                QuitApplication();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            SetBatteryTelemetryVisible(Visible && WindowState != FormWindowState.Minimized);
        }

        #endregion

        #region Event Wiring

        private void WireUpEvents()
        {
            // Performance modes
            buttonEcoMode.Click += (s, e) => ApplyPowerMode(SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline ? (byte)0x06 : (byte)0x00);
            buttonBalancedMode.Click += (s, e) => ApplyPowerMode(0x01);
            buttonPerformanceMode.Click += (s, e) => ApplyPowerMode(0x04);
            buttonTurboFanMode.Click += (s, e) => ApplyPowerMode(0x05);

            // GPU modes
            buttonEnduranceMode.Click += (s, e) => ApplyGpuMode(0);
            buttonGpuStandardMode.Click += (s, e) => ApplyGpuMode(1);
            buttonGpuUltimateMode.Click += (s, e) => ApplyGpuMode(2);

            // Screen refresh
            buttonAutoRefreshRate.Click += (s, e) => ToggleAutoRefreshRate();
            button60Hz.Click += (s, e) => { 
                if (button60Hz.Tag is int rate) {
                    if (!buttonAutoRefreshRate.Activated && GetCurrentRefreshRate() == rate) return;
                    buttonAutoRefreshRate.Activated = false; 
                    SaveState("ScreenAuto", 0); 
                    ApplyDisplayMode(rate); 
                    UpdateScreenCardTitle(); 
                }
            };
            button120Hz.Click += (s, e) => { 
                if (button120Hz.Tag is int rate) {
                    if (!buttonAutoRefreshRate.Activated && GetCurrentRefreshRate() == rate) return;
                    buttonAutoRefreshRate.Activated = false; 
                    SaveState("ScreenAuto", 0); 
                    ApplyDisplayMode(rate); 
                    UpdateScreenCardTitle(); 
                }
            };
            buttonMaxRefreshRate.Click += (s, e) => { 
                if (buttonMaxRefreshRate.Tag is int rate) {
                    if (!buttonAutoRefreshRate.Activated && GetCurrentRefreshRate() == rate) return;
                    buttonAutoRefreshRate.Activated = false; 
                    SaveState("ScreenAuto", 0); 
                    ApplyDisplayMode(rate); 
                    UpdateScreenCardTitle(); 
                }
            };

            // Keyboard lighting
            comboRgbLightingMode.Items.Clear();
            foreach (var name in RgbModeNames) comboRgbLightingMode.Items.Add(name);
            comboRgbLightingMode.SelectedIndex = 0;
            comboRgbLightingMode.SelectedIndexChanged += (s, e) => ApplyRgbModeFromDropdown(comboRgbLightingMode.SelectedIndex);

            buttonRgbProfiles.Click += (s, e) => OpenRgbProfilesForm();
            pictureBacklightSwatch.Click += (s, e) => PickBacklightColor();
            buttonRgbLighting.Click += (s, e) => OpenRgbProfilesForm();

            // Battery Limit Slider
            sliderBatteryChargeLimit.Min = 40;
            sliderBatteryChargeLimit.Max = 100;
            sliderBatteryChargeLimit.Step = 5;
            sliderBatteryChargeLimit.ShowLabels = false;
            sliderBatteryChargeLimit.Value = 100;
            sliderBatteryChargeLimit.ValueChanged += (s, e) => {
                int val = sliderBatteryChargeLimit.Value >= 90 ? 100 : 80;
                if (sliderBatteryChargeLimit.Value != val)
                {
                    sliderBatteryChargeLimit.Value = val;
                    return;
                }
                labelBatteryStatusLimitTitle.Text = $"Battery Charge Limit: {sliderBatteryChargeLimit.Value}%";
                UpdateBatteryLimitButtonFromValue(sliderBatteryChargeLimit.Value);
                if (_isLoaded && !_isApplyingSavedBatteryLimit)
                {
                    _pendingBatteryMode = (val == 80) ? 1 : 0;
                }
            };
            sliderBatteryChargeLimit.Slider.ValueCommitted += (s, e) =>
            {
                if (_isLoaded && !_isApplyingSavedBatteryLimit && _pendingBatteryMode != -1)
                {
                    ApplyBatteryMode(_pendingBatteryMode);
                    _pendingBatteryMode = -1;
                }
            };
            labelBatteryStatusLimitTitle.Text = $"Battery Charge Limit: {sliderBatteryChargeLimit.Value}%"; // Show value at launch
            UpdateBatteryLimitButtonFromValue(sliderBatteryChargeLimit.Value);
            buttonBatteryFull.Click += (s, e) => {
                int nextMode = sliderBatteryChargeLimit.Value == 80 ? 0 : 1;
                _isApplyingSavedBatteryLimit = true;
                try
                {
                    sliderBatteryChargeLimit.Value = nextMode == 1 ? 80 : 100;
                }
                finally
                {
                    _isApplyingSavedBatteryLimit = false;
                }
                _pendingBatteryMode = nextMode;
                ApplyBatteryMode(_pendingBatteryMode);
                _pendingBatteryMode = -1;
            };

            buttonColorProfiles.Click += (s, e) => OpenColorForm();
            buttonTurboFanModePower.Click += (s, e) => OpenFansForm();

            ConfigureFooterButtons();
        }

        #endregion

        #region Telemetry

        private void UpdateTelemetry(object? sender, EventArgs e)
        {
            if (_isTelemetryUpdating) return;
            _isTelemetryUpdating = true;

            bool isFormVisible = Visible && WindowState != FormWindowState.Minimized;
            bool showBatteryTelemetry = _showBatteryTelemetry;

            if (!isFormVisible && !_applyCustomFans && !_maxFanEnabled)
            {
                CheckPowerSourceTransition(IsOnBatteryPower());
                _isTelemetryUpdating = false;
                return;
            }

            Task.Run(() =>
            {
                try
                {
                    _wmi.RefreshAcerService();
                    int cpuTemp = _wmi.CpuTemp;
                    int gpuTemp = _wmi.GpuTemp;
                    int cpuRpm = _wmi.CpuFanRpm;
                    int gpuRpm = _wmi.GpuFanRpm;
                    bool onBattery = IsOnBatteryPower();

                    _cpuTemp = cpuTemp;
                    _gpuTemp = gpuTemp;
                    _cpuRpm = cpuRpm;
                    _gpuRpm = gpuRpm;

                    ApplyActiveFanControl();

                    int currentHz = _currentRefreshRate;
                    if (isFormVisible)
                    {
                        currentHz = GetCurrentRefreshRate();
                    }

                    BeginInvoke(new Action(() =>
                    {
                        if (IsDisposed) return;

                        if (isFormVisible)
                        {
                            labelCpuFanStatus.Text = _cpuTemp > 0 ? $"CPU: {_cpuTemp}°C  {_cpuRpm} RPM" : $"CPU: --°C  {_cpuRpm} RPM";
                            labelGpuModeFan.Text = _gpuTemp > 0 ? $"GPU: {_gpuTemp}°C  {_gpuRpm} RPM" : $"GPU: 0°C  {_gpuRpm} RPM";

                            _currentRefreshRate = currentHz;

                            if (showBatteryTelemetry)
                                RefreshBatteryRateLabel();

                            if (_lastRefreshRate != currentHz)
                            {
                                _lastRefreshRate = currentHz;
                                ApplyGammaForRefreshRate(currentHz);
                                SyncRefreshRateButtons(currentHz, buttonAutoRefreshRate.Activated);
                            }
                        }

                        CheckPowerSourceTransition(onBattery);
                    }));
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"Telemetry background update failed: {ex.Message}");
                }
                finally
                {
                    _isTelemetryUpdating = false;
                }
            });
        }

        public void ToggleCustomFans(bool enable)
        {
            _applyCustomFans = enable;
            buttonTurboFanModePower.Activated = enable;
            buttonTurboFanModePower.BorderColor = enable ? RForm.colorCustom : borderSecond;
            SaveState("Fan_CurveEnabled", enable ? 1 : 0);
            _lastWrittenCpuSpeed = -1;
            _lastWrittenGpuSpeed = -1;
            if (enable)
            {
                _filteredCpuTemp = 0f;
                _filteredGpuTemp = 0f;
                _cpuTargetPending = -1;
                _gpuTargetPending = -1;
                _cpuDelayTicks = 0;
                _gpuDelayTicks = 0;

                byte mode = _lastKnownProfile != 0 ? _lastKnownProfile : _wmi.GetPowerProfile();
                _cpuCurve = FanCurveStorage.LoadCpuCurve(mode);
                _gpuCurve = FanCurveStorage.LoadGpuCurve(mode);
                ApplyActiveFanControl();
            }
            else if (!_maxFanEnabled)
            {
                RestoreAutoFanControl();
            }
        }

        /// <summary>
        /// Returns fans to EC auto mode. Passes the last commanded % so the service
        /// packet matches Predator Sense (fan_custom_auto=1) and avoids a full stall.
        /// Never hand off at 0% — the probe log shows auto+spd=0 drops fans to 0 RPM
        /// for several seconds while the EC reloads its auto table.
        /// </summary>
        private void RestoreAutoFanControl()
        {
            _wmi.RefreshAcerService();

            Task.Run(async () =>
            {
                _wmi.SetFanControl(0);
            });
        }

        private void ApplyActiveFanControl()
        {
            _wmi.RefreshAcerService();

            if (_maxFanEnabled)
            {
                Task.Run(() => _wmi.SetFanControl(1));
                return;
            }

            if (!_applyCustomFans || _cpuCurve.Length == 0 || _gpuCurve.Length == 0)
                return;

            float cpuTemp = _cpuTemp > 0 ? _cpuTemp : 40f;
            float gpuTemp = _gpuTemp > 0 ? _gpuTemp : 40f;

            // Exponential Moving Average temperature smoothing (alpha = 0.25)
            const float alpha = 0.25f;
            bool isCpuFirst = _filteredCpuTemp == 0f;
            bool isGpuFirst = _filteredGpuTemp == 0f;
            _filteredCpuTemp = isCpuFirst ? cpuTemp : (_filteredCpuTemp * (1f - alpha) + cpuTemp * alpha);
            _filteredGpuTemp = isGpuFirst ? gpuTemp : (_filteredGpuTemp * (1f - alpha) + gpuTemp * alpha);

            int targetCpu = GetSpeedFromCurve(_cpuCurve, _filteredCpuTemp);
            int targetGpu = GetSpeedFromCurve(_gpuCurve, _filteredGpuTemp);

            if (isCpuFirst) _lastFanCpuPercent = targetCpu;
            if (isGpuFirst) _lastFanGpuPercent = targetGpu;

            // 2% deadband/hysteresis to avoid continuous minor adjustments
            if (Math.Abs(targetCpu - _lastFanCpuPercent) < 2)
            {
                targetCpu = _lastFanCpuPercent;
                _cpuTargetPending = -1;
                _cpuDelayTicks = 0;
            }
            if (Math.Abs(targetGpu - _lastFanGpuPercent) < 2)
            {
                targetGpu = _lastFanGpuPercent;
                _gpuTargetPending = -1;
                _gpuDelayTicks = 0;
            }

            int T = _fanRampUp; // Ramp up time in seconds (from UI)
            int rampUpTicks = Math.Max(1, T);          // Minimum 1 second (1 tick) delay
            int rampDownTicks = Math.Max(5, T * 4);    // Minimum 5 seconds (5 ticks) delay

            // CPU Fan Ramp Logic
            if (targetCpu > _lastFanCpuPercent)
            {
                if (_cpuTargetPending <= _lastFanCpuPercent)
                {
                    _cpuTargetPending = targetCpu;
                    _cpuDelayTicks = 0;
                }
                _cpuDelayTicks++;
                if (_cpuDelayTicks >= rampUpTicks)
                {
                    _lastFanCpuPercent = targetCpu;
                    _cpuTargetPending = -1;
                    _cpuDelayTicks = 0;
                }
            }
            else if (targetCpu < _lastFanCpuPercent)
            {
                if (_cpuTargetPending >= _lastFanCpuPercent)
                {
                    _cpuTargetPending = targetCpu;
                    _cpuDelayTicks = 0;
                }
                _cpuDelayTicks++;
                if (_cpuDelayTicks >= rampDownTicks)
                {
                    _lastFanCpuPercent = targetCpu;
                    _cpuTargetPending = -1;
                    _cpuDelayTicks = 0;
                }
            }

            // GPU Fan Ramp Logic
            if (targetGpu > _lastFanGpuPercent)
            {
                if (_gpuTargetPending <= _lastFanGpuPercent)
                {
                    _gpuTargetPending = targetGpu;
                    _gpuDelayTicks = 0;
                }
                _gpuDelayTicks++;
                if (_gpuDelayTicks >= rampUpTicks)
                {
                    _lastFanGpuPercent = targetGpu;
                    _gpuTargetPending = -1;
                    _gpuDelayTicks = 0;
                }
            }
            else if (targetGpu < _lastFanGpuPercent)
            {
                if (_gpuTargetPending >= _lastFanGpuPercent)
                {
                    _gpuTargetPending = targetGpu;
                    _gpuDelayTicks = 0;
                }
                _gpuDelayTicks++;
                if (_gpuDelayTicks >= rampDownTicks)
                {
                    _lastFanGpuPercent = targetGpu;
                    _gpuTargetPending = -1;
                    _gpuDelayTicks = 0;
                }
            }

            // Write to WMI in background thread to avoid stutters only if speeds have changed
            int cpuSpeed = _lastFanCpuPercent;
            int gpuSpeed = _lastFanGpuPercent;
            if (cpuSpeed != _lastWrittenCpuSpeed || gpuSpeed != _lastWrittenGpuSpeed)
            {
                _lastWrittenCpuSpeed = cpuSpeed;
                _lastWrittenGpuSpeed = gpuSpeed;
                Task.Run(() => _wmi.SetFanControl(2, cpuSpeed, gpuSpeed));
            }
        }

        /// <summary>
        /// Loads the fan curves for a performance mode into memory. When that mode
        /// is active, enables or disables custom fan control based on its profile.
        /// </summary>
        public void ApplyFanCurvesForMode(byte mode)
        {
            var profile = ProfileManager.LoadProfile(mode);
            _cpuCurve = FanCurveStorage.LoadCpuCurve(mode);
            _gpuCurve = FanCurveStorage.LoadGpuCurve(mode);

            // Reset EMA filters and ramp timers on mode/profile change
            _filteredCpuTemp = 0f;
            _filteredGpuTemp = 0f;
            _cpuTargetPending = -1;
            _gpuTargetPending = -1;
            _cpuDelayTicks = 0;
            _gpuDelayTicks = 0;
            _lastWrittenCpuSpeed = -1;
            _lastWrittenGpuSpeed = -1;

            if (_maxFanEnabled)
            {
                if (mode == 0x00 || mode == 0x06)
                {
                    ToggleMaxFans(false);
                }
                else
                {
                    return;
                }
            }

            bool forceDisableFans = (mode == 0x00 || mode == 0x06);

            if (profile.ApplyFanCurve && !forceDisableFans)
            {
                _applyCustomFans = true;
                buttonTurboFanModePower.Activated = true;
                buttonTurboFanModePower.BorderColor = RForm.colorCustom;
                SaveState("Fan_CurveEnabled", 1);
                _fanRampUp = profile.FanRampUp;
                ApplyActiveFanControl();
            }
            else if (_applyCustomFans || forceDisableFans)
            {
                _applyCustomFans = false;
                buttonTurboFanModePower.Activated = false;
                buttonTurboFanModePower.BorderColor = borderSecond;
                SaveState("Fan_CurveEnabled", 0);
                _fanRampUp = 1;
                RestoreAutoFanControl();
            }
        }

        /// <summary>
        /// Updates the in-memory curves used by telemetry for the active mode.
        /// </summary>
        public void SetActiveFanCurves(PointF[] cpu, PointF[] gpu)
        {
            _cpuCurve = cpu;
            _gpuCurve = gpu;
            if (_applyCustomFans && !_maxFanEnabled)
                ApplyActiveFanControl();
        }

        public void ToggleMaxFans(bool enable)
        {
            _maxFanEnabled = enable;
            SaveState("Fan_MaxSpeed", enable ? 1 : 0);
            if (enable)
            {
                _wmi.SetFanControl(1); // 1 = Max
            }
            else if (_applyCustomFans)
            {
                ToggleCustomFans(true);
            }
            else
            {
                RestoreAutoFanControl();
            }
        }

        private PointF[] LoadCurveFromRegistry(string name)
        {
            byte mode = _lastKnownProfile != 0 ? _lastKnownProfile : _wmi.GetPowerProfile();
            return name == "GpuCurve"
                ? FanCurveStorage.LoadGpuCurve(mode)
                : FanCurveStorage.LoadCpuCurve(mode);
        }

        private static int GetSpeedFromCurve(PointF[] curve, float currentTemp)
        {
            if (curve == null || curve.Length == 0) return 10;
            var sorted = curve.OrderBy(p => p.X).ToArray();
            
            float speed;
            if (currentTemp <= sorted[0].X)
                speed = sorted[0].Y;
            else if (currentTemp >= sorted[sorted.Length - 1].X)
                speed = sorted[sorted.Length - 1].Y;
            else
            {
                int i = 0;
                for (; i < sorted.Length - 1; i++)
                {
                    if (currentTemp >= sorted[i].X && currentTemp <= sorted[i + 1].X)
                        break;
                }
                float t = (currentTemp - sorted[i].X) / (sorted[i + 1].X - sorted[i].X);
                speed = sorted[i].Y + t * (sorted[i + 1].Y - sorted[i].Y);
            }
            return Math.Clamp((int)Math.Round(speed), 10, 100);
        }

        #endregion

        #region App Visibility Controls

        public void ToggleAppVisibility()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(ToggleAppVisibility));
                return;
            }

            IntPtr fgWindow = GetForegroundWindow();
            GetWindowThreadProcessId(fgWindow, out uint fgProcessId);
            bool isOurProcessActive = (fgProcessId == (uint)Environment.ProcessId);

            bool anySubFormVisible = Application.OpenForms.Cast<Form>().Any(f => f != this && !f.IsDisposed && f.Visible);

            if ((this.Visible || anySubFormVisible) && isOurProcessActive)
            {
                HideApp();
            }
            else
            {
                ShowApp();
            }
        }

        public void ShowApp()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(ShowApp));
                return;
            }

            Opacity = 1.0;

            // Force a minimize/restore state transition. This is a reliable Windows Forms trick
            // that bypasses OS-level foreground activation restrictions, ensuring the window
            // is brought to the front and receives keyboard focus without taskbar flashing.
            this.WindowState = FormWindowState.Minimized;
            this.Show();
            this.WindowState = FormWindowState.Normal;

            this.TopMost = true;
            this.Activate();
            this.TopMost = false;
        }

        public void HideApp()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(HideApp));
                return;
            }

            // Close all open sub-windows (Fans, RgbForm, ColorForm, etc.)
            // so Predator key hides every PreySense window at once.
            foreach (Form sub in Application.OpenForms.Cast<Form>().Where(f => f != this).ToList())
            {
                if (!sub.IsDisposed && sub.Visible)
                    sub.Close();
            }

            this.Hide();
            Overlay.ProcessMemoryHelper.TrimAfter();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);

            // Hide the app only if the active window of our application becomes null (user clicked away)
            BeginInvoke(new Action(() =>
            {
                if (Form.ActiveForm == null)
                {
                    HideApp();
                }
            }));
        }

        private void QuitApplication()
        {
            _allowExit = true;
            if (_trayIcon != null)
                _trayIcon.Visible = false;
            Application.Exit();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_allowExit && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideApp();
                return;
            }

            // On Windows shutdown/restart, exit immediately so we don't block the shutdown sequence.
            if (e.CloseReason == CloseReason.WindowsShutDown || e.CloseReason == CloseReason.TaskManagerClosing)
            {
                _trayIcon?.Dispose();
                Environment.Exit(0);
                return;
            }

            SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
            _timer.Stop();
            Program.CancelHardwareOverlayUnload();
            Program.hardwareOverlay?.StopOverlay();
            Program.hardwareOverlay?.Dispose();
            Program.hardwareOverlay = null;
            _keyboardHook?.Dispose();
            _wmiHotkeyWatcher?.Dispose();
            _quickAccessModeWatcher?.Dispose();
            _modeToast?.Dispose();
            _trayIcon?.Dispose();
            _trayMenu?.Dispose();
            _wmiInstance?.Dispose();
            base.OnFormClosing(e);
        }

        #endregion

        #region Stubs to satisfy old hooks

        public void ApplyStickyKeys(bool enable) {}
        public void ToggleLedTimeout() {}
        public void PickZoneColor(int zone) {}
        public void SyncAllZones() {}
        public void SetRgbStaticMode(int mode) {}
        public void SyncRgbControls(int mode) {}
        public void CyclePowerProfile(bool ignoreLockout = false)
        {
            byte currentMode = IsKnownPowerMode(_lastKnownProfile) ? _lastKnownProfile : (byte)0x01;
            bool onBattery = IsOnBatteryPower();
            byte nextMode;
            if (onBattery)
            {
                nextMode = currentMode switch
                {
                    0x06 => 0x01, // Eco -> Balanced
                    0x01 => 0x06, // Balanced -> Eco
                    _ => 0x06
                };
            }
            else
            {
                nextMode = currentMode switch
                {
                    0x00 => 0x01, // Silent -> Balanced
                    0x01 => 0x04, // Balanced -> Performance
                    0x04 => 0x05, // Performance -> Turbo
                    0x05 => 0x00, // Turbo -> Silent
                    0x06 => 0x01, // Eco (saved state) -> Balanced when back on AC
                    _ => 0x01
                };
            }
            AppLogger.Log($"CyclePowerProfile: {currentMode:X2} -> {nextMode:X2} (onBattery={onBattery})");
            ApplyPowerMode(nextMode);
        }

        /// <summary>
        /// Mode / Turbo key handler. Startup seeds UI from hardware; after that,
        /// the physical key advances PreySense's local cycle and applies it.
        /// </summary>
        public void HandleModeKeyPressed()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(HandleModeKeyPressed));
                return;
            }

            if ((DateTime.Now - _lastDirectModeShortcutTime).TotalMilliseconds < DirectShortcutEchoSuppressMs)
            {
                return;
            }

            AppLogger.Log("HandleModeKeyPressed: cycling local power profile.");
            CyclePowerProfile(ignoreLockout: true);
        }

        private void LoadCurrentHardwarePowerMode()
        {
            byte detectedMode = 0x01; // Default to Balanced

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\PreySense");
                if (key != null)
                {
                    bool onBattery = IsOnBatteryPower();
                    string regKey = onBattery ? "PowerBattery" : "PowerAC";
                    int savedMode = GetRegistryInt(key, regKey, -1);
                    if (savedMode == -1)
                    {
                        savedMode = GetRegistryInt(key, "Power", -1);
                    }

                    if (savedMode != -1 && IsKnownPowerMode((byte)savedMode))
                    {
                        detectedMode = (byte)savedMode;
                        AppLogger.Log($"LoadCurrentHardwarePowerMode: loaded startup mode 0x{detectedMode:X2} from registry.");
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"LoadCurrentHardwarePowerMode: failed to read registry: {ex.Message}");
            }

            // Apply the loaded power mode to both the hardware and application state
            ApplyPowerMode(detectedMode, persistHardware: true);
        }

        private static bool IsKnownPowerMode(byte mode)
        {
            return mode is 0x00 or 0x01 or 0x04 or 0x05 or 0x06;
        }

        /// <summary>
        /// Predator key + number shortcut: 1=Eco, 2=Silent, 3=Balanced, 4=Performance, 5=Turbo.
        /// </summary>
        public void HandleModeNumberKey(int number)
        {
            if ((DateTime.Now - _lastDirectModeShortcutTime).TotalMilliseconds < ModeToastDebounceMs)
            {
                return;
            }

            byte mode = number switch
            {
                1 => 0x06, // Eco
                2 => 0x00, // Silent
                3 => 0x01, // Balanced
                4 => 0x04, // Performance
                5 => 0x05, // Turbo
                _ => 0xFF
            };
            if (mode == 0xFF) return;

            _lastDirectModeShortcutTime = DateTime.Now;

            void Apply() => ApplyPowerMode(mode);
            if (InvokeRequired)
                BeginInvoke(Apply);
            else
                Apply();
        }

        private bool IsOnBatteryPower()
        {
            var line = SystemInformation.PowerStatus.PowerLineStatus;
            if (line == PowerLineStatus.Online) return false;
            if (line == PowerLineStatus.Offline) return true;

            if (_wmi.TryGetAcConnected(out bool onAc))
                return !onAc;

            return false; // Unknown — treat as AC so full profile cycle works
        }

        private void UpdatePowerUIForBatteryState(bool onBattery)
        {
            UpdatePowerButtonAccentForPowerSource(onBattery);
            if (onBattery)
            {
                if (buttonEcoMode.Text != "Eco")
                {
                    buttonEcoMode.Text = "Eco";
                }
                buttonPerformanceMode.Enabled = true;
                buttonTurboFanMode.Enabled = true;
            }
            else
            {
                if (buttonEcoMode.Text != "Silent")
                {
                    buttonEcoMode.Text = "Silent";
                }
                buttonPerformanceMode.Enabled = true;
                buttonTurboFanMode.Enabled = true;
            }
        }

        private void OpenColorForm()
        {
            using var f = new ColorForm(this, _currentRefreshRate);
            f.ShowDialog(this);
        }

        private void OnHotkeyEvent(int detail)
        {
            AppLogger.Log($"OnHotkeyEvent: EventDetail={detail}");
            if (detail == 5)
            {
                if ((DateTime.Now - _lastDirectModeShortcutTime).TotalMilliseconds < DirectShortcutEchoSuppressMs)
                {
                    return;
                }

                HandleModeKeyPressed();
            }
        }

        #endregion

        private void ApplyAppIcon()
        {
            try
            {
                Icon? icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon != null)
                {
                    Icon = icon;
                    ShowIcon = true;
                    return;
                }

                string iconPath = Path.Combine(AppContext.BaseDirectory, "appicon.ico");
                if (File.Exists(iconPath))
                {
                    Icon = new Icon(iconPath);
                    ShowIcon = true;
                }
            }
            catch
            {
                ShowIcon = true;
            }
        }

        private Icon LoadTrayIcon()
        {
            try
            {
                string iconPath = Path.Combine(AppContext.BaseDirectory, "appicon.ico");
                if (File.Exists(iconPath))
                    return new Icon(iconPath);
            }
            catch { }

            return Icon.ExtractAssociatedIcon(Application.ExecutablePath)
                ?? (Icon)SystemIcons.Application.Clone();
        }

        private Image LoadTrayMenuImage()
        {
            using Icon icon = LoadTrayIcon();
            return icon.ToBitmap();
        }

        private void ConfigureTrayIcon()
        {
            _trayMenu?.Dispose();
            _trayIcon?.Dispose();

            _trayMenu = new ContextMenuStrip();
            var openItem = new ToolStripMenuItem("Prey Sense", LoadTrayMenuImage(), (_, _) => ShowApp())
            {
                ImageScaling = ToolStripItemImageScaling.SizeToFit
            };
            _trayMenu.Items.Add(openItem);
            _trayMenu.Items.Add("Quit", null, (_, _) => QuitApplication());

            _trayIcon = new NotifyIcon
            {
                Icon = LoadTrayIcon(),
                Text = "Prey Sense",
                ContextMenuStrip = _trayMenu,
                Visible = true
            };
            _trayIcon.DoubleClick += (_, _) => ShowApp();
        }

        private Image GetThemeIcon(Image icon)
        {
            Image adjusted = icon;
            if (this.darkTheme)
            {
                adjusted = PreySense.UI.ControlHelper.AdjustImage(icon);
            }
            return adjusted;
        }

        private static Image ResizeImageToSize(Image image, int width, int height)
        {
            if (image is null)
            {
                return new Bitmap(width, height);
            }

            var destRect = new Rectangle(0, 0, width, height);
            var destImage = new Bitmap(width, height);

            if (image.HorizontalResolution > 0 && image.VerticalResolution > 0)
            {
                destImage.SetResolution(image.HorizontalResolution, image.VerticalResolution);
            }

            using (var graphics = Graphics.FromImage(destImage))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                using (var wrapMode = new ImageAttributes())
                {
                    wrapMode.SetWrapMode(WrapMode.TileFlipXY);
                    graphics.DrawImage(image, destRect, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, wrapMode);
                }
            }

            return destImage;
        }
    }
}


