using System;
using System.Diagnostics;
using System.IO;
using PreySense.Helpers;

namespace PreySense.Mode
{
    /// <summary>
    /// Governs CPU Boost and hardware power limits.
    /// Uses the bundled CPU driver for MSR/MMIO limits and powercfg for boost.
    /// </summary>
    public class PowerLimitController
    {
        private static readonly object CpuLimitGate = new();
        private static readonly object PowerCfgGate = new();
        public static string LastCpuPowerLimitStatus { get; private set; } = "CPU limits have not been applied.";
        /// <summary>
        /// Applies matching limits through the bundled driver's verified MSR/MMIO transaction.
        /// A partial result returns false and is described by LastCpuPowerLimitStatus.
        /// </summary>
        public static bool SetCpuPowerLimits(
            int pl1Watts,
            int pl2Watts,
            bool pl2Enable = true,
            int tauSeconds = PreySense.Core.Driver.CpuPowerLimitConfiguration.FactoryTauSeconds)
        {
            lock (CpuLimitGate)
            {
            pl1Watts = Math.Clamp(pl1Watts, 5, PreySense.Core.Driver.CpuPowerLimitConfiguration.MaxPowerLimitWatts);
            pl2Watts = Math.Clamp(pl2Watts, 5, PreySense.Core.Driver.CpuPowerLimitConfiguration.MaxPowerLimitWatts);
            var driverController = PreySense.Core.Hardware.CpuDriverRuntime.PowerLimits;
            var backend = PreySense.Core.Hardware.CpuDriverRuntime.Backend;
            if (driverController == null || backend == null)
            {
                LastCpuPowerLimitStatus = PreySense.Core.Hardware.CpuDriverRuntime.Status;
                AppLogger.Log(LastCpuPowerLimitStatus);
                return false;
            }

            if (!backend.TryReadMchbar(out ulong mchbar))
            {
                LastCpuPowerLimitStatus = "Cannot discover the supported CPU MCHBAR; nothing was written.";
                return false;
            }
            var result = driverController.ExecuteCombinedTransaction(mchbar, pl1Watts, pl2Watts, pl2Enable, tauSeconds);
            LastCpuPowerLimitStatus = result.Status;
            AppLogger.Log(LastCpuPowerLimitStatus);
            return result.Success;
            }
        }

        public static string GetCpuPowerLimitDiagnostic(
            int pl1Watts,
            int pl2Watts,
            bool pl2Enable = true)
        {
            return LastCpuPowerLimitStatus;
        }

        /// <summary>
        /// Sets Windows PERFBOOSTMODE on the active scheme (AC + DC).
        /// 0 Disabled, 1 Enabled, 2 Aggressive, 3 Efficient Enabled, 4 Efficient Aggressive,
        /// 5 Aggressive At Guaranteed, 6 Efficient Aggressive At Guaranteed.
        /// </summary>
        public static int EnergySaverBatteryThreshold(bool enabled) => enabled ? 100 : 0;

        /// <summary>
        /// Windows applies this DC threshold only while the laptop is on battery.
        /// 100 turns Energy Saver on for the whole battery session. 0 leaves it off.
        /// USB-C and the AC adapter both count as plugged in, so this stays off there.
        /// </summary>
        public static void SetEnergySaverOnBattery(bool enabled)
        {
            lock (PowerCfgGate)
            {
            int threshold = EnergySaverBatteryThreshold(enabled);
            if (!RunPowerCfg($"-setdcvalueindex scheme_current sub_energysaver esbattthreshold {threshold}") ||
                !RunPowerCfg("-setactive scheme_current")) return;
            AppLogger.Log($"Windows Energy Saver on battery: {(enabled ? "on" : "off")} (threshold {threshold}).");
            }
        }

        public static bool SetCpuBoost(int value)
        {
            lock (PowerCfgGate)
            {
            value = Math.Clamp(value, 0, 6);
            return RunPowerCfg($"-setacvalueindex scheme_current sub_processor perfboostmode {value}") &&
                RunPowerCfg($"-setdcvalueindex scheme_current sub_processor perfboostmode {value}") &&
                RunPowerCfg("-setactive scheme_current");
            }
        }

        private static bool RunPowerCfg(string args)
        {
            bool success = RunCommand("powercfg", args, 5000, out string error);
            if (!success) AppLogger.Error($"powercfg {args} failed: {error}");
            return success;
        }

        private static bool RunCommand(string executable, string args, int timeoutMs, out string error)
        {
            error = string.Empty;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                if (process == null) { error = "The process could not be started."; return false; }
                if (!process.WaitForExit(timeoutMs))
                {
                    error = $"Command timed out after {timeoutMs} ms.";
                    try { process.Kill(entireProcessTree: true); } catch { }
                    return false;
                }
                if (process.ExitCode != 0) { error = $"Command exited with code {process.ExitCode}."; return false; }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
