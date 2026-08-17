using System.Windows;
using System.Windows.Media;
using EQBuddy.UI.Shared;

namespace EQBuddy;

/// <summary>
/// Builds the palette ResourceDictionary from <see cref="ThemePalettes"/> and swaps it into
/// Application.Resources. Theme.xaml's styles all reference brush keys via DynamicResource,
/// so replacing the palette dictionary in place repaints every open window immediately —
/// no window needs to be reloaded.
///
/// The palette is composed here rather than loaded from Themes/*.xaml so that the colors
/// live in exactly one place for both UIs (the Avalonia port reads the same table).
/// </summary>
public static class ThemeManager
{
    /// <summary>Index of the palette dictionary within App.xaml's MergedDictionaries
    /// (index 0 — Theme.xaml, the styles, is index 1 and never swapped). Index 0 starts as
    /// an empty placeholder; App.OnStartup applies the saved theme before the first window
    /// is created, and nothing resolves a palette key via StaticResource, so no lookup
    /// happens against the empty dictionary.</summary>
    private const int PaletteIndex = 0;

    /// <summary>Composes the palette the settings select — a catalog theme, or the
    /// Custom theme derived from the user's three colors — and swaps it in. An
    /// unrecognized key (e.g. from an older settings.json) falls back to the first
    /// entry in <see cref="ThemeCatalog"/> rather than throwing.</summary>
    public static void Apply(Core.AppSettings settings) =>
        Apply(settings.Theme, CustomTheme.PaletteFor(settings));

    /// <summary>Raised after every swap with the theme key and its full palette (the
    /// derived tones included). EQBuddy Mobile listens so a paired phone repaints with
    /// the desktop instead of waiting for a reconnect; nothing else subscribes yet.</summary>
    public static event Action<string, IReadOnlyList<(string Key, string Hex)>>? PaletteApplied;

    private static void Apply(string themeKey, IEnumerable<(string Key, string Hex)> palette)
    {
        // The derived tones (hairline, track, raised, accent-deep) come from
        // UI.Shared so the Avalonia lane and the phone compose the same ones.
        // CaptureTheme is a no-op unless EQBUDDY_OPAQUE=1 (scripts/shoot.ps1): it makes
        // the window ground opaque so a screenshot photographs the UI and not whatever
        // was behind it. It runs BEFORE Derive, so the derived tones are derived from
        // the palette actually being painted.
        var rows = CaptureTheme.IfEnabled(palette).ToList();
        var full = rows.Concat(ThemeTones.Derive(rows)).ToList();

        var dictionary = new ResourceDictionary();
        foreach (var (key, hex) in full)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
            brush.Freeze();   // shared across windows and never mutated — WPF swaps the whole dictionary
            dictionary[key] = brush;
        }

        Application.Current.Resources.MergedDictionaries[PaletteIndex] = dictionary;
        PaletteApplied?.Invoke(themeKey, full);
    }
}
