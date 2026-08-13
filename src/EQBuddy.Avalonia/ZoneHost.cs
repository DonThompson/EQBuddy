using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using EQBuddy.Core;

namespace EQBuddy.Avalonia;

/// <summary>
/// What the zone/map windows need from the app shell. Member names mirror the WPF
/// MainWindow surface one-for-one, so wiring is "MainWindow implements IZoneHost" —
/// the members it already grows for the spawn layer satisfy this implicitly.
/// </summary>
public interface IZoneHost
{
    AppSettings Settings { get; }
    string CurrentZoneName { get; }
    StatsSnapshot CurrentSnapshot();
    SpawnTimers SpawnTimers { get; }
    SpawnPointLedger SpawnPoints { get; }
    SpawnCatalog SpawnCatalogData { get; }
    SpawnOverrides SpawnOverridesStore { get; }
    ZoneGraph ZoneGraph { get; }
    MobLookupResult? WikiMobResult(string name);
    void EnsureMobLookup(string name);
}

/// <summary>
/// Shared bits for the zone/map windows. CONSOLIDATION CANDIDATE: Button mirrors the
/// WPF Theming.Button (palette-driven so buttons stay readable on dark themes), and the
/// derived tones mirror WPF's ThemeManager derivations (hairline borders, bar tracks,
/// raised chips — alpha variations of the palette). Both belong in AppTheme once its
/// owner folds them in; kept here per the porting boundary.
/// </summary>
internal static class ZoneTheming
{
    // Live-derived singletons: recomputed whenever AppTheme's base brushes repaint, so
    // a theme switch carries the derived tones along without any window rebuilding.
    public static readonly SolidColorBrush HairlineBrush = new();
    public static readonly SolidColorBrush TrackBrush = new();
    public static readonly SolidColorBrush RaisedBrush = new();

    static ZoneTheming()
    {
        AppTheme.AccentBrush.PropertyChanged += (_, e) =>
        {
            if (e.Property == SolidColorBrush.ColorProperty) Recompute();
        };
        AppTheme.PanelBrush.PropertyChanged += (_, e) =>
        {
            if (e.Property == SolidColorBrush.ColorProperty) Recompute();
        };
        Recompute();
    }

    /// <summary>Same math as the WPF ThemeManager's derived tones, so the two UIs
    /// render the modernized cards identically.</summary>
    private static void Recompute()
    {
        var accent = AppTheme.AccentBrush.Color;
        var panel = AppTheme.PanelBrush.Color;
        HairlineBrush.Color = Color.FromArgb(0x26, accent.R, accent.G, accent.B);
        TrackBrush.Color = Color.FromArgb(0x1E, accent.R, accent.G, accent.B);
        RaisedBrush.Color = Color.FromArgb(
            (byte)Math.Min(255, panel.A * 3 / 2), panel.R, panel.G, panel.B);
    }

    public static Button Button(string label, bool isDefault = false, bool isCancel = false) => new()
    {
        Content = label,
        Padding = new Thickness(12, 2, 12, 2),
        BorderThickness = new Thickness(1),
        FontSize = 12,
        IsDefault = isDefault,
        IsCancel = isCancel,
        Background = AppTheme.PanelBrush,
        Foreground = AppTheme.TextBrush,
        BorderBrush = AppTheme.AccentBrush,
    };
}
