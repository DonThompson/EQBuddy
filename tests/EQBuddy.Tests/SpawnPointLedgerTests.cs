using EQBuddy.Core;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>
/// The per-zone spawn-point archive (David's map brief, 2026-08-13): kills near a
/// fresh /loc cluster into points, archives persist and replay idempotently, and
/// projections come from the zone's own clock — plus the ZoneShare strings that
/// carry a zone's knowledge to another player, deviation gate included.
/// </summary>
public class SpawnPointLedgerTests
{
    private static readonly DateTime T0 = new(2026, 7, 18, 15, 0, 0);

    private static SpawnCatalog TestCatalog() => new()
    {
        Zones =
        [
            new SpawnZone
            {
                Zone = "Lower Guk",
                LogZoneName = "The Ruins of Old Guk",
                NamedDefaultSeconds = 1680,
                Named =
                [
                    new SpawnEntry { Name = "a froglok ghoul lord", RespawnSeconds = 1620 },
                    new SpawnEntry { Name = "the ghoul arch magi", Placeholder = "kor ghoul wizard" },
                ],
            },
            new SpawnZone
            {
                Zone = "Permafrost Keep",
                Named = [new SpawnEntry { Name = "Lady Vox", RespawnSeconds = 604800 }],
            },
        ],
    };

    private static SpawnPointLedger Ledger(string? dir = null) => new(dir, TestCatalog());

    // ---- observation: a point exists only where a /loc anchored a kill ----

    [Fact]
    public void KillNearAFreshLocBecomesASpawnPoint()
    {
        var l = Ledger();
        l.Apply(new ZoneEvent(T0, "The Ruins of Old Guk"));
        l.Apply(new LocationEvent(T0.AddMinutes(1), -500, 120, 3));
        l.Apply(new KillEvent(T0.AddMinutes(2), "a froglok ghoul lord", "You"));

        var archive = l.Snapshot("Lower Guk");
        var p = Assert.Single(archive.Points);
        Assert.Equal(-500, p.LocY);
        Assert.Equal(120, p.LocX);
        var (name, seen) = p.LastKilled();
        // Stored normalized, the same shape kill lines arrive in ("Froglok ghoul lord").
        Assert.True(SpawnCatalog.NameMatches("a froglok ghoul lord", name));
        Assert.Equal(1, seen.Kills);
    }

    [Fact]
    public void NoLocOrStaleLocRecordsNothing()
    {
        var l = Ledger();
        l.Apply(new ZoneEvent(T0, "The Ruins of Old Guk"));
        l.Apply(new KillEvent(T0.AddMinutes(2), "a froglok ghoul lord", "You"));
        Assert.Empty(l.Snapshot("Lower Guk").Points);

        l.Apply(new LocationEvent(T0.AddMinutes(3), -500, 120, 3));
        l.Apply(new KillEvent(T0.AddMinutes(7), "a froglok ghoul lord", "You"));   // > 3-min window
        Assert.Empty(l.Snapshot("Lower Guk").Points);
    }

    [Fact]
    public void ZoningClearsTheLastLoc()
    {
        var l = Ledger();
        l.Apply(new ZoneEvent(T0, "The Ruins of Old Guk"));
        l.Apply(new LocationEvent(T0.AddSeconds(10), -500, 120, 3));
        l.Apply(new ZoneEvent(T0.AddSeconds(20), "Innothule Swamp"));
        l.Apply(new KillEvent(T0.AddSeconds(30), "a gnoll", "You"));
        // The old zone's /loc must not pin a kill in the new zone.
        Assert.Empty(l.Snapshot("Innothule Swamp").Points);
        Assert.Empty(l.Snapshot("Lower Guk").Points);
    }

    // ---- clustering ----

