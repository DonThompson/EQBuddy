using EQBuddy.Core;

namespace EQBuddy.Tests;

/// <summary>
/// Per-ability hit ranges and miss tallies (Companion-parity, Phase 1): the breakdown
/// rows' "hits 88–412" and "14% miss" come from the same aggregation as the totals,
/// fed through real log lines.
/// </summary>
public class BreakdownRangeTests
{
    private static SessionStats Replay(params string[] lines)
    {
        var stats = new SessionStats();
        var i = 0;
        foreach (var msg in lines)
            stats.Apply(LogParser.Parse($"[Wed Aug 12 20:00:{i++:D2} 2026] {msg}")!);
        return stats;
    }

    [Fact]
    public void HitRangeAndMissesRideTheSessionRows()
    {
        var stats = Replay(
            "You slash an orc pawn for 88 points of damage.",
            "You slash an orc pawn for 412 points of damage.",
            "You try to slash an orc pawn, but an orc pawn dodges!",
            "You try to slash an orc pawn, but miss!",
            "You kick an orc pawn for 50 points of damage.");

        var slash = stats.Snapshot().DamageBySource.Single(d => d.Name == "Slash");
        Assert.Equal(88, slash.MinHit);
        Assert.Equal(412, slash.MaxHit);
        Assert.Equal(2, slash.Misses);
        Assert.Equal(2, slash.Hits);              // misses are attempts, not hits
        var kick = stats.Snapshot().DamageBySource.Single(d => d.Name == "Kick");
        Assert.Equal(0, kick.Misses);
        Assert.Equal((50, 50), (kick.MinHit, kick.MaxHit));
    }

    [Fact]
    public void FightRowsCarryTheirOwnMissesButAMissAloneOpensNoFight()
    {
        var stats = Replay(
            "You try to slash an orc pawn, but miss!");        // nothing else — no fight
        Assert.Null(stats.Snapshot().LastFight);

        stats = Replay(
            "You slash an orc pawn for 100 points of damage.",
            "You try to slash an orc pawn, but an orc pawn ripostes!",
            "You have slain an orc pawn!");
        var fight = stats.Snapshot().LastFight!;
        var slash = fight.ByAbility.Single(d => d.Name == "Slash");
        Assert.Equal(1, slash.Misses);
        Assert.Equal(100, slash.MaxHit);
    }

    [Fact]
    public void MissesFollowTheSubstitutedSkillLikeHitsDo()
    {
        // "You will now use Round Kick instead of Kick" — hits AND misses file under
        // the substituted name, or the row splits in two.
        var stats = Replay(
            "You will now use Round Kick instead of Kick while attacking.",
            "You kick an orc pawn for 60 points of damage.",
            "You try to kick an orc pawn, but miss!");

        var row = stats.Snapshot().DamageBySource.Single(d => d.Name == "Round Kick");
        Assert.Equal(1, row.Misses);
        Assert.Equal(1, row.Hits);
    }
}
