using Avalonia.Controls;
using Avalonia.Media;

namespace EQBuddy.Avalonia;

/// <summary>
/// Applies AppSettings.ChipScale to a small floating window (spawn chips, mez chips,
/// the alert banner) — one shared scale for the whole chicklet family: they're read at
/// the same glance, so they should agree on a size (discussion #47, jlewisnj at 4K).
/// WPF sets a LayoutTransform on the window's content; Avalonia controls carry no such
/// property, so chip windows host their root in a LayoutTransformControl (see
/// <see cref="Host"/>) and the scale lands there. SizeToContent re-measures and the
/// window grows to fit, the same mechanism as the widget's UiScale.
/// </summary>
internal static class ChipScale
{
    /// <summary>Wraps a chip window's root so <see cref="Apply"/> has a transform target.</summary>
    public static LayoutTransformControl Host(Control content) => new() { Child = content };

    public static void Apply(Window window, double scale)
    {
        if (window.Content is not LayoutTransformControl root) return;
        root.LayoutTransform = Math.Abs(scale - 1.0) < 0.001
            ? null
            : new ScaleTransform(scale, scale);
    }
}
