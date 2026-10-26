using PreySense.Core.Acpi;

namespace PreySense.Tests;

[TestClass]
public class GpuPowerOffsetsTests
{
    [TestMethod]
    public void PlatformBudgetIsIndependentOfGpuCeiling()
    {
        var value = new GpuPowerOffsets(35, 20);
        Assert.IsTrue(value.IsValid);
        Assert.AreEqual(140, value.NominalReferenceWatts());

    }

    [TestMethod]
    public void ByteFieldsRejectOverflowRatherThanWrapping()
    {
        Assert.IsTrue(new GpuPowerOffsets(255, 255).IsValid);
        Assert.IsFalse(new GpuPowerOffsets(256, 15).IsValid);
        Assert.IsFalse(new GpuPowerOffsets(50, 256).IsValid);
        Assert.IsFalse(new GpuPowerOffsets(-1, 15).IsValid);
    }

    [TestMethod]
    public void LegacyProfilesRetainTheExistingSplit()
    {
        Assert.AreEqual(new GpuPowerOffsets(0, 0), GpuPowerOffsets.FromLegacyTarget(85));
        Assert.AreEqual(new GpuPowerOffsets(15, 0), GpuPowerOffsets.FromLegacyTarget(100));
        Assert.AreEqual(new GpuPowerOffsets(30, 15), GpuPowerOffsets.FromLegacyTarget(130));
        Assert.IsTrue(GpuPowerOffsets.FromLegacyTarget(999).IsValid);
    }

    [TestMethod]
    public void GameOffsetsSurviveSerializationAndBaselineChanges()
    {
        var game = new PreySense.Mode.GameProfile { GpuPowerWatts = 150, GpuDeltaWatts = 40, GpuDynamicBoostWatts = 25 };
        var json = System.Text.Json.JsonSerializer.Serialize(game);
        var loaded = System.Text.Json.JsonSerializer.Deserialize<PreySense.Mode.GameProfile>(json)!;
        Assert.AreEqual(new GpuPowerOffsets(40, 25), loaded.ResolveGpuOffsets(85));
        Assert.AreEqual(new GpuPowerOffsets(40, 25), loaded.ResolveGpuOffsets(115));
        var legacy = new PreySense.Mode.GameProfile { GpuPowerWatts = 150 };
        Assert.AreEqual(new GpuPowerOffsets(50, 15), legacy.ResolveGpuOffsets(85));
        Assert.IsNull(legacy.GpuDeltaWatts);
        Assert.AreEqual(new GpuPowerOffsets(20, 15), legacy.ResolveGpuOffsets(115));
    }

    [TestMethod]
    public void PlatformControlIsRemovedAndSingleTargetWritesUseTheSamePolicy()
    {
        string root = Root();
        string xaml = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "PreySense.Frontend", "Views", "ModeSettingsWindow.xaml"));
        Assert.DoesNotContain("RowGpuPlatform", xaml);
        Assert.DoesNotContain("ModeGpuPlatformValue", xaml);
        Assert.Contains("RowGpuDelta", xaml);
        Assert.Contains("RowGpuDynamicBoost", xaml);
        string client = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "PreySense.Core", "Hardware", "PreySenseWmiDeviceClient.cs"));
        Assert.DoesNotContain("PlatformOffsetWatts", client);
        Assert.Contains("return SetGpuPowerOffsets(PreySense.Core.Acpi.GpuPowerOffsets.FromLegacyTarget((int)targetWatts, baseline))", client);
    }

    private static string Root()
    {
        var directory = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "PreySense.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Solution root not found");
    }

    [TestMethod]
    public void FirmwareContractKeepsOffsetsIndependentAndSupportsEco()
    {
        string dsl = System.IO.File.ReadAllText(System.IO.Path.Combine(Root(), "acpi_staging", "SSDT-PREYDEV.dsl"));
        Assert.Contains("Case (0x0B) // SetGpuPowerOffsets", dsl);
        Assert.Contains("Case (0x0C) // GetGpuPowerOffsets", dsl);
        Assert.Contains("GPOF (DLTW, BSTW)", dsl);
        Assert.Contains("(Arg0 > 255) || (Arg1 > 255)", dsl);
        foreach (int mode in new[] { 1, 2, 3, 4, 5 })
        {
            Assert.Contains($"M{mode}CT = Arg0", dsl);
            Assert.Contains($"M{mode}DB = Arg1", dsl);
            Assert.Contains($"M{mode}CP = 0xFF", dsl);
        }
        Assert.Contains("PKG3 [Zero] = (\\_SB.NPCF.ACBT / 0x08)", dsl);
        Assert.Contains("PKG3 [One] = (\\_SB.NPCF.AMAT / 0x08)", dsl);
        Assert.Contains("ATPP = 0x07F8", dsl);
    }

    [TestMethod]
    public void PackagedAmlMatchesStagingAndHasValidRevisionAndChecksum()
    {
        string root = Root();
        byte[] staging = System.IO.File.ReadAllBytes(System.IO.Path.Combine(root, "acpi_staging", "SSDT-PREYDEV.aml"));
        byte[] packaged = System.IO.File.ReadAllBytes(System.IO.Path.Combine(root, "tools", "OpenCore_PreyBoost_EFI", "EFI", "OC", "ACPI", "SSDT-PREYDEV.aml"));
        CollectionAssert.AreEqual(staging, packaged);
        Assert.AreEqual(0x0201u, BitConverter.ToUInt32(staging, 24));
        Assert.AreEqual((uint)staging.Length, BitConverter.ToUInt32(staging, 4));
        Assert.AreEqual(0, staging.Sum(value => (int)value) & 255);
    }
}
