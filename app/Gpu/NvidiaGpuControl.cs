using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NvAPIWrapper.GPU;
using NvAPIWrapper.Native;
using NvAPIWrapper.Native.GPU;
using NvAPIWrapper.Native.GPU.Structures;
using NvAPIWrapper.Native.Interfaces.GPU;
using static NvAPIWrapper.Native.GPU.Structures.PerformanceStates20InfoV1;
using PreySense;

namespace PreySense.Gpu
{
    public class NvidiaGpuControl : IGpuControl
    {
        public static int MaxCoreOffset = 500;
        public static int MaxMemoryOffset = 3000;
        public static int MinCoreOffset = 0;
        public static int MinMemoryOffset = 0;

        private static PhysicalGPU? _internalGpu;

        public NvidiaGpuControl()
        {
            _internalGpu = GetInternalDiscreteGpu();
            if (IsValid)
            {
                AppLogger.Log($"NVIDIA GPU: {FullName} ({MaxCoreOffset},{MaxMemoryOffset})");
            }
        }

        public bool IsValid => _internalGpu != null;
        public bool IsNvidia => IsValid;
        public string FullName => _internalGpu != null ? _internalGpu.FullName : "NVIDIA GPU";

        public int? _lastTemp;
        public int _lastTempTime = 0;

        private static bool verboseLog = false;

        private enum GpuState { Active, Asleep, Off }

        private GpuState _lastState = GpuState.Off;
        private long _lastStateTime = -StateCacheMs;
        private const int StateCacheMs = 500;
        private const int InactiveStateCacheMs = 5_000;

