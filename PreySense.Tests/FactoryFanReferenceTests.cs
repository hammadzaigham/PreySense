using System.Security.Cryptography;
using PreySense.Mode;

namespace PreySense.Tests;

[TestClass]
public class FactoryFanReferenceTests
{
    [TestMethod]
    [DataRow(0xFF)]
    [DataRow(2)]
    public void UnknownModesNeverInventAFactoryReference(int mode)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FactoryFanReference.GetTable((byte)mode));
    }

    [TestMethod]
    public void EveryOriginalRecordMatchesTheVerifiedFirmwareTableBlock()
    {
        // SHA-256 of original 1320 bytes at EC payload offset 0x10190.
        var bytes = FactoryFanReference.AllTables.SelectMany(table => table.Stages)
            .SelectMany(stage => Convert.FromHexString(stage.RawHex)).ToArray();
        Assert.HasCount(1320, bytes);
        Assert.AreEqual("75b9642d3e9998a1feac5de38678bd7665fc7f2fe6f55f8fda25b2fa16d4844a",
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }

    [TestMethod]
    public void BalancedReferencePreservesOriginalHysteresisCountersAndMaxSlot()
    {
        var table = FactoryFanReference.GetTable(1);
        Assert.AreEqual(0x10298, table.EcImageOffset);
        Assert.AreEqual(56, table.Stages[0].CpuRiseC);
        Assert.AreEqual(50, table.Stages[0].GpuRiseC);
        Assert.AreEqual(55, table.Stages[2].CpuFallC);
        Assert.AreEqual(51, table.Stages[2].GpuFallC);
        Assert.AreEqual(6800, table.Stages[11].CpuTargetRpm);
        Assert.AreEqual(6400, table.Stages[11].GpuTargetRpm);
        Assert.AreEqual(127, table.Stages[11].CpuRiseC);
        Assert.AreEqual(255, table.Stages[11].TransitionCounterRaw);
        Assert.AreEqual(0, table.Stages[11].FilterFlagRaw);
    }

    [TestMethod]
    public void SameCustomPercentHasDifferentTargetsWhenTheFactoryStageChanges()
    {
        var table = FactoryFanReference.GetTable(1);
        Assert.AreEqual(2720, table.EstimateCustomTargetRpm(0, 40));
        Assert.AreEqual(3800, table.EstimateCustomTargetRpm(1, 40));
        Assert.AreEqual(4130, table.EstimateCustomTargetRpm(3, 40));
        Assert.AreEqual(4880, table.EstimateCustomTargetRpm(6, 40));
        Assert.AreEqual(4720, table.EstimateCustomTargetRpm(6, 40, gpu: true));
        Assert.AreEqual(3600, table.EstimateCustomTargetRpm(6, 0));
        Assert.AreEqual(6800, table.EstimateCustomTargetRpm(6, 100));
    }

    [TestMethod]
    public void ReferenceVariantSelectionIsExplicitAndKeepsItsDifferentEndpoints()
    {
        var table = FactoryFanReference.GetTable(1, variantBit: 1);
        Assert.AreEqual(1, table.VariantBit);
        Assert.AreEqual(4850, table.Stages[11].CpuTargetRpm);
        Assert.AreEqual(4900, table.Stages[11].GpuTargetRpm);
        Assert.AreEqual(4850, table.EstimateCustomTargetRpm(0, 100));
        Assert.AreEqual(4900, table.EstimateCustomTargetRpm(0, 100, gpu: true));
    }

    [TestMethod]
    public void ReferenceHasNoSharedMutableStagesWithSavedProfiles()
    {
        var reference = FactoryFanReference.GetTable(1);
        var profile = FanCurveProfile.CreateDefault(1);
        profile.Stages[0].CpuRiseC = 99;
        profile.Stages[11].CpuTargetRpm = 1234;
        Assert.AreEqual(56, reference.Stages[0].CpuRiseC);
        Assert.AreEqual(6800, reference.Stages[11].CpuTargetRpm);
        Assert.IsTrue(((ICollection<FactoryFanStage>)reference.Stages).IsReadOnly);
        Assert.IsTrue(((ICollection<FactoryFanTable>)FactoryFanReference.AllTables).IsReadOnly);
    }
}
