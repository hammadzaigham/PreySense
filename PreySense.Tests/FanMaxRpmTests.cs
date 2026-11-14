using System.Text.Json;
using PreySense.Core.Configuration;
using PreySense.Mode;

namespace PreySense.Tests;

[TestClass]
[DoNotParallelize]
public class FanMaxRpmTests
{
    [TestMethod]
    public void MissingMaximumsKeep6400AndCustomValuesRoundTrip()
    {
        var defaults = JsonSerializer.Deserialize<FanConfig>("{}")!;
        Assert.AreEqual(6400, defaults.CpuMaxRpm);
        Assert.AreEqual(6400, defaults.GpuMaxRpm);
        var custom = JsonSerializer.Deserialize<FanConfig>("{\"CpuMaxRpm\":7000,\"GpuMaxRpm\":6000}")!;
        var restored = JsonSerializer.Deserialize<FanConfig>(JsonSerializer.Serialize(custom))!;
        Assert.AreEqual(7000, restored.CpuMaxRpm);
        Assert.AreEqual(6000, restored.GpuMaxRpm);
    }

    [TestMethod]
    public void SavedDutyUsesEachFansMaximumWithoutChangingItsPercentage()
    {
        var saved = new FanStageConfig { CpuTempC = 60, GpuTempC = 70, CpuDutyPercent = 50, GpuDutyPercent = 75 };
        var stage = saved.ToFanStage(7000, 6000);
        Assert.AreEqual(3500, stage.CpuTargetRpm);
        Assert.AreEqual(4500, stage.GpuTargetRpm);
        Assert.AreEqual(50, stage.CpuDutyPercent);
        Assert.AreEqual(75, stage.GpuDutyPercent);
        var copy = stage.Copy();
        Assert.AreEqual(7000, copy.CpuMaximumRpm);
        Assert.AreEqual(6000, copy.GpuMaximumRpm);
        copy.SetDuty(false, 100);
        Assert.AreEqual(7000, copy.CpuTargetRpm);
        Assert.AreEqual(50, new FanStageConfig(stage).CpuDutyPercent);
    }

    [TestMethod]
    public void StorageLoadsConfiguredMaximumsForBothRuntimeAndGraph()
    {
        var store = ConfigurationService.Instance;
        var original = store.GetCurrent();
        try
        {
            store.Update(cfg =>
            {
                cfg.Fans.CpuMaxRpm = 7000;
                cfg.Fans.GpuMaxRpm = 6000;
                cfg.Fans.Balanced.Stages = [
                    new() { CpuTempC = 60, GpuTempC = 70, CpuDutyPercent = 50, GpuDutyPercent = 25 },
                    new() { CpuTempC = 127, GpuTempC = 127, CpuDutyPercent = 100, GpuDutyPercent = 100 }];
            });
            var profile = FanCurveStorage.Load(1);
            Assert.AreEqual(7000, profile.CpuMaxRpm);
            Assert.AreEqual(6000, profile.GpuMaxRpm);
            Assert.AreEqual(3500, profile.Stages[0].CpuTargetRpm);
            Assert.AreEqual(1500, profile.Stages[0].GpuTargetRpm);
            Assert.AreEqual((50, 25, 0), new FanCurveFollower().Evaluate(40, 40, profile.Stages, synchronizeFanSpeeds: false));
            Assert.AreEqual((100, 100, 1), new FanCurveFollower().Evaluate(80, 80, profile.Stages, synchronizeFanSpeeds: false));
            var copied = profile.Copy();
            copied.Normalize(1);
            Assert.AreEqual(7000, copied.Stages[1].CpuTargetRpm);
            Assert.AreEqual(6000, copied.Stages[1].GpuTargetRpm);
        }
        finally { store.Save(original); }
    }

    [TestMethod]
    [DataRow(0, 6400)]
    [DataRow(6400, 0)]
    [DataRow(65536, 6400)]
    [DataRow(6400, -1)]
    public void InvalidMaximumsAreRejected(int cpu, int gpu)
    {
        var config = new PreySenseConfig();
        config.Fans.CpuMaxRpm = cpu;
        config.Fans.GpuMaxRpm = gpu;
        Assert.Throws<ArgumentOutOfRangeException>(() => ConfigurationService.Validate(config));
    }
}
