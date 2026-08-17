namespace EQBuddy.UI.Shared;

/// <summary>
/// The one change a screenshot needs: an OPAQUE window ground.
///
/// Every theme's <c>BgBrush</c> is deliberately translucent (#F2… — and #FC on
/// HighContrast) because the widget sits over a running game and letting a little of it
/// through is the point. In a capture that same alpha is a defect: whatever happened to
/// be behind the window bleeds into the PNG, so the same shot is a different image every
/// time and a review of it reviews the desktop as much as the UI.
///
/// Gated by <see cref="EnvVar"/> so it can never affect a player: nothing reads it but a
/// deliberate <c>scripts/shoot.ps1</c> run.
///
/// Only the GROUND keys are forced. Everything else in a palette — PanelBrush #26FFFFFF,
/// the hover tints, the washes, the derived hairline and track — is a tint *designed to
/// sit over* a ground, and flattening those would repaint the app rather than photograph
/// it (PanelBrush at full alpha is pure white). Composition inside the window is already
/// correct once the ground beneath it is; this fixes the one layer that isn't.
/// </summary>
public static class CaptureTheme
{
    /// <summary>Set to "1" for an opaque render. Same <c>EQBUDDY_*</c> family as the
    /// window hooks (EQBUDDY_QUESTS, EQBUDDY_EXPAND) that the shoot script drives.</summary>
    public const string EnvVar = "EQBUDDY_OPAQUE";

    /// <summary>Palette keys that paint a window's own ground rather than a tint over
    /// one. Of the three only BgBrush actually carries alpha in the shipped themes; the
    /// popup and combo wells are listed because a future theme (or a hand-edited
    /// settings.json under the Custom theme) could give them one, and a half-opaque
    /// dropdown in a screenshot is the same defect.</summary>
    public static readonly string[] GroundKeys = ["BgBrush", "PopupBrush", "ComboBoxBrush"];

    public static bool Enabled =>
        Environment.GetEnvironmentVariable(EnvVar) == "1";

    /// <summary>The palette with every ground key at full alpha, in the order given.
    /// Non-ground keys pass through untouched.</summary>
    public static IEnumerable<(string Key, string Hex)> Opaque(
        IEnumerable<(string Key, string Hex)> palette)
    {
        foreach (var (key, hex) in palette)
            yield return (key, GroundKeys.Contains(key, StringComparer.Ordinal)
                ? Solid(hex)
                : hex);
    }

    /// <summary>Applies <see cref="Opaque"/> only when <see cref="Enabled"/>, so both
    /// UIs can pipe every palette through one call and stay identical.</summary>
    public static IEnumerable<(string Key, string Hex)> IfEnabled(
        IEnumerable<(string Key, string Hex)> palette) =>
        Enabled ? Opaque(palette) : palette;

    /// <summary>#AARRGGBB with the alpha replaced by FF — the theme's own colour at full
    /// strength, which is exactly what its author picked; no re-mixing.</summary>
    public static string Solid(string hex)
    {
        var (_, r, g, b) = ThemeTones.Parse(hex);
        return ThemeTones.Hex(0xFF, r, g, b);
    }
}
