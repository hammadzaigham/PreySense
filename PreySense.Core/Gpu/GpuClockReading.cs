namespace PreySense.Gpu;

public static class GpuClockReading
{
    /// <summary>RTSS-compatible nearest-integer MHz; absence stays unavailable.</summary>
    public static int? FromKilohertz(bool present, uint kilohertz) =>
        present && kilohertz > 0 ? (int)(((ulong)kilohertz + 500) / 1000) : null;
}
