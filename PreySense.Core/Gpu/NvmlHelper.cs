using System;
using System.Runtime.InteropServices;
using PreySense;

namespace PreySense.Gpu
{
    public static class NvmlHelper
    {
        const string NvmlDll = "nvml.dll";

        [DllImport(NvmlDll)] static extern int nvmlInit_v2();
        [DllImport(NvmlDll)] static extern int nvmlShutdown();
        [DllImport(NvmlDll)] static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);
        [DllImport(NvmlDll)] static extern int nvmlDeviceGetPowerUsage(IntPtr device, out uint powerMilliWatts);
        [DllImport(NvmlDll)] static extern int nvmlDeviceGetPowerManagementDefaultLimit(IntPtr device, out uint defaultMilliWatts);
        [DllImport(NvmlDll)] static extern int nvmlDeviceGetMemoryInfo(IntPtr device, out nvmlMemory_t memory);
        [DllImport(NvmlDll)] static extern int nvmlDeviceGetClockInfo(IntPtr device, int clockType, out uint clockMHz);
        [DllImport(NvmlDll)] static extern int nvmlDeviceGetTemperature(IntPtr device, int sensorType, out uint tempC);

        [StructLayout(LayoutKind.Sequential)]
        public struct nvmlProcessInfo_t { public uint pid; public ulong usedGpuMemory; }

        [DllImport(NvmlDll)] static extern int nvmlDeviceGetGraphicsRunningProcesses(IntPtr device, ref uint infoCount, [In, Out] nvmlProcessInfo_t[]? infos);
        [DllImport(NvmlDll)] static extern int nvmlDeviceGetComputeRunningProcesses(IntPtr device, ref uint infoCount, [In, Out] nvmlProcessInfo_t[]? infos);

        [StructLayout(LayoutKind.Sequential)]
        struct nvmlMemory_t { public ulong total; public ulong free; public ulong used; }

        const int NVML_SUCCESS = 0;
        const int NVML_ERROR_INSUFFICIENT_SIZE = 7;

        private static readonly object _lock = new();
        private static bool _init = false;

        public static void Init()
        {
            if (_init) return;
            try
            {
                int rc = nvmlInit_v2();
                _init = rc == NVML_SUCCESS;
                AppLogger.Log($"NVML Init: {rc}");
            }
            catch (Exception e)
            {
                AppLogger.Log($"NVML Init exception: {e.Message}");
            }
        }

        public static int? GetGpuDefaultPowerLimitWatts(uint gpuIndex = 0)
        {
            lock (_lock)
            {
                Init();
                if (!_init) return null;
                try
                {
                    if (nvmlDeviceGetHandleByIndex_v2(gpuIndex, out IntPtr device) != NVML_SUCCESS) return null;
                    if (nvmlDeviceGetPowerManagementDefaultLimit(device, out uint milliWatts) != NVML_SUCCESS) return null;
                    return PreySense.Core.Acpi.GpuPowerBaseline.FromMilliwatts(milliWatts);
                }
                catch { return null; }
            }
        }

        public static float? GetGpuPower(uint gpuIndex = 0)
        {
            lock (_lock)
            {
                Init();
                if (!_init) return null;
                try
                {
                    if (nvmlDeviceGetHandleByIndex_v2(gpuIndex, out IntPtr device) != NVML_SUCCESS) return null;
                    if (nvmlDeviceGetPowerUsage(device, out uint mW) != NVML_SUCCESS) return null;
                    if (mW > 200_000f) return null;
                    return mW / 1000f;
                }
                catch { return null; }
            }
        }

        public static (long usedMb, long totalMb)? GetMemoryInfo(uint gpuIndex = 0)
        {
            lock (_lock)
            {
                Init();
                if (!_init) return null;
                try
                {
                    if (nvmlDeviceGetHandleByIndex_v2(gpuIndex, out IntPtr device) != NVML_SUCCESS) return null;
                    if (nvmlDeviceGetMemoryInfo(device, out nvmlMemory_t mem) != NVML_SUCCESS) return null;
                    if (mem.total == 0) return null;
                    const ulong MB = 1024 * 1024;
                    return ((long)(mem.used / MB), (long)(mem.total / MB));
                }
                catch { return null; }
            }
        }

        public static int? GetGpuTemperature(uint gpuIndex = 0)
        {
            lock (_lock)
            {
                Init();
                if (!_init) return null;
                try
                {
                    if (nvmlDeviceGetHandleByIndex_v2(gpuIndex, out IntPtr device) != NVML_SUCCESS) return null;
                    if (nvmlDeviceGetTemperature(device, 0, out uint tempC) != NVML_SUCCESS) return null;
                    if (tempC == 0 || tempC > 120) return null;
                    return (int)tempC;
                }
                catch { return null; }
            }
        }

        public static int? GetGpuClock(uint gpuIndex = 0)
        {
            lock (_lock)
            {
                Init();
                if (!_init) return null;
                try
                {
                    if (nvmlDeviceGetHandleByIndex_v2(gpuIndex, out IntPtr device) != NVML_SUCCESS) return null;
                    if (nvmlDeviceGetClockInfo(device, 0, out uint clockMHz) != NVML_SUCCESS) return null;
                    return (int)clockMHz;
                }
                catch { return null; }
            }
        }

        public static int? GetGpuMemoryClock(uint gpuIndex = 0)
        {
            lock (_lock)
            {
                Init();
                if (!_init) return null;
                try
                {
                    if (nvmlDeviceGetHandleByIndex_v2(gpuIndex, out IntPtr device) != NVML_SUCCESS) return null;
                    if (nvmlDeviceGetClockInfo(device, 2, out uint clockMHz) != NVML_SUCCESS) return null; // 2 = NVML_CLOCK_MEM
                    return (int)clockMHz;
                }
                catch { return null; }
            }
        }

        public static System.Collections.Generic.List<uint> GetRunningProcessIds(uint gpuIndex = 0)
        {
            var pids = new System.Collections.Generic.List<uint>();
            lock (_lock)
            {
                Init();
                if (!_init) return pids;
                try
                {
                    if (nvmlDeviceGetHandleByIndex_v2(gpuIndex, out IntPtr device) != NVML_SUCCESS) return pids;

                    uint count = 64;
                    var procInfos = new nvmlProcessInfo_t[count];
                    if (nvmlDeviceGetGraphicsRunningProcesses(device, ref count, procInfos) == NVML_SUCCESS)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            if (procInfos[i].pid > 0 && !pids.Contains(procInfos[i].pid))
                                pids.Add(procInfos[i].pid);
                        }
                    }

                    uint compCount = 64;
                    var compInfos = new nvmlProcessInfo_t[compCount];
                    if (nvmlDeviceGetComputeRunningProcesses(device, ref compCount, compInfos) == NVML_SUCCESS)
                    {
                        for (int i = 0; i < compCount; i++)
                        {
                            if (compInfos[i].pid > 0 && !pids.Contains(compInfos[i].pid))
                                pids.Add(compInfos[i].pid);
                        }
                    }
                }
                catch { }
            }
            return pids;
        }

        public static void Shutdown()
        {
            lock (_lock)
            {
                if (!_init) return;
                try
                {
                    int rc = nvmlShutdown();
                    AppLogger.Log($"NVML Shutdown: {rc}");
                }
                catch { }
                _init = false;
            }
        }
    }
}
