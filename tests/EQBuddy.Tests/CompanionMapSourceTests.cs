using EQBuddy.Companion;
using EQBuddy.Core;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>The map surface: the pack's picture is loaded once per zone, cached, and
/// stamped so a device holding it is never sent it again; the spawn circles and the
/// /loc marker ride every push.</summary>
public class CompanionMapSourceTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 8, 14, 20, 0, 0);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "eqb-map-" + Guid.NewGuid().ToString("N"));

    public CompanionMapSourceTests()
    {
        Directory.CreateDirectory(_dir);
        // A tiny Brewall-shaped map: two segments (one near-black, which the shared
        // readability rule lifts) and one labeled point.
        File.WriteAllLines(Path.Combine(_dir, "befallen.txt"),
        [
            "L 0.0, 0.0, 0.0, 100.0, 50.0, 0.0, 200, 200, 200",
            "L 100.0, 50.0, 0.0, 100.0, 150.0, 0.0, 0, 0, 0",
            "P -20.0, -30.0, 0.0, 240, 200, 60, 3, Camp_One",
        ]);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* temp dir */ }
        GC.SuppressFinalize(this);
    }

    private CompanionMapSource Source() =>
        new(new AppSettings { MapFolder = _dir });

    [Fact]
    public void LoadsTheZonesPictureAndStampsIt()
    {
        var map = Source().Build("Befallen", "Befallen", null, [], null, Now);

        Assert.Equal("Befallen", map.Zone);
        Assert.Null(map.Missing);
        var geo = Assert.IsType<CompanionMapGeometry>(map.Geometry);
        Assert.NotEmpty(geo.Stamp);
        Assert.Equal(map.GeometryStamp, geo.Stamp);
        Assert.False(geo.Truncated);

        // One stroke per colour, segments flattened to x1,y1,x2,y2 — and the black
        // line arrives lifted to the shared readable grey, not invisible on a dark UI.
        Assert.Equal(2, geo.Strokes.Count);
        Assert.Contains(geo.Strokes, s => s.Color == "#C8C8C8" && s.Segments.SequenceEqual([0, 0, 100, 50]));
        Assert.Contains(geo.Strokes, s => s.Color == "#AAAAAA");
        Assert.Equal("Camp One", geo.Pois[0].Label);   // underscores are spaces
        Assert.Equal(-30, geo.MinY);
        Assert.Equal(150, geo.MaxY);
    }

    [Fact]
    public void GeometryIsParsedOncePerZoneAndTheStampHoldsStill()
    {
        var source = Source();
        var first = source.Build("Befallen", "Befallen", null, [], null, Now);
        var second = source.Build("Befallen", "Befallen", null, [], null, Now.AddSeconds(1));

        // The same object, not a re-parse — this is the "never re-serialized per tick"
        // promise the wire's sticky-geometry rule depends on.
        Assert.Same(first.Geometry, second.Geometry);
        Assert.Equal(first.GeometryStamp, second.GeometryStamp);
    }

    [Fact]
    public void AMissingMapNamesTheFileItWanted()
    {
        var map = Source().Build("Plane of Sky", "Plane of Sky", null, [], null, Now);
        Assert.Null(map.Geometry);
        Assert.NotNull(map.Missing);
        Assert.Contains("airplane.txt", map.Missing);
        Assert.Contains(_dir, map.Missing);
    }

    [Fact]
    public void TheMarkerIsTheLastLocInMapSpace()
    {
        // The game prints /loc as (Y, X); a position plots at map (-X, -Y).
        var loc = new LocationEvent(Now.AddSeconds(-45), 30, 20, 0);
        var map = Source().Build("Befallen", "Befallen", null, [], loc, Now);

        Assert.NotNull(map.You);
        Assert.Equal(-20, map.You!.X);
        Assert.Equal(-30, map.You.Y);
        Assert.Equal(45, map.You.AgeSeconds, 1);
    }

    [Fact]
    public void CirclesCarryTheirCountdownAndTheirImminence()
    {
        var catalog = SpawnCatalog.LoadEmbedded();
        var zone = catalog.Zones.FirstOrDefault(z => z.Named.Count > 0);
        Assert.NotNull(zone);
        var named = zone!.Named[0].Name;

        var ledgerDir = Path.Combine(_dir, "ledger");
        var ledger = new SpawnPointLedger(ledgerDir, catalog);
        ledger.Apply(new ZoneEvent(Now.AddMinutes(-10), zone.Zone));
        ledger.Apply(new LocationEvent(Now.AddMinutes(-9), 100, 200, 0));
        ledger.Apply(new KillEvent(Now.AddMinutes(-9), named, "Dranak"));

        // A running timer with 5 seconds left: inside the pulse window.
        var timers = new List<SpawnTimerState>
        {
            new("legends", zone.Zone, named, Now.AddSeconds(-55), 60),
        };
        var map = Source().Build("Befallen", zone.Zone, ledger, timers, null, Now);

        var circle = Assert.Single(map.Circles);
        Assert.True(circle.Named);
        Assert.Equal(named, circle.Label);
        Assert.False(circle.Projected);
        Assert.Equal(5, circle.DueSeconds!.Value, 1);
        Assert.True(circle.Imminent);
        Assert.Equal(1, circle.Kills);
        Assert.Contains(named, circle.Mobs, StringComparison.OrdinalIgnoreCase);
        // Map space again: /loc (100, 200) plots at (-200, -100).
        Assert.Equal(-200, circle.X);
        Assert.Equal(-100, circle.Y);
    }
}
