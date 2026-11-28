using PreySense.Core.Hardware;
using PreySense.Rgb;

namespace PreySense.Tests;

[TestClass]
public class HardwareKeyboardIdleTimeoutTests
{
    private sealed class Rig
    {
        public int Now;
        public int Offs;
        public int Restores;
        public bool Suspended;
        public bool TurnOffResult = true;
        public readonly HardwareKeyboardIdleTimeout Timeout;

        public Rig()
        {
            Timeout = new HardwareKeyboardIdleTimeout(
                () => { Offs++; return TurnOffResult; },
                () => Restores++,
                () => Suspended,
                () => Now,
                run: false);
        }
    }

    [TestMethod]
    public void TurnsOffAfterConfiguredIdleAndRestoresOnKeyPress()
    {
        var rig = new Rig();
        rig.Timeout.SetTimeoutSeconds(20);

        rig.Now = 19_000;
        Assert.AreEqual(1_000, rig.Timeout.Step());
        Assert.AreEqual(0, rig.Offs);

        rig.Now = 20_000;
        rig.Timeout.Step();
        Assert.AreEqual(1, rig.Offs);
        Assert.IsTrue(rig.Timeout.IsDimmed);
        Assert.AreEqual(-1, rig.Timeout.Step(), "Dimmed: wait for input, never turn off twice.");
        Assert.AreEqual(1, rig.Offs);

        rig.Now = 60_000;
        rig.Timeout.NoteActivity();
        Assert.IsFalse(rig.Timeout.IsDimmed);
        rig.Timeout.Step();
        Assert.AreEqual(1, rig.Restores);
        Assert.AreEqual(20_000, rig.Timeout.Step(), "Countdown restarts from the key press.");
    }

    [TestMethod]
    public void KeyPressesPostponeTheTimeout()
    {
        var rig = new Rig();
        rig.Timeout.SetTimeoutSeconds(20);
        rig.Now = 15_000;
        rig.Timeout.NoteActivity();
        rig.Now = 30_000;
        Assert.AreEqual(5_000, rig.Timeout.Step());
        Assert.AreEqual(0, rig.Offs);
        Assert.AreEqual(0, rig.Restores, "A key press while lit must not re-apply lighting.");
    }

    [TestMethod]
    public void DisabledOrSuspendedNeverTurnsOff()
    {
        var rig = new Rig();
        rig.Now = 100_000;
        Assert.AreEqual(-1, rig.Timeout.Step(), "Timeout 0 = always on.");

        rig.Timeout.SetTimeoutSeconds(20);
        rig.Suspended = true; // A software effect owns its own idle handling.
        rig.Now = 200_000;
        Assert.AreEqual(-1, rig.Timeout.Step());
        Assert.AreEqual(0, rig.Offs);
    }

    [TestMethod]
    public void DisablingWhileDimmedRestoresTheBacklight()
    {
        var rig = new Rig();
        rig.Timeout.SetTimeoutSeconds(20);
        rig.Now = 20_000;
        rig.Timeout.Step();
        Assert.IsTrue(rig.Timeout.IsDimmed);

        rig.Timeout.SetTimeoutSeconds(0);
        rig.Timeout.Step();
        Assert.AreEqual(1, rig.Restores);
        Assert.AreEqual(-1, rig.Timeout.Step());
    }

    [TestMethod]
    public void LightingWriteWhileDimmedCountsAsLitWithoutExtraRestore()
    {
        var rig = new Rig();
        rig.Timeout.SetTimeoutSeconds(20);
        rig.Now = 20_000;
        rig.Timeout.Step();

        rig.Now = 25_000;
        rig.Timeout.NoteLightingWritten(); // e.g. power-mode colours re-applied
        Assert.IsFalse(rig.Timeout.IsDimmed);
        Assert.AreEqual(20_000, rig.Timeout.Step());
        Assert.AreEqual(0, rig.Restores);
    }

    [TestMethod]
    public void UnavailableControllerRetriesInsteadOfClaimingDimmed()
    {
        var rig = new Rig { TurnOffResult = false };
        rig.Timeout.SetTimeoutSeconds(20);
        rig.Now = 20_000;
        Assert.AreEqual(5_000, rig.Timeout.Step());
        Assert.IsFalse(rig.Timeout.IsDimmed);
    }

    [TestMethod]
    public void HandlesTickCountWraparound()
    {
        var rig = new Rig { Now = int.MaxValue - 5_000 };
        rig.Timeout.SetTimeoutSeconds(20);
        rig.Now = unchecked(int.MaxValue + 10_000);
        Assert.AreEqual(5_000, rig.Timeout.Step());
        rig.Now = unchecked(int.MaxValue + 15_000);
        rig.Timeout.Step();
        Assert.AreEqual(1, rig.Offs);
    }

    [TestMethod]
    public void OnlyPhn1673UsesSoftwareKeyboardIdleTimeout()
    {
        Assert.IsTrue(AcerModel.RequiresSoftwareKeyboardIdleTimeoutFor("Predator PHN16-73"));
        Assert.IsTrue(AcerModel.RequiresSoftwareKeyboardIdleTimeoutFor("predator phn16-73"));
        Assert.IsFalse(AcerModel.RequiresSoftwareKeyboardIdleTimeoutFor("Predator PHN16S-71"));
        Assert.IsFalse(AcerModel.RequiresSoftwareKeyboardIdleTimeoutFor("Predator PHN16-72"));
        Assert.IsFalse(AcerModel.RequiresSoftwareKeyboardIdleTimeoutFor("Predator PHN16-731"));
        Assert.IsFalse(AcerModel.RequiresSoftwareKeyboardIdleTimeoutFor(""));
    }
}
