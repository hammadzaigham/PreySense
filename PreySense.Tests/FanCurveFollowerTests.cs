using PreySense.Mode;

namespace PreySense.Tests;

[TestClass]
public class FanCurveFollowerTests
{
    private static int[] TestCurve() => [20, 20, 25, 30, 40, 50, 60, 70, 80, 90, 100, 100, 100];
    [TestMethod]
    public void TemperatureBandsDoNotQuantizeDutyToFivePercent()
    {
        int[] curve = TestCurve();
        curve[1] = 37;
        var follower = new FanCurveFollower();
        Assert.AreEqual(20, follower.Evaluate(44, curve, 0, 0));
        Assert.AreEqual(37, follower.Evaluate(45, curve, 0, 1000));
        Assert.AreEqual(37, follower.Evaluate(49, curve, 0, 2000));
    }

    [TestMethod]
    public void FiveDegreeCoolingHysteresisPreventsBoundaryOscillation()
    {
        int[] curve = TestCurve();
        var follower = new FanCurveFollower();
        Assert.AreEqual(30, follower.Evaluate(59, curve, 0, 0));
        Assert.AreEqual(40, follower.Evaluate(60, curve, 0, 1000));
        Assert.AreEqual(40, follower.Evaluate(59, curve, 0, 2000));
        Assert.AreEqual(40, follower.Evaluate(56, curve, 0, 3000));
        Assert.AreEqual(30, follower.Evaluate(55, curve, 0, 4000));
    }

    [TestMethod]
    public void RampUpDelayContinuesWithUnchangedTelemetry()
    {
        int[] curve = TestCurve();
        var follower = new FanCurveFollower();
        Assert.AreEqual(40, follower.Evaluate(60, curve, 2, 0));
        Assert.AreEqual(40, follower.Evaluate(65, curve, 2, 1000));
        Assert.AreEqual(40, follower.Evaluate(65, curve, 2, 2000));
        Assert.AreEqual(50, follower.Evaluate(65, curve, 2, 3000));
        Assert.AreEqual(50, follower.Evaluate(60, curve, 2, 4000));
        Assert.AreEqual(50, follower.Evaluate(60, curve, 2, 5000));
        Assert.AreEqual(40, follower.Evaluate(60, curve, 2, 6000));
    }

    [TestMethod]
    public void RisingBandsDoNotRestartTheRampUpClock()
    {
        var curve = TestCurve();
        var follower = new FanCurveFollower();
        Assert.AreEqual(40, follower.Evaluate(60, curve, 2, 0));
        Assert.AreEqual(40, follower.Evaluate(65, curve, 2, 1000));
        Assert.AreEqual(40, follower.Evaluate(70, curve, 2, 2000));
        Assert.AreEqual(70, follower.Evaluate(75, curve, 2, 3000));
    }

    [TestMethod]
    public void HeatingAndCoolingUseTheSameDelayAndApplyLatestTarget()
    {
        var curve = TestCurve();
        var follower = new FanCurveFollower();
        Assert.AreEqual(70, follower.Evaluate(75, curve, 3, 0));
        Assert.AreEqual(70, follower.Evaluate(70, curve, 3, 1000));
        Assert.AreEqual(70, follower.Evaluate(65, curve, 3, 3000));
        Assert.AreEqual(50, follower.Evaluate(65, curve, 3, 4000));
        Assert.AreEqual(50, follower.Evaluate(75, curve, 3, 5000));
        Assert.AreEqual(70, follower.Evaluate(75, curve, 3, 8000));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(5)]
    public void FanControlDelayAppliesSymmetricallyIncludingZero(int delay)
    {
        var follower = new FanCurveFollower();
        var curve = TestCurve();
        Assert.AreEqual(40, follower.Evaluate(60, curve, delay, 0));
        Assert.AreEqual(delay == 0 ? 50 : 40, follower.Evaluate(65, curve, delay, 1000));
        if (delay > 0)
            Assert.AreEqual(40, follower.Evaluate(65, curve, delay, 1000 + delay * 1000 - 1));
        Assert.AreEqual(50, follower.Evaluate(65, curve, delay, 1000 + delay * 1000));
        Assert.AreEqual(delay == 0 ? 40 : 50, follower.Evaluate(60, curve, delay, 10000));
        if (delay > 0)
            Assert.AreEqual(50, follower.Evaluate(60, curve, delay, 10000 + delay * 1000 - 1));
        Assert.AreEqual(40, follower.Evaluate(60, curve, delay, 10000 + delay * 1000));
    }

    [TestMethod]
    public void ReturningToAppliedDemandCancelsPendingCooldown()
    {
        var curve = TestCurve();
        var follower = new FanCurveFollower();
        Assert.AreEqual(60, follower.Evaluate(70, curve, 5, 0));
        Assert.AreEqual(60, follower.Evaluate(65, curve, 5, 1000));
        Assert.AreEqual(60, follower.Evaluate(70, curve, 5, 4000));
        Assert.AreEqual(60, follower.Evaluate(65, curve, 5, 5000));
        Assert.AreEqual(60, follower.Evaluate(65, curve, 5, 9000));
        Assert.AreEqual(50, follower.Evaluate(65, curve, 5, 10000));
    }

