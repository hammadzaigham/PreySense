using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PreySense.Rgb;

namespace PreySense.Tests;

[TestClass]
public class AcerEcLightingCapabilitiesTests
{
    [TestMethod]
    public void Type172FeaturesDistinguishKeyboardAndOptionalBars()
    {
        byte[] table = [172, 15, 0, 0, 2, 95, 8, 4, 0, 10, 2, 0, 23, 255, 0];
        Assert.AreEqual(new AcerEcLightingCapabilities(2, 95, true, false, 4), AcerEcLightingCapabilities.FromSmbios(table));
        table[13] = 1;
        Assert.IsTrue(AcerEcLightingCapabilities.FromSmbios(table)!.Lightbars);
        table[10] = 1;
        Assert.IsFalse(AcerEcLightingCapabilities.FromSmbios(table)!.Keyboard);
    }

    [TestMethod]
    public void ParserSkipsStringAreaAndRejectsTruncatedTables()
    {
        byte[] tables = [0, 4, 0, 0, 65, 0, 0, 172, 12, 0, 0, 2, 86, 8, 1, 0, 10, 2, 0];
        Assert.IsTrue(AcerEcLightingCapabilities.FromSmbios(tables)!.Keyboard);
        Assert.IsNull(AcerEcLightingCapabilities.FromSmbios(tables.AsSpan(0, tables.Length - 1)));
        Assert.IsNull(AcerEcLightingCapabilities.FromSmbios(new byte[] { 172, 3, 0, 0 }));
    }

    [TestMethod]
    public void UndeclaredAndUnknownValuesNeverEnableTargets()
    {
        byte[] table = [172, 15, 0, 0, 2, 95, 8, 255, 0, 10, 2, 0, 23, 2, 0];
        var capabilities = AcerEcLightingCapabilities.FromSmbios(table)!;
        Assert.IsFalse(capabilities.Keyboard);
        Assert.IsFalse(capabilities.Lightbars);
        Assert.AreEqual(new AcerEcLightingCapabilities(2, 95, false, false),
            AcerEcLightingCapabilities.FromSmbios(new byte[] { 172, 6, 0, 0, 2, 95 }));
    }
}
