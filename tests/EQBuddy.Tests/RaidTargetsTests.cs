using EQBuddy.Core;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>
/// The raid-target ledger: kills the log witnesses, achievements the dump vouches
/// for, per-character scoping, and replay idempotence via the time high-water mark.
/// </summary>
public class RaidTargetsTests
{
    private static readonly DateTime T0 = DateTime.Parse("2026-08-12T22:00:00");

    private static GameEvent Ev(int seconds, string message) =>
        LogParser.Parse($"[{T0.AddSeconds(seconds):ddd MMM d HH:mm:ss yyyy}] {message}")!;

    private static RaidKillLedger Ledger(string character = "Dranak|legends") =>
        new(path: null) { CharacterKey = () => character };

    [Fact]
    public void TheCatalogKnowsTheClassicRaidBosses()
    {
        var c = RaidTargetCatalog.Default;
        Assert.True(c.BossCount >= 20, $"suspiciously small raid catalog: {c.BossCount}");
        Assert.True(c.IsRaidBoss("Lord Nagafen"));
        Assert.True(c.IsRaidBoss("Cazic-Thule"));
        // The dump hyphenates, logs and the wiki don't — both forms must land
        // (a kill line saying "Cazic Thule" was invisible to the ledger before).
        Assert.True(c.IsRaidBoss("Cazic Thule"));
        Assert.False(c.IsRaidBoss("a gnoll pup"));
    }

    [Fact]
    public void ResolveBossReturnsTheCanonicalNameForTitledAndFoldedForms()
    {
        var c = RaidTargetCatalog.Default;
        Assert.Equal("Innoruuk", c.ResolveBoss("Innoruuk"));
        // #140 (AkevoTheBard): the log titles him "Innoruuk, Prince of Hate";
        // the pre-comma head must land on the catalog's bare name.
        Assert.Equal("Innoruuk", c.ResolveBoss("Innoruuk, Prince of Hate"));
        Assert.True(c.IsRaidBoss("Innoruuk, Prince of Hate"));
        Assert.Equal("Cazic-Thule", c.ResolveBoss("Cazic Thule"));   // hyphen fold keeps the dump's spelling
        Assert.Null(c.ResolveBoss("a gnoll pup"));
        // The head consults only the catalog — a comma alone vouches for nothing.
        Assert.Null(c.ResolveBoss("a servant, of nothing"));
    }

    [Fact]
    public void AWitnessedKillIsRecordedWithItsDateWhoeverLandedTheBlow()
    {
        var l = Ledger();
        l.Apply(Ev(0, "Lord Nagafen has been slain by Tankname!"));

        var rec = l.For("Lord Nagafen")!;
        Assert.Equal(1, rec.Kills);
        Assert.Equal(T0, rec.FirstKill);
        Assert.Equal(1, l.DefeatedCount());
    }

    [Fact]
    public void ATitledKillLineLandsOnTheCatalogRow()
    {
        // #140 (AkevoTheBard): "Innoruuk, Prince of Hate has been slain" must
        // record under "Innoruuk" — the name the Raids card and its D0–D4 badge
        // ask For() with — not under the titled log form.
        var l = Ledger();
        l.Apply(Ev(0, "You have entered The Plane of Hate - Solo."));
        l.Apply(Ev(10, "Innoruuk, Prince of Hate has been slain by Tankname!"));

        var rec = l.For("Innoruuk")!;
        Assert.Equal(1, rec.Kills);
        Assert.Equal(1, rec.TierKills["d0"]);
        Assert.Equal(0, rec.HighestDifficulty());
        Assert.Equal(1, l.DefeatedCount());
    }

    [Fact]
    public void ReplayingTheSameLogDoesNotDoubleCount()
    {
        var l = Ledger();
        var kill = Ev(0, "You have slain Lady Vox!");
        l.Apply(kill);
        l.Apply(kill);   // startup replay revisits the same timestamp

        Assert.Equal(1, l.For("Lady Vox")!.Kills);
    }

    [Fact]
    public void AchievementsMarkOldClearsWithoutInventingKills()
    {
        var l = Ledger();
        var achievements = AchievementsImport.Parse(
        [
            "EverQuest: Raids",
            "C\tConqueror of The Permafrost Caverns",
            "C\t\tLady Vox",
            "I\tConqueror of Nagafen's Lair",
            "I\t\tLord Nagafen",
        ]);
        Assert.Equal(1, l.MarkAchievements(achievements));

        var vox = l.For("Lady Vox")!;
        Assert.True(vox.AchievementComplete);
        Assert.Equal(0, vox.Kills);                    // vouched for, never witnessed
        Assert.Null(l.For("Lord Nagafen"));            // incomplete marks nothing
        Assert.Equal(1, l.DefeatedCount());
    }

    [Fact]
    public void EachCharacterKeepsTheirOwnClears()
    {
        var character = "Dranak|legends";
        var l = new RaidKillLedger(path: null) { CharacterKey = () => character };
        l.Apply(Ev(0, "You have slain Lady Vox!"));

        character = "Aenari|legends";                  // the watcher followed a sibling
        Assert.Null(l.For("Lady Vox"));
        Assert.Equal(0, l.DefeatedCount());
    }

    // ---- difficulty tiers, decoded from the zone-enter line (#109 data) ----

