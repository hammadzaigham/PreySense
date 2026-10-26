using PreySense.Mode;

namespace PreySense.Tests;

[TestClass]
public class PawnCpuPowerTests
{
    // 1/8 W power unit and 1/1024 s time unit.
    private const ulong Units = 3UL | (10UL << 16);

    [TestMethod]
    public void EncodesBothLimitsAndTauWhilePreservingUnrelatedBits()
    {
        const ulong current = 0x523456789ABCDEF0UL;
        Assert.IsTrue(IntelRaplPowerLimits.TryBuild(current, Units, 85, 140, true, 56, out ulong target, out string error), error);
        Assert.AreEqual(680UL, target & 0x7FFF);
        Assert.AreEqual(1120UL, (target >> 32) & 0x7FFF);
        Assert.AreEqual(0x6FUL, (target >> 17) & 0x7F);
        Assert.AreEqual(3UL, (target >> 15) & 3);
        Assert.AreEqual(3UL, (target >> 47) & 3);
        Assert.AreEqual(current & ~IntelRaplPowerLimits.WritableMask, target & ~IntelRaplPowerLimits.WritableMask);
    }

    [TestMethod]
    public void LockedRegisterIsNeverReencoded()
    {
        ulong current = IntelRaplPowerLimits.LockBit | 0x123456789UL;
        Assert.IsFalse(IntelRaplPowerLimits.TryBuild(current, Units, 85, 140, true, 56, out ulong target, out string error));
        Assert.AreEqual(current, target);
        Assert.Contains("locked", error);
    }

    [TestMethod]
    public void PowerFieldOverflowIsRejectedInsteadOfWrapping()
    {
        Assert.IsFalse(IntelRaplPowerLimits.TryBuild(0, 15, 85, 140, true, 56, out _, out _));
        Assert.IsFalse(IntelRaplPowerLimits.TryBuild(0, Units, 0, 140, true, 56, out _, out _));
    }

    [TestMethod]
    public void DisablingPl2DoesNotChangeItsTimeWindowOrReservedBits()
    {
        const ulong current = 0x00FE000000000000UL;
        Assert.IsTrue(IntelRaplPowerLimits.TryBuild(current, Units, 55, 100, false, 28, out ulong target, out _));
        Assert.AreEqual(0UL, (target >> 47) & 3);
        Assert.AreEqual(current & ~IntelRaplPowerLimits.WritableMask, target & ~IntelRaplPowerLimits.WritableMask);
        Assert.AreEqual(800UL, (target >> 32) & 0x7FFF);
    }

    [TestMethod]
    public void TauUsesReportedMsrTimeUnits()
    {
        Assert.AreEqual((byte)0x6F, IntelRaplPowerLimits.EncodeTau(56, Units));
        Assert.AreEqual((byte)0x6E, IntelRaplPowerLimits.EncodeTau(56, 3UL | (9UL << 16)));
        Assert.AreEqual((byte)0x7F, IntelRaplPowerLimits.EncodeTau(449, Units));
    }

    [TestMethod]
    public void V140IntelModuleIsEmbeddedUnchanged()
    {
        using var resource = typeof(PawnCpuPowerControl).Assembly.GetManifestResourceStream("PreySense.Core.IntelMSR.bin");
        Assert.IsNotNull(resource);
        using var bytes = new System.IO.MemoryStream();
        resource.CopyTo(bytes);
        byte[] module = bytes.ToArray();
        byte[] header = System.Text.Encoding.ASCII.GetBytes($"blob {module.Length}\0");
        byte[] gitBlob = header.Concat(module).ToArray();
        Assert.AreEqual("f7dad4c83ea297ce726ff7a1eaa77cc780884964", Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(gitBlob)).ToLowerInvariant());
    }

    [TestMethod]
    public void CpuVoltageReplacesTheOldDefaultButPreservesCustomColumns()
    {
        Assert.AreEqual(("Voltage", "Power"), PreySense.Overlay.AppConfig.ParseOverlayColumn("Voltage|Power"));
        Assert.AreEqual(("Voltage", "Voltage"), PreySense.Overlay.AppConfig.ParseOverlayColumn("Voltage"));
        Assert.AreEqual("Voltage|Voltage", PreySense.Overlay.AppConfig.DefaultOverlayColumn(4));
        Assert.AreEqual("Voltage|Voltage", PreySense.Overlay.AppConfig.UpgradeDefaultVoltageColumn("E-Core|Voltage"));
        Assert.AreEqual("Voltage|Voltage", PreySense.Overlay.AppConfig.UpgradeDefaultVoltageColumn(""));
        Assert.AreEqual("Power|Temperature", PreySense.Overlay.AppConfig.UpgradeDefaultVoltageColumn("Power|Temperature"));
        Assert.AreEqual(("E-Core", "Power"), PreySense.Overlay.AppConfig.ParseOverlayColumn("E-Core|Power"));
    }

    [TestMethod]
    public void PawnVoltageUsesOnlyTheVidFieldAndRejectsUnavailableReadings()
    {
        Assert.AreEqual(1.125f, IntelCpuVoltage.DecodePerfStatus(9216UL << 32));
        Assert.IsNull(IntelCpuVoltage.DecodePerfStatus(0));
        Assert.IsNull(IntelCpuVoltage.DecodePerfStatus(0xFFFF));
        Assert.IsNull(IntelCpuVoltage.DecodePerfStatus(0xFFFFUL << 32));
        Assert.IsTrue(IntelCpuVoltage.IsSupportedModel(6, 0xC6));
        Assert.IsTrue(IntelCpuVoltage.IsSupportedModel(6, 0xC5));
        Assert.IsFalse(IntelCpuVoltage.IsSupportedModel(6, 0x97));
    }
}
