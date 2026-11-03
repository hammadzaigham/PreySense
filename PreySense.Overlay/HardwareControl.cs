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
        private static WmiController? _fallbackWmi;
        public static IGpuControl? GpuControl;
        public static string CpuName { get; private set; } = "";
        public static string CpuShortName { get; private set; } = "";
        public static string GpuShortName { get; private set; } = "";

        public static float? cpuTemp;
        public static float? gpuTemp;
        /// <summary>True when the discrete GPU is confirmed in use. Temperature is not used as proof of that.</summary>
        public static bool gpuInUse;

        public static float? cpuPower;
        public static float? gpuPower;

        public static int? cpuFanRPM;
        public static int? gpuFanRPM;

        public static int? cpuUsage;
        public static int? cpuMaxCoreUsage;
        public static int? cpuPCoreUsage;
        public static int? cpuECoreUsage;
        public static int? gpuUsage;
        public static int? vramUsage;
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

        private static readonly PdhCpuFrequencyMonitor _pdhMonitor = new();
        private static readonly PdhCpuPowerMonitor _pdhPowerMonitor = new();

        private static int? _lastCpuUsage;

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
            ramSpeedMhz = GetRamSpeed();
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

        public static void Warmup()
        {
            WinRing0Cleanup.RemoveInstalledDriver();
            CpuUsageMonitor.Warmup();
        }

        public static int? GetCpuMhz()
        {
            eCoreMhz = null;
            try
            {
                if (_pdhMonitor.TrySampleFrequencies(out int pdhMaxMhz, out _, out _, out int eCoreMaxMhz))
                {
                    if (eCoreMaxMhz > 0)
                    {
                        eCoreMhz = eCoreMaxMhz;
                    }
                    if (pdhMaxMhz > 0)
                    {
                        return pdhMaxMhz;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("GetCpuMhz failed: " + ex.Message);
            }

            return null;
        }

        public static float? GetCPUPower()
        {
            try
            {
                if (_pdhPowerMonitor.IsAvailable)
                {
                    float watts = _pdhPowerMonitor.GetPackagePowerWatts();
                    if (watts > 0f)
                    {
                        return watts;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("GetCPUPower PDH failed: " + ex.Message);
            }

            return null;
        }

        private static int? GetCPUUsage()
        {
            int? usage = CpuUsageMonitor.GetCpuUsage();
            if (usage.HasValue)
            {
                _lastCpuUsage = usage.Value;
            }
            return usage;
        }

        private static (int percent, int usedMb)? GetRAMInfo()
        {
            var mem = SystemMemoryMonitor.GetMemoryInfo();
            return mem.HasValue ? (mem.Value.UsedPercent, mem.Value.UsedMb) : null;
        }

        private static float? GetGPUPower()
        {
            try
            {
                float? power = GpuControl?.GetGpuPower();
                if (power is not null) return power.Value;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Failed reading GPU power: " + ex.Message);
            }
            return null;
        }

        public static void ResetMetrics()
        {
            cpuUsage = null;
            cpuMaxCoreUsage = null;
            cpuPCoreUsage = null;
            cpuECoreUsage = null;
            ramUsage = null;
            ramUsedMb = null;
            gpuUsage = null;
            vramUsage = null;
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
            gpuTemp = (snapshot.GpuTemp >= 0 && snapshot.GpuInUse) ? snapshot.GpuTemp : null;
            if (snapshot.CpuRpm.HasValue) cpuFanRPM = snapshot.CpuRpm >= 0 ? snapshot.CpuRpm : null;
            if (snapshot.GpuRpm.HasValue) gpuFanRPM = snapshot.GpuRpm >= 0 ? snapshot.GpuRpm : null;

            if (snapshot.IsFanOnly)
            {
                // If full telemetry has expired, clear stale CPU, RAM, and GPU readings
                if (snapshot.TimestampTicks <= 0)
                {
                    ResetMetrics();
                }
                return;
            }

            cpuUsage = snapshot.CpuUsage;
            cpuMaxCoreUsage = snapshot.CpuMaxCoreUsage;
            cpuPCoreUsage = snapshot.CpuPCoreUsage;
            cpuECoreUsage = snapshot.CpuECoreUsage;
            ramUsage = snapshot.RamUsage;
            ramUsedMb = snapshot.RamUsedMb;
            gpuInUse = snapshot.GpuInUse;
            cpuPower = snapshot.CpuPkgPowerWatts;
            gpuPower = snapshot.GpuPowerWatts;
            cpuVoltage = snapshot.CpuVoltage;
            gpuVoltage = snapshot.GpuVoltage;
            gpuMhz = snapshot.GpuClockMhz;
            gpuUsage = snapshot.GpuUsage;
            vramUsedMb = snapshot.VramUsedMb;
            if (snapshot.VramUsedMb.HasValue && snapshot.VramTotalMb.HasValue && snapshot.VramTotalMb.Value > 0)
            {
                vramUsage = (int)Math.Clamp((long)snapshot.VramUsedMb.Value * 100 / snapshot.VramTotalMb.Value, 0, 100);
            }
            else
            {
                vramUsage = null;
            }
            vramSpeedMhz = snapshot.VramSpeedMhz;
            ramSpeedMhz = GetRamSpeed();
            if (snapshot.CpuClockMhz.HasValue)
            {
                cpuMhz = snapshot.CpuClockMhz;
                eCoreMhz = snapshot.ECoreClockMhz;
            }
            else
            {
                cpuMhz = GetCpuMhz();
            }
        }

        public static void ReadSystemMetrics()
        {
            var cpuSnapshot = CpuUsageMonitor.GetSnapshot();
            if (cpuSnapshot.HasValue)
            {
                cpuUsage = cpuSnapshot.Value.TotalUsage;
                cpuMaxCoreUsage = cpuSnapshot.Value.MaxCoreUsage;
                cpuPCoreUsage = cpuSnapshot.Value.PCoreUsage;
                cpuECoreUsage = cpuSnapshot.Value.ECoreUsage;
                _lastCpuUsage = cpuUsage;
            }
            else
            {
                cpuUsage = GetCPUUsage();
                cpuMaxCoreUsage = CpuUsageMonitor.GetMaxCoreUsage();
                cpuPCoreUsage = CpuUsageMonitor.GetPCoreUsage();
                cpuECoreUsage = CpuUsageMonitor.GetECoreUsage();
            }

            var ram = GetRAMInfo();
            ramUsage = ram?.percent;
            ramUsedMb = ram?.usedMb;
        }

        public static void ReadHardwareSensors()
        {
            var wmi = WmiProvider?.Invoke() ?? (_fallbackWmi ??= new WmiController());
            var nvidia = GpuControl as NvidiaGpuControl;
            // NVML init wakes a dGPU in D3/RTD3 and holds it there. Skip it unless the GPU is already in use.
            // Read live NVIDIA temperature; the EC can retain an old GPU reading.
            bool inUse = nvidia is null || nvidia.IsDiscreteGpuInUse();
            gpuInUse = inUse;

            cpuMhz = GetCpuMhz();
            cpuTemp = PreySense.Mode.PawnCpuPowerControl.ReadPackageTemperature();
            bool needsVoltage = false;
            for (int column = 1; column <= 8; column++)
                needsVoltage |= AppConfig.ParseOverlayColumn(AppConfig.GetString($"overlay_col_{column}")).Cpu == "Voltage";
            cpuVoltage = needsVoltage ? PreySense.Mode.PawnCpuPowerControl.ReadCoreVid() : null;
            bool needsThermal = false;
            for (int column = 1; column <= 8; column++)
            {
                var metrics = AppConfig.ParseOverlayColumn(AppConfig.GetString($"overlay_col_{column}"));
                needsThermal |= metrics.Cpu == "Fan Speed" || metrics.Gpu == "Fan Speed";
            }

            if (wmi != null && needsThermal)
            {
                var thermal = wmi.ReadThermalSensors(includeTemperatures: false);
                cpuFanRPM = thermal.CpuRpm >= 0 ? thermal.CpuRpm : null;
                gpuFanRPM = thermal.GpuRpm >= 0 ? thermal.GpuRpm : null;
            }
            else
            {
                cpuFanRPM = null;
                gpuFanRPM = null;
            }

            gpuTemp = inUse ? GpuControl?.GetCurrentTemperature() : null;

            if (!inUse)
            {
                gpuUsage = null;
                vramUsedMb = null;
                vramUsage = null;
                gpuPower = null;
                gpuMhz = null;
                vramSpeedMhz = null;
                gpuVoltage = null;
            }
            else
            {
                try { gpuUsage = GpuControl?.GetGpuUse(); } catch { gpuUsage = null; }

                try
                {
                    if (GpuControl?.GetVramInfo() is { } v && v.totalMb > 0)
                    {
                        vramUsedMb = (int)v.usedMb;
                        vramUsage = (int)Math.Clamp(v.usedMb * 100 / v.totalMb, 0, 100);
                    }
                    else { vramUsedMb = null; vramUsage = null; }
                }
                catch { vramUsedMb = null; vramUsage = null; }

                gpuPower = GetGPUPower();
                try { gpuMhz = GpuControl?.GetGpuClock(); } catch { gpuMhz = null; }
                try { vramSpeedMhz = GpuControl?.GetGpuMemoryClock(); } catch { vramSpeedMhz = null; }
                try { gpuVoltage = GpuControl?.GetGpuVoltage(); } catch { gpuVoltage = null; }
            }

            cpuPower = GetCPUPower();
        }

        public static void ReadSensorsOverlay()
        {
            ReadSystemMetrics();
            ReadHardwareSensors();
        }

        private static int? _cachedRamSpeed;
        private static bool _ramSpeedQueried;

        public static int? GetRamSpeed()
        {
            if (_ramSpeedQueried) return _cachedRamSpeed;
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Speed, ConfiguredClockSpeed FROM Win32_PhysicalMemory");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var speed = obj["ConfiguredClockSpeed"] ?? obj["Speed"];
                    if (speed != null)
                    {
                        int val = Convert.ToInt32(speed);
                        if (val > 0)
                        {
                            _cachedRamSpeed = val;
                            break;
                        }
                    }
                }
            }
            catch { }
            finally
            {
                _ramSpeedQueried = true;
            }
            return _cachedRamSpeed;
        }
    }
}
