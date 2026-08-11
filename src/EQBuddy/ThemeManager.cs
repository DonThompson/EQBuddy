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
    public static void Apply(Core.AppSettings settings) => Apply(CustomTheme.PaletteFor(settings));

    private static void Apply(IEnumerable<(string Key, string Hex)> palette)
    {
        var dictionary = new ResourceDictionary();
        var colors = new System.Collections.Generic.Dictionary<string, Color>();
        foreach (var (key, hex) in palette)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex)!;
            colors[key] = color;
            var brush = new SolidColorBrush(color);
            brush.Freeze();   // shared across windows and never mutated — WPF swaps the whole dictionary
            dictionary[key] = brush;
        }

        // Derived tones of the 2026-08-11 modernization — alpha variations of palette
        // keys, composed here so all seven themes (and Custom) get them for free:
        //   Hairline — card borders: the accent at a whisper instead of a solid line.
        //   Track    — the empty part of a stat bar, under the accent-filled part.
        //   Raised   — chips and tiles, one step above panel.
        //   AccentDeep — gradient start for bar fills, accent pulled toward the ground.
        void Derived(string key, Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            dictionary[key] = b;
        }
        var accent = colors["AccentBrush"];
        var panel = colors["PanelBrush"];
        Derived("HairlineBrush", Color.FromArgb(0x26, accent.R, accent.G, accent.B));
        Derived("TrackBrush", Color.FromArgb(0x1E, accent.R, accent.G, accent.B));
        Derived("RaisedBrush", Color.FromArgb(
            (byte)Math.Min(255, panel.A * 3 / 2), panel.R, panel.G, panel.B));
        Derived("AccentDeepBrush", Color.FromArgb(accent.A,
            (byte)(accent.R * 6 / 10), (byte)(accent.G * 6 / 10), (byte)(accent.B * 6 / 10)));

        Application.Current.Resources.MergedDictionaries[PaletteIndex] = dictionary;
    }
}
