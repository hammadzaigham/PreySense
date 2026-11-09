using System.Text.Json;
using PreySense.Core.Configuration;
using PreySense.Frontend.Views;
using PreySense.Mode;

namespace PreySense.Tests;

[TestClass]
public class FanCurveStorageTests
{
    [TestMethod]
    [DataRow((byte)0, 7)]
    [DataRow((byte)1, 7)]
    [DataRow((byte)4, 7)]
    [DataRow((byte)5, 8)]
    [DataRow((byte)6, 7)]
    public void CompactStagesPreserveEveryReachableDuty(byte mode, int count)
    {
        var original = FanCurveProfile.CreateDefault(mode);
        var config = new ModeFanCurveConfig
        {
            Stages = original.Stages.Select(stage => new FanStageConfig(stage)).ToArray()
        };
        Assert.HasCount(count, config.Stages!);
        var roundTrip = JsonSerializer.Deserialize<ModeFanCurveConfig>(JsonSerializer.Serialize(config))!;
        var restored = new FanCurveProfile { Stages = roundTrip.Stages!.Select(stage => stage.ToFanStage()).ToArray() };
        restored.Normalize(mode);
        Assert.HasCount(FanCurveProfile.StageCount, restored.Stages);
        var before = new FanCurveFollower();
        var after = new FanCurveFollower();
        foreach (int cpu in new[] { 20, 40, 55, 70, 85, 100, 125, 80, 40 })
            foreach (int gpu in new[] { 20, 40, 55, 70, 85, 100, 125, 80, 40 })
                Assert.AreEqual(before.Evaluate(cpu, gpu, original.Stages),
                    after.Evaluate(cpu, gpu, restored.Stages), $"CPU={cpu}, GPU={gpu}");
    }

    [TestMethod]
    public void GraphCreationForBothTabsDoesNotModifyTheCurve()
    {
        var profile = FanCurveProfile.CreateDefault(0);
        string before = JsonSerializer.Serialize(profile.Stages);
        var cpu = FanCurveWindow.BuildGraph(profile.Stages, gpu: false);
        var gpu = FanCurveWindow.BuildGraph(profile.Stages, gpu: true);
        Assert.HasCount(5, cpu.StageIndices);
        Assert.HasCount(6, gpu.StageIndices);
        Assert.AreEqual(before, JsonSerializer.Serialize(profile.Stages));
    }

    [TestMethod]
    public void GraphIncludesAReachableLastSlot()
    {
        var stages = new[]
        {
            new FanStage { CpuRiseC = 60, GpuRiseC = 60 },
            new FanStage { CpuRiseC = 127, GpuRiseC = 127, CpuTargetRpm = 6400 }
        };
        var graph = FanCurveWindow.BuildGraph(stages, gpu: false);
        CollectionAssert.AreEqual(new[] { 1 }, graph.StageIndices);
        CollectionAssert.AreEqual(new[] { 100 }, graph.Duties);
    }

    [TestMethod]
    public void ShortCurveCannotReactivateFactoryTailTransitions()
    {
        var profile = new FanCurveProfile
        {
            Stages = new[] { new FanStage { CpuRiseC = 60, GpuRiseC = 60 } }
        };
        profile.Normalize(5);
        var result = new FanCurveFollower().Evaluate(125, 125, profile.Stages);
        Assert.AreEqual(0, result.Stage);
    }
}
