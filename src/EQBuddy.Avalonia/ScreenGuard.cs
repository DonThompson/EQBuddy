using Avalonia.Controls;
using EQBuddy.Core;

namespace EQBuddy.Avalonia;

/// <summary>Avalonia adapter for <see cref="WindowPlacement"/>: checks saved positions
/// against the union of all screens. When the platform reports no screens (headless
/// tests), a saved position is trusted as-is — the guard only exists to catch
/// monitor-layout changes, and there is no layout to check against.</summary>
internal static class ScreenGuard
{
    public static bool OnScreen(WindowBase window, double left, double top,
        double width = double.NaN, double height = double.NaN)
    {
        var screens = window.Screens?.All;
        if (screens is null || screens.Count == 0)
            return !double.IsNaN(left) && !double.IsNaN(top);
        double l = double.MaxValue, t = double.MaxValue, r = double.MinValue, b = double.MinValue;
        foreach (var s in screens)
        {
            l = Math.Min(l, s.Bounds.X);
            t = Math.Min(t, s.Bounds.Y);
            r = Math.Max(r, s.Bounds.Right);
            b = Math.Max(b, s.Bounds.Bottom);
        }
        return WindowPlacement.IsReachable(left, top, l, t, r - l, b - t, width, height);
    }
}
