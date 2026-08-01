using EQBuddy.Core;

namespace EQBuddy.Tests;

/// <summary>
/// TrackedRule.Id — a rule's identity, distinct from its display name.
///
/// Written after a real setup: two rules both named "Asaka" (one immediate, one on a
/// respawn delay). Everything downstream — alert cooldowns, the in-flight cue cap,
/// countdowns, alert baselines, mapping a snapshot row back to its rule — was keyed by the
/// name string, so the two rules shared all of it and the second rule's alerts fired with
/// the first rule's sound and delay. Names are labels; ids are identity.
/// </summary>
public class RuleIdentityTests
{
    // ---- the id itself ----

    [Fact]
    public void EveryNewRuleGetsItsOwnId()
    {
        var a = new TrackedRule { Name = "Asaka" };
        var b = new TrackedRule { Name = "Asaka" };

        Assert.NotEqual("", a.Id);
        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public void TheIdSurvivesAJsonRoundTrip()
    {
        var rule = new TrackedRule { Name = "Asaka", Pattern = "Asaka" };
        var json = System.Text.Json.JsonSerializer.Serialize(rule);
        var restored = System.Text.Json.JsonSerializer.Deserialize<TrackedRule>(json)!;

        Assert.Equal(rule.Id, restored.Id);
        Assert.False(restored.IdWasGenerated);
    }

    /// <summary>A settings.json from before ids existed has no Id property. The rule still
    /// gets a usable id — and reports that it was generated, so the loader knows to save.</summary>
    [Fact]
    public void ARuleSavedBeforeIdsExistedGetsOneAndSaysSo()
    {
        var restored = System.Text.Json.JsonSerializer.Deserialize<TrackedRule>(
            """{"Name":"Asaka","Pattern":"Asaka"}""")!;

        Assert.NotEqual("", restored.Id);
        Assert.True(restored.IdWasGenerated);
    }

    /// <summary>A hand-edited settings.json must not be able to hand several rules the same
    /// empty id — that would recreate the collision ids exist to end.</summary>
    [Fact]
    public void AnEmptyStoredIdIsIgnored()
    {
        var restored = System.Text.Json.JsonSerializer.Deserialize<TrackedRule>(
            """{"Id":"","Name":"Asaka"}""")!;

        Assert.NotEqual("", restored.Id);
        Assert.True(restored.IdWasGenerated);
    }

    /// <summary>Loading settings persists freshly generated ids immediately. Without this,
    /// a pre-id rule would re-roll its id every launch until some unrelated edit happened
    /// to save settings — stable-until-restart is not stable.</summary>
    [Fact]
    public void LoadingLegacyRulesMarksThemAsNeedingASave()
    {
        var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
            """{"TrackedRules":[{"Name":"Asaka","Pattern":"Asaka"}],"DefaultRulesVersion":1}""")!;

        Assert.Contains(settings.TrackedRules, r => r.IdWasGenerated);
    }

    // ---- identity flowing into snapshots ----

    private static string At(int mm, int ss, string msg) =>
        $"[Sat Jul 18 15:{mm:D2}:{ss:D2} 2026] {msg}";

    /// <summary>Two same-named rules produce two rows, each carrying its own rule's id, so
    /// a UI can tell which row belongs to which rule without guessing by name.</summary>
    [Fact]
    public void SameNamedRulesProduceSeparatelyIdentifiableRows()
    {
        var immediate = new TrackedRule { Name = "Asaka", Pattern = "Asaka", Kind = WatchKind.Kill };
        var respawn = new TrackedRule
        {
            Name = "Asaka", Pattern = "You have slain Asaka", Kind = WatchKind.Text,
            AlertDelaySeconds = 480,
        };
        var rules = new[] { immediate, respawn };

        var stats = new SessionStats { CharacterName = "Kaybek" };
        stats.RefreshTextPatterns(rules);
        foreach (var line in new[]
                 {
                     At(0, 0, "You have slain Asaka!"),
                 })
        {
            if (LogParser.Parse(line) is { } evt) stats.Apply(evt);
            stats.ObserveRawLine(line);
        }
        var s = stats.Snapshot(null, rules);

        Assert.Equal(2, s.Tracked.Count);
        Assert.Contains(s.Tracked, t => t.Id == immediate.Id);
        Assert.Contains(s.Tracked, t => t.Id == respawn.Id);
        Assert.All(s.Tracked, t => Assert.Equal("Asaka", t.Name));
        Assert.All(s.Tracked, t => Assert.Equal(1, t.TotalQuantity));
    }

    /// <summary>Old history snapshots serialized before ids existed still deserialize —
    /// their rows just have an empty id, which display-only consumers never look at.</summary>
    [Fact]
    public void AHistoryRowWithoutAnIdStillDeserializes()
    {
        var restored = System.Text.Json.JsonSerializer.Deserialize<TrackedRuleResult>(
            """{"Name":"Asaka","TotalQuantity":3,"Items":[],"PerHour":1.0,"PerActiveHour":1.5,"FirstMatch":null,"LastMatch":null,"LastItem":null}""")!;

        Assert.Equal("", restored.Id);
        Assert.Equal(3, restored.TotalQuantity);
    }
}
