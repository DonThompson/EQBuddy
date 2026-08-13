using Avalonia.Media;

namespace EQBuddy.Avalonia;

/// <summary>
/// WPF's ThemeManager composes a few derived alpha tones on top of the palette
/// (2026-08-11 modernization); the Avalonia AppTheme doesn't carry them yet and is
/// owned by another agent — held here until they can be CONSOLIDATED into AppTheme.
/// </summary>
internal static class LocalTheme
{
    /// <summary>Window/card borders: the accent at a whisper instead of a solid line
    /// (alpha 0x26 over AccentBrush — the same recipe as WPF's derived HairlineBrush).</summary>
    public static readonly SolidColorBrush HairlineBrush = new();

    static LocalTheme()
    {
        // AccentBrush is a live-mutated singleton: follow its color so a theme switch
        // repaints hairline chrome along with everything else.
        AppTheme.AccentBrush.PropertyChanged += (_, e) =>
        {
            if (e.Property == SolidColorBrush.ColorProperty) Sync();
        };
        Sync();
    }

    private static void Sync()
    {
        var accent = AppTheme.AccentBrush.Color;
        HairlineBrush.Color = Color.FromArgb(0x26, accent.R, accent.G, accent.B);
    }
}
