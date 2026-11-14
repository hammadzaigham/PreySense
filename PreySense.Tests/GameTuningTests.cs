using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml.Linq;
using PreySense.Core.Configuration;
using PreySense.Core.Services;
using PreySense.Frontend.ViewModels;
using PreySense.Mode;

namespace PreySense.Tests;

[TestClass]
public class GameTuningTests
{
    private static (MainViewModel Vm, PreySenseConfig Config, Action<byte> Select) Create()
    {
        var config = new PreySenseConfig();
        config.Tuning.GpuBasePowerWatts = 85;
        foreach (string mode in new[] { "Eco", "Quiet", "Balanced", "Performance", "Turbo" })
            config.Tuning.FactoryDefaults[mode] = new()
            {
                CpuPl1Watts = 45, CpuPl2Watts = 80, TauSeconds = 56,
                GpuDeltaWatts = 40, GpuDynamicBoostWatts = 15, PlatformPowerOffsetWatts = 25
            };
        var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        var hardware = new MockHardwareService();
        void Set(string name, object value) => typeof(MainViewModel).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, value);
        Set("_hw", hardware);
        Set("_modeSwitchGate", new SemaphoreSlim(1, 1));
        Set("_modeSwitchLock", new object());
        Set("_gameModeSession", new GameModeSession());
        Set("_isHardwareTuningEnabled", true);
        Set("_pendingHardwareMode", (byte)0xFF);
        void Select(byte mode) { Set("_activePowerMode", mode); hardware.SetPowerMode(mode); }
        Select(1);
        vm.TuningConfigurationReader = () => config;
        vm.GameModeSwitcher = mode => { Select(mode); return Task.FromResult(true); };
        return (vm, config, Select);
    }

    private static GameProfile Game() => new()
    {
        PowerMode = 5, ApplyCpuLimits = true, CpuPl1Watts = 65, CpuPl2Watts = 95, TauSeconds = 28,
        ApplyGpuLimits = true, GpuPowerWatts = 150, ApplyCpuBoost = true, CpuBoostMode = 0
    };

    [TestMethod]
    public void ClearingGameSelectionClearsDisplayedPerformanceMode()
    {
        var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        vm.SelectedGameProfile = new GameProfile { PowerMode = 5 };
        Assert.AreEqual("Turbo", vm.SelectedGameModeName);
        vm.SelectedGameProfile = null;
        Assert.AreEqual("", vm.SelectedGameModeName);
    }

    [TestMethod]
    public void NewGamesDefaultToTurboAndSavedModesRemainIntact()
    {
        Assert.AreEqual((byte)5, new GameProfile().PowerMode);
        Assert.AreEqual((byte)5, JsonSerializer.Deserialize<GameProfile>("{}")!.PowerMode);
        Assert.AreEqual((byte)1, JsonSerializer.Deserialize<GameProfile>("{\"PowerMode\":1}")!.PowerMode);
    }

    [TestMethod]
    public void GameListAlwaysReservesFiveRows()
    {
        var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        Assert.AreEqual(162d, vm.GameListHeight);
    }

    [TestMethod]
    public async Task InvalidOrUncalibratedOverridesFailBeforeAnyReadOrWrite()
    {
        var (vm, config, _) = Create();
        vm.GameModeSwitcher = _ => throw new AssertFailedException("No mode writes before preflight.");
        vm.GameGpuProfileReader = _ => throw new AssertFailedException("No hardware reads before preflight.");
        var game = Game(); game.CpuPl2Watts = 5;
        Assert.IsFalse(await vm.ApplyGameProfileWithOverridesAsync(game));
        game = Game(); config.Tuning.FactoryDefaults.Clear();
        Assert.IsFalse(await vm.ApplyGameProfileWithOverridesAsync(game));
        Assert.IsFalse(vm.TryCreateGameTuningPlan(new() { PowerMode = 3 }, out _, out _));
    }

    [TestMethod]
    [DataRow((byte)1)]
    [DataRow((byte)5)]
    [DataRow((byte)6)]
    public async Task GameExitRestoresExactGpuAndBoostWithoutChangingSavedProfiles(byte finalMode)
    {
        var (vm, config, select) = Create();
        var original = new MainViewModel.GameGpuProfile(5, 35, 10, 30);
        var boost = new CpuBoostPolicySnapshot(Guid.NewGuid(), 4, 2);
        string saved = JsonSerializer.Serialize(config.Tuning);
        var writes = new List<string>();
        vm.GameGpuProfileReader = mode => { Assert.AreEqual((byte)5, mode); return original; };
        vm.GameBoostPolicyReader = () => boost;
        vm.GameGpuProfileRestorer = snapshot => { Assert.AreEqual(original, snapshot); writes.Add("gpu restore"); return new(true, "ok"); };
        vm.GameBoostPolicyRestorer = snapshot => { Assert.AreEqual(boost, snapshot); writes.Add("boost restore"); return true; };
        vm.DriverTuningExecutor = request => { writes.Add(request.Pl1 == 65 ? "apply" : "cpu restore"); return new(true, "ok"); };
        vm.CpuBoostWriter = value => { Assert.AreEqual(0, value); writes.Add("boost off"); return true; };
        Assert.IsTrue(await vm.ApplyGameProfileWithOverridesAsync(Game()), vm.GameModeStatus);
        if (finalMode != 1) select(finalMode); // Manual/AC change, or original game mode still active.
        await vm.RestoreModeAfterGameAsync();
        CollectionAssert.AreEqual(new[] { "apply", "boost off", "gpu restore", "cpu restore", "boost restore" }, writes);
        Assert.AreEqual(finalMode == 5 ? (byte)1 : finalMode, vm.ActivePowerMode);
        Assert.AreEqual(saved, JsonSerializer.Serialize(config.Tuning));
    }

    [TestMethod]
    public async Task PartialApplyFailureRollsBackAndDoesNotCommitAGameSession()
    {
        var (vm, _, _) = Create();
        vm.GameGpuProfileReader = _ => new(5, 40, 15, 25);
        vm.GameBoostPolicyReader = () => new(Guid.NewGuid(), 3, 1);
        int restores = 0;
        vm.GameGpuProfileRestorer = _ => { restores++; return new(true, "ok"); };
        vm.GameBoostPolicyRestorer = _ => { restores++; return true; };
        vm.DriverTuningExecutor = request => new(request.Pl1 != 65, "simulated CPU failure after GPU staging");
        vm.CpuBoostWriter = _ => throw new AssertFailedException("Boost must not be written after driver failure.");
        Assert.IsFalse(await vm.ApplyGameProfileWithOverridesAsync(Game()));
        Assert.AreEqual(2, restores);
        Assert.AreEqual((byte)1, vm.ActivePowerMode);
        var session = (GameModeSession)typeof(MainViewModel).GetField("_gameModeSession", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
        Assert.IsNull(session.End(5));
    }

    [TestMethod]
    public void CleanupTriesCpuAndBoostEvenWhenGpuRestoreThrows()
    {
        var (vm, _, _) = Create();
        Assert.IsTrue(vm.TryCreateGameTuningPlan(Game(), out var plan, out _));
        vm.GameGpuProfileRestorer = _ => throw new IOException("GPU unavailable");
        int attempts = 0;
        vm.DriverTuningExecutor = _ => { attempts++; return new(true, "ok"); };
        vm.GameBoostPolicyRestorer = _ => { attempts++; return true; };
        var result = vm.RestoreGameTuningPlan(plan, new(5, 40, 15, 25), new(Guid.NewGuid(), 4, 2));
        Assert.IsFalse(result.Success);
        Assert.AreEqual(2, attempts);
    }

    [TestMethod]
    public void BoostRestorePreservesNewerIndependentUserEdits()
    {
        Guid scheme = Guid.NewGuid();
        var original = new CpuBoostPolicySnapshot(scheme, 4, 2);
        Assert.AreEqual(original, WindowsCpuBoostPolicy.MergeTemporaryRestore(original, new(scheme, 0, 0), 0));
        Assert.AreEqual(new CpuBoostPolicySnapshot(scheme, 6, 2),
            WindowsCpuBoostPolicy.MergeTemporaryRestore(original, new(scheme, 6, 0), 0));
    }

    [TestMethod]
    public void GameOverridesRoundTripAndCopiesKeepUnsavedEditsOutOfTheWatcher()
    {
        var game = Game();
        var copy = game.Copy(); copy.CpuPl1Watts = 100;
        Assert.AreEqual(65, game.CpuPl1Watts);
        var loaded = JsonSerializer.Deserialize<GameProfile>(JsonSerializer.Serialize(game))!;
        Assert.AreEqual(65, loaded.CpuPl1Watts);
        Assert.AreEqual(150, loaded.GpuPowerWatts);
        Assert.AreEqual(0, loaded.CpuBoostMode);
        Assert.IsTrue(loaded.ApplyCpuLimits && loaded.ApplyGpuLimits && loaded.ApplyCpuBoost);
    }

    [TestMethod]
    public async Task PendingRestoreFailureCannotBeOverwrittenByAnotherGame()
    {
        var (vm, _, _) = Create();
        int reads = 0;
        vm.GameGpuProfileReader = _ => { reads++; return new(5, 40, 15, 25); };
        vm.GameBoostPolicyReader = () => new(Guid.NewGuid(), 4, 2);
        vm.GameBoostPolicyRestorer = _ => true;
        vm.CpuBoostWriter = _ => true;
        vm.DriverTuningExecutor = _ => new(true, "ok");
        bool allowRestore = false;
        vm.GameGpuProfileRestorer = _ => new(allowRestore, "GPU restoration failed");
        Assert.IsTrue(await vm.ApplyGameProfileWithOverridesAsync(Game()));
        Assert.IsFalse(await vm.RestoreModeAfterGameAsync());
        Assert.IsFalse(await vm.ApplyGameProfileWithOverridesAsync(Game()));
        Assert.AreEqual(1, reads, "A pending rollback must retain the only original snapshot.");
        allowRestore = true;
        Assert.IsTrue(await vm.RestoreModeAfterGameAsync());
        Assert.AreEqual(1, reads);
    }

    [TestMethod]
    public async Task ReapplyingTrackedGameRestoresBeforeTakingNewSnapshotsAndShutdownUndoesIt()
    {
        var (vm, _, _) = Create();
        var originalGpu = new MainViewModel.GameGpuProfile(5, 35, 10, 30);
        var gpu = originalGpu;
        var originalBoost = new CpuBoostPolicySnapshot(Guid.NewGuid(), 4, 2);
        var boost = originalBoost;
        int reads = 0;
        vm.GameGpuProfileReader = _ => { reads++; Assert.AreEqual(originalGpu, gpu); return gpu; };
        vm.GameGpuProfileRestorer = snapshot => { gpu = snapshot; return new(true, "ok"); };
        vm.GameBoostPolicyReader = () => { Assert.AreEqual(originalBoost, boost); return boost; };
        vm.GameBoostPolicyRestorer = snapshot => { boost = snapshot; return true; };
        vm.DriverTuningExecutor = request => { if (request.ApplyGpuLimits) gpu = new(5, 50, 15, 30); return new(true, "ok"); };
        vm.CpuBoostWriter = value => { boost = boost with { AcMode = (uint)value, DcMode = (uint)value }; return true; };
        Assert.IsTrue(await vm.ApplyGameProfileWithOverridesAsync(Game()));
        Assert.IsTrue(await vm.ApplyGameProfileWithOverridesAsync(Game()), "Resume/reapply must not snapshot its own override.");
        Assert.AreEqual(2, reads);
        await vm.PrepareGameModeShutdownAsync();
        Assert.AreEqual(originalGpu, gpu);
        Assert.AreEqual(originalBoost, boost);
        Assert.AreEqual((byte)1, vm.ActivePowerMode);
    }

    [TestMethod]
    public void GamesUiAutoSizesHidesPathsAndLimitsItsListToFiveRows()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var document = System.Xml.Linq.XDocument.Load(Path.Combine(root, "PreySense.Frontend", "Views", "GamesWindow.xaml"));
        Assert.AreEqual("Height", document.Root!.Attribute("SizeToContent")!.Value);
        Assert.IsFalse(document.Descendants().Attributes().Any(a => a.Value.Contains("ExecutablePath")));
        var list = document.Descendants().Single(e => e.Name.LocalName == "ListBox");
        Assert.AreEqual("162", list.Attribute("MaxHeight")!.Value);
        Assert.AreEqual("{Binding GameListHeight}", list.Attribute("Height")!.Value);
        Assert.AreEqual("Auto", list.Attribute("ScrollViewer.VerticalScrollBarVisibility")!.Value);
        Assert.IsTrue(list.Descendants().Any(e => e.Name.LocalName == "Setter"
            && e.Attribute("Property")?.Value == "Height" && e.Attribute("Value")?.Value == "32"));
    }
    [TestMethod]
    [DataRow(0)] [DataRow(6)]
    public async Task GameBoostAlwaysAppliesAndRestoresWithGlobalTuningOn(int boostMode)
    {
        var (vm, config, _) = Create(); config.Tuning.EnableCpuBoostTuningControl = true;
        var game = new GameProfile { PowerMode = 5, CpuBoostMode = boostMode, ApplyCpuBoost = false };
        var previous = new CpuBoostPolicySnapshot(Guid.NewGuid(), 4, 2);
        int writes = 0, restores = 0;
        vm.GameBoostPolicyReader = () => previous;
        vm.GameBoostPolicyRestorer = snapshot => { restores++; Assert.AreEqual(previous, snapshot); return true; };
        vm.CpuBoostWriter = value => { writes++; Assert.AreEqual(boostMode, value); return true; };
        vm.DriverTuningExecutor = _ => throw new AssertFailedException("A boost-only game must skip driver writes.");
        Assert.IsTrue(await vm.ApplyGameProfileWithOverridesAsync(game), vm.GameModeStatus);
        Assert.AreEqual(1, writes);
        Assert.IsTrue(await vm.RestoreModeAfterGameAsync(), vm.GameModeStatus);
        Assert.AreEqual(1, restores);
    }

    [TestMethod]
    public void GameGpuOffsetsAreIndependentAndIncludeBoth255Endpoints()
    {
        var (vm, _, _) = Create();
        var game = Game(); game.GpuDeltaWatts = 255; game.GpuDynamicBoostWatts = 25; game.PlatformPowerOffsetWatts = 255;
        Assert.IsTrue(vm.TryCreateGameTuningPlan(game, out var plan, out string status), status);
        Assert.AreEqual(new PreySense.Core.Driver.GpuPowerOffsets(255, 25, 255), plan.Apply.GpuOffsets);
        Assert.IsNull(plan.Apply.GpuRuntimeLimitWatts);
        var loaded = JsonSerializer.Deserialize<GameProfile>(JsonSerializer.Serialize(game))!;
        Assert.AreEqual(255, loaded.GpuDeltaWatts); Assert.AreEqual(25, loaded.GpuDynamicBoostWatts);
        Assert.AreEqual(255, loaded.PlatformPowerOffsetWatts);
        game.GpuDynamicBoostWatts = 30;
        Assert.IsFalse(vm.TryCreateGameTuningPlan(game, out _, out _));
    }

    [TestMethod]
    public void GamesControlsMatchModeOrderTypesAndTauLadder()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var document = XDocument.Load(Path.Combine(root, "PreySense.Frontend", "Views", "GamesWindow.xaml"));
        string source = document.ToString();
        Assert.IsLessThan(source.IndexOf("Title=\"CPU Power Limits\"", StringComparison.Ordinal), source.IndexOf("Title=\"CPU Boost\"", StringComparison.Ordinal));
        Assert.IsLessThan(source.IndexOf("Title=\"GPU Power Limits\"", StringComparison.Ordinal), source.IndexOf("Title=\"CPU Power Limits\"", StringComparison.Ordinal));
        Assert.DoesNotContain("ApplyCpuBoost", source);
        Assert.DoesNotContain("Name=\"GameBoost\"", source);
        Assert.AreEqual(2, document.Descendants().Count(e => e.Name.LocalName == "SettingValueRow"));
        var sliders = document.Descendants().Where(e => e.Name.LocalName == "Slider").ToArray();
        CollectionAssert.AreEqual(new[] { "11", "255", "25", "255" }, sliders.Select(e => (string)e.Attribute("Maximum")!).ToArray());
        Assert.AreEqual(449, PreySense.Core.Driver.CpuPowerLimitConfiguration.TauSecondsForIndex(11));
        Assert.AreEqual("∞", PreySense.Core.Driver.CpuPowerLimitConfiguration.FormatTauSelection(449));
    }

    [TestMethod]
    public async Task DisabledGlobalSectionsSkipGameValidationReadsAndWritesButPreserveDrafts()
    {
        var (vm, config, _) = Create();
        config.Tuning.EnableCpuPowerLimitControl = false;
        config.Tuning.EnableGpuPowerLimitControl = false;
        config.Tuning.EnableCpuBoostTuningControl = false;
        config.Tuning.FactoryDefaults.Clear();
        var game = Game(); game.CpuPl1Watts = -1; game.GpuPowerWatts = -1; game.CpuBoostMode = 100;
        vm.GameGpuProfileReader = _ => throw new AssertFailedException("Disabled GPU section must not read hardware.");
        vm.GameBoostPolicyReader = () => throw new AssertFailedException("Disabled boost must not snapshot Windows policy.");
        vm.GameBoostPolicyRestorer = _ => throw new AssertFailedException("Disabled boost must not restore Windows policy.");
        vm.CpuBoostWriter = _ => throw new AssertFailedException("Disabled boost must not write Windows policy.");
        vm.DriverTuningExecutor = _ => throw new AssertFailedException("Disabled sections must not write driver registers.");
        Assert.IsTrue(vm.TryCreateGameTuningPlan(game, out var plan, out string status), status);
        Assert.IsFalse(plan.Apply.ApplyCpuLimits); Assert.IsFalse(plan.Apply.ApplyGpuLimits); Assert.IsNull(plan.CpuBoost);
        Assert.IsTrue(await vm.ApplyGameProfileWithOverridesAsync(game), vm.GameModeStatus);
        Assert.AreEqual((byte)5, vm.ActivePowerMode);
        Assert.IsTrue(await vm.RestoreModeAfterGameAsync(), vm.GameModeStatus);
        Assert.AreEqual((byte)1, vm.ActivePowerMode);
        Assert.IsTrue(game.ApplyCpuLimits && game.ApplyGpuLimits);
        Assert.AreEqual(-1, game.CpuPl1Watts); Assert.AreEqual(100, game.CpuBoostMode);
    }

    [TestMethod]
    [DataRow(true, false, false)] [DataRow(false, true, false)] [DataRow(false, false, true)]
    public void GamePlanIncludesOnlyGloballyEnabledSections(bool cpu, bool gpu, bool boost)
    {
        var (vm, config, _) = Create();
        config.Tuning.EnableCpuPowerLimitControl = cpu; config.Tuning.EnableGpuPowerLimitControl = gpu;
        config.Tuning.EnableCpuBoostTuningControl = boost;
        Assert.IsTrue(vm.TryCreateGameTuningPlan(Game(), out var plan, out string status), status);
        Assert.AreEqual(cpu, plan.Apply.ApplyCpuLimits); Assert.AreEqual(gpu, plan.Apply.ApplyGpuLimits);
        Assert.AreEqual(boost, plan.CpuBoost.HasValue);
    }

}
