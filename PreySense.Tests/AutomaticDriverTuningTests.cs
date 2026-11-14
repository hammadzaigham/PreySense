using System.Reflection;
using System.Runtime.CompilerServices;
using PreySense.Core.Configuration;
using PreySense.Core.Services;
using PreySense.Frontend.ViewModels;

namespace PreySense.Tests;

[TestClass]
public class AutomaticDriverTuningTests
{
    private static MainViewModel Create(PreySenseConfig config)
    {
        foreach (var profile in new[] { config.Tuning.Quiet, config.Tuning.Eco, config.Tuning.Balanced, config.Tuning.Performance, config.Tuning.Turbo })
        {
            profile.CpuPl1Watts ??= 45; profile.CpuPl2Watts ??= 80; profile.TauSeconds ??= 56;
            profile.GpuRuntimeLimitWatts ??= 140;
            profile.CpuBoost ??= true; profile.CpuBoostMode ??= profile.CpuBoost == true ? 1 : 0;
            profile.ApplyCpuLimits = true;
        }
        var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        void Set(string name, object value) => typeof(MainViewModel)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm, value);
        Set("_hw", new MockHardwareService());
        Set("_modeSwitchGate", new SemaphoreSlim(1, 1));
        Set("_modeSwitchLock", new object());
        Set("_isHardwareTuningEnabled", true);
        vm.TuningConfigurationReader = () => config;
        vm.CpuBoostWriter = _ => true; // Keep Windows power-policy writes inside the fake.
        return vm;
    }

    [TestMethod]
    public void GlobalCpuControlOffSkipsSavedModeCpuLimitsDuringModeSelection()
    {
        var cfg = new PreySenseConfig();
        cfg.Tuning.EnableCpuPowerLimitControl = false;
        cfg.Tuning.EnableGpuPowerLimitControl = false;
        var vm = Create(cfg);
        vm.DriverTuningExecutor = _ => throw new AssertFailedException("Disabled mode CPU limits must not write during a game mode switch either.");
        Assert.IsTrue(vm.ApplySavedDriverTuning(1).Success);
    }

    [TestMethod]
    [DataRow(0)] [DataRow(1)] [DataRow(4)] [DataRow(5)] [DataRow(6)]
    public void AutomaticApplication_UsesSavedModeValuesIncludingIndependentGpuOffsets(int mode)
    {
        var config = new PreySenseConfig();
        var profile = mode switch
        {
            0 => config.Tuning.Quiet, 1 => config.Tuning.Balanced, 4 => config.Tuning.Performance,
            5 => config.Tuning.Turbo, _ => config.Tuning.Eco
        };
        profile.CpuPl1Watts = 61; profile.CpuPl2Watts = 103; profile.TauSeconds = 32;
        profile.GpuRuntimeLimitWatts = 145;
        var vm = Create(config);
        MainViewModel.DriverTuningRequest? captured = null;
        vm.DriverTuningExecutor = request => { captured = request; return new(true, "verified"); };
        var result = vm.ApplySavedDriverTuning((byte)mode);
        Assert.IsTrue(result.Success);
        Assert.IsNotNull(captured);
        Assert.AreEqual((byte)mode, captured.Value.Mode);
        Assert.AreEqual(61, captured.Value.Pl1);
        Assert.AreEqual(103, captured.Value.Pl2);
        Assert.AreEqual(32, captured.Value.Tau);
        Assert.AreEqual(145, captured.Value.GpuRuntimeLimitWatts);
        // Re-reading must see newly saved values rather than an old editor draft.
        profile.CpuPl1Watts = 72;
        vm.ApplySavedDriverTuning((byte)mode);
        Assert.AreEqual(72, captured.Value.Pl1);
    }

    [TestMethod]
    public async Task EnableTuning_PersistsForTheNextStartup()
    {
        string directory = Path.Combine(Path.GetTempPath(), "PreySenseTuning-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "preysense.json");
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            typeof(MainViewModel).GetField("_modeSwitchGate", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(vm, new SemaphoreSlim(1, 1));
            typeof(MainViewModel).GetField("_modeSwitchLock", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(vm, new object());
            vm.TuningConfigurationStore = new ConfigurationService(path);
            vm.TuningConfigurationStore.Update(cfg =>
            {
                cfg.Tuning.FactoryDefaults = TuningCalibrationTests.Defaults();
                cfg.Tuning.FactoryStaticCpuPl1Watts = 55;
                cfg.Tuning.FactoryStaticCpuPl2Watts = 140;
            });
            vm.IsHardwareTuningEnabled = true;
            await vm.TuningEnableTask;
            Assert.IsTrue(new ConfigurationService(path).GetCurrent().General.HardwareTuningEnabled);
            vm.IsHardwareTuningEnabled = false;
            Assert.IsFalse(new ConfigurationService(path).GetCurrent().General.HardwareTuningEnabled);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    [DataRow(true)] [DataRow(false)]
    public void StartupRestore_UsesDriverOnlyWhenTuningIsEnabled(bool enabled)
    {
        var vm = Create(new());
        typeof(MainViewModel).GetField("_isHardwareTuningEnabled", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(vm, enabled);
        int calls = 0;
        vm.DriverTuningExecutor = request =>
        {
            calls++;
            Assert.AreEqual((byte)5, request.Mode);
            return new(true, "verified");
        };
        var result = vm.RestoreSavedPowerMode(5);
        Assert.IsTrue(result.Success, result.Status);
        Assert.AreEqual(enabled ? 1 : 0, calls);
    }

    [TestMethod]
    public void StartupRestore_SurfacesDriverInitializationFailure()
    {
        var vm = Create(new());
        vm.DriverTuningExecutor = _ => throw new InvalidOperationException("driver unavailable");
        var result = vm.RestoreSavedPowerMode(5);
        Assert.IsFalse(result.Success);
        Assert.Contains("driver unavailable", result.Status);
    }

    [TestMethod]
    public void InvalidSavedProfile_DoesNotReachDriver()
    {
        var config = new PreySenseConfig();
        config.Tuning.Turbo.CpuPl1Watts = 300;
        var vm = Create(config);
        vm.DriverTuningExecutor = _ => throw new AssertFailedException("Invalid tuning must not reach the driver.");
        var result = vm.ApplySavedDriverTuning(5);
        Assert.IsFalse(result.Success);
        Assert.Contains("out of range", result.Status);
    }

    [TestMethod]
    public async Task ModeSwitchFailure_ReportsDriverErrorAndReleasesGate()
    {
        var vm = Create(new());
        int calls = 0;
        int toasts = 0;
        vm.ModeToastPresenter = (_, _) => toasts++;
        vm.DriverTuningExecutor = _ => { calls++; return new(false, "readback failed"); };
        await vm.ApplyModeSwitchAsync(5);
        Assert.AreEqual(1, calls);
        Assert.Contains("readback failed", vm.DiagnosticMessage);
        Assert.IsFalse(vm.IsBusy);
        await vm.ApplyModeSwitchAsync(4);
        Assert.AreEqual(2, calls, "A failed tuning operation must not retain the mode gate.");
        Assert.AreEqual(0, toasts, "Failed hardware application must not display a success toast.");
    }

    [TestMethod]
    [DataRow(0, "Quiet")] [DataRow(1, "Balanced")] [DataRow(4, "Performance")]
    [DataRow(5, "Turbo")] [DataRow(6, "Eco")]
    public void ConfirmedMode_NotifiesToastAfterUpdatingTheActiveMode(int mode, string expectedName)
    {
        var vm = Create(new());
        int notifications = 0;
        vm.ModeToastPresenter = (actualMode, title) =>
        {
            notifications++;
            Assert.AreEqual((byte)mode, actualMode);
            Assert.AreEqual((byte)mode, vm.ActivePowerMode);
            Assert.AreEqual(expectedName, title);
            Assert.AreEqual(expectedName, vm.PowerModeName);
        };
        vm.NotifyConfirmedPowerMode((byte)mode);
        Assert.AreEqual(1, notifications);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task HardwareModeException_DoesNotEscapeOrRetainTheGate(bool readbackFails)
    {
        var vm = Create(new());
        var hardware = DispatchProxy.Create<IHardwareService, FailingModeHardware>();
        var recorder = (FailingModeHardware)hardware;
        recorder.ReadbackFails = readbackFails;
        typeof(MainViewModel).GetField("_hw", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm, hardware);
        typeof(MainViewModel).GetField("_isHardwareTuningEnabled", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm, false);
        int toasts = 0;
        vm.ModeToastPresenter = (_, _) => toasts++;
        await vm.ApplyModeSwitchAsync(5);
        Assert.Contains("Simulated hardware failure", vm.DiagnosticMessage);
        Assert.AreEqual(readbackFails ? (byte)0xFF : (byte)1, vm.ActivePowerMode);
        await vm.ApplyModeSwitchAsync(4);
        Assert.AreEqual(2, recorder.Attempts, "The failed call must release the mode-switch gate.");
        Assert.AreEqual(0, toasts);
    }

    public class FailingModeHardware : DispatchProxy
    {
        public bool ReadbackFails;
        public int Attempts;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "SetPowerMode") { Attempts++; throw new InvalidOperationException("Simulated hardware failure"); }
            if (method.Name == "TryGetPowerMode")
            {
                if (ReadbackFails) throw new InvalidOperationException("Simulated readback failure");
                args![0] = (byte)1;
                return true;
            }
            return method.ReturnType == typeof(void) ? null : method.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null;
        }
    }

    [TestMethod]
    public async Task SimultaneousModeChanges_SerializeDriverTransactions()
    {
        var vm = Create(new());
        int active = 0, maximum = 0;
        vm.DriverTuningExecutor = _ =>
        {
            int count = Interlocked.Increment(ref active);
            maximum = Math.Max(maximum, count);
            Thread.Sleep(30);
            Interlocked.Decrement(ref active);
            return new(false, "test rejection");
        };
        await Task.WhenAll(vm.ApplyModeSwitchAsync(4), vm.ApplyModeSwitchAsync(5));
        Assert.AreEqual(1, maximum);
    }

    [TestMethod]
    public async Task ManualApply_UsesTheSameDriverExecutorAndReportsPartialFailure()
    {
        var vm = Create(new());
        MainViewModel.DriverTuningRequest? captured = null;
        vm.DriverTuningExecutor = request =>
        {
            captured = request;
            return new(false, "GPU profile applied; CPU limits failed: locked");
        };
        bool success = await vm.ApplyTuningProfileAsync(5, 70, 110, 56, 145, persist: false);
        Assert.IsFalse(success);
        Assert.IsNotNull(captured);
        Assert.AreEqual(70, captured.Value.Pl1);
        Assert.Contains("CPU limits failed: locked", vm.DiagnosticMessage);
        Assert.IsFalse(vm.IsBusy);
    }

    [TestMethod]
    public void ModeChange_AppliesSavedCpuWhenGpuLimitsAreOff()
    {
        var config = new PreySenseConfig();
        config.Tuning.Turbo.CpuPl1Watts = 66;
        config.Tuning.Turbo.CpuPl2Watts = 99;
        config.Tuning.Turbo.TauSeconds = 28;
        config.Tuning.ApplyProchotOffset = true;
        config.Tuning.ProchotOffsetCelsius = 11;
        var hw = new MockHardwareService();
        Assert.AreNotEqual((byte)5, hw.CurrentPowerMode);
        var vm = Create(config);
        config.Tuning.Turbo.ApplyCpuLimits = true;
        config.Tuning.Turbo.GpuRuntimeLimitWatts = null;
        typeof(MainViewModel).GetField("_hw", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(vm, hw);
        MainViewModel.DriverTuningRequest? captured = null;
        vm.DriverTuningExecutor = request => { captured = request; return new(true, "verified"); };

        var result = vm.ApplySavedDriverTuning(5);
        Assert.IsTrue(result.Success, result.Status);
        Assert.AreEqual((byte)5, hw.CurrentPowerMode, "Mode restore must switch Acer mode even when GPU limits are off.");
        Assert.IsNotNull(captured);
        Assert.AreEqual(66, captured.Value.Pl1);
        Assert.AreEqual(99, captured.Value.Pl2);
        Assert.AreEqual(28, captured.Value.Tau);
        Assert.AreEqual(11, captured.Value.ProchotOffsetCelsius);
        Assert.IsTrue(captured.Value.ApplyCpuLimits);
        Assert.IsFalse(captured.Value.ApplyGpuLimits);
    }

    [TestMethod]
    public async Task ManualApply_DoesNotSwitchModeWhenGpuLimitsAreOff()
    {
        var hw = new MockHardwareService();
        byte before = hw.CurrentPowerMode;
        var vm = Create(new());
        typeof(MainViewModel).GetField("_hw", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(vm, hw);
        vm.DriverTuningExecutor = _ => new(true, "verified");
        Assert.IsTrue(await vm.ApplyTuningProfileAsync(5, 60, 100, 56, null, false, 10, true,
            applyCpuLimits: true));
        Assert.AreEqual(before, hw.CurrentPowerMode);
    }
}
