using System;
using System.Runtime.InteropServices;

namespace PreySense.Gpu;

/// <summary>mVolt-aligned read-only thermal queries. Caller must verify D0 before invoking.</summary>
public static class NvapiThermal
{
    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr Query(uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Initialize();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Enumerate([In, Out] IntPtr[] handles, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Read(IntPtr gpu, [In, Out] byte[] buffer);
    private static readonly object Gate = new();
    private static readonly byte[] HotspotRequest = new byte[0x1808];
    private static readonly byte[] ChannelRequest = new byte[0x34c8];
    private static readonly uint[] SensorIds = { 0xad0a90, 0xad0a94, 0xad0a98, 0xad0a9c, 0xad0aa0, 0xad0aa4 };
    private static bool _initialized;
    private static IntPtr _gpu;
    private static Read? _readSensors, _readChannels;

    private static void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;
        try
        {
            var initialize = Query(0x0150e828);
            var enumerate = Query(0xe5ac921f);
            if (initialize == IntPtr.Zero || enumerate == IntPtr.Zero || Marshal.GetDelegateForFunctionPointer<Initialize>(initialize)() != 0) return;
            var handles = new IntPtr[64];
            if (Marshal.GetDelegateForFunctionPointer<Enumerate>(enumerate)(handles, out int count) != 0 || count == 0) return;
            _gpu = handles[0];
            var sensors = Query(0x2eb3c140);
            var channels = Query(0x65fe3aad);
            if (sensors != IntPtr.Zero) _readSensors = Marshal.GetDelegateForFunctionPointer<Read>(sensors);
            if (channels != IntPtr.Zero) _readChannels = Marshal.GetDelegateForFunctionPointer<Read>(channels);
        }
        catch (Exception ex) { AppLogger.Log($"NVAPI thermal initialization failed: {ex.Message}"); }
    }

    // mVolt RVA 0x1ddcf0: bit 30 marks a valid fixed-point sensor sample.
    public static float? DecodeHotspotSample(uint raw) =>
        (raw & 0x40000000) != 0 && (raw & 0xffff0000) != 0xbadf0000 &&
        (raw & 0x3fffffff) is > 0 and < 38400 ? (raw & 0x3fffffff) / 256f : null;

    public static (float? HotspotC, float? MemoryC) ReadTemperatures(IntPtr? explicitHandle = null,
        bool readHotspot = true, bool readMemory = true)
    {
        lock (Gate)
        {
            EnsureInitialized();
            IntPtr gpu = explicitHandle.GetValueOrDefault();
            if (gpu == IntPtr.Zero) return (null, null);
            float? hotspot = null, memory = null;
            try
            {
                if (readHotspot && _readSensors != null)
                {
                    Array.Clear(HotspotRequest);
                    BitConverter.TryWriteBytes(HotspotRequest.AsSpan(), 0x11808u);
                    BitConverter.TryWriteBytes(HotspotRequest.AsSpan(4), SensorIds.Length);
                    for (int i = 0; i < SensorIds.Length; i++)
                    {
                        int offset = 8 + i * 24;
                        BitConverter.TryWriteBytes(HotspotRequest.AsSpan(offset), (ushort)0x15);
                        BitConverter.TryWriteBytes(HotspotRequest.AsSpan(offset + 4), SensorIds[i]);
                    }
                    // Exact read request built at mVolt RVA 0x1dda50; selected sample is entry 4 (RVA 0x2271ab).
                    if (_readSensors(gpu, HotspotRequest) == 0 && BitConverter.ToUInt32(HotspotRequest, 0) == 0x11808 &&
                        BitConverter.ToUInt32(HotspotRequest, 4) == SensorIds.Length)
                    {
                        const int entry = 8 + 4 * 24;
                        if (BitConverter.ToUInt16(HotspotRequest, entry) == 0x15 &&
                            BitConverter.ToUInt16(HotspotRequest, entry + 2) == 0 &&
                            BitConverter.ToUInt32(HotspotRequest, entry + 4) == SensorIds[4] &&
                            BitConverter.ToUInt64(HotspotRequest, entry + 8) == 0 &&
                            BitConverter.ToUInt64(HotspotRequest, entry + 16) <= uint.MaxValue)
                            hotspot = DecodeHotspotSample(BitConverter.ToUInt32(HotspotRequest, entry + 16));
                    }
                }
                if (readMemory && _readChannels != null)
                {
                    Array.Clear(ChannelRequest);
                    BitConverter.TryWriteBytes(ChannelRequest.AsSpan(), 0x334c8u);
                    BitConverter.TryWriteBytes(ChannelRequest.AsSpan(8), 6u);
                    // mVolt RVA 0x212087 -> result +0x20 -> renderer 0x804d2: MEMORY JUNCTION.
                    if (_readChannels(gpu, ChannelRequest) == 0 && BitConverter.ToUInt32(ChannelRequest, 0) == 0x334c8 &&
                        BitConverter.ToUInt32(ChannelRequest, 4) >= 2)
                    {
                        int raw = BitConverter.ToInt32(ChannelRequest, 0x160);
                        if (raw is > 0 and < 38400 && raw != 0xff00) memory = raw / 256f;
                    }
                }
            }
            catch (Exception ex) { AppLogger.Log($"NVAPI thermal read failed: {ex.Message}"); }
            return (hotspot, memory);
        }
    }
    public static float? GetHotspot(IntPtr? handle = null) => ReadTemperatures(handle, true, false).HotspotC;
    public static float? GetMemoryTemperature(IntPtr? handle = null) => ReadTemperatures(handle, false, true).MemoryC;
}
