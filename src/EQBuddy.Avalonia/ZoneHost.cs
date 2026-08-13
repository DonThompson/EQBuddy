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
/// Shared bits for the zone/map windows. Button mirrors the WPF Theming.Button
/// (palette-driven so buttons stay readable on dark themes); the derived tones the
/// windows use live in AppTheme (HairlineBrush/TrackBrush/RaisedBrush).
/// </summary>
internal static class ZoneTheming
{
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
