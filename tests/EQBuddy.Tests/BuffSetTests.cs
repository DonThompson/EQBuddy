using System.Text.Json;
using EQBuddy.Core;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>
/// Buff sets (#120): the evaluator's honesty states — active vs expiring vs missing
/// vs "not seen this session" (a landing never observed is a DIFFERENT claim from a
/// fade observed), rank folding, the warn-threshold boundary, the empty set, and the
/// per-character persistence shape.
/// </summary>
public class BuffSetTests
{
    private static readonly DateTime T0 = DateTime.Parse("2026-08-13T21:00:00");
    private static readonly HashSet<string> None = [];

    private static GameEvent Ev(int seconds, string message) =>
        LogParser.Parse($"[{T0.AddSeconds(seconds):ddd MMM d HH:mm:ss yyyy}] {message}")!;

    private static BuffState Up(string label, double remainingAt0, params string[] candidates) =>
        new(label, candidates.Length > 0 ? candidates : [label], "You",
            T0.AddSeconds(-10), T0.AddSeconds(remainingAt0), Estimated: false);

    private static List<BuffSetEntryState> Eval(
        IEnumerable<string> set, IReadOnlyList<BuffState> active,
        HashSet<string>? landings = null, HashSet<string>? fades = null, double warn = 60) =>
        BuffSetEvaluator.Evaluate(set, active, landings ?? None, fades ?? None, T0, warn);

    [Fact]
    public void EmptySetMakesNoClaimsAtAll()
    {
        Assert.Empty(Eval([], [Up("Temperance", 3000)]));
    }

    [Fact]
    public void ActiveOutsideTheWarnWindowIsActive()
    {
        var s = Assert.Single(Eval(["Temperance"], [Up("Temperance", 3000)]));
        Assert.Equal(BuffSetStatus.Active, s.Status);
        Assert.Equal(3000, s.RemainingSeconds!.Value, 0);
    }

    [Fact]
    public void WarnThresholdBoundary_AtIsExpiring_JustAboveIsActive()
    {
        // The card's rule is remaining <= warn, so exactly-at counts as expiring.
        Assert.Equal(BuffSetStatus.Expiring,
            Assert.Single(Eval(["Temperance"], [Up("Temperance", 60)], warn: 60)).Status);
        Assert.Equal(BuffSetStatus.Active,
            Assert.Single(Eval(["Temperance"], [Up("Temperance", 61)], warn: 60)).Status);
    }

    [Fact]
    public void AnExpiredCountdownIsMissingEvenWhileTheChipLingers()
    {
        // The tracker holds an expired chip at 0:00 (estimates are floors); the set
        // line answers "should I rebuff?" and past the timer the answer is yes.
        var s = Assert.Single(Eval(["Temperance"], [Up("Temperance", -30)]));
        Assert.Equal(BuffSetStatus.Missing, s.Status);
    }

    [Fact]
    public void SeenFadingWithoutBeingActiveIsMissing()
    {
        var s = Assert.Single(Eval(["Temperance"], [], fades: ["Temperance"]));
        Assert.Equal(BuffSetStatus.Missing, s.Status);
    }

    [Fact]
    public void SeenLandingButNoLongerActiveIsMissing()
    {
        // Landed, then the linger cap removed it without a fade line: it was up, its
        // timer ran out long ago — a claim, not an unknown.
        var s = Assert.Single(Eval(["Temperance"], [], landings: ["Temperance"]));
        Assert.Equal(BuffSetStatus.Missing, s.Status);
    }

    [Fact]
    public void NeverSeenThisSessionIsNotSeen_NotMissing()
    {
        // No landing line observed: the buff may be up from before the log was
        // watched. The evaluator must not claim it's missing.
        var s = Assert.Single(Eval(["Aegolism"], []));
        Assert.Equal(BuffSetStatus.NotSeen, s.Status);
    }

    [Fact]
    public void RankSuffixesFoldBothWays()
    {
        // Set says "Temperance", the landing resolved to "Temperance II" — same spell.
        Assert.Equal(BuffSetStatus.Active,
            Assert.Single(Eval(["Temperance"], [Up("Temperance II", 3000)])).Status);
        // And the reverse: a rank in the set matches the folded landing.
        Assert.Equal(BuffSetStatus.Active,
            Assert.Single(Eval(["Temperance II"], [Up("Temperance", 3000)])).Status);
    }

