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
}
