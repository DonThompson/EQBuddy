using EQBuddy.Core;

namespace EQBuddy.Tests;

/// <summary>
/// Perf audit #10/#11: the tracked-rule scan is incremental (per-rule accumulators
/// folding forward over appended journal entries) and the recent-window rate scan
/// walks the journal tail backward. Both are pure optimizations — every test here
/// asserts the OUTPUT is identical to what a from-scratch pass produces, across the
/// events that mutate the journal underneath them: appends, the retention prune,
/// rule edits, session rollovers, and review-mode resets.
/// </summary>
public class TrackedIncrementalTests
{
    private static string At(DateTime t, string msg) =>
        $"[{t.ToString("ddd MMM d HH:mm:ss yyyy", System.Globalization.CultureInfo.InvariantCulture)}] {msg}";

    private static readonly DateTime T0 = new(2026, 8, 8, 20, 0, 0);

    private static void Feed(SessionStats stats, DateTime t, string msg)
    {
        var line = At(t, msg);
        if (LogParser.Parse(line) is { } evt) stats.Apply(evt);
        stats.ObserveRawLine(line);
    }

    private static List<TrackedRule> Rules() =>
    [
        new() { Name = "Bone Chips", Pattern = "bone chips", Kind = WatchKind.Loot },
        new() { Name = "Gnolls", Pattern = "gnoll", Kind = WatchKind.Kill },
        new() { Name = "CH chain", Pattern = "CH -->", Kind = WatchKind.Text },
        new() { Name = "Channeling", Pattern = "Channeling", Kind = WatchKind.SkillUp },
        new() { Name = "Milestones", Pattern = "", Kind = WatchKind.Milestone },
    ];

    /// <summary>Field-by-field dump, because TrackedRuleResult holds a List whose
    /// record equality is referential.</summary>
    private static string Dump(IEnumerable<TrackedRuleResult> results) => string.Join("\n",
        results.Select(r => $"{r.Id}|{r.Name}|{r.TotalQuantity}|{r.PerHour:R}|{r.PerActiveHour:R}" +
            $"|{r.FirstMatch:O}|{r.LastMatch:O}|{r.LastItem}" +
            $"|{string.Join(",", r.Items.Select(i => $"{i.Name}:{i.Count}"))}"));

    private static void AssertMatchesFromScratch(SessionStats stats, IReadOnlyList<TrackedRule> rules)
    {
        var incremental = stats.Snapshot(TimeSpan.FromMinutes(10), rules).Tracked;
        var oracle = stats.TrackedScanFromScratch(rules);
        Assert.Equal(Dump(oracle), Dump(incremental));
    }

    [Fact]
    public void IncrementalScanEqualsFromScratchAcrossInterleavedAppends()
    {
        var stats = new SessionStats { CharacterName = "Kaybek" };
        var rules = Rules();
        stats.RefreshTextPatterns(rules);

        var t = T0;
        string[] script =
        [
            "You have slain a gnoll pup!",
            "--You have looted a Bone Chips from a decaying skeleton's corpse.--",
            "Cleric1 tells the raid, 'CH --> Tankname'",
            "You have become better at Channeling! (34)",
            "You have gained an ability point!  You now have 3 ability points.",
            "You slash a gnoll pup for 12 points of damage.",
            "You have slain a gnoll guardsman!",
            "--You have looted a Bone Chips from a decaying skeleton's corpse.--",
            "You have gained a level! Welcome to level 12!",
            "Cleric1 tells the raid, 'CH --> Tankname'",
        ];
        foreach (var msg in script)
        {
            t = t.AddSeconds(7);
            Feed(stats, t, msg);
            // Snapshot BETWEEN appends too — the accumulators must stay exact no
            // matter where the ticks land relative to the events.
            AssertMatchesFromScratch(stats, rules);
        }

        var s = stats.Snapshot(TimeSpan.FromMinutes(10), rules);
        Assert.Equal(2, s.Tracked.Single(r => r.Name == "Bone Chips").TotalQuantity);
        Assert.Equal(2, s.Tracked.Single(r => r.Name == "Gnolls").TotalQuantity);
        Assert.Equal(2, s.Tracked.Single(r => r.Name == "CH chain").TotalQuantity);
        Assert.Equal(2, s.Tracked.Single(r => r.Name == "Milestones").TotalQuantity);
    }

