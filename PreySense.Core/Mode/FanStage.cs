namespace PreySense.Mode;

/// <summary>One EC-style fan gear: rise advances to the next stage; RPM targets for both fans.</summary>
public sealed class FanStage
{
    public const int CpuMaxRpm = 6400;
    public const int GpuMaxRpm = 6400;
    public const int DisabledTripCelsius = 127;

    public int CpuRiseC { get; set; }
    public int CpuFallC { get; set; }
    public int GpuRiseC { get; set; }
    public int GpuFallC { get; set; }
    public int CpuTargetRpm { get; set; }
    public int GpuTargetRpm { get; set; }

    public int CpuDutyPercent => RpmToDuty(CpuTargetRpm, CpuMaxRpm);
    public int GpuDutyPercent => RpmToDuty(GpuTargetRpm, GpuMaxRpm);
    /// <summary>Both fans run at this duty — the higher of the two stored targets.</summary>
    public int SharedDutyPercent => Math.Max(CpuDutyPercent, GpuDutyPercent);

    public void SetSharedDuty(int dutyPercent)
    {
        int rpm = DutyToRpm(dutyPercent, Math.Max(CpuMaxRpm, GpuMaxRpm));
        CpuTargetRpm = Math.Clamp(rpm, 0, CpuMaxRpm);
        GpuTargetRpm = Math.Clamp(rpm, 0, GpuMaxRpm);
    }

    /// <summary>True when every trip is the EC disabled sentinel (filler plateau stages).</summary>
    public bool IsDisabled =>
        CpuRiseC >= DisabledTripCelsius && CpuFallC >= DisabledTripCelsius
        && GpuRiseC >= DisabledTripCelsius && GpuFallC >= DisabledTripCelsius;

    public int RiseCelsius(bool gpu) => gpu ? GpuRiseC : CpuRiseC;

    public bool HasActiveRise(bool gpu) => RiseCelsius(gpu) < DisabledTripCelsius;

    public static int RpmToDuty(int rpm, int maxRpm) =>
        Math.Clamp((int)Math.Round(Math.Clamp(rpm, 0, maxRpm) * 100.0 / maxRpm, MidpointRounding.AwayFromZero), 0, 100);

    public static int DutyToRpm(int dutyPercent, int maxRpm) =>
        Math.Clamp((int)Math.Round(Math.Clamp(dutyPercent, 0, 100) * maxRpm / 100.0, MidpointRounding.AwayFromZero), 0, maxRpm);

    public FanStage Copy() => new()
    {
        CpuRiseC = CpuRiseC, CpuFallC = CpuFallC,
        GpuRiseC = GpuRiseC, GpuFallC = GpuFallC,
        CpuTargetRpm = CpuTargetRpm, GpuTargetRpm = GpuTargetRpm
    };

    public void Normalize()
    {
        CpuRiseC = ClampTrip(CpuRiseC);
        GpuRiseC = ClampTrip(GpuRiseC);
        // Fall is unused by the follower (retreat uses the previous rise). Keep storage
        // fields coherent: fall mirrors previous-rise semantics as rise - 1 when active.
        CpuFallC = DeriveFall(CpuRiseC, CpuFallC);
        GpuFallC = DeriveFall(GpuRiseC, GpuFallC);
        CpuTargetRpm = Math.Clamp(CpuTargetRpm, 0, CpuMaxRpm);
        GpuTargetRpm = Math.Clamp(GpuTargetRpm, 0, GpuMaxRpm);
    }

