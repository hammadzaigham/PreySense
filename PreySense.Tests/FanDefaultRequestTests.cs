using System.Text.Json;
using PreySense.Core.Configuration;
using PreySense.Mode;

namespace PreySense.Tests;

[TestClass]
public class FanDefaultRequestTests
{
    [TestMethod]
    [DataRow((byte)0)]
    [DataRow((byte)1)]
    [DataRow((byte)4)]
    [DataRow((byte)5)]
    [DataRow((byte)6)]
    public void DefaultAndResetRequestsAreZeroAtEveryStage(byte mode)
    {
        var reset = FanCurveProfile.CreateDefault(mode);
        Assert.AreEqual(FanControlMode.Auto, reset.ControlMode);
        Assert.HasCount(FanCurveProfile.StageCount, reset.Stages);
        AssertZeroRequests(reset.Stages);
        // Hot samples must not reinterpret factory reference RPMs as percentages.
        var follower = new FanCurveFollower();
        foreach (int temperature in new[] { 35, 55, 70, 90, 125 })
        {
            var (cpu, gpu, _) = follower.Evaluate(temperature, temperature, reset.Stages);
            Assert.AreEqual(0, cpu);
            Assert.AreEqual(0, gpu);
        }
    }

    [TestMethod]
    public void ConstructorAndMissingStageFallbackDoNotIntroduceExtraCoolingRequests()
    {
        AssertZeroRequests(new FanCurveProfile().Stages);
        var missing = new FanCurveProfile { Stages = null! };
        missing.Normalize(5);
        Assert.HasCount(FanCurveProfile.StageCount, missing.Stages);
        AssertZeroRequests(missing.Stages);
    }

    [TestMethod]
    public void NewConfigurationPersistsZeroRequestsAcrossEveryPerformanceMode()
    {
        var config = new PreySenseConfig();
        foreach (var mode in new[] { config.Fans.Quiet, config.Fans.Eco, config.Fans.Balanced,
                     config.Fans.Performance, config.Fans.Turbo })
        {
            Assert.IsNotNull(mode.Stages);
            Assert.IsNotEmpty(mode.Stages);
            foreach (var stage in mode.Stages)
            {
                Assert.AreEqual(0, stage.CpuDutyPercent);
                Assert.AreEqual(0, stage.GpuDutyPercent);
            }
        }
    }

    [TestMethod]
    public void LoadingExistingCustomStagesKeepsSavedPercentagesAndTerminalPlateau()
    {
        const string saved = """
            {"ControlMode":2,"Stages":[
              {"CpuTempC":60,"GpuTempC":55,"CpuDutyPercent":23,"GpuDutyPercent":41},
              {"CpuTempC":127,"GpuTempC":127,"CpuDutyPercent":67,"GpuDutyPercent":89}]}
            """;
        var stored = JsonSerializer.Deserialize<ModeFanCurveConfig>(saved)!;
        var restored = new FanCurveProfile
        {
            ControlMode = (FanControlMode)stored.ControlMode,
            Stages = stored.Stages!.Select(stage => stage.ToFanStage()).ToArray()
        };
        restored.Normalize(5);
        Assert.AreEqual(FanControlMode.Custom, restored.ControlMode);
        Assert.AreEqual(23, restored.Stages[0].CpuDutyPercent);
        Assert.AreEqual(41, restored.Stages[0].GpuDutyPercent);
        Assert.AreEqual(67, restored.Stages[1].CpuDutyPercent);
        Assert.AreEqual(89, restored.Stages[1].GpuDutyPercent);
        foreach (var stage in restored.Stages.Skip(1))
        {
            Assert.AreEqual(67, stage.CpuDutyPercent);
            Assert.AreEqual(89, stage.GpuDutyPercent);
            Assert.IsFalse(stage.HasActiveRise(false));
            Assert.IsFalse(stage.HasActiveRise(true));
        }
        var roundTrip = JsonSerializer.Deserialize<ModeFanCurveConfig>(
            JsonSerializer.Serialize(new ModeFanCurveConfig
            {
                ControlMode = 2,
                Stages = restored.Stages.Select(stage => new FanStageConfig(stage)).ToArray()
            }))!;
        Assert.HasCount(2, roundTrip.Stages!);
        Assert.AreEqual(67, roundTrip.Stages![1].CpuDutyPercent);
        Assert.AreEqual(89, roundTrip.Stages[1].GpuDutyPercent);
    }

    [TestMethod]
    public void ResetRequestsDoNotMutateFactoryReferenceData()
    {
        var before = FanCurveProfile.DefaultStages(1);
        var reset = FanCurveProfile.CreateDefault(1);
        reset.Stages[1].CpuTargetRpm = 0;
        var after = FanCurveProfile.DefaultStages(1);
        Assert.AreEqual(1800, before[1].CpuTargetRpm);
        Assert.AreEqual(1800, after[1].CpuTargetRpm);
        Assert.AreNotSame(before[1], after[1]);
    }

    private static void AssertZeroRequests(FanStage[] stages)
    {
        foreach (var stage in stages)
        {
            Assert.AreEqual(0, stage.CpuDutyPercent);
            Assert.AreEqual(0, stage.GpuDutyPercent);
        }
    }
}
