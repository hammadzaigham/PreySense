using PreySense.Core.Driver;

namespace PreySense.Tests;

[TestClass]
public class GpuBaselineAndSummaryTests
{
    [TestMethod]
    public void NvmlDefaultUnitsAreConvertedWithoutAssumingEightyFiveWatts()
    {
        Assert.AreEqual(85, GpuPowerBaseline.FromMilliwatts(85000));
        Assert.AreEqual(115, GpuPowerBaseline.FromMilliwatts(115000));
        Assert.AreEqual(175, GpuPowerBaseline.FromMilliwatts(175000));
        Assert.IsNull(GpuPowerBaseline.FromMilliwatts(0));
        Assert.IsNull(GpuPowerBaseline.FromMilliwatts(uint.MaxValue));
        Assert.IsNull(GpuPowerBaseline.VerifiedSavedWatts(null));
        Assert.IsNull(GpuPowerBaseline.VerifiedSavedWatts(0));
        Assert.AreEqual(85, GpuPowerBaseline.VerifiedSavedWatts(85));
        Assert.AreEqual(115, GpuPowerBaseline.VerifiedSavedWatts(115));
        Assert.AreEqual(150, new GpuPowerOffsets(10, 25).NominalReferenceWatts(115));
        Assert.AreEqual(20, GpuPowerOffsets.FromLegacyTarget(150, 115).DeltaWatts);
        Assert.AreEqual(15, GpuPowerOffsets.FromLegacyTarget(150, 115).DynamicBoostWatts);
    }

}
