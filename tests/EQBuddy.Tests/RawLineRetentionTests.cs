using EQBuddy.Core;

namespace EQBuddy.Tests;

/// <summary>
/// Perf audit #5: RawLineEvents kept for Text rules used to be retained
/// whole-session — a raid night of matched chatter grew the journal without bound.
/// They now age out with the 40-minute combat retention, and the Text-rule
/// ACCUMULATORS (perf audit #10) are the keeper of the counts: totals never shrink
/// mid-session, across prunes and across the state-triggered rescans that rebuild
/// every other rule kind. Only a rules edit recounts text honestly over the
/// retained window — the same trade the combat prune has always made.
/// </summary>
public class RawLineRetentionTests
{
    private static readonly DateTime T0 = new(2026, 8, 8, 20, 0, 0);

    private static string At(DateTime t, string msg) =>
        $"[{t.ToString("ddd MMM d HH:mm:ss yyyy", System.Globalization.CultureInfo.InvariantCulture)}] {msg}";

    private static void Feed(SessionStats stats, DateTime t, string msg)
    {
        var line = At(t, msg);
        if (LogParser.Parse(line) is { } evt) stats.Apply(evt);
        stats.ObserveRawLine(line);
    }

    private static List<TrackedRule> Rules() =>
    [
        new() { Name = "CH chain", Pattern = "CH -->", Kind = WatchKind.Text },
        new() { Name = "Gnolls", Pattern = "gnoll", Kind = WatchKind.Kill },
    ];

    /// <summary>Feed three matched calls, then enough noise 50 minutes later to trip
    /// the every-512-appends prune. The raw events age out (the from-scratch oracle
    /// proves the journal no longer holds them); the displayed total does not.</summary>
    [Fact]
    public void TextRuleTotalSurvivesTheRawLinePrune()
    {
        var stats = new SessionStats { CharacterName = "Kaybek" };
        var rules = Rules();
        stats.RefreshTextPatterns(rules);

        Feed(stats, T0, "Cleric1 tells the raid, 'CH --> Tankname'");
        Feed(stats, T0.AddSeconds(3), "Cleric2 tells the raid, 'CH --> Tankname'");
        Feed(stats, T0.AddSeconds(6), "Cleric1 tells the raid, 'CH --> Tankname'");
        Feed(stats, T0.AddSeconds(9), "You have slain a gnoll pup!");
        stats.Snapshot(TimeSpan.FromMinutes(10), rules);   // fold them into the accumulators

        var t = T0.AddMinutes(50);
        for (var i = 0; i < 520; i++)
            Feed(stats, t.AddSeconds(i), "You slash a gnoll elder for 5 points of damage.");

        var s = stats.Snapshot(TimeSpan.FromMinutes(10), rules);
        Assert.Equal(3, s.Tracked.Single(r => r.Name == "CH chain").TotalQuantity);
        Assert.Equal("Cleric1 tells the raid, 'CH --> Tankname'",
            s.Tracked.Single(r => r.Name == "CH chain").LastItem);
        Assert.Equal(1, s.Tracked.Single(r => r.Name == "Gnolls").TotalQuantity);

        // The oracle scans the pruned journal: the raw lines really are gone —
        // the accumulators are what carried the total across.
        var oracle = stats.TrackedScanFromScratch(rules);
        Assert.Equal(0, oracle.Single(r => r.Name == "CH chain").TotalQuantity);
        Assert.Equal(1, oracle.Single(r => r.Name == "Gnolls").TotalQuantity);
    }

