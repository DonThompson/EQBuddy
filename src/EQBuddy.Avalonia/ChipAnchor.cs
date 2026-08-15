using Avalonia;
using Avalonia.Controls;
using EQBuddy.UI.Shared;

namespace EQBuddy.Avalonia;

/// <summary>
/// Avalonia wiring for a grow-up chip stack. All the arithmetic — and every rule that
/// has ever gone wrong here — lives in <see cref="ChipStackAnchor"/>, which is testable
/// without a window; this file does nothing but connect it to Avalonia's events and
/// convert units.
///
/// The one thing Avalonia adds over the WPF sibling: sizes arrive in DIPs while
/// <see cref="Window.Position"/> speaks device pixels, so every height crossing the
/// boundary goes through <see cref="PixelHeight"/>. The anchor itself is then kept
/// wholly in device pixels, matching the Left/Top these windows already persist.
/// </summary>
internal static class ChipAnchor
{
    /// <param name="restoredBottom">The bottom edge persisted at last close, when the
    /// window restored a saved position in grow-up mode. A grow-up stack's TOP edge
    /// depends on how many chips it held when it closed, so restoring Top makes the
    /// stack walk upward across reopen cycles (#122, Snagglefern) — the bottom edge is
    /// the one the player actually parked.</param>
    /// <returns>The anchor. Persist <see cref="ChipStackAnchor.Bottom"/> at close —
    /// never <c>Position.Y + Bounds.Height</c>, which reads as the top edge once the
    /// window has been torn down (#152).</returns>
    public static ChipStackAnchor Attach(Window w, Func<bool> growsUp, double? restoredBottom = null)
    {
        var anchor = new ChipStackAnchor(restoredBottom);
        w.SizeChanged += (_, e) =>
        {
            var height = PixelHeight(w, e.NewSize.Height);
            if (anchor.ShouldReposition(growsUp(), e.HeightChanged)
                && anchor.TopFor(height) is { } top)
                w.Position = new PixelPoint(w.Position.X, (int)Math.Round(top));
            anchor.Observe(w.Position.Y, height);
        };
        // Drags (and our own Position writes — same math, harmless) move the anchor.
        // Observe ignores the zero heights this fires with before first layout and
        // while closing, which is the whole of #152.
        w.PositionChanged += (_, _) => anchor.Observe(w.Position.Y, PixelHeight(w, w.Bounds.Height));

        // Unlike WPF, these windows place themselves in Opened — after the first
        // measure — so a restored stack may have no further SizeChanged to ride.
        // Apply the restore straight away when the window has already laid out.
        if (restoredBottom is not null && growsUp() && w.Bounds.Height > 0
            && anchor.TopFor(PixelHeight(w, w.Bounds.Height)) is { } restoredTop)
            w.Position = new PixelPoint(w.Position.X, (int)Math.Round(restoredTop));
        return anchor;
    }

    private static double PixelHeight(Window w, double dipHeight)
    {
        var scale = w.DesktopScaling;
        return dipHeight * (scale > 0 ? scale : 1.0);
    }
}
