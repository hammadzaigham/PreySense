namespace PreySense.Gpu;

public readonly record struct GpuClockOffsets(int CoreMhz, int MemoryMhz);

public readonly record struct GpuClockRange(int MinimumMhz, int MaximumMhz)
{
    public bool Contains(int value) => value >= MinimumMhz && value <= MaximumMhz;

    public GpuClockRange ForStep(int step) => new(
        (int)Math.Ceiling(MinimumMhz / (double)step) * step,
        (int)Math.Floor(MaximumMhz / (double)step) * step);

    public int Snap(int value, int step)
    {
        var range = ForStep(step);
        return Math.Clamp((int)Math.Round(value / (double)step, MidpointRounding.AwayFromZero) * step,
            range.MinimumMhz, range.MaximumMhz);
    }

    // Round inwards: an integer MHz setting must remain inside the native kHz range.
    public static GpuClockRange FromKilohertz(int minimum, int maximum, int? maximumMhz = null) =>
        new((int)Math.Ceiling(minimum / 1000.0),
            Math.Min((int)Math.Floor(maximum / 1000.0), maximumMhz ?? int.MaxValue));
}

public sealed record GpuClockTuningState(GpuClockRange Core, GpuClockRange Memory, GpuClockOffsets Current)
{
    public const int MaximumCoreOffsetMhz = 500;
    public const int CoreStepMhz = 5;
    public const int MemoryStepMhz = 100;
    public int CurrentCoreKilohertz { get; init; }
    public int CurrentMemoryKilohertz { get; init; }
    public bool Allows(GpuClockOffsets offsets) => offsets.CoreMhz >= 0 && offsets.MemoryMhz >= 0
        && Core.Contains(offsets.CoreMhz)
        && Memory.Contains(offsets.MemoryMhz) && offsets.CoreMhz <= MaximumCoreOffsetMhz;
}

public interface IGpuClockTuning
{
    bool TryReadClockTuning(out GpuClockTuningState? state, out string status);
    bool TryApplyClockOffsets(GpuClockOffsets offsets, out string status);
}
