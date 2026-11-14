using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using PreySense.Core.Configuration;
using PreySense.Core.Hardware;
using PreySense.Core.Services;
using PreySense.Frontend.ViewModels;
using PreySense.Gpu;

namespace PreySense.Tests;

[TestClass]
public class GpuClockTuningTests
{
    private sealed class Clocks : IGpuClockTuning
    {
        public int Reads, Writes;
        public bool Success = true;
        public GpuClockOffsets? Applied;
        public bool TryReadClockTuning(out GpuClockTuningState? state, out string status)
        {
            Reads++;
            state = new(new(0, 500), new(0, 3200));
            status = "ready";
            return true;
        }
        public bool TryApplyClockOffsets(GpuClockOffsets offsets, out string status)
        {
            Writes++; Applied = offsets; status = Success ? "verified" : "readback mismatch";
            return Success;
        }
    }

    private static MainViewModel Create(ModeTuningConfig profile, Clocks clocks)
    {
        var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        typeof(MainViewModel).GetField("_hw", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(vm, new MockHardwareService());
        vm.TuningConfigurationReader = () => new PreySenseConfig { Tuning = new() { Balanced = profile } };
        vm.GpuClockTuningController = clocks;
        return vm;
    }

    [TestMethod]
    public void GlobalOverclockingControlOffSkipsSavedModeOffsetsAndValidation()
    {
        var clocks = new Clocks();
        var vm = Create(new() { ApplyGpuClockOffsets = true, GpuCoreClockOffsetMhz = 9999 }, clocks);
        vm.TuningConfigurationReader = () => new PreySenseConfig
        {
            Tuning = new() { EnableGpuOverclockingControl = false, Balanced = new()
                { ApplyGpuClockOffsets = true, GpuCoreClockOffsetMhz = 9999 } }
        };
        Assert.IsTrue(vm.ApplySavedDriverTuning(1).Success);
        Assert.AreEqual(0, clocks.Reads);
        Assert.AreEqual(0, clocks.Writes);
    }

    [TestMethod]
    public void SwitchOff_SkipsCapabilityQueriesAndWritesEvenWithSavedOffsets()
    {
        var clocks = new Clocks();
        var vm = Create(new() { GpuCoreClockOffsetMhz = 400, GpuMemoryClockOffsetMhz = 3000 }, clocks);
        Assert.IsTrue(vm.ApplySavedDriverTuning(1).Success);
        Assert.AreEqual(0, clocks.Reads);
        Assert.AreEqual(0, clocks.Writes);
    }

    [TestMethod]
    public void ClockOnlyProfile_Applies500CoreAndFirmwareMemoryRangeWithoutDriverWrites()
    {
        var clocks = new Clocks();
        var vm = Create(new() { ApplyGpuClockOffsets = true, GpuCoreClockOffsetMhz = 500, GpuMemoryClockOffsetMhz = 3000 }, clocks);
        vm.DriverTuningExecutor = _ => throw new Exception("No CPU/power writes expected");
        Assert.IsTrue(vm.ApplySavedDriverTuning(1).Success);
        Assert.AreEqual(new GpuClockOffsets(500, 3000), clocks.Applied);
    }

    [TestMethod]
    [DataRow(501, 0)] [DataRow(0, 3201)] [DataRow(-201, 0)] [DataRow(-1, 0)] [DataRow(0, -1)]
    public void InvalidOffsets_AreRejectedBeforeCpuWrites(int core, int memory)
    {
        var clocks = new Clocks();
        var vm = Create(new() { ApplyGpuClockOffsets = true, GpuCoreClockOffsetMhz = core, GpuMemoryClockOffsetMhz = memory,
            ApplyCpuLimits = true, CpuPl1Watts = 45, CpuPl2Watts = 80, TauSeconds = 56 }, clocks);
        vm.DriverTuningExecutor = _ => throw new Exception("Invalid clocks must fail preflight");
        Assert.IsFalse(vm.ApplySavedDriverTuning(1).Success);
        Assert.AreEqual(0, clocks.Writes);
    }

    [TestMethod]
    public void ClockFailure_IsNotReportedAsSuccessfulTuning()
    {
        var clocks = new Clocks { Success = false };
        var vm = Create(new() { ApplyGpuClockOffsets = true, GpuCoreClockOffsetMhz = 100, GpuMemoryClockOffsetMhz = 200 }, clocks);
        var result = vm.ApplySavedDriverTuning(1);
        Assert.IsFalse(result.Success);
        Assert.AreEqual("readback mismatch", result.Status);
    }

    [TestMethod]
    public void MissingClockValues_DoNotInventZeroOffsets()
    {
        var clocks = new Clocks();
        var vm = Create(new() { ApplyGpuClockOffsets = true }, clocks);
        Assert.IsFalse(vm.ApplySavedDriverTuning(1).Success);
        Assert.AreEqual(0, clocks.Reads);
        Assert.AreEqual(0, clocks.Writes);
    }

    [TestMethod]
    public void DriverFailure_AfterClocksStillReportsFailure()
    {
        // Clocks apply before watts/CPU so a clock miss never leaves driver limits already written.
        var clocks = new Clocks();
        var vm = Create(new() { ApplyGpuClockOffsets = true, GpuCoreClockOffsetMhz = 100, GpuMemoryClockOffsetMhz = 200,
            ApplyCpuLimits = true, CpuPl1Watts = 45, CpuPl2Watts = 80, TauSeconds = 56 }, clocks);
        vm.DriverTuningExecutor = _ => new(false, "CPU readback failed");
        Assert.IsFalse(vm.ApplySavedDriverTuning(1).Success);
        Assert.AreEqual(1, clocks.Writes);
    }

    [TestMethod]
    public async Task SaveAllSwitchesOff_PersistsSkipWithoutAnyClockAccess()
    {
        string directory = Path.Combine(Path.GetTempPath(), "PreySenseClock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var clocks = new Clocks();
            var vm = Create(new(), clocks);
            typeof(MainViewModel).GetField("_isHardwareTuningEnabled", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(vm, true);
            vm.TuningConfigurationStore = new ConfigurationService(Path.Combine(directory, "preysense.json"));
            vm.TuningConfigurationStore.Update(cfg => {
                cfg.Tuning.Balanced.ApplyGpuClockOffsets = true;
                cfg.Tuning.Balanced.GpuCoreClockOffsetMhz = 100;
                cfg.Tuning.Balanced.GpuMemoryClockOffsetMhz = 3000;
            });
            Assert.IsTrue(await vm.ApplyTuningProfileAsync(1, 0, 0, 0, null, persist: true,
                applyCpuLimits: false));
            var saved = vm.TuningConfigurationStore.GetCurrent().Tuning.Balanced;
            Assert.IsFalse(saved.ApplyGpuClockOffsets);
            Assert.AreEqual(3000, saved.GpuMemoryClockOffsetMhz);
            Assert.AreEqual(0, clocks.Reads);
            Assert.AreEqual(0, clocks.Writes);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void NativeRanges_RoundInwardAndCapOnlyCore()
    {
        Assert.AreEqual(new GpuClockRange(-199, 500), GpuClockRange.FromKilohertz(-199999, 999999, 500));
        Assert.AreEqual(new GpuClockRange(-499, 3299), GpuClockRange.FromKilohertz(-499999, 3299999));
        Assert.IsFalse(new GpuClockTuningState(new(0, 1000), new(0, 5000)).Allows(new(501, 0)));
    }

    [TestMethod]
    public void SliderSteps_StayZeroAlignedAndInsideFirmwareLimits()
    {
        var core = new GpuClockRange(0, 500);
        Assert.AreEqual(new GpuClockRange(0, 500), core.ForStep(5));
        Assert.AreEqual(500, core.Snap(503, 5));
        Assert.AreEqual(0, core.Snap(-3, 5));
        var memory = new GpuClockRange(-950, 4450);
        Assert.AreEqual(new GpuClockRange(-900, 4400), memory.ForStep(100));
        Assert.AreEqual(4400, memory.Snap(4500, 100));
        Assert.AreEqual(0, memory.Snap(0, 100));
    }

    [TestMethod]
    public void Turbo_DefaultsToAcerStockOffsetsDisabled()
    {
        var turbo = new PreySenseConfig().Tuning.Turbo;
        Assert.IsFalse(turbo.ApplyGpuClockOffsets);
        Assert.AreEqual(100, turbo.GpuCoreClockOffsetMhz);
        Assert.AreEqual(200, turbo.GpuMemoryClockOffsetMhz);
        Assert.IsFalse(turbo.HasValues);
    }

    [TestMethod]
    public void Json_DefaultsToOffAndNullAndPreservesLargeMemoryOffset()
    {
        var empty = new ModeTuningConfig();
        Assert.IsFalse(empty.ApplyGpuClockOffsets);
        Assert.IsNull(empty.GpuCoreClockOffsetMhz);
        Assert.IsNull(empty.GpuMemoryClockOffsetMhz);
        var restored = JsonSerializer.Deserialize<ModeTuningConfig>(JsonSerializer.Serialize(new ModeTuningConfig {
            ApplyGpuClockOffsets = true, GpuCoreClockOffsetMhz = 450, GpuMemoryClockOffsetMhz = 3000 }))!;
        Assert.IsTrue(restored.HasValues);
        Assert.AreEqual(3000, restored.GpuMemoryClockOffsetMhz);
    }

    [TestMethod]
    [DataRow(-1, 0)] [DataRow(0, -1)]
    public void Configuration_RejectsNegativeOffsets(int core, int memory)
    {
        var config = new PreySenseConfig();
        config.Tuning.Balanced.GpuCoreClockOffsetMhz = core;
        config.Tuning.Balanced.GpuMemoryClockOffsetMhz = memory;
        Assert.Throws<ArgumentOutOfRangeException>(() => ConfigurationService.Validate(config));
    }

    [TestMethod]
    [DataRow(0x4649)] [DataRow(0x4641)] [DataRow(0x4621)]
    [DataRow(0x4601)] [DataRow(0x4660)] [DataRow(0x4668)]
    public void AlderLakeHostBridges_AreAcceptedOnlyForIntel(int device)
    {
        Assert.IsTrue(PreySenseDriverBackend.IsSupportedHostBridge(((uint)device << 16) | 0x8086));
        Assert.IsFalse(PreySenseDriverBackend.IsSupportedHostBridge(((uint)device << 16) | 0x1022));
    }
}
