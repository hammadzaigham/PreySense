using PreySense.Core.Configuration;
using PreySense.Core.Services;
using PreySense.Frontend.ViewModels;

namespace PreySense.Tests;

[TestClass]
[DoNotParallelize]
public class StartupSourceModeTests
{
    [TestMethod]
    [DataRow(PowerSourceKind.Ac, (byte)4)]
    [DataRow(PowerSourceKind.Battery, (byte)6)]
    [DataRow(PowerSourceKind.Unknown, (byte)1)]
    public void StartupUsesCurrentSourceInsteadOfLastSession(PowerSourceKind source, byte expected)
    {
        var store = ConfigurationService.Instance;
        var original = store.GetCurrent();
        try
        {
            store.Update(cfg =>
            {
                cfg.Session.ActivePowerMode = 5;
                cfg.Session.AcPowerMode = 4;
                cfg.Session.BatteryPowerMode = 6;
                cfg.Battery.BatteryEnergySaver = false;
                cfg.Games.Enabled = false;
            });
            using var vm = new MainViewModel(new SourceHardware(source));
            Assert.AreEqual(expected, vm.ActivePowerMode);
            Assert.AreNotEqual((byte)5, vm.ActivePowerMode);
        }
        finally { store.Save(original); }
    }

    private sealed class SourceHardware(PowerSourceKind source) : MockHardwareService, IHardwareService
    {
        public new PowerSourceKind GetPowerSource() => source;
    }
}
