using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using PreySense.Core.Configuration;
using PreySense.Frontend.ViewModels;
using PreySense.Mode;

namespace PreySense.Tests;

[TestClass]
[DoNotParallelize]
public class FanControlDelayTests
{
    [TestMethod]
    public void MissingGlobalDelayDefaultsToZero()
    {
        Assert.AreEqual(0, JsonSerializer.Deserialize<FanConfig>("{}")!.ControlDelaySeconds);
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(6)]
    public void InvalidGlobalDelayIsRejected(int delay)
    {
        var config = new PreySenseConfig();
        config.Fans.ControlDelaySeconds = delay;
        Assert.Throws<ArgumentOutOfRangeException>(() => ConfigurationService.Validate(config));
    }

    [TestMethod]
    public void EveryModeUsesGlobalDelayAndSavingACurveDoesNotChangeIt()
    {
        var store = ConfigurationService.Instance;
        var original = store.GetCurrent();
        try
        {
            store.Update(config =>
            {
                config.Fans.ControlDelaySeconds = 4;
                config.Fans.Quiet.RampUpSeconds = 1;
                config.Fans.Eco.RampUpSeconds = 2;
                config.Fans.Balanced.RampUpSeconds = 3;
                config.Fans.Performance.RampUpSeconds = 0;
                config.Fans.Turbo.RampUpSeconds = 5;
            });
            foreach (byte mode in new byte[] { 0, 6, 1, 4, 5 })
                Assert.AreEqual(4, FanCurveStorage.Load(mode).RampUpSeconds);
            var draft = FanCurveStorage.Load(1);
            draft.RampUpSeconds = 1;
            FanCurveStorage.Save(1, draft);
            Assert.AreEqual(4, store.GetCurrent().Fans.ControlDelaySeconds);
            Assert.AreEqual(3, store.GetCurrent().Fans.Balanced.RampUpSeconds);
            Assert.AreEqual(4, FanCurveStorage.Load(1).RampUpSeconds);
        }
        finally { store.Save(original); }
    }

    [TestMethod]
    public void SettingsEditUpdatesRunningCustomCurveAndResetsPendingTransitionWithoutHardwareWrites()
    {
        var store = ConfigurationService.Instance;
        var original = store.GetCurrent();
        try
        {
            store.Update(config => config.Fans.ControlDelaySeconds = 1);
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var follower = new FanCurveFollower();
            var profile = FanCurveProfile.CreateDefault(1);
            profile.ControlMode = FanControlMode.Custom;
            typeof(MainViewModel).GetField("_fanControlGate", flags)!.SetValue(vm, new object());
            typeof(MainViewModel).GetField("_fanFollower", flags)!.SetValue(vm, follower);
            typeof(MainViewModel).GetField("_runningFanProfile", flags)!.SetValue(vm, profile);
            Assert.IsGreaterThan(0, follower.Evaluate(100, 100, profile.Stages).Stage);
            vm.InitializeFanSettings();
            Assert.AreEqual(1, vm.FanControlDelaySeconds);
            vm.FanControlDelaySeconds = 3;
            Assert.AreEqual(3, store.GetCurrent().Fans.ControlDelaySeconds);
            Assert.AreEqual(3, profile.RampUpSeconds);
            Assert.AreEqual(0, follower.Evaluate(100, 100, profile.Stages, 3, 1000).Stage);
            // No hardware service is installed on this isolated VM: saving never invokes one.
        }
        finally { store.Save(original); }
    }
}
