using System;
using System.Runtime.InteropServices;
using PreySense;

namespace PreySense.Gpu
{
    public static class NvmlHelper
    {
        const string NvmlDll = "nvml.dll";

        [DllImport(NvmlDll, CallingConvention = CallingConvention.Cdecl)] static extern int nvmlInit_v2();
        [DllImport(NvmlDll, CallingConvention = CallingConvention.Cdecl)] static extern int nvmlShutdown();
        [DllImport(NvmlDll, CallingConvention = CallingConvention.Cdecl)] static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);
        [DllImport(NvmlDll, CallingConvention = CallingConvention.Cdecl)] static extern int nvmlDeviceGetPowerUsage(IntPtr device, out uint powerMilliWatts);
        [DllImport(NvmlDll, CallingConvention = CallingConvention.Cdecl)] static extern int nvmlDeviceGetPowerManagementDefaultLimit(IntPtr device, out uint defaultMilliWatts);
        [DllImport(NvmlDll, CallingConvention = CallingConvention.Cdecl)] static extern int nvmlDeviceGetMemoryInfo(IntPtr device, out nvmlMemory_t memory);
        [DllImport(NvmlDll, CallingConvention = CallingConvention.Cdecl)] static extern int nvmlDeviceGetClockInfo(IntPtr device, int clockType, out uint clockMHz);
        [DllImport(NvmlDll, CallingConvention = CallingConvention.Cdecl)] static extern int nvmlDeviceGetCurrentClocksThrottleReasons(IntPtr device, out ulong clocksThrottleReasons);
        [DllImport(NvmlDll, CallingConvention = CallingConvention.Cdecl)] static extern int nvmlDeviceGetEnforcedPowerLimit(IntPtr device, out uint limitMilliWatts);

        [StructLayout(LayoutKind.Sequential)]
        struct nvmlMemory_t { public ulong total; public ulong free; public ulong used; }

        const int NVML_SUCCESS = 0;

        private static readonly object _lock = new();
        private static bool _init = false;
        private static IntPtr _cachedDevice = IntPtr.Zero;
        private static uint _cachedGpuIndex = uint.MaxValue;

        private static void EnsureInitializedLocked()
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

        private static bool TryGetDeviceLocked(uint gpuIndex, out IntPtr device)
        {
            EnsureInitializedLocked();
            if (!_init)
            {
                device = IntPtr.Zero;
                return false;
            }
            if (_cachedDevice != IntPtr.Zero && _cachedGpuIndex == gpuIndex)
            {
                device = _cachedDevice;
                return true;
            }
            if (nvmlDeviceGetHandleByIndex_v2(gpuIndex, out device) != NVML_SUCCESS || device == IntPtr.Zero)
            {
                device = IntPtr.Zero;
                return false;
            }
            _cachedDevice = device;
            _cachedGpuIndex = gpuIndex;
            return true;
        }

        public static int? GetGpuDefaultPowerLimitWatts(uint gpuIndex = 0)
        {
            lock (_lock)
            {
                try
                {
                    if (!TryGetDeviceLocked(gpuIndex, out IntPtr device)) return null;
                    if (nvmlDeviceGetPowerManagementDefaultLimit(device, out uint milliWatts) != NVML_SUCCESS) return null;
                    return PreySense.Core.Driver.GpuPowerBaseline.FromMilliwatts(milliWatts);
                }
                catch { return null; }
            }
        }

        public static float? GetGpuPower(uint gpuIndex = 0)
        {
            lock (_lock)
            {
                try
                {
                    if (!TryGetDeviceLocked(gpuIndex, out IntPtr device)) return null;
                    if (nvmlDeviceGetPowerUsage(device, out uint mW) != NVML_SUCCESS) return null;
                    if (mW > 600_000f) return null;
                    return mW / 1000f;
                }
                catch { return null; }
            }
        }

        public static (long usedMb, long totalMb)? GetMemoryInfo(uint gpuIndex = 0)
        {
            lock (_lock)
            {
                try
                {
                    if (!TryGetDeviceLocked(gpuIndex, out IntPtr device)) return null;
                    if (nvmlDeviceGetMemoryInfo(device, out nvmlMemory_t mem) != NVML_SUCCESS) return null;
                    if (mem.total == 0) return null;
                    const ulong MB = 1024 * 1024;
                    return ((long)(mem.used / MB), (long)(mem.total / MB));
                }
                catch { return null; }
            }
        }

        public static ulong? GetThrottleReasons(uint gpuIndex = 0)
        {
            lock (_lock)
            {
                try
                {
                    if (!TryGetDeviceLocked(gpuIndex, out IntPtr device)) return null;
                    if (nvmlDeviceGetCurrentClocksThrottleReasons(device, out ulong reasons) != NVML_SUCCESS) return null;
                    return reasons;
                }
                catch { return null; }
            }
        }

        public static float? GetGpuPowerLimitWatts(uint gpuIndex = 0)
        {
            lock (_lock)
            {
                try
                {
                    if (!TryGetDeviceLocked(gpuIndex, out IntPtr device)) return null;
                    if (nvmlDeviceGetEnforcedPowerLimit(device, out uint mW) != NVML_SUCCESS) return null;
                    if (mW == 0 || mW > 600_000) return null;
                    return mW / 1000f;
                }
                catch { return null; }
            }
        }

        public static int? GetGpuMemoryClock(uint gpuIndex = 0)
        {
            lock (_lock)
            {
                try
                {
                    if (!TryGetDeviceLocked(gpuIndex, out IntPtr device)) return null;
                    if (nvmlDeviceGetClockInfo(device, 2, out uint clockMHz) != NVML_SUCCESS) return null; // 2 = NVML_CLOCK_MEM
                    return (int)clockMHz;
                }
                catch { return null; }
            }
        }



        public static void Shutdown()
        {
            lock (_lock)
            {
                _cachedDevice = IntPtr.Zero;
                _cachedGpuIndex = uint.MaxValue;
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
