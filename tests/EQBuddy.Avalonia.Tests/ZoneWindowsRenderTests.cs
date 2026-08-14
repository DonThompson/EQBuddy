using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EQBuddy.Core;
using Path = System.IO.Path;

namespace EQBuddy.Avalonia.Tests;

/// <summary>
/// The zone/map cluster (MapWindow, TravelWindow, ZoneShareWindow, SessionPickerWindow)
/// rendered against a fake IZoneHost — real Core collaborators, no MainWindow.
/// </summary>
[Collection("avalonia")]
public sealed class ZoneWindowsRenderTests : IDisposable
{
    private readonly string _profile = Directory.CreateTempSubdirectory("eqbuddy-zone-render-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_profile, recursive: true); }
        catch (Exception ex) { Console.Error.WriteLine($"profile cleanup failed: {ex.Message}"); }
    }

    private sealed class FakeZoneHost : IZoneHost
    {
        public AppSettings Settings { get; } = new();
        public string CurrentZoneName { get; set; } = "";
        public SpawnCatalog SpawnCatalogData { get; } = SpawnCatalog.LoadEmbedded();
        public SpawnOverrides SpawnOverridesStore { get; } = new();
        public SpawnPointLedger SpawnPoints { get; }
        public SpawnTimers SpawnTimers { get; }
        public ZoneGraph ZoneGraph { get; } = ZoneGraph.LoadEmbedded();
        public StatsSnapshot Snapshot { get; set; } = new();

        public FakeZoneHost(string profileDir)
        {
            SpawnPoints = new SpawnPointLedger(Path.Combine(profileDir, "spawnpoints"), SpawnCatalogData);
            SpawnTimers = new SpawnTimers(SpawnCatalogData, SpawnOverridesStore);
        }

        public StatsSnapshot CurrentSnapshot() => Snapshot;
        public MobLookupResult? WikiMobResult(string name) => null;
        public void EnsureMobLookup(string name) { }
    }

    /// <summary>One archived kill in Befallen — enough for a spawn point.</summary>
    private static void ObserveOneKill(FakeZoneHost host)
    {
        var t = new DateTime(2026, 8, 13, 20, 0, 0);
        host.SpawnPoints.Apply(new ZoneEvent(t, "Befallen"));
        host.SpawnPoints.Apply(new LocationEvent(t, 100, 200, 0));
        host.SpawnPoints.Apply(new KillEvent(t.AddSeconds(30), "a decaying skeleton", "You"));
    }

    [AvaloniaFact]
    public void MapWindowDrawsTheMapAndArchivedSpawnCircles()
    {
        var host = new FakeZoneHost(_profile);
        var maps = Directory.CreateDirectory(Path.Combine(_profile, "maps")).FullName;
        File.WriteAllText(Path.Combine(maps, "befallen.txt"),
            "L 0, 0, 0, 100, 100, 0, 0, 0, 0\n" +
            "L 100, 100, 0, 200, 0, 0, 255, 0, 0\n" +
            "P 50, 50, 0, 255, 255, 255, 2, Test_Label\n");
        host.Settings.MapFolder = maps;
        host.CurrentZoneName = "Befallen";
        ObserveOneKill(host);

        var window = new MapWindow(host);
        window.Show();
        window.MaybeRefresh(force: true);

        var text = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains(text, t => t?.StartsWith("befallen — type /loc in game") == true);
        Assert.Contains("Test Label", text);                 // POI label, underscores unfolded
        Assert.Contains("⏳ Named — Befallen", text);        // side panel follows the zone
        // Marker + spawn-circle halo/ring + POI dot at minimum.
        Assert.True(window.GetVisualDescendants().OfType<Ellipse>().Count() >= 4);

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void TravelWindowAnswersForTheCurrentZone()
    {
        var host = new FakeZoneHost(_profile);
        var zone = host.ZoneGraph.Zones.First();
        host.CurrentZoneName = zone;

        var window = new TravelWindow(host);
        window.Show();
        var dest = window.GetVisualDescendants().OfType<AutoCompleteBox>().Single();
        dest.Text = zone;
        window.RenderRoute();

        var text = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains($"From: {zone}", text);
        Assert.Contains("You're already there.", text);

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void ZoneShareWindowPreviewsItsOwnExportAsNoChange()
    {
        var host = new FakeZoneHost(_profile);
        ObserveOneKill(host);

        var window = new ZoneShareWindow(host.SpawnPoints, host.SpawnCatalogData,
            host.SpawnOverridesStore, "Befallen");
        window.Show();
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Text?.StartsWith("Your Befallen archive: 1 spawn point, 1 kill observed") == true);

        var export = ZoneShare.Export(host.SpawnPoints.Snapshot("Befallen"),
            host.SpawnCatalogData.FindZone("Befallen"), host.SpawnOverridesStore);
        window.GetVisualDescendants().OfType<TextBox>().Single().Text = export;
        var preview = window.GetVisualDescendants().OfType<Button>()
            .Single(b => b.Content as string == "Preview…");
        preview.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        // Its own point re-finds its own cluster: nothing new, one "refined", no
        // fresh observations — the add-only merge contract, visible in the preview.
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Text?.StartsWith("Befallen: +0 new points, 1 refined, +0 observations") == true);

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void SessionPickerListsNewestFirstAndPreselectsIt()
    {
        var older = new LogSessionInfo(0, 10,
            new DateTime(2026, 8, 1, 10, 0, 0), new DateTime(2026, 8, 1, 11, 0, 0));
        var newer = new LogSessionInfo(10, 20,
            new DateTime(2026, 8, 2, 10, 0, 0), new DateTime(2026, 8, 2, 10, 30, 0));

        var window = new SessionPickerWindow("eqlog_Test.txt", [older, newer]);
        window.Show();

        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Text?.Contains("holds 2 sessions") == true);
        var items = window.GetVisualDescendants().OfType<ListBoxItem>().ToList();
        Assert.Equal(2, items.Count);
        Assert.Same(newer, items[0].Tag);
        Assert.True(items[0].IsSelected);

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }
}
