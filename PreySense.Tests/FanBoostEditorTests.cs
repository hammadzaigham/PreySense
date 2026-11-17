using PreySense.Core.Configuration;
using PreySense.Frontend.Controls;
using PreySense.Frontend.Views;
using PreySense.Mode;

namespace PreySense.Tests;

[TestClass]
public class FanBoostEditorTests
{
    [TestMethod]
    public void RaisingEarlyZeroPointPersistsEveryRaisedWarmerPointForSelectedFan()
    {
        int[] values = [0, 0, 0];
        WpfFanCurveControl.SetDuty(values, 0, 40);
        CollectionAssert.AreEqual(new[] { 40, 40, 40 }, values);
        var stages = Enumerable.Range(0, 4).Select(_ => new FanStage()).ToArray();
        FanCurveWindow.ApplyGraphDuties(stages, [1, 2, 3], values, gpu: false);
        foreach (var stage in stages.Skip(1))
        {
            var saved = new FanStageConfig(stage);
            Assert.AreEqual(40, saved.CpuDutyPercent);
            Assert.AreEqual(0, saved.GpuDutyPercent);
        }
        Assert.AreEqual(0, stages[0].CpuDutyPercent);
    }

    [TestMethod]
    public void LoweringPointLowersEarlierPointsAndPreservesOtherFanWhenIndependent()
    {
        int[] values = [40, 60, 80];
        WpfFanCurveControl.SetDuty(values, 1, 20);
        CollectionAssert.AreEqual(new[] { 20, 20, 80 }, values);
        var stages = Enumerable.Range(0, 3).Select(_ => new FanStage()).ToArray();
        FanCurveWindow.ApplyGraphDuties(stages, [0, 1, 2], values, gpu: true);
        CollectionAssert.AreEqual(values, stages.Select(s => s.GpuDutyPercent).ToArray());
        Assert.IsTrue(stages.All(s => s.CpuDutyPercent == 0));
    }
}
