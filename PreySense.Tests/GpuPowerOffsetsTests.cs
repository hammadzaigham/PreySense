using PreySense.Core.Driver;

namespace PreySense.Tests;

[TestClass]
public class GpuPowerOffsetsTests
{
    [TestMethod]
    [DoNotParallelize]
    public void SavedOffsetsAreReadFreshWhileUnsavedEditsArePreserved()
    {
        string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PreySenseOffsets-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);
        var store = new PreySense.Core.Configuration.ConfigurationService(System.IO.Path.Combine(directory, "preysense.json"));
        try
        {
            var vm = (PreySense.Frontend.ViewModels.MainViewModel)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(PreySense.Frontend.ViewModels.MainViewModel));
            var drafts = new Dictionary<int, GpuPowerOffsets>();
            typeof(PreySense.Frontend.ViewModels.MainViewModel).GetField("_gpuOffsets", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(vm, drafts);
            vm.TuningConfigurationStore = store;
            store.Update(cfg =>
            {
                cfg.Tuning.Balanced.GpuDeltaWatts = 10;
                cfg.Tuning.Balanced.GpuDynamicBoostWatts = 15;
                cfg.Tuning.Balanced.PlatformPowerOffsetWatts = 5;
            });
            Assert.AreEqual(new GpuPowerOffsets(10, 15, 5), vm.GpuOffsetsForMode(2, 130));
            store.Update(cfg => cfg.Tuning.Balanced.GpuDeltaWatts = 20);
            Assert.AreEqual(new GpuPowerOffsets(20, 15, 5), vm.GpuOffsetsForMode(2, 130));
            vm.StoreGpuOffsets(2, new GpuPowerOffsets(30, 5, 10));
            Assert.AreEqual(new GpuPowerOffsets(30, 5, 10), vm.GpuOffsetsForMode(2, 130));
            store.Update(cfg => cfg.Tuning.Balanced.GpuDeltaWatts = 40);
            Assert.AreEqual(new GpuPowerOffsets(30, 5, 10), vm.GpuOffsetsForMode(2, 130), "Newly saved values must not overwrite an unsaved editor draft.");
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
        Assert.IsTrue(new GpuPowerOffsets(255, 255).IsValid);
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
    public void PlatformPowerOffsetControlIsPresentWithGpuOffsets()
    {
        string root = Root();
        string xaml = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "PreySense.Frontend", "Views", "ModeSettingsWindow.xaml"));
        Assert.Contains("Platform Power Limit", xaml);
        Assert.Contains("SliderPlatformPowerOffset", xaml);
        Assert.Contains("ModePlatformPowerOffsetValue", xaml);
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
