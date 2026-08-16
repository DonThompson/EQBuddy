namespace EQBuddy.Core;

/// <summary>
/// The last window position the windowing system reported while the window was
/// actually on screen (#169, joma65's Linux report).
///
/// <para>Reading a window's position inside a Closed handler is not safe: the X11
/// and Wayland backends can zero it while the window is tearing down, and the
/// zero then goes to settings.json as a real choice. The chip stacks already
/// avoided this by hand — "Position can be reset by the native backend while a
/// window is closing" — but MainWindow, SpawnsWindow, QuestsWindow and
/// FightTimelineWindow each read Position directly, and every one of those is a
/// window joma65 found at 0,0. This is that guard, in one place, so a fifth
/// window cannot quietly miss it.</para>
///
/// <para>It deliberately records only what an <c>PositionChanged</c> event
/// reported while the window was visible. A synchronous read taken right after
/// assigning Position is the other half of the same trap: the window manager
/// applies a programmatic move asynchronously, so the read can hand back the old
/// value — or 0,0 — before the move lands.</para>
///
/// <para>Having seen nothing is a meaningful answer, not a failure: it means the
/// window never reported a position we can vouch for, and the caller should keep
/// whatever was already saved rather than invent one.</para>
/// </summary>
public struct LastVisiblePosition
{
    private double _x, _y;

    /// <summary>True once a trustworthy position has been observed.</summary>
    public bool Have { get; private set; }

    /// <summary>Record a position reported while the window was on screen. Callers
    /// pass their own visibility test — an off-screen or closing window has nothing
    /// worth recording.</summary>
    public void Observe(double x, double y, bool visible)
    {
        if (!visible) return;
        _x = x;
        _y = y;
        Have = true;
    }

    /// <summary>The observed position, or the caller's fallback when nothing was
    /// ever observed. Pass the SAVED coordinates as the fallback: keeping a known
    /// good spot beats persisting a position no one vouched for.</summary>
    public readonly (double X, double Y) Or(double fallbackX, double fallbackY) =>
        Have ? (_x, _y) : (fallbackX, fallbackY);
}
