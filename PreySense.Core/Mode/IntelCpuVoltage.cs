using System;
using System.Runtime.Intrinsics.X86;

namespace PreySense.Mode;

/// <summary>Optional Arrow Lake MSR-reported VID, not measured motherboard Vcore.</summary>
public static class IntelCpuVoltage
{
    public static bool IsSupportedModel(int family, int model) => family == 6 && model is 0xC5 or 0xC6;

    public static bool IsSupportedCpu()
    {
        if (!X86Base.IsSupported) return false;
        var vendor = X86Base.CpuId(0, 0);
        if (vendor.Ebx != 0x756E6547 || vendor.Edx != 0x49656E69 || vendor.Ecx != 0x6C65746E) return false;
        int signature = X86Base.CpuId(1, 0).Eax;
        int family = (signature >> 8) & 15;
        int model = (signature >> 4) & 15;
        if (family == 6 || family == 15) model |= ((signature >> 16) & 15) << 4;
        if (family == 15) family += (signature >> 20) & 255;
        return IsSupportedModel(family, model);
    }

    public static float? DecodePerfStatus(ulong raw)
    {
        // IA32_PERF_STATUS[47:32], 1/8192 V. Never interpret low frequency/ratio
        // bits as voltage when the VID field is zero or unavailable.
        float volts = ((raw >> 32) & 0xFFFF) / 8192f;
        return volts > 0 && volts <= 2.5f ? volts : null;
    }
}
