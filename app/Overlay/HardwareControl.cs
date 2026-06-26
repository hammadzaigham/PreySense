using System;
using System.Diagnostics;
using System.Management;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using PreySense.Gpu;
using PreySense.Helpers;

namespace PreySense.Overlay
{
    public static class HardwareControl
    {
        public static IGpuControl? GpuControl;
        public static string CpuName { get; private set; } = "";
        public static string CpuShortName { get; private set; } = "";
        public static string GpuShortName { get; private set; } = "";

        public static float? cpuTemp = -1;
        public static float? gpuTemp = -1;

        public static float? cpuPower;
        public static float? gpuPower;

        public static int? cpuFanRPM;
        public static int? gpuFanRPM;

        public static int? cpuUsage;
        public static int? gpuUsage;
        public static int? vramUsage;
        public static int? ramUsage;
        public static int? vramUsedMb;
        public static int? ramUsedMb;

        // Extra metrics
        public static int?   cpuMhz;
        public static int?   gpuMhz;
        public static float? cpuVoltage;

        private static PerformanceCounter? _cpuPowerCounter;
        private static bool _cpuPowerCounterFailed;
        private static bool _cpuPowerInitStarted;
        private static int _cpuPowerNullTicks;
        private static int _cpuPowerReadErrors;
        private const int CpuPowerMaxReadErrors = 3;
        private static readonly string[] _powerCounterInstances = { "Apu Power", "RAPL_Package0_PKG", "CPU Power", "Socket Power", "Current Socket Power" };

        private static PerformanceCounter? _cpuPerfCounter;
        private static int _maxCpuSpeed = -1;