    [TestMethod]
    public void EitherSensorCanRaiseBothIndependentFanTargets()
    {
        var cpu = TestCurve();
        var gpu = TestCurve();
        gpu[8] = 83;
        foreach (var (cpuTemp, gpuTemp) in new[] { (80, 50), (50, 80) })
        {
            int temperature = FanCurveProfile.CoolingTemperature(cpuTemp, gpuTemp);
            Assert.AreEqual(80, new FanCurveFollower().Evaluate(temperature, cpu, 0, 0));
            Assert.AreEqual(83, new FanCurveFollower().Evaluate(temperature, gpu, 0, 0));
        }
    }

    [TestMethod]
    public void EmergencyIsImmediateAndRecoveryHonorsCooldown()
    {
        var curve = new int[FanCurveProfile.PointCount];
        var follower = new FanCurveFollower();
        Assert.AreEqual(100, follower.Evaluate(95, curve, 10, 0, emergency: true));
        Assert.AreEqual(100, follower.Evaluate(80, curve, 10, 1000));
        Assert.AreEqual(0, follower.Evaluate(80, curve, 10, 6000));
    }

    [TestMethod]
    public void EmergencyCoolingStaysLatchedUntilBothSensorsClearReleaseMargins()
    {
        var safety = new FanCoolingSafety();
        Assert.IsFalse(safety.Evaluate(80, 70));
        Assert.IsTrue(safety.Evaluate(95, 70));
        Assert.IsTrue(safety.Evaluate(94, 70));
        Assert.IsTrue(safety.Evaluate(90, 83));
        Assert.IsFalse(safety.Evaluate(90, 82));
        Assert.IsTrue(safety.Evaluate(70, 87));
        Assert.IsTrue(safety.Evaluate(70, 86));
        Assert.IsTrue(safety.Evaluate(0, 0));
        Assert.IsFalse(safety.Evaluate(80, 80));
    }

    [TestMethod]
    public void EmergencyReleaseWaitsTheConfiguredDelayBeforeReducingDuty()
    {
        var safety = new FanCoolingSafety();
        var follower = new FanCurveFollower();
        var curve = TestCurve();
        Assert.AreEqual(100, follower.Evaluate(95, curve, 2, 0, safety.Evaluate(95, 60)));
        Assert.AreEqual(100, follower.Evaluate(94, curve, 2, 1000, safety.Evaluate(94, 60)));
        Assert.AreEqual(100, follower.Evaluate(80, curve, 2, 2000, safety.Evaluate(80, 60)));
        Assert.AreEqual(100, follower.Evaluate(80, curve, 2, 3000, safety.Evaluate(80, 60)));
        Assert.AreEqual(80, follower.Evaluate(80, curve, 2, 4000, safety.Evaluate(80, 60)));
    }

    [TestMethod]
    public void OldRampUpValuesCannotDelayAnIncreaseLongerThanFiveSeconds()
    {
        var follower = new FanCurveFollower();
        var curve = TestCurve();
        Assert.AreEqual(40, follower.Evaluate(60, curve, 99, 0));
        Assert.AreEqual(40, follower.Evaluate(70, curve, 99, 1000));
        Assert.AreEqual(40, follower.Evaluate(70, curve, 99, 5000));
        Assert.AreEqual(60, follower.Evaluate(70, curve, 99, 6000));
    }

    [TestMethod]
    public void EmergencyCoolingBypassesEvenStoppedCurveAndLongRamp()
    {
        int[] curve = new int[FanCurveProfile.PointCount];
        var follower = new FanCurveFollower();
        Assert.AreEqual(0, follower.Evaluate(60, curve, 10, 0));
        Assert.AreEqual(100, follower.Evaluate(95, curve, 10, 1, emergency: true));
        Assert.IsTrue(FanCurveProfile.RequiresMaxCooling(95, 60));
        Assert.IsTrue(FanCurveProfile.RequiresMaxCooling(60, 87));
        Assert.IsFalse(FanCurveProfile.ValidTemperatures(60, 0));
        Assert.IsFalse(FanCurveProfile.ValidTemperatures(0, 60));
        Assert.IsTrue(FanCurveProfile.ValidTemperatures(60, 70));
    }

    [TestMethod]
    public void ResetAppliesNewModeWithoutOldRampState()
    {
        var follower = new FanCurveFollower();
        var curve = TestCurve();
        Assert.AreEqual(40, follower.Evaluate(60, curve, 10, 0));
        follower.Reset();
        curve[4] = 73;
        Assert.AreEqual(73, follower.Evaluate(60, curve, 10, 1));
    }

