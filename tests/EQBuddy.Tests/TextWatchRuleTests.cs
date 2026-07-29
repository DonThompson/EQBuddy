using EQBuddy.Core;

namespace EQBuddy.Tests;

/// <summary>
/// WatchKind.Text — alerting on raw log lines, the escape hatch from WATCH-001's
/// structured-events rule. Requested in discussion #22 for a cleric healing chain, where
/// the lines come from another player's raid-assist script and match no pattern EQBuddy
/// owns (or ever could).
///
/// The lines below are that shape: a raid channel message nobody parses, mixed in with
/// chat that must not match.
/// </summary>
public class TextWatchRuleTests
{
    private static string At(int mm, int ss, string msg) =>
        $"[Sat Jul 18 15:{mm:D2}:{ss:D2} 2026] {msg}";

    /// <summary>Feeds lines the way LogWatcher does — parse if we can, and offer every line
    /// raw either way.</summary>
    private static SessionStats Replay(IEnumerable<TrackedRule>? rules, params string[] lines)
    {
        var stats = new SessionStats { CharacterName = "Kaybek" };
        stats.RefreshTextPatterns(rules);
        foreach (var line in lines)
        {
            if (LogParser.Parse(line) is { } evt) stats.Apply(evt);
            stats.ObserveRawLine(line);
        }
        return stats;
    }

    private static TrackedRule TextRule(string pattern, string name = "CH chain") =>
        new() { Name = name, Pattern = pattern, Kind = WatchKind.Text };

    [Fact]
    public void MatchesAnUnparseableRaidLine()
    {
        var rules = new[] { TextRule("CH -->") };
        var s = Replay(rules,
            At(0, 0, "Cleric1 tells the raid, 'CH --> Tankname'"),
            At(0, 5, "Someone tells the guild, 'anyone need a port?'"),
            At(0, 9, "Cleric2 tells the raid, 'CH --> Tankname'")).Snapshot(null, rules);

        var tracked = Assert.Single(s.Tracked);
        Assert.Equal("CH chain", tracked.Name);
        Assert.Equal(2, tracked.TotalQuantity);
    }

    /// <summary>Rows are keyed by the whole line, so an announcement repeated verbatim
    /// collapses into one row with a count while a different one gets its own. Nothing is
    /// normalised away — guessing which part of someone else's raid text is "the same
    /// message" would be inventing structure we don't have.</summary>
    [Fact]
    public void IdenticalLinesGroupAndDifferentOnesDoNot()
    {
        var rules = new[] { TextRule("CH -->") };
        var s = Replay(rules,
            At(0, 0, "Cleric1 tells the raid, 'CH --> Tankname'"),
            At(0, 6, "Cleric1 tells the raid, 'CH --> Tankname'"),
            At(0, 12, "Cleric2 tells the raid, 'CH --> Tankname'")).Snapshot(null, rules);

        var tracked = Assert.Single(s.Tracked);
        Assert.Equal(3, tracked.TotalQuantity);
        Assert.Equal(2, tracked.Items.Count);
        Assert.Equal(2, tracked.Items[0].Count);        // Cleric1, twice
        Assert.Contains("Cleric1", tracked.Items[0].Name);
        Assert.Equal(1, tracked.Items[1].Count);        // Cleric2, once
    }

    /// <summary>The timestamp prefix is not part of the text — otherwise a pattern like
    /// "Jul" or a time fragment would match every line in the log.</summary>
    [Fact]
    public void TheTimestampPrefixIsNotMatched()
    {
        var rules = new[] { TextRule("Jul 18") };
        var s = Replay(rules, At(0, 0, "Cleric1 tells the raid, 'CH --> Tankname'")).Snapshot(null, rules);

        Assert.Equal(0, Assert.Single(s.Tracked).TotalQuantity);
    }

    /// <summary>A line EQBuddy already understands is still offered to text rules — the
    /// rule is about the words, not about what we made of them. This one is a real kill
    /// line, so it also has to stay counted as a kill.</summary>
    [Fact]
    public void ParsedLinesAreAlsoAvailableToTextRules()
    {
        var rules = new[] { TextRule("orc pawn", "pawn mentions") };
        var stats = Replay(rules, At(0, 0, "You have slain orc pawn!"));
        var s = stats.Snapshot(null, rules);

        Assert.Equal(1, Assert.Single(s.Tracked).TotalQuantity);
        Assert.Equal(1, s.YourKillCount);
    }

