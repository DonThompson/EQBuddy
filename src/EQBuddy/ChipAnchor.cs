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
    public static void Attach(Window w, Func<bool> growsUp)
    {
        var bottom = double.NaN;
        w.SizeChanged += (_, e) =>
        {
            if (growsUp() && e.HeightChanged && !double.IsNaN(bottom))
                w.Top = bottom - w.ActualHeight;
            bottom = w.Top + w.ActualHeight;
        };
        // Drags (and our own Top writes — same math, harmless) move the anchor.
        w.LocationChanged += (_, _) => bottom = w.Top + w.ActualHeight;
    }
}
