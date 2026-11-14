using System.Reflection;
using System.Runtime.CompilerServices;
using PreySense.Core.Configuration;
using PreySense.Core.Services;
using PreySense.Frontend.ViewModels;
using PreySense.Mode;

namespace PreySense.Tests;

[TestClass]
[DoNotParallelize]
public class FanCalibrationTests
{
    [TestMethod]
    [DataRow("success")]
    [DataRow("cancel")]
    [DataRow("invalid")]
    [DataRow("failure")]
    [DataRow("shutdown")]
    public async Task CalibrationAlwaysRestoresAutoAndOnlySavesValidPair(string outcome)
    {
        var store = ConfigurationService.Instance;
        var before = store.GetCurrent();
        try
        {
            store.Update(cfg => { cfg.Fans.Balanced.ControlMode = 0; cfg.Fans.CpuMaxRpm = 6400; cfg.Fans.GpuMaxRpm = 6400; });
            var vm = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
            void Set(string field, object value) => typeof(MainViewModel).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm, value);
            var fans = new Fans();
            Set("_hw", fans); Set("_fanControlGate", new object()); Set("_fanFollower", new FanCurveFollower());
            Set("_modeSwitchGate", new SemaphoreSlim(1, 1));
            Set("_runningFanProfile", FanCurveProfile.CreateDefault(1));
            using var cancellation = new CancellationTokenSource();
            Task? shutdown = null;
            bool spunUp = false;
            vm.FanCalibrationWait = (duration, token) =>
            {
                Assert.AreEqual(TimeSpan.FromSeconds(20), duration);
                Assert.AreEqual("custom:100/100", fans.Calls.Single());
                spunUp = true;
                return Task.CompletedTask;
            };
            vm.FanCalibrationSampler = token =>
            {
                Assert.AreEqual("Calibrating Fan RPM...", vm.FanRpmCalibrationStatus);
                Assert.IsTrue(spunUp, "Measurement must wait for the full spin-up period.");
                Assert.AreEqual("custom:100/100", fans.Calls.Single());
                if (outcome == "shutdown") { shutdown = vm.PrepareFanCalibrationShutdownAsync(); token.ThrowIfCancellationRequested(); }
                if (outcome == "cancel") { cancellation.Cancel(); token.ThrowIfCancellationRequested(); }
                if (outcome == "failure") throw new InvalidOperationException("sensor failed");
                return Task.FromResult(outcome == "invalid" ? (0, 7000) : (6712, 7035));
            };
            bool saved = await vm.CalibrateFanRpmsAsync(cancellation.Token);
            if (shutdown is not null) await shutdown.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.AreEqual(outcome == "success", saved);
            if (saved) Assert.AreEqual("Fan RPM Calibrated. CPU: 6800 RPM. GPU: 7100 RPM.", vm.FanRpmCalibrationStatus);
            Assert.AreEqual("mode:0", fans.Calls.Last());
            Assert.IsFalse(vm.IsFanRpmCalibrationRunning);
            var config = store.GetCurrent();
            Assert.AreEqual(saved ? 6800 : 6400, config.Fans.CpuMaxRpm);
            Assert.AreEqual(saved ? 7100 : 6400, config.Fans.GpuMaxRpm);
            Assert.AreEqual(0, config.Fans.Balanced.ControlMode);
        }
        finally { store.Save(before); TelemetryCoordinator.SetDemand(TelemetryDemand.FanController, false); }
    }

    [TestMethod]
    [DataRow(6120, 6200)]
    [DataRow(6380, 6400)]
    [DataRow(6400, 6400)]
    public void MaximumRpmRoundsUpToHundred(int measured, int expected) =>
        Assert.AreEqual(expected, MainViewModel.RoundFanMaximumRpm(measured));

    private sealed class Fans : MockHardwareService, IHardwareService
    {
        public List<string> Calls { get; } = [];
        public new byte CurrentPowerMode => 1;
        public bool TryGetPowerMode(out byte mode) { mode = 1; return true; }
        public new bool SetFanSpeeds(byte cpu, byte gpu) { Calls.Add($"custom:{cpu}/{gpu}"); return true; }
        public new bool SetFanMode(byte cpu, byte gpu) { Calls.Add($"mode:{cpu}"); return true; }
    }
}
