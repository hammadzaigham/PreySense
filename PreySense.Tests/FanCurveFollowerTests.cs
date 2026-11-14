using PreySense.Mode;

namespace PreySense.Tests;

[TestClass]
public class FanCurveFollowerTests
{
    private static FanStage[] Balanced() => FanCurveProfile.DefaultStages(1);

    [TestMethod]
    public void UnsynchronizedFansUseTheirOwnTemperatureAndDelay()
    {
        var stages = new[]
        {
            new FanStage { CpuRiseC = 75, GpuRiseC = 90, CpuTargetRpm = 1920, GpuTargetRpm = 1920 },
            new FanStage { CpuRiseC = 127, GpuRiseC = 127, CpuTargetRpm = 6400, GpuTargetRpm = 6400 }
        };
        var follower = new FanCurveFollower();
        Assert.AreEqual((100, 30, 1), follower.Evaluate(90, 45, stages, 0, 0, false));
        Assert.AreEqual((30, 100, 1), follower.Evaluate(45, 95, stages, 0, 1, false));
        follower.Reset();
        Assert.AreEqual((30, 30, 0), follower.Evaluate(90, 45, stages, 2, 0, false));
        Assert.AreEqual((30, 30, 0), follower.Evaluate(90, 95, stages, 2, 1000, false));
        Assert.AreEqual((100, 30, 1), follower.Evaluate(90, 95, stages, 2, 2000, false));
        Assert.AreEqual((100, 100, 1), follower.Evaluate(90, 95, stages, 2, 3000, false));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void InitialHotSampleHonorsConfiguredDelay(bool synchronized)
    {
        var follower = new FanCurveFollower();
        var stages = Balanced();
        Assert.AreEqual(0, follower.Evaluate(56, 40, stages, 2, 0, synchronized).Stage);
        Assert.AreEqual(0, follower.Evaluate(56, 40, stages, 2, 1999, synchronized).Stage);
        Assert.AreEqual(1, follower.Evaluate(56, 40, stages, 2, 2000, synchronized).Stage);
    }

    [TestMethod]
    public void StageZeroStaysStoppedUntilRiseTrip()
    {
        var follower = new FanCurveFollower();
        var stages = Balanced();
        var (cpu, gpu, stage) = follower.Evaluate(40, 40, stages, 0, 0);
        Assert.AreEqual(0, stage);
        Assert.AreEqual(0, cpu);
        Assert.AreEqual(0, gpu);
        (cpu, gpu, stage) = follower.Evaluate(56, 40, stages, 0, 1000);
        Assert.AreEqual(1, stage);
        int shared = Math.Max(
            FanStage.RpmToDuty(1800, FanStage.CpuMaxRpm),
            FanStage.RpmToDuty(1850, FanStage.GpuMaxRpm));
        Assert.AreEqual(shared, cpu);
        Assert.AreEqual(shared, gpu);
    }

    [TestMethod]
    public void SynchronizeFanSpeeds_ControlsIndependentOrCoupledDuties()
    {
        var follower = new FanCurveFollower();
        var stages = Balanced();
        // Advance to stage 1 (CPU 1800 RPM -> 28%, GPU 1850 RPM -> 29%)
        int cpuDuty = FanStage.RpmToDuty(1800, FanStage.CpuMaxRpm);
        int gpuDuty = FanStage.RpmToDuty(1850, FanStage.GpuMaxRpm);
        Assert.AreNotEqual(cpuDuty, gpuDuty);

        // When synchronized is true: both fans run at the higher duty
        var (syncCpu, syncGpu, _) = follower.Evaluate(56, 40, stages, 0, 1000, synchronizeFanSpeeds: true);
        Assert.AreEqual(Math.Max(cpuDuty, gpuDuty), syncCpu);
        Assert.AreEqual(Math.Max(cpuDuty, gpuDuty), syncGpu);

        // When synchronized is false: fans run at their respective independent duties
        var (indCpu, indGpu, _) = follower.Evaluate(56, 40, stages, 0, 2000, synchronizeFanSpeeds: false);
        Assert.AreEqual(cpuDuty, indCpu);
        Assert.AreEqual(stages[0].GpuDutyPercent, indGpu, "A cool GPU must stay on its own stage.");
    }

    [TestMethod]
    public void GpuRiseAloneAdvancesStage()
    {
        var follower = new FanCurveFollower();
        var stages = Balanced();
        var (_, _, stage) = follower.Evaluate(40, 50, stages, 0, 0);
        Assert.AreEqual(1, stage);
    }

    [TestMethod]
    public void RetreatUsesPreviousRiseAndRequiresBothSensorsClear()
    {
        var follower = new FanCurveFollower();
        var stages = Balanced();
        follower.Evaluate(65, 56, stages, 0, 0);
        var (_, _, stage) = follower.Evaluate(65, 56, stages, 0, 1000);
        Assert.AreEqual(2, stage);
        // Stage 2 was entered via stage 1 rises (CPU 65 / GPU 56). GPU still at 56 must not retreat.
        (_, _, stage) = follower.Evaluate(50, 56, stages, 0, 2000);
        Assert.AreEqual(2, stage, "CPU below previous rise but GPU still at previous rise must not retreat.");
        // Both below stage-1 rises → stage 1; still above stage-0 rises (56/50) → stay at 1.
        (_, _, stage) = follower.Evaluate(60, 52, stages, 0, 3000);
        Assert.AreEqual(1, stage);
        // Both below stage-0 rises → cold floor.
        (_, _, stage) = follower.Evaluate(40, 40, stages, 0, 4000);
        Assert.AreEqual(0, stage);
    }

    [TestMethod]
    public void StageChangesImmediatelyWithoutDelay()
    {
        var follower = new FanCurveFollower();
        var stages = Balanced();
        var (_, _, stage) = follower.Evaluate(40, 40, stages);
        Assert.AreEqual(0, stage);
        (_, _, stage) = follower.Evaluate(56, 40, stages);
        Assert.AreEqual(1, stage);
    }

    [TestMethod]
    public void SharedDelayHoldsStageChangeUntilElapsed()
    {
        var follower = new FanCurveFollower();
        var stages = Balanced();
        follower.Evaluate(40, 40, stages, delaySeconds: 2, nowMs: 0);
        var (_, _, stage) = follower.Evaluate(56, 40, stages, delaySeconds: 2, nowMs: 500);
        Assert.AreEqual(0, stage, "Stage must wait for the shared delay.");
        (_, _, stage) = follower.Evaluate(56, 40, stages, delaySeconds: 2, nowMs: 2499);
        Assert.AreEqual(0, stage);
        (_, _, stage) = follower.Evaluate(56, 40, stages, delaySeconds: 2, nowMs: 2500);
        Assert.AreEqual(1, stage);
    }

    [TestMethod]
    public void DisabledTripDoesNotAdvanceOnThatSensor()
    {
        var follower = new FanCurveFollower();
        var quiet = FanCurveProfile.DefaultStages(0);
        // Quiet stage 5: CPU rise 127, GPU rise 80 — only GPU can advance to stage 6.
        follower.Evaluate(90, 75, quiet, 0, 0);
        var (_, _, stage) = follower.Evaluate(100, 75, quiet, 0, 1000);
        Assert.IsLessThanOrEqualTo(5, stage);
        (_, _, stage) = follower.Evaluate(100, 80, quiet, 0, 2000);
        Assert.AreEqual(6, stage);
    }

    [TestMethod]
    public void FactoryPresetsMatchEcRpmTargets()
    {
        var balanced = FanCurveProfile.CreateDefault(1);
        Assert.HasCount(12, balanced.Stages);
        Assert.AreEqual(0, balanced.Stages[0].CpuTargetRpm);
        Assert.AreEqual(1800, balanced.Stages[1].CpuTargetRpm);
        Assert.AreEqual(2350, balanced.Stages[3].CpuTargetRpm);
        Assert.AreEqual(6400, balanced.Stages[11].CpuTargetRpm);
        CollectionAssert.AreEqual(
            FanCurveProfile.DefaultStages(0).Select(s => s.CpuTargetRpm).ToArray(),
            FanCurveProfile.DefaultStages(6).Select(s => s.CpuTargetRpm).ToArray());
    }

    [TestMethod]
    public void PerModeMaxChoiceDoesNotReplaceSavedStagesOrLeakToAnotherProfile()
    {
        var turbo = new FanCurveProfile { ControlMode = FanControlMode.Max, Stages = FanCurveProfile.DefaultStages(5) };
        var balanced = new FanCurveProfile();
        var draft = turbo.Copy();
        Assert.AreEqual(FanControlMode.Max, draft.ControlMode);
        Assert.AreEqual(FanControlMode.Auto, balanced.ControlMode);
        draft.ControlMode = FanControlMode.Custom;
        Assert.AreEqual(FanControlMode.Max, turbo.ControlMode);
        Assert.AreEqual(2150, turbo.Stages[1].CpuTargetRpm);
    }

    [TestMethod]
    public void DecodeControlModePrefersSavedValue()
    {
        Assert.AreEqual(FanControlMode.Max, FanCurveStorage.DecodeControlMode(1, false, false, 0));
        Assert.AreEqual(FanControlMode.Custom, FanCurveStorage.DecodeControlMode(2, false, true, 0));
        Assert.AreEqual(FanControlMode.Auto, FanCurveStorage.DecodeControlMode(null, false, true, 1));
        Assert.AreEqual(FanControlMode.Max, FanCurveStorage.DecodeControlMode(null, true, true, 1));
    }
}