    [TestMethod]
    public void ProfilesCloneWithoutSharingDraftArraysAndClampInvalidPoints()
    {
        var profile = new FanCurveProfile { RampUpSeconds = 99, Cpu = [-4, 150] };
        profile.Normalize();
        Assert.AreEqual(5, profile.RampUpSeconds);
        Assert.AreEqual(FanCurveProfile.PointCount, profile.Cpu.Length);
        Assert.AreEqual(0, profile.Cpu[0]);
        Assert.AreEqual(100, profile.Cpu[1]);
        var draft = profile.Copy();
        draft.Cpu[0] = 49;
        Assert.AreEqual(0, profile.Cpu[0]);
    }

    [TestMethod]
    public void CurveEditsCannotDecreaseAtHigherTemperatures()
    {
        int[] curve = TestCurve();
        FanCurveProfile.SetPoint(curve, 6, 1);
        Assert.AreEqual(curve[5], curve[6]);
        FanCurveProfile.SetPoint(curve, 6, 99);
        Assert.AreEqual(curve[7], curve[6]);
        FanCurveProfile.SetPoint(curve, 0, -10);
        Assert.AreEqual(0, curve[0]);
        FanCurveProfile.SetPoint(curve, FanCurveProfile.PointCount - 1, 200);
        Assert.AreEqual(100, curve[FanCurveProfile.PointCount - 1]);
    }

    [TestMethod]
    public void LoadedCurvesNormalizeWithoutLoweringEarlierCoolingTargets()
    {
        var profile = new FanCurveProfile { Cpu = [20, 40, 10, 60, 30], Gpu = [15, 80, 20] };
        profile.Normalize();
        Assert.AreEqual(40, profile.Cpu[2]);
        Assert.AreEqual(60, profile.Cpu[4]);
        foreach (var curve in new[] { profile.Cpu, profile.Gpu })
            for (int i = 1; i < curve.Length; i++) Assert.IsTrue(curve[i] >= curve[i - 1]);
    }

    [TestMethod]
    public void PerModeMaxChoiceDoesNotReplaceSavedCurveOrLeakToAnotherProfile()
    {
        var turbo = new FanCurveProfile { ControlMode = FanControlMode.Max };
        var balanced = new FanCurveProfile();
        var draft = turbo.Copy();
        Assert.AreEqual(FanControlMode.Max, draft.ControlMode);
        Assert.AreEqual(FanControlMode.Auto, balanced.ControlMode);
        draft.ControlMode = FanControlMode.Custom;
        Assert.AreEqual(FanControlMode.Max, turbo.ControlMode);
        CollectionAssert.AreEqual(FanCurveProfile.DefaultCurve(), turbo.Cpu);
    }

    [TestMethod]
    public void ExtractedTablePresetsUseWmiModeCodesAndKeepBothFansMonotonic()
    {
        var balanced = FanCurveProfile.CreateDefault(1);
        var turbo = FanCurveProfile.CreateDefault(5);
        var quiet = FanCurveProfile.CreateDefault(0);
        Assert.AreEqual(35, balanced.Cpu[5]); // 65°C, extracted stage 2350/6800.
        Assert.AreEqual(37, balanced.Gpu[5]); // Same stage, 2350/6400.
        Assert.AreEqual(58, turbo.Cpu[5]);
        Assert.AreEqual(29, quiet.Cpu[5]);
        CollectionAssert.AreEqual(quiet.Cpu, FanCurveProfile.CreateDefault(6).Cpu);
        foreach (byte mode in new byte[] { 0, 1, 4, 5, 6 })
        {
            var profile = FanCurveProfile.CreateDefault(mode);
            foreach (var curve in new[] { profile.Cpu, profile.Gpu })
            {
                Assert.AreEqual(FanCurveProfile.PointCount, curve.Length);
                Assert.AreEqual(100, curve[11]);
                for (int i = 0; i < curve.Length; i++)
                {
                    Assert.IsTrue(curve[i] is >= 20 and <= 100);
                    if (i > 0) Assert.IsTrue(curve[i] >= curve[i - 1]);
                }
            }
        }
        turbo.Cpu[0] = 0;
        Assert.AreEqual(20, FanCurveProfile.CreateDefault(5).Cpu[0]);
    }

    [TestMethod]
    public void PerModeSelectionsOverrideLegacyGlobalAndMigrateOldSwitches()
    {
        Assert.AreEqual(FanControlMode.Max, FanCurveStorage.DecodeControlMode(1, false, false, 0));
        Assert.AreEqual(FanControlMode.Custom, FanCurveStorage.DecodeControlMode(2, false, true, 0));
        Assert.AreEqual(FanControlMode.Auto, FanCurveStorage.DecodeControlMode(0, true, true, 2));
        Assert.AreEqual(FanControlMode.Max, FanCurveStorage.DecodeControlMode(null, true, true, 2));
        Assert.AreEqual(FanControlMode.Custom, FanCurveStorage.DecodeControlMode(null, true, false, 2));
        Assert.AreEqual(FanControlMode.Auto, FanCurveStorage.DecodeControlMode(null, false, true, 2));
        Assert.AreEqual(FanControlMode.Auto, FanCurveStorage.DecodeControlMode(null, true, true, 0));
        Assert.AreEqual(FanControlMode.Auto, FanCurveStorage.DecodeControlMode(99, true, true, 2));
    }
}