        static HardwareControl()
        {
            try
            {
                GpuControl = new NvidiaGpuControl();
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

        public static void ResetCPUPowerCounter()
        {
            _cpuPowerReadErrors = 0;
            _cpuPowerCounterFailed = false;
        }

        public static void InitCPUPowerAsync()
        {
            if (_cpuPowerInitStarted) return;
            _cpuPowerInitStarted = true;

            Task.Run(() =>
            {
                var cached = AppConfig.GetString("cpu_power_counter");
                if (!string.IsNullOrEmpty(cached))
                {
                    try
                    {
                        var counter = new PerformanceCounter("Energy Meter", "Power", cached, true);
                        counter.NextValue();
                        _cpuPowerCounter = counter;
                        return;
                    }
                    catch
                    {
                        AppConfig.Set("cpu_power_counter", "");
                    }
                }

                try
                {
                    var category = new PerformanceCounterCategory("Energy Meter");
                    var instances = category.GetInstanceNames();

                    foreach (var name in _powerCounterInstances)
                    {
                        if (instances.Contains(name, StringComparer.OrdinalIgnoreCase))
                        {
                            var counter = new PerformanceCounter("Energy Meter", "Power", name, true);
                            counter.NextValue();
                            _cpuPowerCounter = counter;
                            AppConfig.Set("cpu_power_counter", name);
                            return;
                        }
                    }

                    _cpuPowerCounterFailed = true;
                }
                catch
                {
                    _cpuPowerCounterFailed = true;
                }
            });
        }

        public static void InitCpuSpeedCounter()
        {
            if (_maxCpuSpeed > 0) return;

            // 1. Get Max Clock Speed from WMI once
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT MaxClockSpeed FROM Win32_Processor");
                foreach (ManagementObject obj in searcher.Get())
                {
                    _maxCpuSpeed = Convert.ToInt32(obj["MaxClockSpeed"]);
                    break;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("Failed to get MaxClockSpeed from WMI: " + ex.Message);
                _maxCpuSpeed = 3000; // default fallback
            }

            // 2. Initialize performance counter for cpu performance percentage
            try
            {
                // Try "Processor Information" -> "% Processor Performance"
                var pc = new PerformanceCounter("Processor Information", "% Processor Performance", "_Total", true);
                pc.NextValue();
                _cpuPerfCounter = pc;
            }
            catch
            {
                try
                {
                    // Fallback to "Processor" -> "% of Maximum Frequency"
                    var pc = new PerformanceCounter("Processor", "% of Maximum Frequency", "_Total", true);
                    pc.NextValue();
                    _cpuPerfCounter = pc;
                }
                catch (Exception ex)
                {
                    AppLogger.Log("Failed to initialize CPU speed performance counter: " + ex.Message);
                }
            }
        }

        public static float? GetCPUPower()
        {
            if (_cpuPowerCounterFailed || _cpuPowerCounter == null) return null;

            try
            {
                float mW = _cpuPowerCounter.NextValue();
                if (mW > 0) return mW / 1000f;
            }
            catch
            {
                _cpuPowerCounter?.Dispose();
                _cpuPowerCounter = null;
                if (++_cpuPowerReadErrors >= CpuPowerMaxReadErrors)
                {
                    _cpuPowerCounterFailed = true;
                }
                else
                {
                    _cpuPowerCounterFailed = false;
                    _cpuPowerInitStarted = false;
                }
            }

            return null;
        }

        private static long _cpuLastIdle, _cpuLastKernel, _cpuLastUser, _cpuLastTick;
        private static bool _cpuUsageBaseline;

        public static int? GetCPUUsage()
        {
            if (!NativeMethods.GetSystemTimes(out long idle, out long kernel, out long user)) return null;

            long now = Environment.TickCount64;

            if (!_cpuUsageBaseline || now - _cpuLastTick > 2000)
            {
                _cpuLastIdle = idle; _cpuLastKernel = kernel; _cpuLastUser = user; _cpuLastTick = now;
                _cpuUsageBaseline = true;
                return null;
            }

            long deltaIdle = idle - _cpuLastIdle;
            long deltaTotal = (kernel - _cpuLastKernel) + (user - _cpuLastUser);

            _cpuLastIdle = idle; _cpuLastKernel = kernel; _cpuLastUser = user; _cpuLastTick = now;

            if (deltaTotal <= 0) return 0;
            return Math.Clamp((int)Math.Round((1.0 - (double)deltaIdle / deltaTotal) * 100), 0, 100);
        }

        public static (int percent, int usedMb)? GetRAMInfo()
        {
            var status = new NativeMethods.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>() };
            if (!NativeMethods.GlobalMemoryStatusEx(ref status)) return null;
            int usedMb = (int)((status.ullTotalPhys - status.ullAvailPhys) / (1024 * 1024));
            return ((int)status.dwMemoryLoad, usedMb);
        }

        public static float? GetGPUPower()
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

        public static void ReadSensorsOverlay()
        {
            var wmi = Program.settingsForm?.Wmi;

            cpuFanRPM = wmi?.CpuFanRpm;
            gpuFanRPM = wmi?.GpuFanRpm;

            cpuTemp = wmi?.CpuTemp ?? -1;
            gpuTemp = wmi?.GpuTemp ?? -1;

            if (GpuControl != null && GpuControl.IsValid)
            {
                var nvidTemp = GpuControl.GetCurrentTemperature();
                if (nvidTemp.HasValue && nvidTemp.Value > 0)
                {
                    gpuTemp = nvidTemp.Value;
                }
            }

            cpuUsage = GetCPUUsage();
            try { gpuUsage = GpuControl?.GetGpuUse(); } catch { gpuUsage = null; }

            var ram = GetRAMInfo();
            ramUsage = ram?.percent;
            ramUsedMb = ram?.usedMb;

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

            cpuPower = null;
            if (cpuPower == null || cpuPower <= 0)
            {
                InitCPUPowerAsync();
                float? newCpu = GetCPUPower();
                if (newCpu > 0)
                {
                    cpuPower = newCpu;
                    _cpuPowerNullTicks = 0;
                }
                else if (++_cpuPowerNullTicks >= 5)
                {
                    cpuPower = null;
                }
            }

            gpuPower = GetGPUPower();
            cpuMhz = null;
            gpuMhz = null;
            cpuVoltage = null;
        }
    }
}
