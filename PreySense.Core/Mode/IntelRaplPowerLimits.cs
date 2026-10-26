using System;

namespace PreySense.Mode;

/// <summary>Intel package-limit bit encoding, separated from privileged I/O.</summary>
public static class IntelRaplPowerLimits
{
    public const ulong LockBit = 1UL << 63;
    public const ulong WritableMask = 0x0001FFFF00FFFFFFUL;

    public static bool TryBuild(ulong current, ulong units, int pl1Watts, int pl2Watts,
        bool pl2Enable, int tauSeconds, out ulong target, out string error)
    {
        target = current;
        error = string.Empty;
        if ((current & LockBit) != 0) { error = "MSR 0x610 is firmware locked (bit 63)."; return false; }
        double powerUnit = Math.Pow(2, -(int)(units & 0xF));
        double pl1 = Math.Round(pl1Watts / powerUnit);
        double pl2 = Math.Round(pl2Watts / powerUnit);
        if (pl1Watts <= 0 || pl2Watts <= 0 || pl1 > 0x7FFF || pl2 > 0x7FFF)
        { error = "Requested watts exceed the MSR power-field range."; return false; }
        if (tauSeconds < 0) { error = "Tau must not be negative."; return false; }
        byte tau = EncodeTau(tauSeconds, units);
        target = current & ~WritableMask;
        target |= (ulong)pl1 | (1UL << 15) | (1UL << 16) | ((ulong)tau << 17);
        target |= (ulong)pl2 << 32;
        if (pl2Enable) target |= (1UL << 47) | (1UL << 48);
        return true;
    }

    public static byte EncodeTau(int seconds, ulong units)
    {
        if (seconds >= PreySense.Core.Acpi.CpuPowerLimitConfiguration.InfiniteTauSliderValue) return 0x7F;
        double timeUnit = Math.Pow(2, -(int)((units >> 16) & 0xF));
        double bestError = double.MaxValue;
        byte best = 0;
        for (int encoded = 0; encoded < 128; encoded++)
        {
            double actual = Math.Pow(2, encoded & 31) * (1 + ((encoded >> 5) & 3) / 4.0) * timeUnit;
            double error = Math.Abs(actual - seconds);
            if (error < bestError) { bestError = error; best = (byte)encoded; }
        }
        return best;
    }
}