    [Fact]
    public void NearbyKillsClusterAndRefineTheCentroid()
    {
        var l = Ledger();
        l.Apply(new ZoneEvent(T0, "The Ruins of Old Guk"));
        l.Apply(new LocationEvent(T0.AddMinutes(1), -500, 120, 3));
        l.Apply(new KillEvent(T0.AddMinutes(2), "a froglok ghoul lord", "You"));
        l.Apply(new LocationEvent(T0.AddMinutes(3), -520, 120, 3));   // 20 units away
        l.Apply(new KillEvent(T0.AddMinutes(4), "kor ghoul wizard", "You"));

        var archive = l.Snapshot("Lower Guk");
        var p = Assert.Single(archive.Points);
        Assert.Equal(-510, p.LocY);   // centroid moved halfway toward the second obs
        Assert.Equal(2, p.Mobs.Count);
        Assert.Equal(2, p.TotalKills());
    }

    [Fact]
    public void DistantKillStartsANewPoint()
    {
        var l = Ledger();
        l.Apply(new ZoneEvent(T0, "The Ruins of Old Guk"));
        l.Apply(new LocationEvent(T0.AddMinutes(1), -500, 120, 3));
        l.Apply(new KillEvent(T0.AddMinutes(2), "a froglok ghoul lord", "You"));
        l.Apply(new LocationEvent(T0.AddMinutes(3), -600, 300, 3));
        l.Apply(new KillEvent(T0.AddMinutes(4), "kor ghoul wizard", "You"));
        Assert.Equal(2, l.Snapshot("Lower Guk").Points.Count);
    }

    // ---- persistence + replay ----

    [Fact]
    public void ArchivePersistsAndReplayIsIdempotent()
    {
        var dir = Path.Combine(Path.GetTempPath(), "eqbuddy-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var l = Ledger(dir);
            l.Apply(new ZoneEvent(T0, "The Ruins of Old Guk"));
            l.Apply(new LocationEvent(T0.AddMinutes(1), -500, 120, 3));
            l.Apply(new KillEvent(T0.AddMinutes(2), "a froglok ghoul lord", "You"));

            // A restart replays the same log history into a fresh ledger.
            var l2 = Ledger(dir);
            l2.Apply(new ZoneEvent(T0, "The Ruins of Old Guk"));
            l2.Apply(new LocationEvent(T0.AddMinutes(1), -500, 120, 3));
            l2.Apply(new KillEvent(T0.AddMinutes(2), "a froglok ghoul lord", "You"));
            var p = Assert.Single(l2.Snapshot("Lower Guk").Points);
            Assert.Equal(1, p.TotalKills());   // high-water mark: not double-counted

            // Genuinely new history still lands.
            l2.Apply(new LocationEvent(T0.AddMinutes(30), -500, 120, 3));
            l2.Apply(new KillEvent(T0.AddMinutes(31), "a froglok ghoul lord", "You"));
            Assert.Equal(2, l2.Snapshot("Lower Guk").Points.Single().TotalKills());
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }

    // ---- named + projection ----

    [Fact]
    public void NamedPointWearsTheAccentOrdinaryDoesNot()
    {
        var l = Ledger();
        l.Apply(new ZoneEvent(T0, "The Ruins of Old Guk"));
        l.Apply(new LocationEvent(T0.AddMinutes(1), -500, 120, 3));
        l.Apply(new KillEvent(T0.AddMinutes(2), "froglok ghoul lord", "You"));   // article-folded
        l.Apply(new LocationEvent(T0.AddMinutes(3), -600, 300, 3));
        l.Apply(new KillEvent(T0.AddMinutes(4), "a froglok guard", "You"));

        var archive = l.Snapshot("Lower Guk");
        var named = archive.Points.Single(p => p.Mobs.ContainsKey("froglok ghoul lord"));
        var trash = archive.Points.Single(p => p.Mobs.ContainsKey("froglok guard"));
        Assert.True(l.IsNamedPoint("Lower Guk", named));
        Assert.False(l.IsNamedPoint("Lower Guk", trash));
    }

    [Fact]
    public void ProjectedRespawnUsesTheZoneClockOrStaysHonestlyUnknown()
    {
        var l = Ledger();
        l.Apply(new ZoneEvent(T0, "The Ruins of Old Guk"));
        l.Apply(new LocationEvent(T0.AddMinutes(1), -500, 120, 3));
        l.Apply(new KillEvent(T0.AddMinutes(2), "a froglok guard", "You"));
        var p = l.Snapshot("Lower Guk").Points.Single();
        Assert.Equal(T0.AddMinutes(2).AddSeconds(1680), l.ProjectedRespawn("Lower Guk", p));

        var l2 = Ledger();
        l2.Apply(new ZoneEvent(T0, "Permafrost Keep"));
        l2.Apply(new LocationEvent(T0.AddMinutes(1), 100, 100, 0));
        l2.Apply(new KillEvent(T0.AddMinutes(2), "a goblin", "You"));
        var p2 = l2.Snapshot("Permafrost Keep").Points.Single();
        Assert.Null(l2.ProjectedRespawn("Permafrost Keep", p2));   // no zone clock
    }

    [Fact]
    public void InstanceZoneNamesResolveToTheCatalogZone()
    {
        var l = Ledger();
        l.Apply(new ZoneEvent(T0, "The Ruins of Old Guk 4 (Refined)"));
        l.Apply(new LocationEvent(T0.AddMinutes(1), -500, 120, 3));
        l.Apply(new KillEvent(T0.AddMinutes(2), "a froglok ghoul lord", "You"));
        Assert.Single(l.Snapshot("Lower Guk").Points);
    }
}

public class ZoneShareTests
{
    private static readonly DateTime T0 = new(2026, 7, 18, 15, 0, 0);

