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
    public void ReplayingTheSameLogDoesNotDoubleCount()
    {
        var l = Ledger();
        var kill = Ev(0, "You have slain Lady Vox!");
        l.Apply(kill);
        l.Apply(kill);   // startup replay revisits the same timestamp

        Assert.Equal(1, l.For("Lady Vox")!.Kills);
    }

    [Fact]
    public void ASecondCharactersOlderKillSurvivesTheFirstOnesNewerOne()
    {
        // Audit finding 2a: one global high-water mark meant that after the watcher
        // followed character A to a kill at T+600, character B's replayed kill at T
        // was "history" forever — B's clear never landed. The gate is per
        // character-and-boss now.
        var character = "Dranak|legends";
        var l = new RaidKillLedger(path: null) { CharacterKey = () => character };
        l.Apply(Ev(600, "You have slain Lady Vox!"));

        character = "Aenari|legends";                  // switch; B's log replays from earlier
        l.Apply(Ev(0, "You have slain Lord Nagafen!"));
        Assert.Equal(1, l.For("Lord Nagafen")!.Kills);
        Assert.Equal(1, l.DefeatedCount());
    }

    [Fact]
    public void TwoBossesDyingInTheSameLogSecondBothRecord()
    {
        // Audit finding 2b: log stamps have 1-second resolution, and the global mark
        // swallowed the second boss of a same-second double kill even live.
        var l = Ledger();
        l.Apply(Ev(0, "Lord Nagafen has been slain by Tankname!"));
        l.Apply(Ev(0, "Lady Vox has been slain by Tankname!"));

        Assert.Equal(1, l.For("Lord Nagafen")!.Kills);
        Assert.Equal(1, l.For("Lady Vox")!.Kills);
        Assert.Equal(2, l.DefeatedCount());
    }

    [Fact]
    public void ReplayingAWholeMultiKillLogTwiceChangesNothing()
    {
        // The idempotence bar the per-record gate has to clear: a full replay of
        // everything already recorded — several bosses, repeat kills, a same-second
        // pair — must leave every count exactly where it was.
        var l = Ledger();
        var history = new[]
        {
            Ev(0, "Lord Nagafen has been slain by Tankname!"),
            Ev(0, "Lady Vox has been slain by Tankname!"),
            Ev(300, "You have slain Lord Nagafen!"),
        };
        foreach (var e in history) l.Apply(e);
        foreach (var e in history) l.Apply(e);   // auto-follow away and back

        Assert.Equal(2, l.For("Lord Nagafen")!.Kills);
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
