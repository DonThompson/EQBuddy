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
    /// <returns>The live bottom edge — what a grow-up stack must persist at close.
    /// Reading <c>Top + ActualHeight</c> in a Closed handler instead gives the wrong
    /// answer: the window is already torn down and ActualHeight is 0, so the saved
    /// "bottom" is really the TOP edge, and the next open subtracts a chip height from
    /// it. That is the stack walking up the screen one chip per disappear/reappear
    /// cycle (#152, Snagglefern — the same drift as #122, arriving by a different
    /// route). This closure keeps the last value seen while the window was alive.</returns>
    public static Func<double> Attach(Window w, Func<bool> growsUp, double? restoredBottom = null)
    {
        var bottom = restoredBottom ?? double.NaN;
        var firstLayout = restoredBottom is not null;
        w.SizeChanged += (_, e) =>
        {
            if (growsUp() && !double.IsNaN(bottom) && (e.HeightChanged || firstLayout))
                w.Top = bottom - w.ActualHeight;
            firstLayout = false;
            Remember();
        };
        // Drags (and our own Top writes — same math, harmless) move the anchor.
        w.LocationChanged += (_, _) => Remember();
        return () => bottom;

        // Only ever from a laid-out window. A zero height means the window is closing
        // or has not measured yet; recording that would collapse the anchor onto Top.
        void Remember()
        {
            if (w.ActualHeight > 0) bottom = w.Top + w.ActualHeight;
        }
    }
}
