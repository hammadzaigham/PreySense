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
        private static readonly Color SilentModeOutlineColor = Color.FromArgb(210, 210, 210);

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

            buttonEcoMode.BorderColor = SilentModeOutlineColor;
            buttonBalancedMode.BorderColor = colorStandard;
            buttonPerformanceMode.BorderColor = PerformanceModeColor;
            buttonTurboFanMode.BorderColor = TurboModeColor;
            buttonEnduranceMode.BorderColor = colorEco;
            buttonGpuStandardMode.BorderColor = colorStandard;
            buttonGpuUltimateMode.BorderColor = colorTurbo;
            buttonTurboFanModePower.BorderColor = colorCustom;
            button60Hz.BorderColor = AccentColor;
            button120Hz.BorderColor = AccentColor;
            buttonMaxRefreshRate.BorderColor = AccentColor;
            buttonAutoRefreshRate.BorderColor = colorGray;
            buttonRgbLighting.BorderColor = colorGray;

            buttonColorProfiles.Text = "Display";
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
            UpdatePowerButtonAccentForPowerSource(SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline);
        }

        private void ConfigureStartupPanels()
        {
            panelScreen.Visible = true;
            panelRgb.Visible = false;
            panelBattery.Visible = true;
            panelStartup.Visible = true;
            MatchSectionTitleColors();
        }

        private void UpdatePowerButtonAccentForPowerSource(bool onBattery)
        {
            buttonEcoMode.BorderColor = onBattery ? colorEco : SilentModeOutlineColor;
        }

        private void ConfigureStartupActions()
        {
            checkRunOnStartup.Checked = IsRunOnStartupEnabled();
            checkRunOnStartup.ForeColor = foreMain;
            checkRunOnStartup.UseVisualStyleBackColor = false;
            checkRunOnStartup.CheckedChanged += (_, _) => SetRunOnStartup(checkRunOnStartup.Checked);

            checkAutoGpuBattery.Checked = IsGpuBatteryAutoEnabled();
            checkAutoGpuBattery.ForeColor = foreMain;
            checkAutoGpuBattery.UseVisualStyleBackColor = false;
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
            _wmiHotkeyWatcher = new WmiHotkeyWatcher(OnHotkeyEvent);
            _quickAccessModeWatcher = new QuickAccessModeWatcher(HandleModeKeyPressed);
        }

        private void QueueStartupWork()
        {
            Task.Run(() => ClosePredatorSenseUiProcesses());

            if (Environment.CommandLine.Contains("-hidden"))
            {
                HideApp();
            }

            CheckCalibration();
            ApplySavedBatteryLimitAsync();

            _ = Task.Run(() =>
            {
                _wmi.RefreshAcerService();
            });
        }

        private static void ClosePredatorSenseUiProcesses()
        {
            string[] processNames =
            [
                "PredatorSense",
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
                    _lastRefreshRate = currentHz;
                    LoadCurrentHardwarePowerMode();
                }));

                // Apply gamma LUT on the background thread — SetDeviceGammaRamp doesn't require UI thread
                // and doing it here avoids blocking the first paint with a GPU display pipeline stall.
                ApplyGammaForRefreshRate(currentHz);
            });
        }

        private async Task CheckForUpdatesAsync()
        {
            try
            {
                using var client = new System.Net.Http.HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("PreySense-Updater");
                string json = await client.GetStringAsync("https://api.github.com/repos/hammadzaigham/PreySense/releases/latest");
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("tag_name", out var tagProperty))
                {
                    string? latestTag = tagProperty.GetString();
                    if (!string.IsNullOrEmpty(latestTag))
                    {
                        string latestVersionStr = latestTag.TrimStart('v', 'V');
                        string currentVersionStr = Program.VersionString;

                        if (Version.TryParse(latestVersionStr, out Version? latestVersion) &&
                            Version.TryParse(currentVersionStr, out Version? currentVersion))
                        {
                            if (latestVersion > currentVersion)
                            {
                                BeginInvoke(new Action(() =>
                                {
                                    DialogResult result = Dialogs.ConfirmDialog.Show(
                                        this,
                                        $"A new version ({latestTag}) of Prey Sense is available.\n\n" +
                                        "Would you like to open the GitHub repository to download it?",
                                        "Prey Sense - Update Available",
                                        "Download",
                                        "Later");

                                    if (result == DialogResult.Yes)
                                    {
                                        try
                                        {
                                            Process.Start(new ProcessStartInfo
                                            {
                                                FileName = "https://github.com/hammadzaigham/PreySense/releases",
                                                UseShellExecute = true
                                            });
                                        }
                                        catch (Exception ex)
                                        {
                                            AppLogger.Log($"Failed to open update link: {ex.Message}");
                                        }
                                    }
                                }));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Update check failed: {ex.Message}");
            }
        }
    }
}
