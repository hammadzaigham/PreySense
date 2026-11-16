using System.Collections.ObjectModel;

namespace PreySense.Mode;

/// <summary>
/// Read-only offline reference for the PHN16S-71 ZBJ-KQ EC 1.08 image.
/// This does not detect the running firmware, select a hardware variant, or control fans.
/// </summary>
public static class FactoryFanReference
{
    public const string Model = "Predator PHN16S-71";
    public const string EcVersion = "1.08";
    public const string EcBuild = "2025-12-29";
    public const string SourceSha256 = "5ed20cd688e448aac0152aea8a6d2b0d320186ab0552f0455d5a468398e0adba";
    public const int DisabledTransitionCelsius = 127;

    private static readonly ReadOnlyCollection<FactoryFanTable> Tables = LoadTables();

    public static IReadOnlyList<FactoryFanTable> AllTables => Tables;

    /// <summary>Variant is an explicit reference selection, never a detected machine setting.</summary>
    public static FactoryFanTable GetTable(byte wmiMode, int variantBit = 0)
    {
        if (variantBit is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(variantBit));
        int table = wmiMode switch
        {
            5 => 0, 4 => 1, 1 => 2, 0 => 3, 6 => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(wmiMode))
        };
        return Tables[variantBit * 5 + table];
    }

    private static ReadOnlyCollection<FactoryFanTable> LoadTables()
    {
        // Original 11-byte records, including counters, filter flags and terminal/max slots.
        // Verified against source SHA above; EC payload base 0x1E6B50, table base 0x10190.
        string[] rawTables =
        [
        "00ff37003400000000000101053c2837280866086601020541383a35092e092e010305463d3d3809f609f60104054a42403b0c6c0c620105ff4e47433e0f6e0f3c0106ff5f4b5641135613560107ff7f557f4e157c157c0108ff7f7f7f7f157c157c0009ff7f7f7f7f157c157c000aff7f7f7f7f157c157c000bff7f7f7f7f1a90190000", // Turbo, variant 0
        "00ff32003200000000000101053628382807b207bc0102053c333d33092e092e0103054137423909f609f6010405463d473e0c6c0c620105ff4b424c430f3c0f3c0106ff7f477f4810fe10fe0107ff7f7f7f7f10fe10fe0008ff7f7f7f7f10fe10fe0009ff7f7f7f7f10fe10fe000aff7f7f7f7f10fe10fe000bff7f7f7f7f1a90190000", // Performance, variant 0
        "00ff3800320000000000010105412838280708073a01020548373e3307b207bc0103054e404439092e092e0104055447493f09f609f60105ff594d4d450c6c0c620106ff7f537f4a0e100e100107ff7f7f7f7f0e100e100008ff7f7f7f7f0e100e100009ff7f7f7f7f0e100e10000aff7f7f7f7f0e100e10000bff7f7f7f7f1a90190000", // Balanced, variant 0
        "00ff390039000000000001010540283e280708073a0102054638423807b207bc0103054c41473d0866086601040551474b41092e092e0105ff7f4d504609f609f60106ff7f7f7f4a0b220b220107ff7f7f7f7f0b220b220008ff7f7f7f7f0b220b220009ff7f7f7f7f0b220b22000aff7f7f7f7f0b220b22000bff7f7f7f7f1a90190000", // Quiet, variant 0
        "00ff390039000000000001010540283e280708073a0102054638423807b207bc0103054c41473d0866086601040551474b41092e092e0105ff7f4d504609f609f60106ff7f7f7f4a0b220b220107ff7f7f7f7f0b220b220008ff7f7f7f7f0b220b220009ff7f7f7f7f0b220b22000aff7f7f7f7f0b220b22000bff7f7f7f7f1a90190000", // Eco, variant 0
        "00ff37003400000000000101053c32372f0780076201020541383a350848085c010305463d3d3808de08f20104054a42403b0ac80af00105ff4e47433e0dac0e100106ff5f4b5641119411e40107ff7f557f5212f213240108ff7f7f7f7f12f213240009ff7f7f7f7f12f21324000aff7f7f7f7f12f21324000bff7f7f7f7f12f2132400", // Turbo, variant 1
        "00ff3000370000000000010105342d3a3506e006cc01020538313d38078007620103053d35403b0848085c0104054139433e08de08f20105ff463e46410ac80af00106ff4b424a440dac0e100107ff55475647102210400108ff7f4c7f4c119411e40109ff7f7f7f7f119411e4000aff7f7f7f7f119411e4000bff7f7f7f7f12f2132400", // Performance, variant 1
        "00ff3800320000000000010105402d352f0668066801020547373b3106e006cc0103054c3f41340848085c0104055146473a08de08f20105ff564b4e400ac80af00106ff7f507f460c580c6c0107ff7f7f7f7f0c580c6c0008ff7f7f7f7f0c580c6c0009ff7f7f7f7f0c580c6c000aff7f7f7f7f0c580c6c000bff7f7f7f7f12f2132400", // Balanced, variant 1
        "00ff390039000000000001010540283e28066806680102054638423806e006cc0103054c41473d0780076201040551474b410848085c0105ff7f4d504608de08f20106ff7f7f7f4a09f609d80107ff7f7f7f7f09f609d80008ff7f7f7f7f09f609d80009ff7f7f7f7f09f609d8000aff7f7f7f7f09f609d8000bff7f7f7f7f12f2132400", // Quiet, variant 1
        "00ff390039000000000001010540283e28066806680102054638423806e006cc0103054c41473d0780076201040551474b410848085c0105ff7f4d504608de08f20106ff7f7f7f4a09f609d80107ff7f7f7f7f09f609d80008ff7f7f7f7f09f609d80009ff7f7f7f7f09f609d8000aff7f7f7f7f09f609d8000bff7f7f7f7f12f2132400", // Eco, variant 1
        ];
        string[] names = ["Turbo", "Performance", "Balanced", "Quiet", "Eco"];
        byte[] modes = [5, 4, 1, 0, 6];
        var tables = new FactoryFanTable[rawTables.Length];
        for (int table = 0; table < rawTables.Length; table++)
        {
            byte[] bytes = Convert.FromHexString(rawTables[table]);
            var stages = new FactoryFanStage[12];
            for (int i = 0; i < stages.Length; i++)
            {
                int p = i * 11;
                stages[i] = new(bytes[p], bytes[p + 1], bytes[p + 2], bytes[p + 3],
                    bytes[p + 4], bytes[p + 5], bytes[p + 6] * 256 + bytes[p + 7],
                    bytes[p + 8] * 256 + bytes[p + 9], bytes[p + 10],
                    Convert.ToHexString(bytes.AsSpan(p, 11)).ToLowerInvariant());
            }
            int mode = table % 5;
            tables[table] = new FactoryFanTable(names[mode], modes[mode], table / 5,
                mode, 0x10190 + table * 132, Array.AsReadOnly(stages));
        }
        return Array.AsReadOnly(tables);
    }
}

