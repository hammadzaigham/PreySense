using System.Runtime.Intrinsics.X86;

namespace PreySense.Mode;

public static class IntelCpuTemperature
{
    public static bool IsSupportedCpu()
    {
        if (!X86Base.IsSupported) return false;
        var vendor = X86Base.CpuId(0, 0);
        if (vendor.Ebx != 0x756E6547 || vendor.Edx != 0x49656E69 || vendor.Ecx != 0x6C65746E || vendor.Eax < 6) return false;
        return (X86Base.CpuId(6, 0).Eax & (1 << 6)) != 0;
    }

    public static int? DecodePackageTemperature(ulong target, ulong thermalStatus)
    {
        if ((thermalStatus & (1UL << 31)) == 0) return null;
        int tjMax = (int)((target >> 16) & 0xFF);
        int distance = (int)((thermalStatus >> 16) & 0x7F);
        if (tjMax is < 50 or > 125) return null;
        int temperature = tjMax - distance;
        return temperature is > 0 and <= 125 ? temperature : null;
    }
}
