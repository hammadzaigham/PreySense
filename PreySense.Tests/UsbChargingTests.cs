using PreySense.Core.Configuration;
using PreySense.Helpers;

namespace PreySense.Tests;

[TestClass]
public class UsbChargingTests
{
    [TestMethod]
    public void NewSettings_DisablePowerOffUsbCharging() =>
        Assert.AreEqual(0, new PreySenseConfig().Battery.UsbPowerOffChargingCutoffPercent);

    [TestMethod]
    [DataRow(0, 663300L, 663296L)]
    [DataRow(0, 663300L, 1318656L)]
    [DataRow(0, 663300L, 1974016L)]
    [DataRow(10, 659204L, 659200L)]
    [DataRow(20, 1314564L, 1314560L)]
    [DataRow(30, 1969924L, 1969920L)]
    public void FirmwareCommandsAndReadback_KeepCutoffAndDisabledDistinct(int percent, long command, long status)
    {
        Assert.AreEqual((ulong)command, WmiController.EncodeUsbChargingLevel(percent));
        Assert.AreEqual(percent, WmiController.DecodeUsbChargingLevel((ulong)status));
    }

    [TestMethod]
    public void UnsupportedReadsAndSettings_AreNotTreatedAsDisabled()
    {
        Assert.AreEqual(-1, WmiController.DecodeUsbChargingLevel(null));
        Assert.AreEqual(-1, WmiController.DecodeUsbChargingLevel(0));
        Assert.AreEqual(-1, WmiController.DecodeUsbChargingLevel(0x1000));
        Assert.AreEqual(-1, WmiController.DecodeUsbChargingLevel(ulong.MaxValue));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => WmiController.EncodeUsbChargingLevel(15));
        var config = new PreySenseConfig();
        config.Battery.UsbPowerOffChargingCutoffPercent = 15;
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ConfigurationService.Validate(config));
    }
}
