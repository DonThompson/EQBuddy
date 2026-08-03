using System.Windows;
using EQBuddy.Core;

namespace EQBuddy;

/// <summary>WPF adapter for <see cref="WindowPlacement"/>: checks saved positions
/// against the virtual screen (all monitors), not just the primary work area.</summary>
internal static class ScreenGuard
{
    public static bool OnScreen(double left, double top,
        double width = double.NaN, double height = double.NaN) =>
        WindowPlacement.IsReachable(left, top,
            SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight,
            width, height);
}
