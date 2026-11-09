using PreySense.Frontend.Services;

namespace PreySense.Tests;

[TestClass]
public class WindowVisibilitySessionTests
{
    [TestMethod]
    public void DuplicateHide_KeepsWindowsAndFocusTargetForRestore()
    {
        var session = new WindowVisibilitySession<string>();
        session.BeginHide(new[] { "settings", "tuning" }, "tuning");
        Assert.IsFalse(session.BeginHide(Array.Empty<string>(), null));
        session.BeginRestore();
        CollectionAssert.AreEqual(new[] { "settings", "tuning" }, session.HiddenWindows.ToArray());
        Assert.AreEqual("tuning", session.PreferredWindow);
    }

    [TestMethod]
    public void StaleClickAwayEvent_CannotHideANewerShowRequest()
    {
        var session = new WindowVisibilitySession<string>();
        long clickAwayRevision = session.Revision;
        long showRevision = session.BeginRestore();
        Assert.IsFalse(session.CanAutoHide(showRevision));
        session.CompleteRestore(showRevision);
        Assert.IsFalse(session.CanAutoHide(clickAwayRevision));
        Assert.IsTrue(session.CanAutoHide(showRevision));
    }

    [TestMethod]
    public void OverlappingShows_OnlyLatestCompletionCanReleaseRestoreGuard()
    {
        var session = new WindowVisibilitySession<string>();
        session.BeginHide(new[] { "settings" }, "settings");
        long first = session.BeginRestore();
        long second = session.BeginRestore();
        session.CompleteRestore(first);
        Assert.IsTrue(session.IsRestoring);
        Assert.HasCount(1, session.HiddenWindows);
        session.CompleteRestore(second);
        Assert.IsFalse(session.IsRestoring);
        Assert.IsEmpty(session.HiddenWindows);
    }

    [TestMethod]
    public void ExplicitHideDuringRestore_IsNotUndoneByOldCompletion()
    {
        var session = new WindowVisibilitySession<string>();
        long showing = session.BeginRestore();
        session.BeginHide(new[] { "fans" }, "fans");
        session.CompleteRestore(showing);
        Assert.IsTrue(session.IsHidden);
        Assert.IsFalse(session.IsRestoring);
        CollectionAssert.AreEqual(new[] { "fans" }, session.HiddenWindows.ToArray());
        Assert.IsFalse(session.CanAutoHide(session.Revision));
    }
}
