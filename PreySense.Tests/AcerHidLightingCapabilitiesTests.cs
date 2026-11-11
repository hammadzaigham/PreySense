using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PreySense.Rgb;

namespace PreySense.Tests;

[TestClass]
public class AcerHidLightingCapabilitiesTests
{
    [TestMethod]
    public void AttributeAdvertisementConfirmsOnlyAdvertisedSurfaces()
    {
        var targets = AcerHidLightingCapabilities.ParseAttributeReport(new byte[] { 0xA1, 3, 0x65, 0x21, 0x83, 0, 0, 0, 0, 0, 0 });
        CollectionAssert.AreEquivalent(new byte[] { 0x65, 0x21, 0x83 }, targets.ToArray());
        Assert.DoesNotContain((byte)0x84, targets);
        Assert.DoesNotContain((byte)0x22, targets);
    }

    [TestMethod]
    public void MalformedCountsOrWrongReportNeverYieldPartialCapabilities()
    {
        Assert.IsEmpty(AcerHidLightingCapabilities.ParseAttributeReport(new byte[] { 0xA1, 2, 0x83 }));
        Assert.IsEmpty(AcerHidLightingCapabilities.ParseAttributeReport(new byte[] { 0xA4, 1, 0x83 }));
        Assert.IsEmpty(AcerHidLightingCapabilities.ParseAttributeReport(new byte[] { 0xA1 }));
    }

    [TestMethod]
    public void UnsupportedTargetsAreIgnoredAndDuplicatesCollapse()
    {
        var targets = AcerHidLightingCapabilities.ParseAttributeReport(new byte[] { 0xA1, 4, 0x83, 0xFF, 0x83, 0x22 });
        CollectionAssert.AreEquivalent(new byte[] { 0x83, 0x22 }, targets.ToArray());
    }
}
