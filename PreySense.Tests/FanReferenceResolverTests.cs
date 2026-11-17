using PreySense.Mode;

namespace PreySense.Tests;

[TestClass]
public class FanReferenceResolverTests
{
    [TestMethod]
    public void TemperatureEstimateUsesBundledReferenceCurve()
    {
        var source = FanReferenceResolver.BundledFallback;
        Assert.AreEqual(3902, source.EstimateAtTemperature(1, 65, 40).MinimumRpm);
    }
    [TestMethod]
    public void TemperatureEstimateUsesFactoryRisesAndAllowsUnknownCoupledSensor()
    {
        var source = FanReferenceResolver.BundledFallback;
        var cool = source.EstimateAtTemperature(1, 40, 40);
        Assert.AreEqual(2720, cool.MinimumRpm);
        Assert.AreEqual(4880, cool.MaximumRpm);
        var warm = source.EstimateAtTemperature(1, 70, 40);
        Assert.AreEqual(3902, warm.MinimumRpm);
        Assert.AreEqual(4880, warm.MaximumRpm);
        var hot = source.EstimateAtTemperature(1, 90, 40);
        Assert.AreEqual(4880, hot.MinimumRpm);
        Assert.AreEqual(4880, hot.MaximumRpm);
        var gpu = source.EstimateAtTemperature(1, 80, 40, gpu: true);
        Assert.AreEqual(4720, gpu.MinimumRpm);
        Assert.AreEqual(4720, gpu.MaximumRpm);
        Assert.IsTrue(gpu.IsApproximate);
        StringAssert.Contains(gpu.Description, "unknown");
    }

    [TestMethod]
    public void RangeExcludesUnreachableMaxSlotButRetainsItAsInterpolationEndpoint()
    {
        var source = FanReferenceResolver.BundledFallback;
        var zero = source.EstimateRange(1, 0);
        Assert.AreEqual(0, zero.MinimumRpm);
        Assert.AreEqual(3600, zero.MaximumRpm);
        var maximum = source.EstimateRange(1, 100);
        Assert.AreEqual(6800, maximum.MinimumRpm);
        Assert.AreEqual(6800, maximum.MaximumRpm);
        var variant = source.EstimateRange(1, 100, gpu: true, variantBit: 1);
        Assert.AreEqual(4900, variant.MinimumRpm);
        Assert.AreEqual(4900, variant.MaximumRpm);
    }

}