    /// <summary>A state-triggered rescan (here: the spell catalog learning mid-fight,
    /// which rebuilds every other rule kind from the journal) must not forget text
    /// counts whose raw events were pruned — text matching depends on nothing the
    /// rescan triggers cover.</summary>
    [Fact]
    public void TextCountsSurviveAStateTriggeredRescan()
    {
        var stats = new SessionStats { CharacterName = "Kaybek" };
        var rules = Rules();
        stats.RefreshTextPatterns(rules);

        Feed(stats, T0, "Cleric1 tells the raid, 'CH --> Tankname'");
        Feed(stats, T0.AddSeconds(2), "Cleric2 tells the raid, 'CH --> Tankname'");
        Feed(stats, T0.AddSeconds(5), "You have slain a gnoll pup!");
        stats.Snapshot(TimeSpan.FromMinutes(10), rules);

        var t = T0.AddMinutes(50);
        for (var i = 0; i < 520; i++)
            Feed(stats, t.AddSeconds(i), "You slash a gnoll elder for 5 points of damage.");
        stats.Snapshot(TimeSpan.FromMinutes(10), rules);   // prune has run, text = 2 from accs

        // A DoT line teaches the spell catalog (SpellCatalog.Revision bumps), which
        // forces the non-text accumulators to rebuild from the pruned journal.
        Feed(stats, t.AddSeconds(521), "Gnoll elder has taken 10 damage from your Poison Bolt.");
        Feed(stats, t.AddSeconds(522), "You have slain a gnoll elder!");

        var s = stats.Snapshot(TimeSpan.FromMinutes(10), rules);
        Assert.Equal(2, s.Tracked.Single(r => r.Name == "CH chain").TotalQuantity);
        // Non-text kinds still agree with the from-scratch oracle after the rescan.
        var oracle = stats.TrackedScanFromScratch(rules);
        Assert.Equal(oracle.Single(r => r.Name == "Gnolls").TotalQuantity,
            s.Tracked.Single(r => r.Name == "Gnolls").TotalQuantity);
        Assert.Equal(2, s.Tracked.Single(r => r.Name == "Gnolls").TotalQuantity);
    }

    /// <summary>The one accepted divergence: EDITING a text rule mid-session
    /// recounts over the retained window only — pre-retention lines are gone from
    /// the journal, and pretending otherwise would require keeping them forever,
    /// which is exactly what this audit item removes.</summary>
    [Fact]
    public void EditingATextRuleRecountsOverTheRetainedWindowOnly()
    {
        var stats = new SessionStats { CharacterName = "Kaybek" };
        var rules = Rules();
        stats.RefreshTextPatterns(rules);

        Feed(stats, T0, "Cleric1 tells the raid, 'CH --> Tankname'");
        stats.Snapshot(TimeSpan.FromMinutes(10), rules);

        var t = T0.AddMinutes(50);
        for (var i = 0; i < 520; i++)
            Feed(stats, t.AddSeconds(i), "You slash a gnoll elder for 5 points of damage.");
        Assert.Equal(1, stats.Snapshot(TimeSpan.FromMinutes(10), rules)
            .Tracked.Single(r => r.Name == "CH chain").TotalQuantity);

        rules[0].Pattern = "CH ";   // fingerprint change: honest recount
        Assert.Equal(0, stats.Snapshot(TimeSpan.FromMinutes(10), rules)
            .Tracked.Single(r => r.Name == "CH chain").TotalQuantity);
    }

    /// <summary>Session rollover still resets text counts with everything else —
    /// the accumulators are session state, not forever state.</summary>
    [Fact]
    public void SessionRolloverStillResetsTextCounts()
    {
        var stats = new SessionStats { CharacterName = "Kaybek" };
        var rules = Rules();
        stats.RefreshTextPatterns(rules);

        Feed(stats, T0, "Cleric1 tells the raid, 'CH --> Tankname'");
        stats.Snapshot(TimeSpan.FromMinutes(10), rules);

        Feed(stats, T0.AddMinutes(61), "Cleric2 tells the raid, 'CH --> Tankname'");
        Assert.Equal(1, stats.Snapshot(TimeSpan.FromMinutes(10), rules)
            .Tracked.Single(r => r.Name == "CH chain").TotalQuantity);
    }
}
