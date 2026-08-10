using System.Text;
using EQBuddy.Core;

namespace EQBuddy.Tests;

public class ZoneMapTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("eqbuddy-maps-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void ParsesBrewallLinesAndPoints()
    {
        var path = Path.Combine(_dir, "crushbone.txt");
        File.WriteAllText(path, string.Join("\r\n",
        [
            "L 100.5, -200.0, 3.1, 150.0, -250.0, 3.1, 0, 0, 0",
            "L -50, 60, 0, -80, 90, 0, 255, 128, 0",
            "P 120.0, -210.0, 3.1, 0, 127, 0, 2, Orc_Camp_One",
            "garbage line that matches nothing",
            "P bad, data, here",
        ]), Encoding.ASCII);

        var map = ZoneMap.Load(path);

        Assert.Equal(2, map.Lines.Count);
        Assert.Single(map.Points);
        Assert.Equal("Orc Camp One", map.Points[0].Label);   // underscores become spaces
        Assert.Equal((255, 128, 0), (map.Lines[1].R, map.Lines[1].G, map.Lines[1].B));
        Assert.Equal(-80, map.MinX);
        Assert.Equal(150, map.MaxX);
    }

    [Fact]
    public void LocPlotsWithTheClassicAxisConvention()
    {
        // /loc prints "Y, X, Z"; a game position lands at map (-X, -Y) — the
        // convention every EQ map tool settled on. A character at loc
        // "-1085.05, -675.53" therefore plots at map (675.53, 1085.05).
        Assert.Equal((675.53, 1085.05), ZoneMap.FromLoc(-1085.05, -675.53));
    }

    [Fact]
    public void LocationLineParses()
    {
        var e = Assert.IsType<LocationEvent>(LogParser.Parse(
            "[Sat Jul 18 15:00:00 2026] Your Location is -1085.05, -675.53, 3.75"));
        Assert.Equal((-1085.05, -675.53, 3.75), (e.LocY, e.LocX, e.LocZ));
    }

    [Fact]
    public void ZoneNamesResolveToMapFiles()
    {
        foreach (var stem in new[] { "ecommons", "crushbone", "unrest", "qey2hh1", "gfaydark" })
            File.WriteAllText(Path.Combine(_dir, stem + ".txt"), "L 0, 0, 0, 1, 1, 0, 0, 0, 0");
        File.WriteAllText(Path.Combine(_dir, "crushbone_1.txt"), "L 0, 0, 0, 2, 2, 0, 0, 0, 0");

        Assert.EndsWith("ecommons.txt", ZoneMapFiles.Resolve(_dir, "East Commonlands")!);
        Assert.EndsWith("crushbone.txt", ZoneMapFiles.Resolve(_dir, "Crushbone")!);
        Assert.EndsWith("unrest.txt", ZoneMapFiles.Resolve(_dir, "The Estate of Unrest")!);
        Assert.EndsWith("qey2hh1.txt", ZoneMapFiles.Resolve(_dir, "West Karana")!);
        // Difficulty tiers fold to the base zone (the spawn-timer rule, reused).
        Assert.EndsWith("unrest.txt", ZoneMapFiles.Resolve(_dir, "Estate of Unrest 4 (Refined)")!);
        Assert.Null(ZoneMapFiles.Resolve(_dir, "Plane of Nowhere"));

        // Layer files ride along with their base map, in order.
        Assert.Equal(2, ZoneMapFiles.WithLayers(Path.Combine(_dir, "crushbone.txt")).Count());
    }

    [Fact]
    public void BreadcrumbTrailAccumulatesAndClearsOnZoning()
    {
        var stats = new SessionStats { CharacterName = "Dranak" };
        string At(int m, string msg) => $"[Sat Jul 18 15:{m:D2}:00 2026] {msg}";
        stats.Apply(LogParser.Parse(At(0, "You have entered Crushbone."))!);
        for (var i = 1; i <= 3; i++)
            stats.Apply(LogParser.Parse(At(i, $"Your Location is -{i}00.0, 50.0, 3.0"))!);
        var trail = stats.Snapshot().LocationTrail;
        Assert.Equal(3, trail.Count);
        Assert.Equal(-100.0, trail[0].LocY);   // oldest first
        Assert.Equal(-300.0, trail[2].LocY);

        stats.Apply(LogParser.Parse(At(5, "You have entered Greater Faydark."))!);
        Assert.Empty(stats.Snapshot().LocationTrail);
    }

    [Fact]
    public void WikiLocationFieldsParseIntoCoordinates()
    {
        var svc = new EqlWikiMobService(Path.Combine(_dir, "mobcache"),
            _ => Task.FromResult<string?>(
                "{{Namedmobpage\n| name = Lord Gimblox\n| zone = [[Solusek's Eye]]\n" +
                "| location = -565, 30 (tower, third floor)\n| known_loot = {{:Runed Cloak}}\n}}"));
        var result = svc.LookupAsync("Lord Gimblox").GetAwaiter().GetResult();
        Assert.Equal("-565, 30 (tower, third floor)", result.Mob!.Location);
        Assert.Equal((-565.0, 30.0), result.Mob.LocYX!.Value);
    }

    [Fact]
    public void SessionTracksLastLocPerZone()
    {
        var stats = new SessionStats { CharacterName = "Dranak" };
        string At(int m, string msg) => $"[Sat Jul 18 15:{m:D2}:00 2026] {msg}";
        stats.Apply(LogParser.Parse(At(0, "You have entered Crushbone."))!);
        stats.Apply(LogParser.Parse(At(1, "Your Location is -100.0, 50.0, 3.0"))!);
        Assert.Equal(-100.0, stats.Snapshot().LastLocation!.LocY);

        // Zoning clears it — a position from the last zone would lie on this map.
        stats.Apply(LogParser.Parse(At(2, "You have entered Greater Faydark."))!);
        Assert.Null(stats.Snapshot().LastLocation);
    }
}
