using System.Windows;
using EQBuddy.UI.Shared;

namespace EQBuddy;

/// <summary>
/// WPF wiring for a grow-up chip stack. All the arithmetic — and every rule that has
/// ever gone wrong here — lives in <see cref="ChipStackAnchor"/>, which is testable
/// without a window; this file does nothing but connect it to WPF's events.
/// </summary>
public static class ChipAnchor
{
    /// <param name="restoredBottom">The bottom edge persisted at last close, when the
    /// window restored a saved position in grow-up mode.</param>
    /// <returns>The anchor. Persist <see cref="ChipStackAnchor.Bottom"/> at close —
    /// never <c>Top + ActualHeight</c>, which reads as the top edge once the window has
    /// been torn down (#152).</returns>
    public static ChipStackAnchor Attach(Window w, Func<bool> growsUp, double? restoredBottom = null)
    {
        var anchor = new ChipStackAnchor(restoredBottom);
        w.SizeChanged += (_, e) =>
        {
            if (anchor.ShouldReposition(growsUp(), e.HeightChanged)
                && anchor.TopFor(w.ActualHeight) is { } top)
                w.Top = top;
            anchor.Observe(w.Top, w.ActualHeight);
        };
        // Drags (and our own Top writes — same math, harmless) move the anchor.
        w.LocationChanged += (_, _) => anchor.Observe(w.Top, w.ActualHeight);
        return anchor;
    }
}
