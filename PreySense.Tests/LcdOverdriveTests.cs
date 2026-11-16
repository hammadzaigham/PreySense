using PreySense.Helpers;

namespace PreySense.Tests;

[TestClass]
public class LcdOverdriveTests
{
    [TestMethod]
    [DataRow(0, false)]
    [DataRow(1, true)]
    public void ReadsProfileByteSixInsteadOfAnApplicationCache(int state, bool expected)
    {
        var control = new LcdOverdriveControl((method, input) =>
        {
            Assert.AreEqual("GetGamingProfile", method);
            Assert.AreEqual(0ul, input);
            return (true, ((ulong)state << 48) | 0x123456780000ul);
        });
        Assert.IsTrue(control.TryGet(out bool enabled));
        Assert.AreEqual(expected, enabled);
    }

    [TestMethod]
    [DataRow(0xFF)]
    [DataRow(2)]
    public void UnsupportedOrUnknownStatePreventsWrites(int state)
    {
        int reads = 0;
        var control = new LcdOverdriveControl((method, _) =>
        {
            Assert.AreEqual("GetGamingProfile", method);
            reads++;
            return (true, (ulong)state << 48);
        });
        Assert.IsFalse(control.Set(true));
        Assert.AreEqual(1, reads);
    }

    [TestMethod]
    public void UnreadableStatePreventsWrites()
    {
        int calls = 0;
        var control = new LcdOverdriveControl((_, _) => { calls++; return (false, 0); });
        Assert.IsFalse(control.Set(true));
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    [DataRow(true, 0x1000000000010ul)]
    [DataRow(false, 0x10ul)]
    public void SetsOnlyThePanelSelectionAndVerifiesState(bool requested, ulong payload)
    {
        bool state = !requested;
        int reads = 0, writes = 0;
        var control = new LcdOverdriveControl((method, input) =>
        {
            if (method == "GetGamingProfile") { reads++; return (true, state ? 1ul << 48 : 0); }
            Assert.AreEqual("SetGamingProfile", method);
            Assert.AreEqual(payload, input);
            writes++;
            state = requested;
            return (true, 0);
        });
        Assert.IsTrue(control.Set(requested));
        Assert.AreEqual(2, reads);
        Assert.AreEqual(1, writes);
    }

    [TestMethod]
    public void AcceptedCommandWithoutChangedReadbackDoesNotClaimSuccess()
    {
        var control = new LcdOverdriveControl((_, _) => (true, 0));
        Assert.IsFalse(control.Set(true));
    }

    [TestMethod]
    public void AlreadySelectedStateDoesNotWrite()
    {
        int calls = 0;
        var control = new LcdOverdriveControl((method, _) =>
        {
            Assert.AreEqual("GetGamingProfile", method);
            calls++;
            return (true, 1ul << 48);
        });
        Assert.IsTrue(control.Set(true));
        Assert.AreEqual(1, calls);
    }
}
