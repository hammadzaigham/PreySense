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
        [DllImport(NvmlDll)] static extern int nvmlDeviceGetMemoryInfo(IntPtr device, out nvmlMemory_t memory);

        [StructLayout(LayoutKind.Sequential)]
        struct nvmlMemory_t { public ulong total; public ulong free; public ulong used; }

        const int NVML_SUCCESS = 0;

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
