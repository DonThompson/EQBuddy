using EQBuddy.Core;

namespace EQBuddy.Tests;

/// <summary>Recent-window rates, active-time, tracked rules, and markers (Release A core).</summary>
public class JournalTests
{
    private static string At(int hh, int mm, int ss, string msg) =>
        $"[Sat Jul 18 {hh:D2}:{mm:D2}:{ss:D2} 2026] {msg}";

    private static SessionStats Replay(params string[] lines)
    {
        var stats = new SessionStats { CharacterName = "Kaybek" };
        foreach (var line in lines)
        {
            var evt = LogParser.Parse(line);
            if (evt is not null) stats.Apply(evt);
        }
        return stats;
    }

    [Fact]
    public void RecentWindowCountsOnlyEventsInsideWindow()
    {
        var stats = Replay(
            At(15, 0, 0, "You gain party experience! (10%)"),          // outside 15m window
            At(15, 0, 1, "You have slain orc pawn!"),
            At(15, 50, 0, "You gain party experience! (5%)"),          // inside
            At(15, 55, 0, "You receive 5 gold from the corpse."),      // inside
            At(15, 59, 0, "You have slain orc centurion!"));           // inside (window end)

        var s = stats.Snapshot(TimeSpan.FromMinutes(15), rules: null);
        Assert.NotNull(s.Recent);
        Assert.True(s.Recent!.HasFullWindow);
        Assert.Equal(5, s.Recent.XpPercent, 1);
        Assert.Equal(20, s.Recent.XpPerHour, 1);   // 5% in 15 min
        Assert.Equal(1, s.Recent.Kills);
        Assert.Equal(500, s.Recent.Copper);
        // Session totals unaffected
        Assert.Equal(15, s.XpPercent, 1);
        Assert.Equal(2, s.YourKillCount);
    }

    [Fact]
    public void RecentDpsUsesCombatTimeInsideWindowOnly()
    {
        var stats = Replay(
            At(15, 0, 0, "You slash orc pawn for 100 points of damage."),   // old fight
            At(15, 0, 10, "You slash orc pawn for 100 points of damage."),
            At(15, 50, 0, "You slash orc pawn for 30 points of damage."),   // recent fight
            At(15, 50, 5, "You slash orc pawn for 30 points of damage."));

        var s = stats.Snapshot(TimeSpan.FromMinutes(15), rules: null);
        // Recent: 60 damage over the 5s recent fight (open window) = 12 dps
        Assert.Equal(12, s.Recent!.Dps, 0);
        // Session: 260 over 15s of combat
        Assert.Equal(260.0 / 15, s.SessionDps, 1);
    }

    [Fact]
    public void ShortSessionReportsPartialWindow()
    {
        var stats = Replay(At(15, 0, 0, "You have slain orc pawn!"));
        var s = stats.Snapshot(TimeSpan.FromMinutes(15), rules: null);
        Assert.False(s.Recent!.HasFullWindow);
    }

    [Fact]
    public void ActiveTimeCountsBucketsWithEvents()
    {
        // Events in 3 distinct 2-minute buckets across a 30-minute span → 6 min active.
        var stats = Replay(
            At(15, 0, 0, "You have slain orc pawn!"),
            At(15, 10, 0, "You have slain orc pawn!"),
            At(15, 30, 0, "You have slain orc pawn!"));
        var s = stats.Snapshot();
        Assert.Equal(3 * 120, s.ActiveSeconds, 0);
        // 3 kills in 0.1 active hours = 30 kills per active hour
        Assert.Equal(30, s.KillsPerActiveHour, 0);
    }

    [Fact]
    public void TrackedRuleMatchesSubstringCaseInsensitive()
    {
        var stats = Replay(
            At(15, 0, 0, "--You have looted a Crystallized Fire Mote from orc centurion's corpse.--"),
            At(15, 5, 0, "--You have looted a Faint Mote of Shadow from orc oracle's corpse.--"),
            At(15, 6, 0, "--You have looted a Crystallized Fire Mote from orc centurion's corpse.--"),
            At(15, 10, 0, "You looted 3 Spider Silk from a giant spider's corpse and sold it for 3 copper."));

        var rules = new[] { new TrackedRule { Name = "Motes", Pattern = "mote" } };
        var s = stats.Snapshot(null, rules);

        var r = Assert.Single(s.Tracked);
        Assert.Equal("Motes", r.Name);
        Assert.Equal(3, r.TotalQuantity);
        Assert.Equal(2, r.Items.Count);
        Assert.Equal(("Crystallized Fire Mote", 2), (r.Items[0].Name, r.Items[0].Count));
        Assert.NotNull(r.LastMatch);
    }

    [Fact]
    public void TrackedRuleCountsAutoSellQuantities()
    {
        var stats = Replay(
            At(15, 0, 0, "You looted 3 Spider Silk from a giant spider's corpse and sold it for 3 copper."));
        var rules = new[] { new TrackedRule { Pattern = "silk" } };
        var s = stats.Snapshot(null, rules);
        Assert.Equal(3, Assert.Single(s.Tracked).TotalQuantity);
    }

    [Fact]
    public void DisabledRulesAreSkipped()
    {
        var stats = Replay(
            At(15, 0, 0, "--You have looted a Snake Egg from an asp's corpse.--"));
        var rules = new[] { new TrackedRule { Pattern = "egg", Enabled = false } };
        Assert.Empty(stats.Snapshot(null, rules).Tracked);
    }

    [Fact]
    public void MarkersAppearInSnapshot()
    {
        var stats = Replay(At(15, 0, 0, "You have slain orc pawn!"));
        stats.AddMarker("Camp 2 — basement");
        var s = stats.Snapshot();
        Assert.Equal("Camp 2 — basement", Assert.Single(s.Markers).Text);
    }

    [Fact]
    public void AggregatesSurviveJournalRetentionAndOldCombatStaysOutOfRecent()
    {
        // 600 swings, then a 45-minute pause (same session), then one fresh hit.
        // Aggregates keep everything; the recent window sees only the fresh hit.
        var lines = new List<string>();
        for (var i = 0; i < 600; i++)
            lines.Add(At(14, i / 60, i % 60, "You slash orc pawn for 1 point of damage."));
        lines.Add(At(14, 55, 0, "You slash orc centurion for 5 points of damage."));
        var stats = Replay(lines.ToArray());

        var s = stats.Snapshot(TimeSpan.FromMinutes(15), rules: null);
        Assert.Equal(605, s.DamageDealt);
        Assert.Equal(5, s.Recent!.Dps, 0);
    }
}