/// <summary>Original EC row; rising thresholds leave this row for the next stage.</summary>
public sealed record FactoryFanStage(int Index, int TransitionCounterRaw,
    int CpuRiseC, int CpuFallC, int GpuRiseC, int GpuFallC,
    int CpuTargetRpm, int GpuTargetRpm, int FilterFlagRaw, string RawHex);

public sealed class FactoryFanTable
{
    internal FactoryFanTable(string modeName, byte wmiMode, int variantBit, int ecModeCode,
        int ecImageOffset, IReadOnlyList<FactoryFanStage> stages)
    {
        ModeName = modeName;
        WmiMode = wmiMode;
        VariantBit = variantBit;
        EcModeCode = ecModeCode;
        EcImageOffset = ecImageOffset;
        Stages = stages;
    }

    public string ModeName { get; }
    public byte WmiMode { get; }
    public int VariantBit { get; }
    public int EcModeCode { get; }
    public int EcImageOffset { get; }
    public IReadOnlyList<FactoryFanStage> Stages { get; }

    /// <summary>
    /// Reference arithmetic decoded at EC payload 0x11466 / 0x21285 for this image only.
    /// Acer Custom adds a percentage of remaining RPM headroom to the factory stage target.
    /// This is a target estimate, not measured RPM, direct PWM duty, or a portable hardware API.
    /// </summary>
    public int EstimateCustomTargetRpm(int stage, int percent, bool gpu = false)
    {
        if ((uint)stage >= (uint)Stages.Count) throw new ArgumentOutOfRangeException(nameof(stage));
        if (percent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(percent));
        int baseline = gpu ? Stages[stage].GpuTargetRpm : Stages[stage].CpuTargetRpm;
        int maximum = gpu ? Stages[^1].GpuTargetRpm : Stages[^1].CpuTargetRpm;
        return baseline + (maximum - baseline) * percent / 100;
    }
}
