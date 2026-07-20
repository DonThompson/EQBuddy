using EQBuddy.Core;

namespace EQBuddy.Tests;

/// <summary>Releases C/D/E: encounters, reward correlation, mob farming, stance, watch kinds.</summary>
public class EncounterTests
{
    private static string At(int mm, int ss, string msg) =>
        $"[Sat Jul 18 15:{mm:D2}:{ss:D2} 2026] {msg}";

    private static SessionStats Replay(params string[] lines)
    {
        var stats = new SessionStats { CharacterName = "Kaybek", ServerName = "freeport" };
        foreach (var line in lines)
        {
            var evt = LogParser.Parse(line);
            if (evt is not null) stats.Apply(evt);
        }
        return stats;
    }

    [Fact]
    public void SequentialSameNameKillsAreDistinctEncounters()
    {
        var s = Replay(
            At(0, 0, "You slash orc pawn for 10 points of damage."),
            At(0, 5, "You have slain orc pawn!"),
            At(0, 20, "You slash orc pawn for 10 points of damage."),
            At(0, 30, "You have slain orc pawn!"),
            At(0, 40, "You slash orc pawn for 10 points of damage."),
            At(0, 45, "You have slain orc pawn!")).Snapshot();

        Assert.Equal(3, s.EncounterCount);
        Assert.All(s.RecentEncounters, e => Assert.Equal("Killed", e.Outcome));
        Assert.Equal(3, s.Mobs.Single(m => m.Name == "Orc pawn").Encounters);
    }

    [Fact]
    public void AbandonedFightTimesOut()
    {
        var s = Replay(
            At(0, 0, "You slash a ghoul for 10 points of damage."),
            At(0, 2, "You slash a ghoul for 10 points of damage."),
            At(5, 0, "You have entered West Commonlands.")).Snapshot();   // 5 min later, no kill

        var enc = Assert.Single(s.RecentEncounters);
        Assert.Equal(("Ghoul", "Timeout"), (enc.Name, enc.Outcome));
        Assert.Equal(20, enc.DamageOut);
    }

    [Fact]
    public void EncounterDpsAndDamageInTracked()
    {
        var s = Replay(
            At(0, 0, "You slash orc centurion for 30 points of damage."),
            At(0, 5, "Orc centurion hits YOU for 7 points of damage."),
            At(0, 10, "You slash orc centurion for 30 points of damage."),
            At(0, 10, "You have slain orc centurion!")).Snapshot();

        var enc = Assert.Single(s.RecentEncounters);
        Assert.Equal(60, enc.DamageOut);
        Assert.Equal(7, enc.DamageIn);
        Assert.Equal(6, enc.Dps, 0);   // 60 over 10s
    }

    [Fact]
    public void RewardsCorrelateToTheKilledCreature()
    {
        var s = Replay(
            At(0, 0, "You slash a ghoul for 10 points of damage."),
            At(0, 5, "You have slain a ghoul!"),
            At(0, 6, "You gain party experience! (0.5%)"),
            At(0, 7, "You receive 2 gold from the corpse."),
            At(0, 8, "--You have looted a Research Page from a ghoul's corpse.--"),
            // Unrelated coin a minute later must NOT correlate (window is 3 s).
            At(1, 30, "You receive 9 platinum from the corpse.")).Snapshot();

        var mob = s.Mobs.Single(m => m.Name == "Ghoul");
        Assert.Equal(1, mob.Kills);
        Assert.Equal(0.5, mob.XpPercent, 2);
        Assert.Equal(200, mob.Copper);
        var loot = Assert.Single(mob.Loot);
        Assert.Equal(("Research Page", 1, 100.0), (loot.Item, loot.Count, loot.DropRatePct!.Value));
    }

    [Fact]
    public void DropRateUsesKillDenominator()
    {
        var s = Replay(
            At(0, 0, "You have slain a ghoul!"),
            At(0, 10, "You have slain a ghoul!"),
            At(0, 20, "You have slain a ghoul!"),
            At(0, 30, "You have slain a ghoul!"),
            At(0, 31, "--You have looted a Research Page from a ghoul's corpse.--")).Snapshot();

        var mob = s.Mobs.Single(m => m.Name == "Ghoul");
        Assert.Equal(4, mob.Kills);
        Assert.Equal(25.0, Assert.Single(mob.Loot).DropRatePct!.Value, 1);
    }

    [Fact]
    public void LootFromUnkilledCreatureHasNoRate()
    {
        // Group killed it; we only looted — rate denominator is 0 → no percentage claimed.
        var s = Replay(
            At(0, 0, "--You have looted a Fine Steel Long Sword from a ghoul knight's corpse.--")).Snapshot();
        var mob = s.Mobs.Single(m => m.Name == "Ghoul knight");
        Assert.Equal(0, mob.Kills);
        Assert.Null(Assert.Single(mob.Loot).DropRatePct);
    }

    [Fact]
    public void StanceWindowsAttributeDamageAndCombatTime()
    {
        var s = Replay(
            At(0, 0, "You assume an offensive stance."),
            At(0, 5, "You slash orc pawn for 40 points of damage."),
            At(0, 6, "You slash orc pawn for 40 points of damage."),
            At(1, 0, "You assume a defensive stance."),
            At(1, 5, "You slash orc pawn for 10 points of damage."),
            At(1, 6, "You slash orc pawn for 10 points of damage."),
            At(5, 0, "You have entered West Commonlands.")).Snapshot();

        Assert.Equal("Defensive", s.CurrentStance);
        var off = s.Stances.Single(x => x.Name == "Offensive");
        var def = s.Stances.Single(x => x.Name == "Defensive");
        Assert.Equal(80, off.Damage);
        Assert.Equal(20, def.Damage);
        Assert.True(off.CombatSeconds >= 1);
    }

    [Fact]
    public void KillWatchRuleCountsAndBreaksDown()
    {
        var stats = Replay(
            At(0, 0, "You have slain orc pawn!"),
            At(0, 10, "You have slain orc centurion!"),
            At(0, 20, "You have slain a ghoul!"));
        var rules = new[] { new TrackedRule { Name = "Orcs", Pattern = "orc", Kind = WatchKind.Kill } };
        var r = Assert.Single(stats.Snapshot(null, rules).Tracked);
        Assert.Equal(2, r.TotalQuantity);
        Assert.Equal(2, r.Items.Count);
    }

    [Fact]
    public void MilestoneWatchRuleMatchesWithEmptyPattern()
    {
        var stats = Replay(
            At(0, 0, "You have gained a level! Welcome to level 12!"),
            At(0, 10, "You have gained an ability point!  You now have 3 ability points."));
        var rules = new[] { new TrackedRule { Name = "Dings", Kind = WatchKind.Milestone } };
        Assert.Equal(2, Assert.Single(stats.Snapshot(null, rules).Tracked).TotalQuantity);
    }

    [Fact]
    public void DeathWatchRuleFiltersByKiller()
    {
        var stats = Replay(
            At(0, 0, "You have been slain by a greater mummy!"),
            At(0, 30, "You have been slain by orc taskmaster!"));
        var all = new[] { new TrackedRule { Name = "Deaths", Kind = WatchKind.Death } };
        var mummy = new[] { new TrackedRule { Name = "MummyDeaths", Pattern = "mummy", Kind = WatchKind.Death } };
        Assert.Equal(2, Assert.Single(stats.Snapshot(null, all).Tracked).TotalQuantity);
        Assert.Equal(1, Assert.Single(stats.Snapshot(null, mummy).Tracked).TotalQuantity);
    }
}
