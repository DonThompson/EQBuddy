using EQBuddy.Core;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>
/// The zone-map coverage audit (the Qeynos Hills field report, 2026-08-13: a zone
/// whose name neither the alias table nor containment can bridge is a zone with NO
/// map). Ground truth is the client's own zone table — map-file stem to the
/// "You have entered X." display name — mined via eqltools.com
/// (scripts/harvests/eqltools/layout-extract.json). Every zone EQBuddy knows
/// (ZoneGraph nodes, SpawnCatalog names) must resolve to a stem the game actually
/// ships, so a future zone addition cannot silently lose its map.
/// </summary>
public class ZoneMapCoverageTests : IDisposable
{
    /// <summary>Stem → client display name, verbatim from the client extract, plus
    /// "arena": The Arena is a ZoneGraph node the extract carries no map for, and
    /// packs that do cover it use the classic stem.</summary>
    private static readonly (string Stem, string Display)[] ClientZones =
    [
        ("airplane", "The Plane of Sky"),
        ("akanon", "Ak'Anon"),
        ("befallen", "Befallen"),
        ("beholder", "The Gorge of King Xorbb"),
        ("blackburrow", "Blackburrow"),
        ("burningwood", "The Burning Woods"),
        ("butcher", "Butcherblock Mountains"),
        ("cabeast", "Cabilis East"),
        ("cabwest", "Cabilis West"),
        ("cauldron", "Dagnor's Cauldron"),
        ("cazicthule", "Temple of Cazic-Thule"),
        ("charasis", "Howling Stones"),
        ("chardok", "Chardok"),
        ("citymist", "The City of Mist"),
        ("cobaltscar", "Cobalt Scar"),
        ("commons", "West Commonlands"),
        ("crushbone", "Clan Crushbone"),
        ("crystal", "Crystal Caverns"),
        ("dalnir", "The Crypt of Dalnir"),
        ("dreadlands", "The Dreadlands"),
        ("droga", "The Temple of Droga"),
        ("eastkarana", "The Eastern Plains of Karana"),
        ("eastwastes", "Eastern Wastes"),
        ("ecommons", "East Commonlands"),
        ("emeraldjungle", "The Emerald Jungle"),
        ("erudnext", "Erudin"),
        ("erudnint", "Erudin Palace"),
        ("erudsxing", "Erud's Crossing"),
        ("everfrost", "Everfrost Peaks"),
        ("fearplane", "The Plane of Fear"),
        ("feerrott", "The Feerrott"),
        ("felwithea", "Northern Felwithe"),
        ("felwitheb", "Southern Felwithe"),
        ("fieldofbone", "The Field of Bone"),
        ("firiona", "Firiona Vie"),
        ("freporte", "East Freeport"),
        ("freportn", "North Freeport"),
        ("freportw", "West Freeport"),
        ("frontiermtns", "Frontier Mountains"),
        ("frozenshadow", "The Tower of Frozen Shadow"),
        ("gfaydark", "The Greater Faydark"),
        ("greatdivide", "The Great Divide"),
        ("grobb", "Grobb"),
        ("growthplane", "The Plane of Growth"),
        ("gukbottom", "The Ruins of Old Guk"),
        ("guktop", "The City of Guk"),
        ("halas", "Halas"),
        ("hateplane", "The Plane of Hate"),
        ("highkeep", "High Keep"),
        ("highpass", "Highpass Hold"),
        ("hole", "The Ruins of Old Paineel"),
        ("iceclad", "The Iceclad Ocean"),
        ("innothule", "Innothule Swamp"),
        ("kael", "Kael Drakkel"),
        ("kaesora", "Kaesora"),
        ("kaladima", "South Kaladim"),
        ("kaladimb", "North Kaladim"),
        ("karnor", "Karnor's Castle"),
        ("kedge", "Kedge Keep"),
        ("kerraridge", "Kerra Isle"),
        ("kithicor", "Kithicor Forest"),
        ("kurn", "Kurn's Tower"),
        ("lakeofillomen", "Lake of Ill Omen"),
        ("lakerathe", "Lake Rathetear"),
        ("lavastorm", "The Lavastorm Mountains"),
        ("lfaydark", "The Lesser Faydark"),
        ("mischiefplane", "The Plane of Mischief"),
        ("mistmoore", "The Castle of Mistmoore"),
        ("misty", "Misty Thicket"),
        ("najena", "Najena"),
        ("necropolis", "Dragon Necropolis"),
        ("nektulos", "Nektulos Forest"),
        ("neriaka", "Neriak - Foreign Quarter"),
        ("neriakb", "Neriak - Commons"),
        ("neriakc", "Neriak - Third Gate"),
        ("newsebexp", "New Sebilis Expedition"),
        ("northkarana", "The Northern Plains of Karana"),
        ("nro", "The Northern Desert of Ro"),
        ("nurga", "The Mines of Nurga"),
        ("oasis", "The Oasis of Marr"),
        ("oggok", "Oggok"),
        ("oot", "The Ocean of Tears"),
        ("overthere", "The Overthere"),
        ("paineel", "Paineel"),
        ("paw", "The Lair of the Splitpaw"),
        ("permafrost", "Permafrost Keep"),
        ("qcat", "The Qeynos Aqueduct System"),
        ("qey2hh1", "The Western Plains of Karana"),
        ("qeynos", "South Qeynos"),
        ("qeynos2", "North Qeynos"),
        ("qeytoqrg", "Qeynos Hills"),
        ("qrg", "Surefall Glade"),
        ("rathemtn", "The Rathe Mountains"),
        ("rivervale", "Rivervale"),
        ("runnyeye", "The Liberated Citadel of Runnyeye"),
        ("sebilis", "The Ruins of Sebilis"),
        ("sirens", "Siren's Grotto"),
        ("skyfire", "The Skyfire Mountains"),
        ("skyshrine", "Skyshrine"),
        ("sleeper", "The Sleeper's Tomb"),
        ("soldunga", "Solusek's Eye"),
        ("soldungb", "Nagafen's Lair"),
        ("soltemple", "The Temple of Solusek Ro"),
        ("southkarana", "The Southern Plains of Karana"),
        ("sro", "The Southern Desert of Ro"),
        ("steamfont", "The Steamfont Mountains"),
        ("stonebrunt", "The Stonebrunt Mountains"),
        ("swampofnohope", "The Swamp of No Hope"),
        ("templeveeshan", "The Temple of Veeshan"),
        ("thurgadina", "The City of Thurgadin"),
        ("thurgadinb", "Icewell Keep"),
        ("timorous", "Timorous Deep"),
        ("tox", "Toxxulia Forest"),
        ("trakanon", "Trakanon's Teeth"),
        ("unrest", "The Estate of Unrest"),
        ("veeshan", "Veeshan's Peak"),
        ("velketor", "Velketor's Labyrinth"),
        ("wakening", "The Wakening Land"),
        ("warrens", "The Warrens"),
        ("warslikswood", "The Warsliks Woods"),
        ("westwastes", "The Western Wastes"),
        ("arena", "The Arena"),
    ];

