using System.Windows;

namespace EQBuddy;

/// <summary>
/// Growth direction for the self-sizing chip stacks (#95, badly-developed: boss
/// timers above mez timers, one stack growing up and the other down). A
/// SizeToContent window normally holds its TOP edge and grows downward; anchored
/// windows hold their BOTTOM edge instead — new chips push the stack upward, and
/// the edge you parked next to something stays parked.
/// </summary>
public static class ChipAnchor
{
    /// <param name="restoredBottom">The bottom edge persisted at last close, when the
    /// window restored a saved position in grow-up mode. A grow-up stack's TOP edge
    /// depends on how many chips it held when it closed, so restoring Top makes the
    /// stack walk upward across close/reopen cycles and fight the player's drags
    /// (#122, Snagglefern) — the bottom edge is the one the player actually parked.</param>
    public static void Attach(Window w, Func<bool> growsUp, double? restoredBottom = null)
    {
        var bottom = restoredBottom ?? double.NaN;
        var firstLayout = restoredBottom is not null;
        w.SizeChanged += (_, e) =>
        {
            if (growsUp() && !double.IsNaN(bottom) && (e.HeightChanged || firstLayout))
                w.Top = bottom - w.ActualHeight;
            firstLayout = false;
            bottom = w.Top + w.ActualHeight;
        };
        // Drags (and our own Top writes — same math, harmless) move the anchor.
        w.LocationChanged += (_, _) => bottom = w.Top + w.ActualHeight;
    }
}
