using System.Diagnostics;
using PreySense.Core.Services;
using PreySense.Core.Driver;
using PreySense.Mode;
using PreySense.Overlay;

namespace PreySense.Tests;

[TestClass]
[DoNotParallelize]
public class FallbackPolicyTests
{
    [TestMethod]
    public void MissingBaselineAndPowerRemainUnavailable()
    {
        Assert.IsNull(new GpuPowerOffsets(20, 15).NominalReferenceWatts(null));
        Assert.IsNull(TelemetrySnapshot.Empty.ToHardwareTelemetry().CpuPkgPowerWatts);
        Assert.AreEqual(-1, TelemetrySnapshot.Empty.ToHardwareTelemetry().CpuFanMaxRpm);
        Assert.IsTrue(float.IsNaN(TelemetryGraphValue.Read(TelemetrySnapshot.Empty, "Power", false)));
        Assert.AreEqual(0f, TelemetryGraphValue.Read(TelemetrySnapshot.Empty with { CpuPkgPowerWatts = 0 }, "Power", false));
    }

    [TestMethod]
    public void FailedMemoryRead_DoesNotReuseThePreviousSample()
    {
        long tick = 1;
        bool success = true;
        SystemMemoryMonitor.ConfigureForTesting(() => tick, (ref SystemMemoryMonitor.MEMORYSTATUSEX status) =>
        {
            status.ullTotalPhys = 16UL * 1024 * 1024 * 1024;
            status.ullAvailPhys = 8UL * 1024 * 1024 * 1024;
            status.dwMemoryLoad = 50;
            return success;
        });
        try
        {
            Assert.IsNotNull(SystemMemoryMonitor.GetMemoryInfo());
            tick += Stopwatch.Frequency;
            success = false;
            Assert.IsNull(SystemMemoryMonitor.GetMemoryInfo());
        }
        finally { SystemMemoryMonitor.Reset(); }
    }

    [TestMethod]
    public void UnknownHardwareModes_AreRejectedInsteadOfSelectingBalanced()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ProfileManager.SourceProfileMode(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProfileManager.ModeToProfileName(255));
        Assert.Throws<ArgumentOutOfRangeException>(() => WindowsPowerPolicy.OverlaySchemeForMode(255));
    }
}
