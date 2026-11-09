namespace PreySense.Frontend.Services;

/// <summary>Tracks one hide/restore cycle; UI-thread-owned, without native window calls.</summary>
internal sealed class WindowVisibilitySession<TWindow> where TWindow : class
{
    public IReadOnlyList<TWindow> HiddenWindows { get; private set; } = Array.Empty<TWindow>();
    public TWindow? PreferredWindow { get; private set; }
    public long Revision { get; private set; }
    public bool IsHidden { get; private set; }
    public bool IsRestoring { get; private set; }

    public bool BeginHide(IEnumerable<TWindow> visibleWindows, TWindow? activeWindow)
    {
        if (IsHidden) return false;
        HiddenWindows = visibleWindows.ToArray();
        PreferredWindow = activeWindow;
        IsHidden = true;
        IsRestoring = false;
        Revision++;
        return true;
    }

    public long BeginRestore()
    {
        IsHidden = false;
        IsRestoring = true;
        return ++Revision;
    }

    public void CompleteRestore(long revision)
    {
        if (revision != Revision) return;
        HiddenWindows = Array.Empty<TWindow>();
        PreferredWindow = null;
        IsRestoring = false;
    }

    public bool CanAutoHide(long revision) =>
        revision == Revision && !IsHidden && !IsRestoring;
}
