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

    /// <summary>Composes the palette for a theme and swaps it in. An unrecognized key (e.g.
    /// from an older settings.json) falls back to the first entry in
    /// <see cref="ThemeCatalog"/> rather than throwing.</summary>
    public static void Apply(string themeKey)
    {
        var dictionary = new ResourceDictionary();
        foreach (var (key, hex) in ThemePalettes.For(themeKey))
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
            brush.Freeze();   // shared across windows and never mutated — WPF swaps the whole dictionary
            dictionary[key] = brush;
        }
        Application.Current.Resources.MergedDictionaries[PaletteIndex] = dictionary;
    }
}