    [Fact]
    public void AnUnresolvedLandingMatchesThroughItsCandidates()
    {
        // "You feel different." kept every candidate — the set entry counts as up if
        // it's among them, the same identity the tracker itself uses.
        var s = Assert.Single(Eval(["Aegolism"],
            [Up("You feel different.", 3000, "Temperance", "Aegolism")]));
        Assert.Equal(BuffSetStatus.Active, s.Status);
    }

    [Fact]
    public void DuplicateEntriesFoldToOneClaim()
    {
        Assert.Single(Eval(["Temperance", "Temperance V"], [Up("Temperance", 3000)]));
    }

    [Fact]
    public void TrackerSightsDistinguishMissingFromNotSeen()
    {
        // Armor of Faith: cast, landed, faded. Temperance: never mentioned all session.
        var fadeLine = FadeMessageCatalog.Default.FindBySpell("Armor of Faith");
        Assert.NotNull(fadeLine);
        var t = new BuffTracker();
        t.Apply(Ev(0, "You begin casting Armor of Faith."));
        t.Apply(Ev(3, "You feel the favor of the gods upon you."));
        t.Apply(Ev(600, fadeLine!.Message));

        var now = T0.AddSeconds(601);
        var sights = t.SetSights();
        var states = BuffSetEvaluator.Evaluate(["Armor of Faith", "Temperance"],
            t.Snapshot(now), sights.Landings, sights.Fades, now, 60);

        Assert.Equal(BuffSetStatus.Missing, states.Single(s => s.Spell == "Armor of Faith").Status);
        Assert.Equal(BuffSetStatus.NotSeen, states.Single(s => s.Spell == "Temperance").Status);
    }

    [Fact]
    public void AFadeSeenWithoutItsLandingStillCountsAsMissing()
    {
        // Log opened mid-session: no landing line, but the wear-off proves it WAS up.
        var fadeLine = FadeMessageCatalog.Default.FindBySpell("Armor of Faith")!;
        var t = new BuffTracker();
        t.Apply(Ev(0, fadeLine.Message));

        var sights = t.SetSights();
        var s = Assert.Single(BuffSetEvaluator.Evaluate(["Armor of Faith"],
            t.Snapshot(T0.AddSeconds(1)), sights.Landings, sights.Fades, T0.AddSeconds(1), 60));
        Assert.Equal(BuffSetStatus.Missing, s.Status);
    }

    [Fact]
    public void OwnBuffCastsAreRememberedForTheEditor()
    {
        var t = new BuffTracker();
        t.Apply(Ev(0, "You begin casting Armor of Faith."));
        Assert.Contains("Armor of Faith", t.SetSights().OwnCasts);
    }

    [Fact]
    public void BuffSetsPersistPerCharacter()
    {
        // Same serializer shape AppSettings uses (NaN window positions are legal).
        var opts = new JsonSerializerOptions
        {
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };
        var settings = new AppSettings();
        settings.BuffSets["dranak_legends"] = ["Temperance", "Aegolism"];
        settings.BuffSets["pipsqueak_legends"] = ["Spirit of Wolf"];

        var reloaded = JsonSerializer.Deserialize<AppSettings>(
            JsonSerializer.Serialize(settings, opts), opts)!;

        Assert.Equal(["Temperance", "Aegolism"], reloaded.BuffSets["dranak_legends"]);
        Assert.Equal(["Spirit of Wolf"], reloaded.BuffSets["pipsqueak_legends"]);
        Assert.Equal(2, reloaded.BuffSets.Count);
    }

    [Fact]
    public void TheCatalogExposesItsSearchableSpellNames()
    {
        // The editor's search space: every attributable buff spell, Armor of Faith
        // among them (the rest of the suite already leans on it).
        Assert.Contains(BuffDurationCatalog.Default.SpellNames,
            s => s.Equals("Armor of Faith", StringComparison.OrdinalIgnoreCase));
        Assert.True(BuffDurationCatalog.Default.SpellNames.Count > 200);
    }
}
