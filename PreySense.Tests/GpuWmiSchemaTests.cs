using PreySense.Core.Hardware;

namespace PreySense.Tests;

[TestClass]
public class GpuWmiSchemaTests
{
    [TestMethod]
    public void CurrentSchemaAcceptsReorderedParameters()
    {
        Assert.IsTrue(PreySenseWmiDeviceClient.GpuOffsetSchemaMatches(
            ["ReturnCode", "DynamicBoostWatts", "DeltaWatts"],
            ["DynamicBoostWatts", "DeltaWatts"], ["ReturnCode"]));
    }

    [TestMethod]
    public void OldPlatformParameterCannotPassCurrentVersionValidation()
    {
        string[] read = ["DeltaWatts", "DynamicBoostWatts", "ReturnCode"];
        string[] write = ["DeltaWatts", "DynamicBoostWatts"];
        Assert.IsFalse(PreySenseWmiDeviceClient.GpuOffsetSchemaMatches(
            [.. read, "PlatformOffsetWatts"], write, ["ReturnCode"]));
        Assert.IsFalse(PreySenseWmiDeviceClient.GpuOffsetSchemaMatches(
            read, [.. write, "PlatformOffsetWatts"], ["ReturnCode"]));
        Assert.IsFalse(PreySenseWmiDeviceClient.GpuOffsetSchemaMatches(
            ["DeltaWatts", "ReturnCode"], write, ["ReturnCode"]));
    }
}
