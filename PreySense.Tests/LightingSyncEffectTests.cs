using PreySense.Frontend.ViewModels;

namespace PreySense.Tests;

[TestClass]
public class LightingSyncEffectTests
{
    [TestMethod]
    [DataRow("MODE", 1, -2)]
    [DataRow("HW:Mode", 1, -2)]
    [DataRow("HW:0", 1, 2)]
    [DataRow("HW:1", 2, 4)]
    [DataRow("HW:2", 3, 5)]
    [DataRow("HW:3", 3, 5)]
    [DataRow("HW:4", 3, 5)]
    [DataRow("HW:5", 3, 5)]
    [DataRow("HW:6", 3, 5)]
    [DataRow("HW:7", 3, 5)]
    [DataRow("DYN:RainbowCycle", 3, 5)]
    [DataRow("DYN:FireEmber", 3, 5)]
    public void SyncedAccessoriesUseSupportedEquivalentEffects(string effect, int cover, int key)
    {
        Assert.AreEqual(cover, MainViewModel.SyncedCoverModeFromKeyboardEffect(effect));
        Assert.AreEqual(key, MainViewModel.SyncedModeKeyModeFromKeyboardEffect(effect));
    }
}
