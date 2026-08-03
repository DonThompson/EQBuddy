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
}
