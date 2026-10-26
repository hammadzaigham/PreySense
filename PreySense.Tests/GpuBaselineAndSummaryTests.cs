using PreySense.Core.Acpi;

namespace PreySense.Tests;

[TestClass]
public class GpuBaselineAndSummaryTests
{
    private static FactoryPowerProfileSnapshot Snapshot(bool tauRead = true) => new(
        new CpuPowerLimitConfiguration(), new GpuPowerLimitConfiguration(), 0x01, true, true, true, tauRead);

    [TestMethod]
    public void NvmlDefaultUnitsAreConvertedWithoutAssumingEightyFiveWatts()
    {
        Assert.AreEqual(85, GpuPowerBaseline.FromMilliwatts(85000));
        Assert.AreEqual(115, GpuPowerBaseline.FromMilliwatts(115000));
        Assert.AreEqual(175, GpuPowerBaseline.FromMilliwatts(175000));
        Assert.IsNull(GpuPowerBaseline.FromMilliwatts(0));
        Assert.IsNull(GpuPowerBaseline.FromMilliwatts(uint.MaxValue));
        Assert.IsNull(GpuPowerBaseline.VerifiedSavedWatts(5, null));
        Assert.IsNull(GpuPowerBaseline.VerifiedSavedWatts(5, "minimum power limit"));
        Assert.AreEqual(85, GpuPowerBaseline.VerifiedSavedWatts(85, GpuPowerBaseline.VerifiedDefaultSource));
        Assert.AreEqual(115, GpuPowerBaseline.VerifiedSavedWatts(115, GpuPowerBaseline.VerifiedDefaultSource));
        Assert.AreEqual(150, new GpuPowerOffsets(10, 25).NominalReferenceWatts(115));
        Assert.AreEqual(20, GpuPowerOffsets.FromLegacyTarget(150, 115).DeltaWatts);
        Assert.AreEqual(15, GpuPowerOffsets.FromLegacyTarget(150, 115).DynamicBoostWatts);
    }

    [TestMethod]
    public void IdenticalTauAndTripAppearOnlyInSharedRows()
    {
        var snapshot = Snapshot();
        snapshot.Cpu.EcoTauSeconds = snapshot.Cpu.QuietTauSeconds = snapshot.Cpu.BalancedTauSeconds = snapshot.Cpu.PerformanceTauSeconds = snapshot.Cpu.TurboTauSeconds = 56;
        string text = FactoryCalibrationSummary.Build(snapshot, 0x1F, 115);
        Assert.AreEqual(1, text.Split("Tau", StringSplitOptions.None).Length - 1);
        Assert.AreEqual(1, text.Split("GPU Thermal Trip Point", StringSplitOptions.None).Length - 1);
        Assert.Contains("GPU Baseline: 115W", text);
        Assert.DoesNotContain("Shared values", text);
        Assert.Contains("Profiles", text);
        Assert.Contains("Eco", text);
        Assert.Contains("Turbo", text);
        Assert.IsTrue(text.IndexOf("Turbo", StringComparison.Ordinal) < text.IndexOf("GPU Baseline", StringComparison.Ordinal));
        Assert.Contains("Tau: 56s", text);
    }

    [TestMethod]
    public void DifferentTauValuesRemainVisiblePerProfile()
    {
        var snapshot = Snapshot();
        snapshot.Cpu.QuietTauSeconds = 28;
        snapshot.Cpu.TurboTauSeconds = 56;
        string text = FactoryCalibrationSummary.Build(snapshot, 2 | 16, 85);
        Assert.Contains("Quiet: ", text);
        Assert.Contains("Tau 28", text);
        Assert.Contains("Tau 56", text);
        Assert.DoesNotContain("Tau: ", text);
    }

    [TestMethod]
    public void RawOffsetsUseDetectedBaselineAndCommonOffsetsAreNotRepeated()
    {
        var raw = new Dictionary<int, GpuPowerOffsets>
        {
            [1] = new(0, 15), [4] = new(30, 15)
        };
        string text = FactoryCalibrationSummary.Build(Snapshot(), 2 | 16, 115, raw);
        Assert.Contains("GPU 130W [Delta 0W · Dynamic Boost 15W]", text);
        Assert.Contains("GPU 160W [Delta 30W · Dynamic Boost 15W]", text);
        Assert.DoesNotContain("Factory platform offset", text);
        string unavailable = FactoryCalibrationSummary.Build(Snapshot(false), 0x1F, null);
        Assert.Contains("not detected", unavailable);
        Assert.AreEqual(1, unavailable.Split("Tau", StringSplitOptions.None).Length - 1);
    }
}
