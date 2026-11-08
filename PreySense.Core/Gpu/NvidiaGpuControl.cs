using System;
using PreySense.Core.Services;
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

        public int? ReadCurrentTemperature(bool log = false)
        {
            if (PreySense.Helpers.DeviceHelper.GetDisplayDevicePowerState("VEN_10DE") != 1) return null;
            if (GetGpuState(out var perfState) != GpuState.Active) return null;
            if (IsIdlePerformanceState(perfState) && !HasUserGpuClient())
            {
                NvmlHelper.Shutdown();
                return null;
            }
            return ReadAwakeTemperature(log);
        }

        private int? ReadAwakeTemperature(bool log = false)
        {
            if (!IsValid) return null;
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

        public readonly record struct Metrics(bool InUse, int? Temperature, int? Usage,
            int? ClockMhz, int? MemoryClockMhz, float? Voltage, float? PowerWatts,
            long? VramUsedMb, long? VramTotalMb, float? HotspotTemperature = null,
            ulong? ThrottleReasons = null, float? PowerLimitWatts = null, float? MemoryTemperature = null,
            int? ActiveClockMhz = null);

        /// <summary>One power-state decision for the entire shared GPU sampling batch.</summary>
        public Metrics ReadMetrics(TelemetryMetrics requested)
        {
            bool Wants(TelemetryMetrics metric) => (requested & metric) != 0;
            if (!Wants(TelemetryMetrics.Gpu)) return default;
            if (PreySense.Helpers.DeviceHelper.GetDisplayDevicePowerState("VEN_10DE") != 1)
            {
                NvmlHelper.Shutdown();
                return default;
            }
            if (!Wants(TelemetryMetrics.GpuPower | TelemetryMetrics.GpuMemory | TelemetryMetrics.GpuMemoryClock | TelemetryMetrics.GpuHotspot | TelemetryMetrics.GpuThrottleReasons | TelemetryMetrics.GpuPowerLimit))
                NvmlHelper.Shutdown();
            if (GetGpuState(out var performanceState) != GpuState.Active)
            {
                NvmlHelper.Shutdown();
                return default;
            }
            // Idle P-state with no other client: skip all sensor reads and release NVML so RTD3 can sleep.
            if (IsIdlePerformanceState(performanceState) && !HasUserGpuClient())
            {
                NvmlHelper.Shutdown();
                return default;
            }
            var temperature = Wants(TelemetryMetrics.GpuTemperature) ? ReadAwakeTemperature() : null;
            if ((requested & ~TelemetryMetrics.GpuTemperature) == 0)
                return new Metrics(true, temperature, null, null, null, null, null, null, null);
            var activeClock = Wants(TelemetryMetrics.GpuActiveClock) ? GetGpuActiveClock(true) : null;
            var usage = Wants(TelemetryMetrics.GpuUsage) ? GetGpuUse(true) : null;
            var memoryClock = Wants(TelemetryMetrics.GpuMemoryClock) ? GetGpuMemoryClock(true) : null;
            var voltage = Wants(TelemetryMetrics.GpuVoltage) ? GetGpuVoltage(true) : null;
            var power = Wants(TelemetryMetrics.GpuPower) ? GetGpuPower(true) : null;
            var memory = Wants(TelemetryMetrics.GpuMemory) ? GetVramInfo(true) : null;
            var thermal = Wants(TelemetryMetrics.GpuHotspot | TelemetryMetrics.GpuMemoryTemperature)
                ? NvapiThermal.ReadTemperatures(_internalGpu?.Handle.MemoryAddress, Wants(TelemetryMetrics.GpuHotspot), Wants(TelemetryMetrics.GpuMemoryTemperature))
                : ((float? HotspotC, float? MemoryC))(null, null);
            var throttleReasons = Wants(TelemetryMetrics.GpuThrottleReasons) ? GetThrottleReasons(true) : null;
            var powerLimit = Wants(TelemetryMetrics.GpuPowerLimit) ? GetGpuPowerLimit(true) : null;
            return new Metrics(true, temperature, usage, null, memoryClock, voltage, power,
                memory?.usedMb, memory?.totalMb, thermal.HotspotC, throttleReasons, powerLimit, thermal.MemoryC, activeClock);
        }

        public float? GetGpuHotspot() => GetGpuHotspot(false);

        private float? GetGpuHotspot(bool stateChecked)
        {
            if (!IsValid) return null;
            if (!stateChecked && (PreySense.Helpers.DeviceHelper.GetDisplayDevicePowerState("VEN_10DE") != 1 || GetGpuState() != GpuState.Active)) return null;
            return NvapiThermal.GetHotspot(_internalGpu?.Handle.MemoryAddress);
        }

        public float? GetGpuMemoryTemperature() => GetGpuMemoryTemperature(false);

        private float? GetGpuMemoryTemperature(bool stateChecked)
        {
            if (!IsValid) return null;
            if (!stateChecked && (PreySense.Helpers.DeviceHelper.GetDisplayDevicePowerState("VEN_10DE") != 1 || GetGpuState() != GpuState.Active)) return null;
            return NvapiThermal.GetMemoryTemperature(_internalGpu?.Handle.MemoryAddress);
        }

        public ulong? GetThrottleReasons() => GetThrottleReasons(false);

        private ulong? GetThrottleReasons(bool stateChecked)
        {
            if (!IsValid) return null;
            if (!stateChecked && GetGpuState() != GpuState.Active) return null;
            return NvmlHelper.GetThrottleReasons();
        }

        public float? GetGpuPowerLimit() => GetGpuPowerLimit(false);

        private float? GetGpuPowerLimit(bool stateChecked)
        {
            if (!IsValid) return null;
            if (!stateChecked && GetGpuState() != GpuState.Active) return null;
            return NvmlHelper.GetGpuPowerLimitWatts();
        }

        public int? GetGpuUse() => GetGpuUse(false);

        private int? GetGpuUse(bool stateChecked)
        {
            if (!IsValid) return null;
            if (!stateChecked && GetGpuState() != GpuState.Active) return null;

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

        public float? GetGpuPower() => GetGpuPower(false);

        private float? GetGpuPower(bool stateChecked)
        {
            if (!IsValid) return null;
            var state = stateChecked ? GpuState.Active : GetGpuState();
            if (state == GpuState.Off)
            {
                NvmlHelper.Shutdown();
                return null;
            }
            if (state != GpuState.Active) return null;
            return NvmlHelper.GetGpuPower();
        }

        public (long usedMb, long totalMb)? GetVramInfo() => GetVramInfo(false);

        private (long usedMb, long totalMb)? GetVramInfo(bool stateChecked)
        {
            if (!IsValid) return null;
            if (!stateChecked && GetGpuState() != GpuState.Active) return null;
            return NvmlHelper.GetMemoryInfo();
        }


        /// <summary>NVAPI current graphics clock in kHz, rounded like mVolt's RTSS display.</summary>
        private int? GetGpuActiveClock(bool stateChecked)
        {
            if (!IsValid || !stateChecked && GetGpuState() != GpuState.Active) return null;
            try
            {
                // Verified mVolt call: 0x14022786C, NV_GPU_CLOCK_FREQUENCIES_V2 (0x20108), type 0.
                var clock = GPUApi.GetAllClockFrequencies(_internalGpu!.Handle,
                    new ClockFrequenciesV2(ClockType.CurrentClock)).GraphicsClock;
                return GpuClockReading.FromKilohertz(clock.IsPresent, clock.Frequency);
            }
            catch (Exception ex)
            {
                if (verboseLog) AppLogger.Log($"GPU active clock unavailable: {ex.Message}");
                return null;
            }
        }

        public int? GetGpuMemoryClock() => GetGpuMemoryClock(false);

        private int? GetGpuMemoryClock(bool stateChecked)
        {
            if (!IsValid) return null;
            if (!stateChecked && GetGpuState() != GpuState.Active) return null;
            return NvmlHelper.GetGpuMemoryClock();
        }

        public float? GetGpuVoltage() => GetGpuVoltage(false);

        private float? GetGpuVoltage(bool stateChecked)
        {
            if (!IsValid) return null;
            if (!stateChecked && GetGpuState() != GpuState.Active) return null;
            try
            {
                var status = GPUApi.GetCurrentVoltage(_internalGpu!.Handle);
                if (status.ValueInMicroVolt > 0)
                    return (float)(status.ValueInMicroVolt / 1000000.0);
            }
            catch { }
            return null;
        }

        public System.Collections.Generic.List<GpuProcessInfo> GetGpuProcesses()
        {
            var list = new System.Collections.Generic.List<GpuProcessInfo>();
            int currentPid = Process.GetCurrentProcess().Id;

            // Prefer NvAPI — same source KillGPUApps / HasUserGpuClient use. NVML's
            // unversioned process APIs often return empty on current drivers, which
            // silently skipped the iGPU-only confirmation modal.
            if (IsValid && _internalGpu is not null)
            {
                try
                {
                    Process[] processes = _internalGpu.GetActiveApplications();
                    foreach (Process process in processes)
                    {
                        try
                        {
                            if (process.Id == currentPid) continue;
                            if (process.SessionId == 0) continue;
                            if (_systemProcessNames.Contains(process.ProcessName)) continue;
                            list.Add(new GpuProcessInfo
                            {
                                ProcessId = (uint)process.Id,
                                ProcessName = process.ProcessName
                            });
                        }
                        catch { }
                        finally { process.Dispose(); }
                    }
                    if (list.Count > 0)
                        return list;
                }
                catch { }
            }

            foreach (uint pid in NvmlHelper.GetRunningProcessIds())
            {
                if (pid == 0 || pid == (uint)currentPid) continue;
                try
                {
                    using var proc = Process.GetProcessById((int)pid);
                    if (proc.SessionId == 0) continue;
                    if (_systemProcessNames.Contains(proc.ProcessName)) continue;
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
