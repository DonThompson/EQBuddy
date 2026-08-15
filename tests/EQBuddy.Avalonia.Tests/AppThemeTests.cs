using Avalonia.Headless.XUnit;
using Avalonia.Media;
using EQBuddy.UI.Shared;

namespace EQBuddy.Avalonia.Tests;

/// <summary>
/// The derived theme tones (Hairline/Track/Raised/AccentDeep) must match the WPF
/// ThemeManager's formulas exactly — they're computed in both UIs rather than stored
/// in ThemePalettes, so this is the spot where the two could silently drift.
/// </summary>
[Collection("avalonia")]
public class AppThemeTests
{
    [AvaloniaFact]
    public void DerivedTonesFollowThePaletteFormulas()
    {
        AppTheme.Apply("BlueGrey");
        var accent = AppTheme.AccentBrush.Color;
        var panel = AppTheme.PanelBrush.Color;

        Assert.Equal(Color.FromArgb(0x26, accent.R, accent.G, accent.B), AppTheme.HairlineBrush.Color);
        Assert.Equal(Color.FromArgb(0x1E, accent.R, accent.G, accent.B), AppTheme.TrackBrush.Color);
        Assert.Equal(Color.FromArgb(
            (byte)Math.Min(255, panel.A * 3 / 2), panel.R, panel.G, panel.B), AppTheme.RaisedBrush.Color);
        Assert.Equal(Color.FromArgb(accent.A,
            (byte)(accent.R * 6 / 10), (byte)(accent.G * 6 / 10), (byte)(accent.B * 6 / 10)),
            AppTheme.AccentDeepBrush.Color);

        // Leave the shared singletons as the other render tests expect them.
        AppTheme.Apply("ParchmentBrass");
    }

    [AvaloniaFact]
    public void DerivedTonesRetintOnThemeSwitch()
    {
        AppTheme.Apply("ParchmentBrass");
        var before = AppTheme.HairlineBrush.Color;
        AppTheme.Apply("Turquoise");
        var after = AppTheme.HairlineBrush.Color;
        Assert.NotEqual(before, after);          // hairline follows the accent...
        Assert.Equal(before.A, after.A);         // ...at the same whisper of alpha
        AppTheme.Apply("ParchmentBrass");
    }

    [AvaloniaFact]
    public void EveryCatalogThemeAppliesCleanly()
    {
        foreach (var (key, _) in ThemeCatalog.Themes.Where(t => t.Key != CustomTheme.Key))
            AppTheme.Apply(key);   // an unparseable hex would throw here
        AppTheme.Apply("ParchmentBrass");
    }
}
