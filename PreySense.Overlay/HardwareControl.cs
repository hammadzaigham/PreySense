using System;
using System.Diagnostics;
using System.Management;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using PreySense.Gpu;
using PreySense.Helpers;
using System.Runtime.InteropServices;
using PreySense.Core.Services;

namespace PreySense.Overlay
{
    public static class HardwareControl
    {
        public static Func<WmiController?>? WmiProvider { get; set; }
        public static IGpuControl? GpuControl;
        public static string CpuName { get; private set; } = "";
        public static string CpuShortName { get; private set; } = "";
        public static string GpuShortName { get; private set; } = "";

        public static float? cpuTemp;
        public static float? gpuTemp;
        public static float? gpuHotspotTemp;
        public static float? gpuMemoryTemp;

        public static float? cpuPower;
        public static float? gpuPower;
        public static float? gpuPowerLimit;
        public static int?   cpuPl1Watts;
        public static int?   cpuPl2Watts;

        public static int? cpuFanRPM;
        public static int? gpuFanRPM;

        public static int? cpuUsage;
        public static int? gpuUsage;
        public static int? ramUsage;
        public static int? vramUsedMb;
        public static int? ramUsedMb;

        // Extra metrics
        public static int?   cpuActiveMhz;
        public static int?   cpuEffectiveMhz;
        public static int?   gpuActiveMhz;
        public static float? gpuVoltage;
        public static float? cpuVoltage;
        public static int?   ramSpeedMhz;
        public static int?   vramSpeedMhz;
        public static ulong? gpuThrottleReasons;



        static HardwareControl()
        {
            try
            {
                GpuControl = NvidiaGpuControl.Shared;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"HardwareControl GpuControl init failed: {ex.Message}");
            }

            CpuName = ReadCpuName();
            CpuShortName = ShortCpuName(CpuName);
            RefreshGpuName();
        }

        public static void RefreshGpuName() => GpuShortName = ShortGpuName(GpuControl?.FullName);

        private static string ReadCpuName()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                return key?.GetValue("ProcessorNameString")?.ToString()?.Trim() ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static string ShortGpuName(string? full)
        {
            if (string.IsNullOrEmpty(full)) return "";
            foreach (string tag in new[] { "RTX", "GTX", "RX", "Arc" })
            {
                int i = full.IndexOf(tag, StringComparison.OrdinalIgnoreCase);
                if (i < 0) continue;
                string[] p = full[i..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return p.Length >= 2 ? p[0] + " " + p[1] : p[0];
            }
            return full;
        }

        private static string ShortCpuName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";

            var m = Regex.Match(name, @"i[3579]-\w+");
            if (m.Success) return m.Value;

            m = Regex.Match(name, @"Ultra\s+\d+\s+(\w*\d\w*)");
            if (m.Success) return "Ultra " + m.Groups[1].Value;

            if (name.Contains("Ryzen", StringComparison.OrdinalIgnoreCase))
            {
                m = Regex.Match(name, @"(?:[A-Z]{2,}\s+)?\d{3,}\w*");
                return m.Success ? "Ryzen " + m.Value : "Ryzen";
            }

            return name.Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } t ? t[0] : "";
        }

        public static void ResetMetrics()
        {
            cpuTemp = null;
            cpuFanRPM = gpuFanRPM = null;
            ramSpeedMhz = null;
            cpuUsage = null;
            ramUsage = null;
            ramUsedMb = null;
            cpuPower = default;
            cpuPl1Watts = cpuPl2Watts = null;
            cpuActiveMhz = null;
            cpuEffectiveMhz = null;
            cpuVoltage = null;
            ClearGpuMetrics();
        }

        public static void ClearGpuMetrics()
        {
            gpuTemp = gpuHotspotTemp = gpuMemoryTemp = null;
            gpuUsage = null;
            vramUsedMb = null;
            vramSpeedMhz = null;
            gpuPower = gpuPowerLimit = null;
            gpuActiveMhz = null;
            gpuVoltage = null;
            gpuThrottleReasons = null;
        }

        public static void ApplySnapshot(TelemetrySnapshot snapshot)
        {
            cpuTemp = snapshot.CpuTemp >= 0 ? snapshot.CpuTemp : null;
            cpuFanRPM = snapshot.CpuRpm >= 0 ? snapshot.CpuRpm : null;
            gpuFanRPM = snapshot.GpuRpm >= 0 ? snapshot.GpuRpm : null;

            cpuUsage = snapshot.CpuUsage;
            ramSpeedMhz = snapshot.RamSpeedMhz;
            ramUsage = snapshot.RamUsage;
            ramUsedMb = snapshot.RamUsedMb;
            cpuPower = snapshot.CpuPkgPowerWatts;
            cpuPl1Watts = snapshot.CpuPl1Watts;
            cpuPl2Watts = snapshot.CpuPl2Watts;
            cpuVoltage = snapshot.CpuVoltage;
            cpuActiveMhz = snapshot.CpuActiveFrequencyMhz;
            cpuEffectiveMhz = snapshot.CpuEffectiveFrequencyMhz;

            // Idle/asleep GPU snapshots must not keep the last active readings on screen.
            if (!snapshot.GpuInUse)
            {
                ClearGpuMetrics();
                return;
            }

            gpuTemp = snapshot.GpuTemp >= 0 ? snapshot.GpuTemp : null;
            gpuHotspotTemp = snapshot.GpuHotspotTemp >= 0 ? snapshot.GpuHotspotTemp : null;
            gpuMemoryTemp = snapshot.GpuMemoryTemp >= 0 ? snapshot.GpuMemoryTemp : null;
            gpuPower = snapshot.GpuPowerWatts;
            gpuPowerLimit = snapshot.GpuPowerLimitWatts;
            gpuVoltage = snapshot.GpuVoltage;
            gpuActiveMhz = snapshot.GpuActiveClockMhz;
            gpuUsage = snapshot.GpuUsage;
            vramUsedMb = snapshot.VramUsedMb;
            vramSpeedMhz = snapshot.VramSpeedMhz;
            gpuThrottleReasons = snapshot.GpuThrottleReasons;
        }

    }
}
