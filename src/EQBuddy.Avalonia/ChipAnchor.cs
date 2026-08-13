using Avalonia;
using Avalonia.Controls;

namespace EQBuddy.Avalonia;

/// <summary>
/// Growth direction for the self-sizing chip stacks (#95, badly-developed: boss
/// timers above mez timers, one stack growing up and the other down). A
/// SizeToContent window normally holds its TOP edge and grows downward; anchored
/// windows hold their BOTTOM edge instead — new chips push the stack upward, and
/// the edge you parked next to something stays parked. Sizes arrive in DIPs while
/// Position speaks device pixels, so the window's own scaling converts each touch.
/// </summary>
internal static class ChipAnchor
{
    public static void Attach(Window w, Func<bool> growsUp)
    {
        var bottom = int.MinValue;   // unset until the window has a real position
        w.SizeChanged += (_, e) =>
        {
            if (growsUp() && e.HeightChanged && bottom != int.MinValue)
                w.Position = new PixelPoint(w.Position.X, bottom - PixelHeight(w, e.NewSize.Height));
            bottom = w.Position.Y + PixelHeight(w, e.NewSize.Height);
        };
        // Drags (and our own Position writes — same math, harmless) move the anchor.
        w.PositionChanged += (_, _) => bottom = w.Position.Y + PixelHeight(w, w.Bounds.Height);
    }

    private static int PixelHeight(Window w, double dipHeight)
    {
        var scale = w.DesktopScaling;
        return (int)Math.Round(dipHeight * (scale > 0 ? scale : 1.0));
    }
}
