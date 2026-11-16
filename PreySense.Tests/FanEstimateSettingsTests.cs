using PreySense.Core.Configuration;

namespace PreySense.Tests;

[TestClass]
public class FanEstimateSettingsTests
{
    [TestMethod]
    public void SmallEstimateSnapshotTracksCommittedCalibrationWithoutAllocatingConfigClones()
    {
        string path = Path.Combine(Path.GetTempPath(), $"preysense-estimate-{Guid.NewGuid():N}.json");
        try
        {
            var store = new ConfigurationService(path);
            store.Update(config =>
            {
                config.Fans.HasMeasuredFanMaximums = true;
                config.Fans.CpuMaxRpm = 7100;
                config.Fans.GpuMaxRpm = 6500;
            });
            Assert.AreEqual((true, 7100, 6500), store.GetFanEstimateSettings());
            _ = store.GetFanEstimateSettings();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) _ = store.GetFanEstimateSettings();
            Assert.AreEqual(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
