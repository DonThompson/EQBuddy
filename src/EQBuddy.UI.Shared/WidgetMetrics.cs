namespace EQBuddy.UI.Shared;

/// <summary>
/// The widget's screen-pixels-to-layout-units arithmetic, extracted so it can be tested
/// without a window.
///
/// The widget's content sits under a UI-scale transform. Anything measured from the
/// SCREEN — the monitor's work area, a cursor position — is in real pixels, while
/// anything assigned to a control INSIDE the transform is in pre-scale layout units.
/// The two agree only at 100%, so a mix-up is invisible until someone moves the size
/// slider. That is exactly how discussion #144 shipped: the section list was handed a
/// monitor-derived cap in screen pixels, believed it had more room than the window
/// could show, and clipped the last card with no scrollbar to reach it.
///
/// Every conversion between the two lives here, with the direction in the method name.
/// </summary>
public static class WidgetMetrics
{
    /// <summary>Never let the card list shrink below this (pre-scale units) — a list
    /// too short to hold one card is a broken window, not a small one.</summary>
    public const double MinSectionHeight = 120;

    /// <summary>Guards the divisions below. A scale of zero would produce infinity, and
    /// settings files have carried surprising values before.</summary>
    public static double SafeScale(double uiScale) => Math.Max(0.25, uiScale);

    /// <summary>
    /// The card list's MaxHeight, in the PRE-SCALE units the control expects.
    /// </summary>
    /// <param name="screenCap">The monitor-derived ceiling, in screen pixels.</param>
    /// <param name="contentHeight">The player's dragged height in pre-scale units, or
    /// NaN for automatic. Stored pre-scale deliberately, so it survives scale changes —
    /// which is also why it is compared against a pre-scale cap and not a screen one.</param>
    public static double SectionMaxHeight(double screenCap, double contentHeight, double uiScale)
    {
        var cap = screenCap / SafeScale(uiScale);
        return double.IsNaN(contentHeight)
            ? cap
            : Math.Clamp(contentHeight, MinSectionHeight, Math.Max(MinSectionHeight, cap));
    }

    /// <summary>A bottom-edge drag turned into a stored height. The cursor travels in
    /// screen pixels while the list it resizes lives under the transform, so the delta
    /// is divided rather than added raw.</summary>
    public static double ContentHeightFromDrag(
        double startHeight, double cursorDeltaPixels, double uiScale) =>
        Math.Max(MinSectionHeight, startHeight + cursorDeltaPixels / SafeScale(uiScale));
}