    [Fact]
    public void MatchingIsCaseInsensitive()
    {
        var rules = new[] { TextRule("ch -->") };
        var s = Replay(rules, At(0, 0, "Cleric1 tells the raid, 'CH --> Tankname'")).Snapshot(null, rules);

        Assert.Equal(1, Assert.Single(s.Tracked).TotalQuantity);
    }

    /// <summary>An empty text rule matches nothing. Every other kind treats an empty
    /// pattern as "match all of this kind", which for raw text would mean alerting on
    /// every line in the log.</summary>
    [Fact]
    public void AnEmptyTextRuleMatchesNothing()
    {
        var rules = new[] { new TrackedRule { Name = "", Pattern = "", Kind = WatchKind.Text } };
        var s = Replay(rules,
            At(0, 0, "Cleric1 tells the raid, 'CH --> Tankname'"),
            At(0, 1, "Someone tells the guild, 'hello'")).Snapshot(null, rules);

        Assert.Empty(s.Tracked);
    }

    /// <summary>A disabled rule doesn't keep lines, so re-enabling it doesn't retroactively
    /// produce a burst of matches (and a burst of alerts) from while it was off.</summary>
    [Fact]
    public void DisabledRulesKeepNothing()
    {
        var rules = new[] { TextRule("CH -->") };
        rules[0].Enabled = false;
        var s = Replay(rules, At(0, 0, "Cleric1 tells the raid, 'CH --> Tankname'")).Snapshot(null, rules);

        Assert.Empty(s.Tracked);
    }

    /// <summary>With no text rules configured, raw lines are dropped at ingest — a session
    /// full of chat mustn't grow the journal for people who never asked for this.</summary>
    [Fact]
    public void NoTextRulesMeansNothingIsRetained()
    {
        var stats = Replay(null,
            At(0, 0, "Cleric1 tells the raid, 'CH --> Tankname'"),
            At(0, 1, "Someone tells the guild, 'hello'"));

        Assert.Empty(stats.Snapshot(null, rules: null).Tracked);
        // The kill below is what a journal entry looks like when something IS tracked;
        // the chat above added none.
        Assert.Equal(0, stats.Snapshot(null, rules: null).YourKillCount);
    }

    /// <summary>Two rules can match the same line. It's kept once at ingest, and each rule
    /// still claims it independently at snapshot time.</summary>
    [Fact]
    public void OneLineCanSatisfyTwoRules()
    {
        var rules = new[] { TextRule("CH -->", "chain"), TextRule("Tankname", "my tank") };
        var s = Replay(rules, At(0, 0, "Cleric1 tells the raid, 'CH --> Tankname'")).Snapshot(null, rules);

        Assert.Equal(2, s.Tracked.Count);
        Assert.All(s.Tracked, t => Assert.Equal(1, t.TotalQuantity));
    }

    /// <summary>Text matches are not evidence you were playing: a raid macro fires while
    /// you're stood in the bank. Active-play time counts your own actions only.</summary>
    [Fact]
    public void MatchedTextDoesNotCountAsActivePlay()
    {
        var rules = new[] { TextRule("CH -->") };
        var s = Replay(rules,
            At(0, 0, "Cleric1 tells the raid, 'CH --> Tankname'"),
            At(4, 0, "Cleric1 tells the raid, 'CH --> Tankname'")).Snapshot(null, rules);

        Assert.Equal(0, s.ActiveSeconds);
    }

    /// <summary>Long raid announcements become row labels, so they're trimmed to something
    /// a 320px card can show.</summary>
    [Fact]
    public void LongLinesAreTrimmedForDisplay()
    {
        var rules = new[] { TextRule("CH -->") };
        var line = "Cleric1 tells the raid, 'CH --> Tankname " + new string('x', 200) + "'";
        var s = Replay(rules, At(0, 0, line)).Snapshot(null, rules);

        var item = Assert.Single(Assert.Single(s.Tracked).Items);
        Assert.True(item.Name.Length <= 64, $"label was {item.Name.Length} chars");
        Assert.EndsWith("…", item.Name);
    }
}
