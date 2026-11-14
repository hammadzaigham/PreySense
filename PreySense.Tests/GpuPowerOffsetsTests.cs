using PreySense.Core.Driver;

namespace PreySense.Tests;

[TestClass]
public class GpuPowerOffsetsTests
{
    [TestMethod]
    [DoNotParallelize]
    public void SavedRuntimeLimitsAreReadFreshWhileUnsavedEditsArePreserved()
    {
        string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PreySenseLimits-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);
        var store = new PreySense.Core.Configuration.ConfigurationService(System.IO.Path.Combine(directory, "preysense.json"));
        try
        {
            var vm = (PreySense.Frontend.ViewModels.MainViewModel)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(PreySense.Frontend.ViewModels.MainViewModel));
            var drafts = new Dictionary<int, int?>();
            typeof(PreySense.Frontend.ViewModels.MainViewModel).GetField("_gpuRuntimeLimits", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(vm, drafts);
            vm.TuningConfigurationStore = store;
            store.Update(cfg =>
            {
                cfg.Tuning.Balanced.GpuRuntimeLimitWatts = 140;
            });
            Assert.AreEqual(140, vm.GpuRuntimeLimitForModeIndex(2));
            store.Update(cfg => cfg.Tuning.Balanced.GpuRuntimeLimitWatts = 145);
            Assert.AreEqual(145, vm.GpuRuntimeLimitForModeIndex(2));
            vm.StoreGpuRuntimeLimit(2, 150);
            Assert.AreEqual(150, vm.GpuRuntimeLimitForModeIndex(2));
            store.Update(cfg => cfg.Tuning.Balanced.GpuRuntimeLimitWatts = 155);
            Assert.AreEqual(150, vm.GpuRuntimeLimitForModeIndex(2), "Newly saved values must not overwrite an unsaved editor draft.");
        }
        finally
        {
            System.IO.Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void PlatformBudgetIsIndependentOfGpuCeiling()
    {
        var value = new GpuPowerOffsets(35, 20);
        Assert.IsTrue(value.IsValid);
        Assert.AreEqual(140, value.NominalReferenceWatts(85));

    }

    [TestMethod]
    public void ByteFieldsRejectOverflowRatherThanWrapping()
    {
        Assert.IsTrue(new GpuPowerOffsets(255, 25, 255).IsValid);
        Assert.IsFalse(new GpuPowerOffsets(255, 26).IsValid);
        Assert.IsFalse(new GpuPowerOffsets(256, 15).IsValid);
        Assert.IsFalse(new GpuPowerOffsets(50, 256).IsValid);
        Assert.IsFalse(new GpuPowerOffsets(-1, 15).IsValid);
    }

    [TestMethod]
    public void LegacyProfilesRetainTheExistingSplit()
    {
        Assert.AreEqual(new GpuPowerOffsets(0, 0), GpuPowerOffsets.FromLegacyTarget(85, 85));
        Assert.AreEqual(new GpuPowerOffsets(15, 0), GpuPowerOffsets.FromLegacyTarget(100, 85));
        Assert.AreEqual(new GpuPowerOffsets(30, 15), GpuPowerOffsets.FromLegacyTarget(130, 85));
        Assert.IsTrue(GpuPowerOffsets.FromLegacyTarget(999, 85).IsValid);
    }

    [TestMethod]
    public void GpuPowerLimitControlExposesIndependentOffsets()
    {
        string root = Root();
        string xaml = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "PreySense.Frontend", "Views", "ModeSettingsWindow.xaml"));
        Assert.Contains("GPU Power Limit", xaml);
        Assert.Contains("SwitchApplyGpuLimits", xaml);
        Assert.Contains("BorderGpuPowerLimit", xaml);
        Assert.Contains("SliderPlatformPowerOffset", xaml);
        Assert.Contains("SliderGpuDelta", xaml);
        Assert.Contains("SliderGpuDynamicBoost", xaml);
    }

    private static string Root()
    {
        var directory = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "PreySense.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Solution root not found");
    }


}