    private readonly string _dir = Directory.CreateTempSubdirectory("eqbuddy-map-audit-").FullName;

    public ZoneMapCoverageTests()
    {
        // One folder holding every stem the game ships. Assertions demand the EXACT
        // stem, so a containment near-miss (neriakb landing on neriakc.txt) fails
        // loud instead of quietly showing the wrong zone.
        foreach (var (stem, _) in ClientZones)
            File.WriteAllText(Path.Combine(_dir, stem + ".txt"), "L 0, 0, 0, 1, 1, 0, 0, 0, 0");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void AssertResolves(string zoneName, string expectedStem, string source)
    {
        var file = ZoneMapFiles.Resolve(_dir, zoneName);
        Assert.True(file is not null,
            $"{source} '{zoneName}': no map resolved — expected {expectedStem}.txt; add an alias in ZoneMapFiles.Shortnames");
        Assert.True(string.Equals(Path.GetFileNameWithoutExtension(file), expectedStem, StringComparison.OrdinalIgnoreCase),
            $"{source} '{zoneName}': resolved {Path.GetFileName(file)}, expected {expectedStem}.txt");
    }

    [Fact]
    public void EveryClientZoneNameResolvesToItsOwnMap()
    {
        foreach (var (stem, display) in ClientZones)
        {
            AssertResolves(display, stem, "client");
            // The no-map guidance must name the same file resolution would load.
            Assert.Equal(stem, ZoneMapFiles.ExpectedShortname(display));
        }
    }

    [Fact]
    public void EveryZoneGraphZoneResolvesToAShippedStem()
    {
        var stems = ClientZones.Select(z => z.Stem).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var graph = ZoneGraph.LoadEmbedded();
        Assert.True(graph.ZoneCount > 100, "ZoneGraph failed to load");
        foreach (var zone in graph.Zones)
        {
            var expected = ZoneMapFiles.ExpectedShortname(zone);
            Assert.True(stems.Contains(expected),
                $"ZoneGraph '{zone}': expected stem '{expected}' is no map file the game ships — add an alias in ZoneMapFiles.Shortnames");
            AssertResolves(zone, expected, "ZoneGraph");
        }
    }

    [Fact]
    public void EverySpawnCatalogZoneNameResolvesToAShippedStem()
    {
        var stems = ClientZones.Select(z => z.Stem).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var catalog = SpawnCatalog.LoadEmbedded();
        Assert.True(catalog.Zones.Count > 100, "SpawnCatalog failed to load");
        foreach (var zone in catalog.Zones)
            // Zone, LogZoneName, and every log alias all reach the map window as
            // "the zone I'm in" — each must land on a real map.
            foreach (var name in new[] { zone.Zone, zone.LogZoneName }.Concat(zone.LogZoneAliases)
                         .Where(n => n.Length > 0))
            {
                var expected = ZoneMapFiles.ExpectedShortname(name);
                Assert.True(stems.Contains(expected),
                    $"SpawnCatalog '{name}': expected stem '{expected}' is no map file the game ships — add an alias in ZoneMapFiles.Shortnames");
                AssertResolves(name, expected, "SpawnCatalog");
            }
    }
}