    [Fact]
    public void IncrementalScanSurvivesTheJournalRetentionPrune()
    {
        var stats = new SessionStats { CharacterName = "Kaybek" };
        var rules = Rules();
        stats.RefreshTextPatterns(rules);

        // Old block: rule-matched events plus a pile of prunable combat noise.
        var t = T0;
        Feed(stats, t, "--You have looted a Bone Chips from a decaying skeleton's corpse.--");
        Feed(stats, t.AddSeconds(1), "You have slain a gnoll pup!");
        for (var i = 0; i < 200; i++)
            Feed(stats, t.AddSeconds(2), "You slash a gnoll pup for 5 points of damage.");
        stats.Snapshot(TimeSpan.FromMinutes(10), rules);   // scan index now past the noise

        // 50 minutes later (inside the same session, past the 40-min combat
        // retention): enough appends to trip the every-512 prune, which removes the
        // old combat noise IN FRONT of the scan index.
        t = t.AddMinutes(50);
        for (var i = 0; i < 520; i++)
        {
            Feed(stats, t.AddSeconds(i), "You slash a gnoll elder for 5 points of damage.");
            if (i % 97 == 0) Feed(stats, t.AddSeconds(i), "--You have looted a Bone Chips from a decaying skeleton's corpse.--");
        }
        AssertMatchesFromScratch(stats, rules);

        // The pre-prune loot and kill still count: pruned kinds are exactly the
        // ones no rule can match, so totals never rewind.
        var s = stats.Snapshot(TimeSpan.FromMinutes(10), rules);
        Assert.Equal(7, s.Tracked.Single(r => r.Name == "Bone Chips").TotalQuantity);
        Assert.Equal(1, s.Tracked.Single(r => r.Name == "Gnolls").TotalQuantity);
    }

    [Fact]
    public void RuleEditsRescanTheWholeJournal()
    {
        var stats = new SessionStats { CharacterName = "Kaybek" };
        var rules = Rules();
        stats.RefreshTextPatterns(rules);

        Feed(stats, T0, "--You have looted a Bone Chips from a decaying skeleton's corpse.--");
        Feed(stats, T0.AddSeconds(5), "--You have looted a Rusty Sword from an orc pawn's corpse.--");
        AssertMatchesFromScratch(stats, rules);

        // Pattern edit: the accumulators are stale for the new fingerprint and must
        // rebuild — the Rusty Sword looted BEFORE the edit now counts.
        rules[0].Pattern = "rusty";
        AssertMatchesFromScratch(stats, rules);
        Assert.Equal("Rusty Sword",
            stats.Snapshot(null, rules).Tracked.Single(r => r.Name == "Bone Chips").LastItem);

        // Disabling a rule changes the enabled subsequence the accumulators mirror.
        rules[1].Enabled = false;
        AssertMatchesFromScratch(stats, rules);
        Assert.DoesNotContain(stats.Snapshot(null, rules).Tracked, r => r.Name == "Gnolls");
    }

    [Fact]
    public void SessionRolloverStartsTheTrackedTotalsOver()
    {
        var stats = new SessionStats { CharacterName = "Kaybek" };
        var rules = Rules();
        stats.RefreshTextPatterns(rules);

        Feed(stats, T0, "--You have looted a Bone Chips from a decaying skeleton's corpse.--");
        Feed(stats, T0.AddSeconds(3), "You have slain a gnoll pup!");
        stats.Snapshot(TimeSpan.FromMinutes(10), rules);

        // 61-minute gap: the session rolls, the journal clears, and the incremental
        // state must start over rather than replay stale totals.
        Feed(stats, T0.AddMinutes(61), "--You have looted a Bone Chips from a decaying skeleton's corpse.--");
        AssertMatchesFromScratch(stats, rules);
        var s = stats.Snapshot(TimeSpan.FromMinutes(10), rules);
        Assert.Equal(1, s.Tracked.Single(r => r.Name == "Bone Chips").TotalQuantity);
        Assert.Equal(0, s.Tracked.Single(r => r.Name == "Gnolls").TotalQuantity);
    }

