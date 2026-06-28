using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;
using PreySense.Battery;
using PreySense.Fan;
using PreySense.Helpers;
using PreySense.Mode;
using PreySense.UI;
using PreySense.UI.Controls;

namespace PreySense
{
    public partial class MainForm
    {
        public void SaveState(string name, int value)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\PreySense");
                key.SetValue(name, value);
                if (name == "GpuBatteryAuto")
                {
                    _gpuBatteryAuto = (value == 1);
                }
            }
            catch { }
        }

        public void SaveCurveState(string name, PointF[] curve)
        {
            byte mode = GetActivePowerMode();
            SaveCurveState(mode, name, curve);
        }

        public void SaveCurveState(byte mode, string name, PointF[] curve)
        {
            if (name == "CpuCurve")
                FanCurveStorage.SaveCpuCurve(mode, curve);
            else if (name == "GpuCurve")
                FanCurveStorage.SaveGpuCurve(mode, curve);

            byte active = GetActivePowerMode();
            if (mode == active)
            {
                if (name == "CpuCurve") _cpuCurve = curve;
                else if (name == "GpuCurve") _gpuCurve = curve;
            }
        }

        private byte GetActivePowerMode()
        {
            return IsKnownPowerMode(_lastKnownProfile) ? _lastKnownProfile : _wmi.GetPowerProfile();
        }

        public byte CurrentPowerMode => GetActivePowerMode();

        private static int MapDropdownIndexToMode(int dropdownIndex) =>
            dropdownIndex >= 0 && dropdownIndex < RgbProfile.ModeCount ? dropdownIndex : 0;

        private static int MapModeToDropdownIndex(int mode) =>
            Math.Clamp(RgbProfile.NormalizeSavedMode(mode), 0, RgbProfile.ModeCount - 1);

        private void HighlightFanMode(int mode) { }

        private const string StartupTaskName = "PreySense";


        private static bool IsRunOnStartupEnabled()
        {
            try
            {
                // Check whether a scheduled task with our task name exists and is enabled.
                using var proc = Process.Start(new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/Query /TN \"{StartupTaskName}\" /FO LIST",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                });
                proc?.WaitForExit(3000);
                // Exit code 0 means the task was found.
                return proc?.ExitCode == 0;
            }
            catch { return false; }
        }

        private void SetRunOnStartup(bool enabled)
        {
            try
            {
                if (enabled)
                {
                    // Create (or replace) a logon-triggered task that runs PreySense elevated.
                    // /RL HIGHEST gives it the same privilege level as requireAdministrator.
                    string exePath = Application.ExecutablePath;
                    string args =
                        $"/Create /F /TN \"{StartupTaskName}\" " +
                        $"/TR \"\\\"{exePath}\\\" -hidden\" " +
                        "/SC ONLOGON " +
                        "/RL HIGHEST";

                    RunSchtasks(args);
                }
                else
                {
                    RunSchtasks($"/Delete /F /TN \"{StartupTaskName}\"");
                }
            }
            catch { }
        }

        private static void RunSchtasks(string arguments)
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            proc?.WaitForExit(5000);
        }

        private static bool IsGpuBatteryAutoEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\PreySense", false);
                return key != null && (int)key.GetValue("GpuBatteryAuto", 0) == 1;
            }
            catch { return false; }
        }

        private void SetGpuBatteryAuto(bool enabled)
        {
            SaveState("GpuBatteryAuto", enabled ? 1 : 0);
        }

        private void CheckCalibration()
        {
            // CPU limit calibration has been replaced by hardcoded per-mode defaults.
        }

        public async Task RunCalibrationAsync()
        {
            return;
        }

        private void OpenFansForm()
        {
            if (fansForm == null || fansForm.IsDisposed)
            {
                fansForm = new Fans(this, _wmi);
            }
            fansForm.ShowDialog(this);
        }

        private void OpenMetricsOverlay()
        {
            Task.Run(() =>
            {
                var overlay = Program.GetHardwareOverlay();
                if (overlay.IsActive)
                {
                    Overlay.AppConfig.Set("overlay", 0);
                    BeginInvoke(new Action(() => overlay.StopOverlay()));
                    return;
                }

                Overlay.AppConfig.Set("overlay", 1);
                // Pre-initialize NVAPI/NVML and sensors in the background thread to prevent UI micro-stutters
                PreySense.Overlay.HardwareControl.ReadSensorsOverlay();
                BeginInvoke(new Action(() => overlay.StartOverlay()));
            });
        }
    }
}