    private static SpawnZone Befallen() => new()
    {
        Zone = "Befallen",
        NamedDefaultSeconds = 270,   // David's verification example: ~4:30
        Named = [new SpawnEntry { Name = "Marnek the Sage", RespawnSeconds = 270 }],
    };

    private static SpawnPointLedger.ZoneArchive Archive(params (double Y, double X, string Mob, int Kills)[] points)
    {
        var a = new SpawnPointLedger.ZoneArchive { Zone = "Befallen" };
        foreach (var (y, x, mob, kills) in points)
            a.Points.Add(new SpawnPointLedger.SpawnPoint
            {
                LocY = y, LocX = x,
                Mobs = { [mob] = new SpawnPointLedger.MobSeen { Kills = kills, LastKill = T0 } },
            });
        return a;
    }

    [Fact]
    public void RoundTripCarriesPointsAndLearnedTimersOnly()
    {
        var overrides = new SpawnOverrides();
        var learned = overrides.GetOrAdd("Befallen", "Marnek the Sage");
        learned.RespawnSeconds = 275;
        learned.Learned = true;
        var manual = overrides.GetOrAdd("Befallen", "an elf skeleton");
        manual.RespawnSeconds = 999;   // Learned=false: the sharer's manual edit

        var s = ZoneShare.Export(Archive((-100, 50, "Marnek the Sage", 3)), Befallen(), overrides);
        Assert.StartsWith(ZoneShare.Prefix, s);

        var preview = ZoneShare.PreviewImport(s, new SpawnPointLedger.ZoneArchive { Zone = "Befallen" },
            Befallen(), new SpawnOverrides());
        Assert.NotNull(preview);
        Assert.Equal(1, preview!.NewPoints);
        Assert.Equal(3, preview.NewObservations);
        var diff = Assert.Single(preview.Timers);
        Assert.Equal("Marnek the Sage", diff.Name);
        Assert.Equal(275, diff.IncomingSeconds);
        Assert.False(diff.Flagged);   // 275 vs 270 is well inside the gate
    }

    [Fact]
    public void GarbageStringsPreviewAsNull()
    {
        var local = new SpawnPointLedger.ZoneArchive { Zone = "Befallen" };
        Assert.Null(ZoneShare.PreviewImport("not a share string", local, Befallen(), new SpawnOverrides()));
        Assert.Null(ZoneShare.PreviewImport(ZoneShare.Prefix + "!!!corrupt!!!", local, Befallen(), new SpawnOverrides()));
    }

