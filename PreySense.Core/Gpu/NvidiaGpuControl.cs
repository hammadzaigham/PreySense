using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NvAPIWrapper.GPU;
using NvAPIWrapper.Native;
using NvAPIWrapper.Native.GPU;
using NvAPIWrapper.Native.GPU.Structures;
using NvAPIWrapper.Native.Interfaces.GPU;
using PreySense;

namespace PreySense.Gpu
{
    public class NvidiaGpuControl : IGpuControl
    {
        private static readonly Lazy<NvidiaGpuControl> SharedLazy = new(() => new NvidiaGpuControl(), true);
        public static NvidiaGpuControl Shared => SharedLazy.Value;

        private static PhysicalGPU? _internalGpu;

        public NvidiaGpuControl()
        {
            _internalGpu = GetInternalDiscreteGpu();
        }

        public void RefreshDevice() => _internalGpu = GetInternalDiscreteGpu();
        public bool IsValid => _internalGpu != null;
        public bool IsNvidia => IsValid;
        public string FullName => _internalGpu != null ? _internalGpu.FullName : "NVIDIA GPU";

        private static bool verboseLog = false;

        private enum GpuState { Active, Asleep, Off }


        private GpuState GetGpuState() => GetGpuState(out _);

        private GpuState GetGpuState(out PerformanceStateId perfState)
        {
            perfState = PerformanceStateId.Undefined;
            if (!IsValid) return GpuState.Off;
            try
            {
                perfState = GPUApi.GetCurrentPerformanceState(_internalGpu!.Handle);
                if (verboseLog) AppLogger.Log($"GPU: {perfState}");
                return GpuState.Active;
            }
            catch (Exception ex)
            {
                if (verboseLog) AppLogger.Log($"GPU: {ex.Message}");
                return ex.Message == "NVAPI_GPU_NOT_POWERED" ? GpuState.Asleep : GpuState.Off;
            }
        }

        /// <summary>
        /// True when something other than this process is using the discrete GPU.
        /// A powered-off or idle GPU is left in D3/RTD3: NVML is released and not opened.
        /// </summary>
        public bool IsDiscreteGpuInUse()
        {
            bool inUse = GetGpuState(out var perfState) == GpuState.Active
                && (!IsIdlePerformanceState(perfState) || HasUserGpuClient());
            if (!inUse)
                NvmlHelper.Shutdown();

            return inUse;
        }

        private static bool IsIdlePerformanceState(PerformanceStateId state)
            => state is >= PerformanceStateId.P8_HDVideoPlayback and <= PerformanceStateId.Undefined;

        private bool HasUserGpuClient()
        {
            if (!IsValid || _internalGpu is null) return false;
            int currentPid = Process.GetCurrentProcess().Id;
            try
            {
                Process[] processes = _internalGpu.GetActiveApplications();
                bool found = false;
                foreach (Process process in processes)
                {
                    try
                    {
                        if (!found
                            && process.Id != currentPid
                            && process.SessionId != 0
                            && !_systemProcessNames.Contains(process.ProcessName))
                            found = true;
                    }
                    catch { }
                    finally { process.Dispose(); }
                }
                return found;
            }
            catch
            {
                return false;
            }
        }

        public int? ReadCurrentTemperature(bool log = false, bool forceWake = false)
        {
            if (!IsValid) return null;

            // Don't wake the dGPU just to read temperature
            if (!forceWake)
            {
                var state = GetGpuState();
                if (state != GpuState.Active) return null;
            }

            try
            {
                var thermalSettings = GPUApi.GetThermalSettings(_internalGpu!.Handle);
                if (thermalSettings.Sensors is null) return null;

                IThermalSensor? gpuSensor = thermalSettings.Sensors
                    .FirstOrDefault(s => s.Target == ThermalSettingsTarget.GPU);

                if (log || verboseLog) AppLogger.Log($"GPU Temp: {gpuSensor?.CurrentTemperature}C");
                return gpuSensor?.CurrentTemperature;
            }
            catch
            {
                return null;
            }
        }

        public int? GetCurrentTemperature() => ReadCurrentTemperature();

        public void Dispose()
        {
            _internalGpu = null;
        }

        private static readonly HashSet<string> _systemProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "dwm", "csrss", "winlogon", "services", "lsass", "smss", "wininit",
            "svchost", "fontdrvhost", "igfxem", "igfxhk", "igfxext",
            "nvcontainer", "nvdisplay.container", "nvsettings", "nvspcaps64",
            "nvsphelper64", "nvwmi64", "nvcplui", "atieclxx", "atiesrxx",
            "explorer", "taskhostw", "sihost", "runtimebroker", "shellexperiencehost",
            "searchhost", "startmenuexperiencehost", "textinputhost",
            "applicationframehost", "systemsettings", "dllhost", "conhost",
            "audiodg", "ctfloader", "spoolsv", "wlanext", "msdtc",
        };