    [Fact]
    public void ReviewModeResetStartsTheTrackedTotalsOver()
    {
        var stats = new SessionStats { CharacterName = "Kaybek" };
        var rules = Rules();
        stats.RefreshTextPatterns(rules);

        Feed(stats, T0, "--You have looted a Bone Chips from a decaying skeleton's corpse.--");
        stats.Snapshot(TimeSpan.FromMinutes(10), rules);

        // Review-mode log switches funnel through Reset() (LogWatcher.Select).
        stats.Reset();
        AssertMatchesFromScratch(stats, rules);
        Assert.Equal(0, stats.Snapshot(null, rules)
            .Tracked.Single(r => r.Name == "Bone Chips").TotalQuantity);

        Feed(stats, T0.AddMinutes(90), "--You have looted a Bone Chips from a decaying skeleton's corpse.--");
        AssertMatchesFromScratch(stats, rules);
        Assert.Equal(1, stats.Snapshot(null, rules)
            .Tracked.Single(r => r.Name == "Bone Chips").TotalQuantity);
    }

    /// <summary>Perf audit #11: the backward tail walk must keep the exact window
    /// semantics of the old forward filter — an event exactly AT the window start
    /// counts, one a second older does not.</summary>
    [Fact]
    public void RecentWindowRatesAreExactAtTheWindowBoundary()
    {
        var stats = new SessionStats { CharacterName = "Kaybek" };
        var window = TimeSpan.FromMinutes(10);

        Feed(stats, T0, "You gain experience! (0.5%)");                 // session anchor
        Feed(stats, T0.AddSeconds(59), "You gain party experience! (0.2%)");  // 1 s OUTSIDE the window
        Feed(stats, T0.AddMinutes(1), "You have slain a gnoll pup!");   // exactly AT winStart
        Feed(stats, T0.AddMinutes(2), "You receive 10 platinum from the corpse.");
        Feed(stats, T0.AddMinutes(5), "You gain experience! (0.7%)");
        Feed(stats, T0.AddMinutes(11), "You have slain a gnoll elder!"); // winEnd anchor

        var recent = stats.Snapshot(window, rules: null).Recent;
        Assert.NotNull(recent);
        // winEnd = last event (T0+11m), winStart = T0+1m: both kills inside (the
        // boundary one included), the coin inside, and NO xp — both xp events fall
        // before winStart.
        Assert.Equal(2, recent!.Kills);
        Assert.Equal(10_000, recent.Copper);
        Assert.Equal(0.7, recent.XpPercent, 6);
    }

    /// <summary>The combat-span walk stops at spans ending before the window; a span
    /// straddling the boundary still contributes exactly its overlap.</summary>
    [Fact]
    public void RecentWindowDpsCountsOnlyTheOverlappingCombatSeconds()
    {
        var stats = new SessionStats { CharacterName = "Kaybek" };

        // Fight 1: T0 .. T0+30s (closed by the 10 s combat gap).
        Feed(stats, T0, "You slash a gnoll pup for 100 points of damage.");
        Feed(stats, T0.AddSeconds(30), "You slash a gnoll pup for 100 points of damage.");
        // Fight 2 opens 20 minutes later.
        Feed(stats, T0.AddMinutes(20), "You slash a gnoll elder for 300 points of damage.");
        Feed(stats, T0.AddMinutes(20).AddSeconds(10), "You slash a gnoll elder for 300 points of damage.");

        // Window = last 10 minutes: only fight 2's damage and seconds count.
        var recent = stats.Snapshot(TimeSpan.FromMinutes(10), rules: null).Recent;
        Assert.NotNull(recent);
        Assert.Equal(600.0 / 10.0, recent!.Dps, 3);
    }
}
