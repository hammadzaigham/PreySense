using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using PreySense.Input;

namespace PreySense
{
    public partial class MainForm
    {
        private void ConfigureStartupWindow()
        {
            ApplyAppIcon();
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MinimizeBox = false;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Prey Sense";
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
        }

        private void ConfigureStartupButtons()
        {
            buttonTurboFanMode.Secondary = false;
            buttonTurboFanModePower.Secondary = false;

            buttonEcoMode.Text = "Silent";
            buttonBalancedMode.Text = "Balanced";
            buttonPerformanceMode.Text = "Performance";
            buttonTurboFanMode.Text = "Turbo";

            buttonEnduranceMode.Text = "Endurance";
            buttonGpuStandardMode.Text = "Standard";
            buttonGpuUltimateMode.Text = "Ultimate";
            buttonTurboFanModePower.Text = "Fans + Power";

            buttonEcoMode.BorderColor = colorEco;
            buttonBalancedMode.BorderColor = colorStandard;
            buttonPerformanceMode.BorderColor = PerformanceModeColor;
            buttonTurboFanMode.BorderColor = TurboModeColor;
            buttonEnduranceMode.BorderColor = colorEco;
            buttonGpuStandardMode.BorderColor = colorStandard;
            buttonGpuUltimateMode.BorderColor = colorTurbo;
            buttonTurboFanModePower.BorderColor = colorCustom;
            buttonTurboFanModePower.BackColor = buttonSecond;
            buttonTurboFanModePower.ForeColor = foreMain;
            button60Hz.BorderColor = AccentColor;
            button120Hz.BorderColor = AccentColor;
            buttonMaxRefreshRate.BorderColor = AccentColor;
            buttonAutoRefreshRate.BorderColor = colorGray;
            buttonRgbLighting.BorderColor = colorGray;

            buttonColorProfiles.Text = "Color";
            buttonColorProfiles.TabStop = false;
            buttonColorProfiles.Borderless = false;
            buttonColorProfiles.Secondary = true;
            buttonColorProfiles.BackColor = buttonSecond;
            buttonColorProfiles.ForeColor = foreMain;
            buttonColorProfiles.FlatAppearance.BorderColor = borderSecond;
            buttonColorProfiles.BorderColor = colorGray;
            buttonColorProfiles.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
            buttonColorProfiles.TextAlign = ContentAlignment.MiddleCenter;
            buttonColorProfiles.Width = 80;
            buttonColorProfiles.Margin = new Padding(0, 4, 0, 4);

            button120Hz.Text = "120Hz + OD";
            buttonBatteryFull.Text = "100%";
            labelBatteryStatus.Visible = true;
        }

        private void ConfigureStartupPanels()
        {
            panelMatrix.Visible = false;
            panelScreen.Visible = true;
            panelRgb.Visible = false;
            panelBattery.Visible = true;
            panelStartup.Visible = true;
            MatchSectionTitleColors();
        }

        private void ConfigureStartupActions()
        {
            checkRunOnStartup.Checked = IsRunOnStartupEnabled();
            checkRunOnStartup.CheckedChanged += (_, _) => SetRunOnStartup(checkRunOnStartup.Checked);

            checkAutoGpuBattery.Checked = IsGpuBatteryAutoEnabled();
            checkAutoGpuBattery.CheckedChanged += (_, _) => SetGpuBatteryAuto(checkAutoGpuBattery.Checked);
        }

        private void ConfigureStartupInfrastructure()
        {
            ConfigureTrayIcon();

            _timer.Interval = 1000;
            _timer.Tick += UpdateTelemetry;
            _timer.Start();

            SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
            _keyboardHook = new KeyboardHook(ToggleAppVisibility, null, HandleModeNumberKey, OpenMetricsOverlay);
            _quickAccessModeWatcher = new QuickAccessModeWatcher(HandleModeKeyPressed);
        }

        private void QueueStartupWork()
        {
            ClosePredatorSenseUiProcesses();

            if (Environment.CommandLine.Contains("-hidden"))
            {
                HideApp();
            }

            CheckCalibration();
            ApplySavedBatteryLimitAsync();

            _ = Task.Run(() =>
            {
                _wmi.RefreshAcerService();
                BeginInvoke(ApplySavedRgbLighting);
            });
        }

        private static void ClosePredatorSenseUiProcesses()
        {
            string[] processNames =
            [
                "PredatorSense",
                "PredatorSenseApp",
                "PredatorSenseLauncher",
                "PSLauncher"
            ];

            int currentProcessId = Environment.ProcessId;
            foreach (string processName in processNames)
            {
                foreach (Process process in Process.GetProcessesByName(processName))
                {
                    using (process)
                    {
                        try
                        {
                            if (process.Id == currentProcessId)
                                continue;

                            AppLogger.Log($"Startup: closing conflicting Predator Sense process '{process.ProcessName}' (PID {process.Id}).");
                            if (!process.CloseMainWindow() || !process.WaitForExit(1500))
                            {
                                process.Kill(entireProcessTree: true);
                            }
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Log($"Startup: failed to close Predator Sense process '{processName}': {ex.Message}");
                        }
                    }
                }
            }
        }

        private void StartDeferredStartupInitialization()
        {
            _ = Task.Run(() =>
            {
                int maxHz = GetMaxRefreshRate();
                int currentHz = GetCurrentRefreshRate();

                BeginInvoke(new Action(() =>
                {
                    if (IsDisposed) return;

                    _maxHz = maxHz;
                    _currentRefreshRate = currentHz;
                    button120Hz.Text = $"{_maxHz}Hz + OD";
                    UpdatePowerUIForBatteryState(SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline);
                    ConfigureRefreshRateButtons();
                    LoadMemory();
                    SyncRefreshRateButtons(currentHz, buttonAutoRefreshRate.Activated);
                    UpdateScreenCardTitle();
                    ApplyGammaForRefreshRate(currentHz);
                    _lastRefreshRate = currentHz;
                    LoadCurrentHardwarePowerMode();
                }));
            });
        }
    }
}