        public void KillGPUApps()
        {
            if (!IsValid) return;
            PhysicalGPU internalGpu = _internalGpu!;

            int currentPid = Process.GetCurrentProcess().Id;

            try
            {
                Process[] processes = internalGpu.GetActiveApplications();
                foreach (Process process in processes)
                    try
                    {
                        if (process.Id == currentPid) continue;
                        if (process.SessionId == 0) continue;
                        if (_systemProcessNames.Contains(process.ProcessName)) continue;

                        AppLogger.Log("Kill:" + process.ProcessName);
                        process.Kill();
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Log(ex.Message);
                    }
            }
            catch (Exception ex)
            {
                AppLogger.Log(ex.Message);
            }
        }

        private static bool RunPowershellCommand(string script, int timeoutMs = 0)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    if (timeoutMs > 0) process.WaitForExit(timeoutMs);
                    else process.WaitForExit();
                }
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Log(ex.ToString());
                return false;
            }
        }

        public static void RestartNVService()
        {
            RunPowershellCommand(@"Restart-Service -Name 'NVDisplay.ContainerLocalSystem' -Force", 30000);
            RunPowershellCommand(@"Restart-Service -Name 'NvContainerLocalSystem' -Force", 30000);
        }

        public static void StopNVService()
        {
            RunPowershellCommand(@"Stop-Service -Name 'NvContainerLocalSystem' -Force", 30000);
            RunPowershellCommand(@"Stop-Service -Name 'NVDisplay.ContainerLocalSystem' -Force", 30000);
        }

        private static PhysicalGPU? GetInternalDiscreteGpu()
        {
            try
            {
                return PhysicalGPU
                    .GetPhysicalGPUs()
                    .FirstOrDefault(gpu =>
                        gpu.FullName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) &&
                        gpu.SystemType != SystemType.Unknown);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                return null;
            }
        }

        public int? GetGpuUse()
        {
            if (!IsValid) return null;
            if (GetGpuState() != GpuState.Active) return null;

            try
            {
                PhysicalGPU internalGpu = _internalGpu!;
                IUtilizationDomainInfo? gpuUsage = GPUApi.GetUsages(internalGpu.Handle).GPU;
                return (int?)gpuUsage?.Percentage;
            }
            catch
            {
                return null;
            }
        }

        public float? GetGpuPower()
        {
            if (!IsValid) return null;
            var state = GetGpuState();
            if (state == GpuState.Off)
            {
                NvmlHelper.Shutdown();
                return null;
            }
            if (state != GpuState.Active) return 0f;
            return NvmlHelper.GetGpuPower() ?? 0f;
        }

        public (long usedMb, long totalMb)? GetVramInfo()
        {
            if (!IsValid) return null;
            if (GetGpuState() != GpuState.Active) return null;
            return NvmlHelper.GetMemoryInfo();
        }

        public int? GetGpuClock()
        {
            if (!IsValid) return null;
            if (GetGpuState() != GpuState.Active) return null;
            return NvmlHelper.GetGpuClock();
        }

        public int? GetGpuMemoryClock()
        {
            if (!IsValid) return null;
            if (GetGpuState() != GpuState.Active) return null;
            return NvmlHelper.GetGpuMemoryClock();
        }

        public float? GetGpuVoltage()
        {
            if (!IsValid) return null;
            if (GetGpuState() != GpuState.Active) return null;
            try
            {
                var status = GPUApi.GetCurrentVoltage(_internalGpu!.Handle);
                if (status.ValueInMicroVolt > 0)
                {
                    return (float)(status.ValueInMicroVolt / 1000000.0);
                }
            }
            catch { }
            return null;
        }

        public System.Collections.Generic.List<GpuProcessInfo> GetGpuProcesses()
        {
            var list = new System.Collections.Generic.List<GpuProcessInfo>();
            var pids = NvmlHelper.GetRunningProcessIds();
            foreach (var pid in pids)
            {
                try
                {
                    using var proc = Process.GetProcessById((int)pid);
                    list.Add(new GpuProcessInfo { ProcessId = pid, ProcessName = proc.ProcessName });
                }
                catch
                {
                    list.Add(new GpuProcessInfo { ProcessId = pid, ProcessName = $"PID {pid}" });
                }
            }
            return list;
        }
    }

    public class GpuProcessInfo
    {
        public uint ProcessId { get; set; }
        public string ProcessName { get; set; } = "";
    }
}