    [Fact]
    public void DeviationGateFlagsTankedTimers()
    {
        // David's Befallen test: the zone clock says ~4:30 (270s). Someone shipping
        // 600s (+122%) is flagged; 300s (+11%) sails through.
        var overrides = new SpawnOverrides();
        var o = overrides.GetOrAdd("Befallen", "Marnek the Sage");
        o.RespawnSeconds = 600;
        o.Learned = true;
        var tanked = ZoneShare.Export(Archive(), Befallen(), overrides);
        var preview = ZoneShare.PreviewImport(tanked, new SpawnPointLedger.ZoneArchive { Zone = "Befallen" },
            Befallen(), new SpawnOverrides());
        Assert.True(Assert.Single(preview!.Timers).Flagged);
        Assert.Single(preview.FlaggedTimers);

        o.RespawnSeconds = 300;
        var fine = ZoneShare.Export(Archive(), Befallen(), overrides);
        preview = ZoneShare.PreviewImport(fine, new SpawnPointLedger.ZoneArchive { Zone = "Befallen" },
            Befallen(), new SpawnOverrides());
        Assert.False(Assert.Single(preview!.Timers).Flagged);
    }

    [Fact]
    public void FlaggedTimersApplyOnlyWhenTheImporterSaysSo()
    {
        var sharer = new SpawnOverrides();
        var o = sharer.GetOrAdd("Befallen", "Marnek the Sage");
        o.RespawnSeconds = 600;
        o.Learned = true;
        var s = ZoneShare.Export(Archive(), Befallen(), sharer);

        var local = new SpawnPointLedger.ZoneArchive { Zone = "Befallen" };
        var mine = new SpawnOverrides();
        var preview = ZoneShare.PreviewImport(s, local, Befallen(), mine)!;

        ZoneShare.Apply(preview, local, mine, includeFlagged: false);
        Assert.Null(mine.Find("Befallen", "Marnek the Sage"));

        ZoneShare.Apply(preview, local, mine, includeFlagged: true);
        var applied = mine.Find("Befallen", "Marnek the Sage");
        Assert.Equal(600, applied?.RespawnSeconds);
        Assert.True(applied?.Learned);
    }

    [Fact]
    public void ImportNeverOverwritesAManualEdit()
    {
        var sharer = new SpawnOverrides();
        var o = sharer.GetOrAdd("Befallen", "Marnek the Sage");
        o.RespawnSeconds = 280;
        o.Learned = true;
        var s = ZoneShare.Export(Archive(), Befallen(), sharer);

        var mine = new SpawnOverrides();
        var my = mine.GetOrAdd("Befallen", "Marnek the Sage");
        my.RespawnSeconds = 240;   // Learned=false: I typed this myself
        var local = new SpawnPointLedger.ZoneArchive { Zone = "Befallen" };
        var preview = ZoneShare.PreviewImport(s, local, Befallen(), mine)!;
        ZoneShare.Apply(preview, local, mine, includeFlagged: true);

        var kept = mine.Find("Befallen", "Marnek the Sage");
        Assert.Equal(240, kept?.RespawnSeconds);
        Assert.False(kept?.Learned);
    }

    [Fact]
    public void PointMergeIsAddOnlyAndReimportAddsNothing()
    {
        var incoming = ZoneShare.Export(Archive((-100, 50, "an elf skeleton", 5)), Befallen(), new SpawnOverrides());
        var local = Archive((-110, 55, "an elf skeleton", 2));   // same cluster (≤30 units)
        var mine = new SpawnOverrides();

        var preview = ZoneShare.PreviewImport(incoming, local, Befallen(), mine)!;
        Assert.Equal(0, preview.NewPoints);
        Assert.Equal(1, preview.RefinedPoints);
        Assert.Equal(3, preview.NewObservations);   // 5 theirs − 2 mine

        ZoneShare.Apply(preview, local, mine, includeFlagged: false);
        Assert.Equal(5, local.Points.Single().TotalKills());   // max, not sum

        var again = ZoneShare.PreviewImport(incoming, local, Befallen(), mine)!;
        Assert.Equal(0, again.NewObservations);
        ZoneShare.Apply(again, local, mine, includeFlagged: false);
        Assert.Equal(5, local.Points.Single().TotalKills());   // idempotent
    }
}