    [Theory]
    [InlineData("The Plane of Hate", InstanceTier.OpenWorld)]
    [InlineData("The Plane of Hate - Solo", 0)]
    [InlineData("Nagafen's Lair - Group 3 (Fused)", 3)]
    [InlineData("Najena 4 (Refined)", 4)]
    [InlineData("The Plane of Sky 1 (Awakened)", 1)]
    [InlineData("Befallen 2 (Adaptive)", 2)]
    [InlineData("Kaesora 7 (Mythic)", InstanceTier.UnknownAdjective)]  // unknown adjective ≠ D0
    public void ZoneLinesDecodeToTiers(string zone, int tier) =>
        Assert.Equal(tier, InstanceTier.FromZoneName(zone));

    [Fact]
    public void KillsRecordTheDifficultyTheyHappenedAt()
    {
        var l = Ledger();
        l.Apply(Ev(0, "You have entered Nagafen's Lair - Group 3 (Fused)."));
        l.Apply(Ev(10, "Lord Nagafen has been slain by Tankname!"));
        l.Apply(Ev(500, "You have entered Nagafen's Lair - Solo."));
        l.Apply(Ev(510, "Lord Nagafen has been slain by Tankname!"));
        l.Apply(Ev(900, "You have entered Nagafen's Lair."));
        l.Apply(Ev(910, "Lord Nagafen has been slain by Tankname!"));

        var rec = l.For("Lord Nagafen")!;
        Assert.Equal(3, rec.Kills);
        Assert.Equal(1, rec.TierKills["d3"]);
        Assert.Equal(1, rec.TierKills["d0"]);
        Assert.Equal(1, rec.TierKills["open"]);
        Assert.Equal(3, rec.HighestDifficulty());      // the badge: highest PROVEN
    }

    [Fact]
    public void AKillBeforeAnyZoneLineIsHonestlyUnknown()
    {
        var l = Ledger();
        l.Apply(Ev(0, "You have slain Lady Vox!"));
        var rec = l.For("Lady Vox")!;
        Assert.Equal(1, rec.TierKills["unknown"]);
        Assert.Null(rec.HighestDifficulty());          // no badge from a guess
    }

    [Fact]
    public void ForReturnsASnapshotNotTheLiveRecord()
    {
        // 2026-08-13 review: the live record's TierKills dictionary mutates on the
        // watcher thread; handing the UI the same instance invited an
        // enumerate-during-add crash. For() must clone.
        var l = Ledger();
        l.Apply(Ev(0, "You have entered Nagafen's Lair - Group 3 (Fused)."));
        l.Apply(Ev(1, "Lord Nagafen has been slain by Tankname!"));

        var snapshot = l.For("Lord Nagafen")!;
        l.Apply(Ev(700, "Lord Nagafen has been slain by Tankname!"));

        Assert.Equal(1, snapshot.Kills);                    // the snapshot froze
        Assert.Equal(1, snapshot.TierKills["d3"]);
        Assert.Equal(2, l.For("Lord Nagafen")!.Kills);      // a fresh read sees both
    }

    [Fact]
    public void LoadRekeysPreCanonicalRecordsAndMergesDuplicates()
    {
        // Pre-#140 stores keyed kills by the raw log spelling: a "Cazic Thule"
        // kill could never be read back through the catalog's "Cazic-Thule" row.
        // Loading must re-key through ResolveBoss, merge with any canonical
        // duplicate (kills sum — the keys held disjoint events; tiers max;
        // FirstKill earliest, LastKill latest; achievement ORs), leave keys the
        // catalog can't resolve alone, and persist once.
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        File.WriteAllText(path, """
            {"Records":{
              "Dranak|legends|Cazic thule":{"Kills":2,"FirstKill":"2026-07-01T20:00:00","LastKill":"2026-07-08T20:00:00","TierKills":{"d1":2}},
              "Dranak|legends|Cazic-Thule":{"Kills":1,"FirstKill":"2026-07-15T20:00:00","LastKill":"2026-07-15T20:00:00","AchievementComplete":true,"TierKills":{"d3":1}},
              "Dranak|legends|Fippy Darkpaw":{"Kills":5}},
             "HighWater":"2026-07-15T20:00:00"}
            """);
        try
        {
            var l = new RaidKillLedger(path) { CharacterKey = () => "Dranak|legends" };

            var rec = l.For("Cazic-Thule")!;
            Assert.Equal(3, rec.Kills);
            Assert.Equal(DateTime.Parse("2026-07-01T20:00:00"), rec.FirstKill);
            Assert.Equal(DateTime.Parse("2026-07-15T20:00:00"), rec.LastKill);
            Assert.True(rec.AchievementComplete);
            Assert.Equal(2, rec.TierKills["d1"]);
            Assert.Equal(1, rec.TierKills["d3"]);
            Assert.Equal(5, l.For("Fippy Darkpaw")!.Kills);   // unresolvable key untouched

            var once = File.ReadAllText(path);
            Assert.Contains("Cazic-Thule", once);              // migrated form persisted
            _ = new RaidKillLedger(path);
            Assert.Equal(once, File.ReadAllText(path));        // second load changes nothing
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RecordsFromBeforeTierTrackingRoundTrip()
    {
        // A pre-1.72 store has no TierKills field at all: it must load, count as
        // before, and simply carry no badge.
        var rec = System.Text.Json.JsonSerializer.Deserialize<RaidBossRecord>(
            """{"Kills":4,"FirstKill":"2026-07-19T20:00:00","LastKill":"2026-08-01T21:00:00","AchievementComplete":false}""")!;
        Assert.Equal(4, rec.Kills);
        Assert.Empty(rec.TierKills);
        Assert.Null(rec.HighestDifficulty());
    }
}
