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

        public static float? cpuPower;
        public static float? gpuPower;

        public static int? cpuFanRPM;
        public static int? gpuFanRPM;

        public static int? cpuUsage;
        public static int? cpuMaxCoreUsage;
        public static int? cpuPCoreUsage;
        public static int? cpuECoreUsage;
        public static int? gpuUsage;
        public static int? ramUsage;
        public static int? vramUsedMb;
        public static int? ramUsedMb;

        // Extra metrics
        public static int?   cpuMhz;
        public static int?   eCoreMhz;
        public static int?   gpuMhz;
        public static float? gpuVoltage;
        public static float? cpuVoltage;
        public static int?   ramSpeedMhz;
        public static int?   vramSpeedMhz;



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
            cpuTemp = gpuTemp = null;
            cpuFanRPM = gpuFanRPM = null;
            ramSpeedMhz = vramSpeedMhz = null;
            cpuUsage = null;
            cpuMaxCoreUsage = null;
            cpuPCoreUsage = null;
            cpuECoreUsage = null;
            ramUsage = null;
            ramUsedMb = null;
            gpuUsage = null;
            vramUsedMb = null;
            cpuPower = default;
            gpuPower = null;
            cpuMhz = null;
            eCoreMhz = null;
            gpuMhz = null;
            gpuVoltage = null;
            cpuVoltage = null;
        }

        public static void ApplySnapshot(TelemetrySnapshot snapshot)
        {
            cpuTemp = snapshot.CpuTemp >= 0 ? snapshot.CpuTemp : null;
            gpuTemp = snapshot.GpuTemp >= 0 ? snapshot.GpuTemp : null;
            cpuFanRPM = snapshot.CpuRpm >= 0 ? snapshot.CpuRpm : null;
            gpuFanRPM = snapshot.GpuRpm >= 0 ? snapshot.GpuRpm : null;

            cpuUsage = snapshot.CpuUsage;
            cpuMaxCoreUsage = snapshot.CpuMaxCoreUsage;
            cpuPCoreUsage = snapshot.CpuPCoreUsage;
            cpuECoreUsage = snapshot.CpuECoreUsage;
            ramSpeedMhz = snapshot.RamSpeedMhz;
            ramUsage = snapshot.RamUsage;
            ramUsedMb = snapshot.RamUsedMb;
            cpuPower = snapshot.CpuPkgPowerWatts;
            gpuPower = snapshot.GpuPowerWatts;
            cpuVoltage = snapshot.CpuVoltage;
            gpuVoltage = snapshot.GpuVoltage;
            gpuMhz = snapshot.GpuClockMhz;
            gpuUsage = snapshot.GpuUsage;
            vramUsedMb = snapshot.VramUsedMb;
            vramSpeedMhz = snapshot.VramSpeedMhz;
            cpuMhz = snapshot.CpuClockMhz;
            eCoreMhz = snapshot.ECoreClockMhz;
        }

    }
}