        private GpuState GetGpuState()
        {
            if (!IsValid) return GpuState.Off;
            int cacheMs = _lastState == GpuState.Active ? StateCacheMs : InactiveStateCacheMs;
            if (Environment.TickCount64 - _lastStateTime < cacheMs) return _lastState;
            try
            {
                var perfState = GPUApi.GetCurrentPerformanceState(_internalGpu!.Handle);
                if (verboseLog) AppLogger.Log($"GPU: {perfState}");
                _lastState = GpuState.Active;
            }
            catch (Exception ex)
            {
                if (verboseLog) AppLogger.Log($"GPU: {ex.Message}");
                _lastState = ex.Message == "NVAPI_GPU_NOT_POWERED" ? GpuState.Asleep : GpuState.Off;
            }
            _lastStateTime = Environment.TickCount64;
            return _lastState;
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

        private Task<int?>? _readTask;

        public int? GetCurrentTemperature()
        {
            if (!IsValid) return null;

            var state = GetGpuState();
            if (state == GpuState.Off) return null;

            if ((_readTask?.IsCompleted ?? true) && (state == GpuState.Active || ShouldRefresh()))
            {
                _readTask = Task.Run(() =>
                {
                    var temp = ReadCurrentTemperature();
                    if (temp is not null)
                    {
                        _lastTemp = temp;
                        _lastTempTime = Environment.TickCount;
                    }
                    return temp;
                });
            }

            _readTask?.Wait(500);
            return _lastTemp;
        }

        private bool ShouldRefresh()
        {
            const int minInterval = 5_000;

            if (_lastTemp is null) return true;

            // Simple default temperature delta logic since CPU temp is not accessible globally
            var refresh = Environment.TickCount > _lastTempTime + minInterval;
            if (verboseLog) AppLogger.Log($"GPU Temp Refresh: {refresh}");
            return refresh;
        }

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

        public bool GetClocks(out int core, out int memory, bool forceWake = true)
        {
            if (!IsValid)
            {
                core = memory = 0;
                return false;
            }

            // Don't wake the dGPU just for clock reading (e.g. overlay metrics)
            if (!forceWake)
            {
                var state = GetGpuState();
                if (state != GpuState.Active)
                {
                    core = memory = 0;
                    return false;
                }
            }

            PhysicalGPU internalGpu = _internalGpu!;

            try
            {
                var temp = ReadCurrentTemperature(false, forceWake); // Only force wake if caller requested

                IPerformanceStates20Info states = GPUApi.GetPerformanceStates20(internalGpu.Handle);
                var p0Clocks = states.Clocks[PerformanceStateId.P0_3DPerformance];
                var coreClock = p0Clocks.FirstOrDefault(c => c.DomainId == PublicClockDomain.Graphics);
                var memoryClock = p0Clocks.FirstOrDefault(c => c.DomainId == PublicClockDomain.Memory);
                if (coreClock == null || memoryClock == null)
                {
                    AppLogger.Log("GET GPU CLOCKS: P0 graphics or memory clock entry missing.");
                    core = memory = 0;
                    return false;
                }

                core = coreClock.FrequencyDeltaInkHz.DeltaValue / 1000;
                memory = memoryClock.FrequencyDeltaInkHz.DeltaValue / 1000;
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Log("GET GPU CLOCKS:" + ex.Message);
                core = memory = 0;
                return false;
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

        public int SetClocks(int core, int memory)
        {
            if (core < MinCoreOffset || core > MaxCoreOffset) return 0;
            if (memory < MinMemoryOffset || memory > MaxMemoryOffset) return 0;

            if (!GetClocks(out int currentCore, out int currentMemory))
            {
                AppLogger.Log("SET GPU CLOCKS: unable to read current clocks before applying offsets.");
            }

            // Nothing to set
            if (Math.Abs(core - currentCore) < 5 && Math.Abs(memory - currentMemory) < 5) return 0;

            if (!IsValid) return -1;
            PhysicalGPU internalGpu = _internalGpu!;

            var coreClock = new PerformanceStates20ClockEntryV1(PublicClockDomain.Graphics, new PerformanceStates20ParameterDelta(core * 1000));
            var memoryClock = new PerformanceStates20ClockEntryV1(PublicClockDomain.Memory, new PerformanceStates20ParameterDelta(memory * 1000));

            PerformanceStates20ClockEntryV1[] clocks = { coreClock, memoryClock };
            PerformanceStates20BaseVoltageEntryV1[] voltages = { };

            PerformanceState20[] performanceStates = { new PerformanceState20(PerformanceStateId.P0_3DPerformance, clocks, voltages) };
            var overclock = new PerformanceStates20InfoV1(performanceStates, 2, 0);

            try
            {
                AppLogger.Log($"SET GPU CLOCKS: {core}, {memory}");
                GPUApi.SetPerformanceStates20(internalGpu.Handle, overclock);
            }
            catch (Exception ex)
            {
                AppLogger.Log("SET GPU CLOCKS: " + ex.Message);
                return -1;
            }

            return 1;
        }

        public string DescribePerformanceStates()
        {
            if (!IsValid)
            {
                return "NVAPI PStates20: no valid NVIDIA GPU found.";
            }

            try
            {
                IPerformanceStates20Info states = GPUApi.GetPerformanceStates20(_internalGpu!.Handle);
                var sb = new StringBuilder();
                sb.AppendLine($"NVAPI PStates20: IsEditable={states.IsEditable}, PerformanceStates={states.PerformanceStates.Length}, ClockStates={states.Clocks.Count}, VoltageStates={states.Voltages.Count}, GeneralVoltages={states.GeneralVoltages.Length}");

                foreach (var state in states.PerformanceStates.OrderBy(s => s.StateId.ToString()))
                {
                    sb.AppendLine($"  State {state.StateId}: IsEditable={state.IsEditable}");
                }

                foreach (var kvp in states.Clocks.OrderBy(k => k.Key.ToString()))
                {
                    sb.AppendLine($"  Clocks[{kvp.Key}] count={kvp.Value.Length}");
                    for (int i = 0; i < kvp.Value.Length; i++)
                    {
                        var clock = kvp.Value[i];
                        var delta = clock.FrequencyDeltaInkHz;
                        sb.AppendLine(
                            $"    [{i}] Domain={clock.DomainId}, Type={clock.ClockType}, IsEditable={clock.IsEditable}, DeltaMHz={delta.DeltaValue / 1000}, RangeMHz={delta.DeltaRange.Minimum / 1000}..{delta.DeltaRange.Maximum / 1000}");
                    }
                }

                foreach (var kvp in states.Voltages.OrderBy(k => k.Key.ToString()))
                {
                    sb.AppendLine($"  Voltages[{kvp.Key}] count={kvp.Value.Length}");
                    for (int i = 0; i < kvp.Value.Length; i++)
                    {
                        var voltage = kvp.Value[i];
                        var delta = voltage.ValueDeltaInMicroVolt;
                        sb.AppendLine(
                            $"    [{i}] Domain={voltage.DomainId}, IsEditable={voltage.IsEditable}, ValueuV={voltage.ValueInMicroVolt}, DeltaRangeuV={delta.DeltaRange.Minimum}..{delta.DeltaRange.Maximum}");
                    }
                }

                for (int i = 0; i < states.GeneralVoltages.Length; i++)
                {
                    var voltage = states.GeneralVoltages[i];
                    var delta = voltage.ValueDeltaInMicroVolt;
                    sb.AppendLine(
                        $"  GeneralVoltage[{i}] Domain={voltage.DomainId}, IsEditable={voltage.IsEditable}, ValueuV={voltage.ValueInMicroVolt}, DeltaRangeuV={delta.DeltaRange.Minimum}..{delta.DeltaRange.Maximum}");
                }

                string description = sb.ToString().TrimEnd();
                AppLogger.Log(description);
                return description;
            }
            catch (Exception ex)
            {
                string message = "NVAPI PStates20 diagnostics failed: " + ex.Message;
                AppLogger.Log(message);
                return message;
            }
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
    }
}
