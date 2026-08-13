namespace EQBuddy.Core;

/// <summary>
/// Guards saved window positions against monitor-layout changes. A position saved
/// while a second monitor (or higher resolution) was attached can land entirely
/// off-screen after the layout changes; settings.json survives reinstalls, so the
/// window stays invisible until the file is hand-edited (field report, 2026-08-03).
/// </summary>
public static class WindowPlacement
{
    /// <summary>Minimum pixels of the window that must remain visible, per axis,
    /// for a saved position to be trusted. Our windows drag from anywhere in the
    /// body, so any grab-able corner this size is enough to rescue one by hand.</summary>
    public const double Margin = 40;

    /// <summary>
    /// True when a window at (left, top) keeps at least a Margin×Margin grab area
    /// inside the virtual-screen rectangle. NaN coordinates are never reachable
    /// (first launch). Width/height are optional: windows that size to content pass
    /// NaN, which conservatively shrinks the window to the grab area — a position
    /// is then trusted only if its top-left corner region is visible, while a known
    /// width lets a window deliberately tucked past an edge keep its spot.
    /// </summary>
    public static bool IsReachable(double left, double top,
        double screenLeft, double screenTop, double screenWidth, double screenHeight,
        double width = double.NaN, double height = double.NaN)
    {
        if (double.IsNaN(left) || double.IsNaN(top)) return false;
        if (double.IsNaN(width) || width < Margin) width = Margin;
        if (double.IsNaN(height) || height < Margin) height = Margin;
        var overlapW = Math.Min(left + width, screenLeft + screenWidth) - Math.Max(left, screenLeft);
        var overlapH = Math.Min(top + height, screenTop + screenHeight) - Math.Max(top, screenTop);
        return overlapW >= Margin && overlapH >= Margin;
    }

    /// <summary>
    /// What a window's Closed handler should persist (#117, Snagglefern's 4-screen
    /// rig). A window placed at its FALLBACK — the saved position was rejected,
    /// which happens under a TRANSIENT monitor topology (sleeping displays, an RDP
    /// hop, an update relaunching the app with a screen off) — and never moved by
    /// the user must keep the ORIGINAL saved values: the monitors come back, and
    /// persisting the fallback would permanently teleport a window the player had
    /// parked with care. User movement always wins, as does a genuine restore.
    /// </summary>
    public static (double Left, double Top) PositionToPersist(
        bool restoredFromSaved, double placedLeft, double placedTop,
        double currentLeft, double currentTop, double savedLeft, double savedTop)
    {
        var moved = currentLeft != placedLeft || currentTop != placedTop;
        return PositionToPersist(restoredFromSaved, moved, currentLeft, currentTop, savedLeft, savedTop);
    }

    /// <summary>Overload for windows that MOVE THEMSELVES (the grow-up chip stacks,
    /// whose anchor rewrites Top on every size change — 2026-08-13 review): a
    /// coordinate delta can't tell a user drag from the anchor's own writes, so the
    /// caller states user movement explicitly, set where the drag actually starts.</summary>
    public static (double Left, double Top) PositionToPersist(
        bool restoredFromSaved, bool userMoved,
        double currentLeft, double currentTop, double savedLeft, double savedTop) =>
        restoredFromSaved || userMoved || double.IsNaN(savedLeft) || double.IsNaN(savedTop)
            ? (currentLeft, currentTop)
            : (savedLeft, savedTop);
}