    /// <summary>
    /// Writes unique entry °C values onto the previous stage's rise for one sensor,
    /// ends the chain with 127 after the last visible stage, and derives fall as rise-1.
    /// </summary>
    public static void ApplyEntryTemperatures(
        FanStage[] stages,
        IReadOnlyList<int> stageIndices,
        IReadOnlyList<int> entryCelsius,
        bool gpu)
    {
        if (stages is null || stageIndices is null || entryCelsius is null) return;
        if (stageIndices.Count == 0 || stageIndices.Count != entryCelsius.Count) return;

        int[] temps = new int[entryCelsius.Count];
        for (int i = 0; i < entryCelsius.Count; i++)
            temps[i] = ClampTrip(entryCelsius[i]);
        EnforceStrictlyIncreasing(temps);

        for (int v = 0; v < stageIndices.Count; v++)
        {
            int stageIndex = stageIndices[v];
            if (stageIndex <= 0 || stageIndex >= stages.Length) continue;
            SetRise(stages[stageIndex - 1], gpu, temps[v]);
        }

        int lastVisible = stageIndices[^1];
        for (int i = lastVisible; i < stages.Length; i++)
            SetRise(stages[i], gpu, DisabledTripCelsius);

        // Derive fall from the rise ladder (storage only).
        for (int i = 0; i < stages.Length; i++)
        {
            int rise = stages[i].RiseCelsius(gpu);
            int fall = i == 0 || rise >= DisabledTripCelsius
                ? (rise >= DisabledTripCelsius ? DisabledTripCelsius : 0)
                : DeriveFallFromPreviousRise(stages[i - 1].RiseCelsius(gpu));
            SetFall(stages[i], gpu, fall);
            stages[i].Normalize();
        }
    }

    /// <summary>Keeps active rises strictly increasing so graph X values stay unique.</summary>
    public static int ClampRiseBetweenNeighbors(int riseC, int? previousRiseC, int? nextRiseC)
    {
        riseC = ClampTrip(riseC);
        if (riseC >= DisabledTripCelsius)
            return DisabledTripCelsius;
        int min = previousRiseC is int prev && prev < DisabledTripCelsius ? prev + 1 : 0;
        int max = nextRiseC is int next && next < DisabledTripCelsius ? next - 1 : DisabledTripCelsius - 1;
        if (min > max) return Math.Clamp(min, 0, DisabledTripCelsius - 1);
        return Math.Clamp(riseC, min, max);
    }

    public static void EnforceStrictlyIncreasing(int[] temperatures)
    {
        if (temperatures is null || temperatures.Length == 0) return;
        temperatures[0] = ClampTrip(temperatures[0]);
        if (temperatures[0] >= DisabledTripCelsius)
            temperatures[0] = DisabledTripCelsius - 1;
        for (int i = 1; i < temperatures.Length; i++)
        {
            temperatures[i] = ClampTrip(temperatures[i]);
            if (temperatures[i] >= DisabledTripCelsius)
                temperatures[i] = DisabledTripCelsius - 1;
            if (temperatures[i] <= temperatures[i - 1])
                temperatures[i] = Math.Min(DisabledTripCelsius - 1, temperatures[i - 1] + 1);
        }
    }

    private static int DeriveFall(int riseC, int fallC)
    {
        if (riseC >= DisabledTripCelsius) return DisabledTripCelsius;
        fallC = ClampTrip(fallC);
        if (fallC >= DisabledTripCelsius) return Math.Max(0, riseC - 1);
        if (fallC >= riseC) return Math.Max(0, riseC - 1);
        return fallC;
    }

    private static int DeriveFallFromPreviousRise(int previousRiseC)
    {
        if (previousRiseC >= DisabledTripCelsius) return DisabledTripCelsius;
        return Math.Max(0, previousRiseC - 1);
    }

    private static void SetRise(FanStage stage, bool gpu, int riseC)
    {
        if (gpu) stage.GpuRiseC = riseC;
        else stage.CpuRiseC = riseC;
    }

    private static void SetFall(FanStage stage, bool gpu, int fallC)
    {
        if (gpu) stage.GpuFallC = fallC;
        else stage.CpuFallC = fallC;
    }

    private static int ClampTrip(int celsius) => Math.Clamp(celsius, 0, DisabledTripCelsius);
}
