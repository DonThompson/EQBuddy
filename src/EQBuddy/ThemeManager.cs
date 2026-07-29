using System.Windows;

namespace EQBuddy;

/// <summary>
/// Swaps the palette ResourceDictionary merged into Application.Resources. Theme.xaml's
/// styles all reference brush keys via DynamicResource, so replacing the palette dictionary
/// in place repaints every open window immediately — no window needs to be reloaded.
/// </summary>
public static class ThemeManager
{
    /// <summary>Index of the palette dictionary within App.xaml's MergedDictionaries
    /// (index 0 — Theme.xaml, the styles, is index 1 and never swapped).</summary>
    private const int PaletteIndex = 0;

    /// <summary>Loads Themes/{key}.xaml and swaps it into the app's merged dictionaries.
    /// An unrecognized key (e.g. from an older settings.json) falls back to the first
    /// entry in <see cref="EQBuddy.UI.Shared.ThemeCatalog"/> rather than throwing.</summary>
    public static void Apply(string themeKey)
    {
        var key = EQBuddy.UI.Shared.ThemeCatalog.Themes[
            EQBuddy.UI.Shared.ThemeCatalog.IndexOf(themeKey)].Key;
        var dictionary = new ResourceDictionary
        {
            Source = new Uri($"Themes/{key}.xaml", UriKind.Relative),
        };
        Application.Current.Resources.MergedDictionaries[PaletteIndex] = dictionary;
    }
}
