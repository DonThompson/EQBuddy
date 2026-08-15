using System.Text.Json;
using EQBuddy.Core;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>
/// Buff sets stage 3 (#120, Frankthetankk — the final committed stage): new-buff-unlock
/// suggestions (a rank-up folds into the same slot and NEVER suggests — that is the
/// requester's rank-up answer, and it's structural) and the lost-buff history with
/// cause (expired vs faded vs a tightly-correlated overwrite vs death), capped,
/// per-character, reset with the session.
/// </summary>
public class BuffSetStage3Tests
{
    private static readonly DateTime T0 = DateTime.Parse("2026-08-14T21:00:00");

    // A hand-built catalog keeps the suggestion tests independent of the harvest.
    private static readonly BuffDurationCatalog Cat = new(
    [
        new BuffDurationCatalog.Entry
        {
            Message = "You feel temperate.", Label = "Temperance",
            Spells = [new BuffDurationCatalog.BuffSpell { Name = "Temperance", DurationSeconds = 3600 }],
        },
        new BuffDurationCatalog.Entry
        {
            Message = "You feel holy.", Label = "Aegolism",
            Spells = [new BuffDurationCatalog.BuffSpell { Name = "Aegolism", DurationSeconds = 3600 }],
        },
    ]);

    private static SpellUnlock Unlock(string spell, params string[] classes) =>
        new(spell, classes.Length > 0 ? classes : ["Cleric"]);

    // ---- Part A: new-buff-unlock suggestions ----

    [Fact]
    public void ANewRankOfASetSpellIsCoveredAndNeverSuggested()
    {
        // The requester's hard design point, answered structurally: sets store base
        // names and everything compares rank-folded, so "Temperance III" folds into
        // the "Temperance" slot — covered, no suggestion needed.
        Assert.Empty(BuffSuggestions.Compute(
            [Unlock("Temperance III")], ["Temperance"], [], Cat));
    }

    [Fact]
    public void AGenuinelyNewBuffLineSuggests_TargetingTheGainingClass()
    {
        var s = Assert.Single(BuffSuggestions.Compute(
            [Unlock("Aegolism", "Cleric", "Paladin")], ["Temperance"], [], Cat));
        Assert.Equal("Aegolism", s.Spell);
        Assert.Equal("Cleric", s.Class);
    }

    [Fact]
    public void TwoRanksInOneDingSuggestOnce()
    {
        Assert.Single(BuffSuggestions.Compute(
            [Unlock("Aegolism"), Unlock("Aegolism II")], [], [], Cat));
    }

    [Fact]
    public void NonBuffUnlocksNeverSuggest()
    {
        // Not in the duration catalog = the tracker could never attribute it — the
        // same restriction both set editors enforce.
        Assert.Empty(BuffSuggestions.Compute([Unlock("Complete Heal")], [], [], Cat));
    }

    [Fact]
    public void CoveredThroughAnyBucketOfTheAssembledSetExcludes()
    {
        // Stored under Warrior, suggested for Cleric: the assembled set (both classes
        // active) already covers the line — no suggestion, whatever bucket holds it.
        var byClass = new Dictionary<string, Dictionary<string, List<string>>>();
        BuffSetStore.Add(byClass, "dranak_legends", "Warrior", "Aegolism");
        var assembled = BuffSetStore.Assemble(byClass["dranak_legends"], ["Warrior", "Cleric"]);

        Assert.Empty(BuffSuggestions.Compute([Unlock("Aegolism", "Cleric")], assembled, [], Cat));
    }

    [Fact]
    public void DismissalSuppressesAcrossRanks_AndPersistsInSettings()
    {
        var settings = new AppSettings();
        Assert.True(BuffSuggestions.Dismiss(
            settings.BuffSuggestionDismissed, "dranak_legends", "Aegolism II"));
        Assert.False(BuffSuggestions.Dismiss(   // idempotent — nothing new to save
            settings.BuffSuggestionDismissed, "dranak_legends", "Aegolism III"));

        var opts = new JsonSerializerOptions
        {
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };
        var reloaded = JsonSerializer.Deserialize<AppSettings>(
            JsonSerializer.Serialize(settings, opts), opts)!;
        var dismissed = BuffSuggestions.DismissedFor(
            reloaded.BuffSuggestionDismissed, "Dranak_Legends");   // key matched case-insensitively

        Assert.Equal(["Aegolism"], dismissed);   // stored rank-folded
        Assert.Empty(BuffSuggestions.Compute([Unlock("Aegolism III")], [], dismissed, Cat));
    }

    [Fact]
    public void DismissalsArePerCharacter()
    {
        var dismissed = new Dictionary<string, List<string>>();
        BuffSuggestions.Dismiss(dismissed, "dranak_legends", "Aegolism");
        Assert.Empty(BuffSuggestions.DismissedFor(dismissed, "pipsqueak_legends"));
        Assert.Single(BuffSuggestions.Compute([Unlock("Aegolism")], [],
            BuffSuggestions.DismissedFor(dismissed, "pipsqueak_legends"), Cat));
    }

    // ---- Part B: the lost-buff history with cause ----

    private static readonly SlowDebuffCatalog Slows = new(
    [
        new SlowDebuffCatalog.Entry
        {
            Message = "You feel lethargic.", Label = "Turgur's Insects",
            Spells = [new SlowDebuffCatalog.SlowSpell { Name = "Turgur's Insects", PctMin = 50, PctMax = 50 }],
        },
    ]);

    private static BuffSetEntryState Active(string spell, bool estimated = false) =>
        new(spell, BuffSetStatus.Active, 600, estimated);

    private static BuffSetEntryState Missing(string spell, bool estimated = false) =>
        new(spell, BuffSetStatus.Missing, null, estimated);

    [Fact]
    public void ExpiryWithoutAFadeReadsExpired_EstWhenEstimated()
    {
        var log = new BuffLossLog(Slows);
        log.Observe([Active("Temperance"), Active("Aegolism", estimated: true)], T0);
        log.Observe([Missing("Temperance"), Missing("Aegolism", estimated: true)], T0.AddSeconds(1));

        var entries = log.Snapshot();
        Assert.Equal(2, entries.Count);
        Assert.Equal("expired", entries.Single(e => e.Spell == "Temperance").Cause);
        Assert.Equal("expired (est)", entries.Single(e => e.Spell == "Aegolism").Cause);
        Assert.Equal(T0.AddSeconds(1), entries[0].Time);
    }

    [Fact]
    public void AFadeLineReadsFaded_AtTheFadeLinesOwnTime()
    {
        var log = new BuffLossLog(Slows);
        log.Observe([Active("Temperance")], T0);
        log.Apply(new BuffFadeEvent(T0.AddSeconds(30), "Temperance", ["Temperance"]));
        log.Observe([Missing("Temperance")], T0.AddSeconds(31));

        var e = Assert.Single(log.Snapshot());
        Assert.Equal("faded", e.Cause);
        Assert.Equal(T0.AddSeconds(30), e.Time);
    }

    [Fact]
    public void AWornOffLineForYourOwnBuffAlsoReadsFaded()
    {
        var log = new BuffLossLog(Slows);
        log.Observe([Active("Temperance")], T0);
        log.Apply(new SpellWornOffEvent(T0.AddSeconds(30), "Temperance II", Target: ""));
        log.Observe([Missing("Temperance")], T0.AddSeconds(31));   // rank-folded identity

        Assert.Equal("faded", Assert.Single(log.Snapshot()).Cause);
    }

    [Fact]
    public void AHostileLandJustBeforeTheFadeNamesTheOverwrite()
    {
        // The requester's dev-report scenario: buff up, hostile spell lands on YOU,
        // buff gone — the cause names the spell and its caster.
        var log = new BuffLossLog(Slows);
        log.Observe([Active("Temperance")], T0);
        log.Apply(new DamageTakenEvent(T0.AddSeconds(29), "a swamp rat", 40,
            Melee: false, Ability: "Plague"));
        log.Apply(new BuffFadeEvent(T0.AddSeconds(31), "Temperance", ["Temperance"]));
        log.Observe([Missing("Temperance")], T0.AddSeconds(32));

        Assert.Equal("lost as Plague landed (a swamp rat)", Assert.Single(log.Snapshot()).Cause);
    }

    [Fact]
    public void TheCorrelationWindowIsTight_AtTwoSecondsIn_OutAtThree()
    {
        // Exactly at the window still correlates…
        var at = new BuffLossLog(Slows);
        at.Observe([Active("Temperance")], T0);
        at.Apply(new DamageTakenEvent(T0.AddSeconds(28), "a swamp rat", 40,
            Melee: false, Ability: "Plague"));
        at.Apply(new BuffFadeEvent(T0.AddSeconds(30), "Temperance", ["Temperance"]));
        at.Observe([Missing("Temperance")], T0.AddSeconds(31));
        Assert.StartsWith("lost as Plague landed", Assert.Single(at.Snapshot()).Cause);

        // …one second wider and the honest answer is plain "faded" — a loose window
        // would blame unrelated nukes.
        var past = new BuffLossLog(Slows);
        past.Observe([Active("Temperance")], T0);
        past.Apply(new DamageTakenEvent(T0.AddSeconds(27), "a swamp rat", 40,
            Melee: false, Ability: "Plague"));
        past.Apply(new BuffFadeEvent(T0.AddSeconds(30), "Temperance", ["Temperance"]));
        past.Observe([Missing("Temperance")], T0.AddSeconds(31));
        Assert.Equal("faded", Assert.Single(past.Snapshot()).Cause);
    }

    [Fact]
    public void AHostileLandAfterTheFadeNeverCorrelates()
    {
        // The displacement order is land-then-fade; a nuke arriving after the fade
        // line is a different story and must not be blamed.
        var log = new BuffLossLog(Slows);
        log.Observe([Active("Temperance")], T0);
        log.Apply(new BuffFadeEvent(T0.AddSeconds(30), "Temperance", ["Temperance"]));
        log.Apply(new DamageTakenEvent(T0.AddSeconds(31), "a swamp rat", 40,
            Melee: false, Ability: "Plague"));
        log.Observe([Missing("Temperance")], T0.AddSeconds(32));

        Assert.Equal("faded", Assert.Single(log.Snapshot()).Cause);
    }

    [Fact]
    public void MeleeSelfAndDotTickDamageAreNotOverwriteEvidence()
    {
        var log = new BuffLossLog(Slows);
        log.Observe([Active("Temperance")], T0);
        log.Apply(new DamageTakenEvent(T0.AddSeconds(29), "a swamp rat", 40,
            Melee: true, Ability: "Hit"));
        log.Apply(new DamageTakenEvent(T0.AddSeconds(29), "You", 40,
            Melee: false, Self: true, Ability: "Cannibalize"));
        log.Apply(new DamageTakenEvent(T0.AddSeconds(29), "a swamp rat", 40,
            Melee: false, Ability: "Plague", OverTime: true));
        log.Apply(new BuffFadeEvent(T0.AddSeconds(30), "Temperance", ["Temperance"]));
        log.Observe([Missing("Temperance")], T0.AddSeconds(31));

        Assert.Equal("faded", Assert.Single(log.Snapshot()).Cause);
    }

    [Fact]
    public void ASlowFlavorLineCorrelatesThroughItsCatalogLabel()
    {
        // The slow line names no spell; the catalog's label stands in — the closest
        // the log comes to "debuff Z displaced buff X".
        var log = new BuffLossLog(Slows);
        log.Observe([Active("Temperance")], T0);
        log.Apply(new SlowLandedEvent(T0.AddSeconds(30), "You feel lethargic."));
        log.Apply(new BuffFadeEvent(T0.AddSeconds(31), "Temperance", ["Temperance"]));
        log.Observe([Missing("Temperance")], T0.AddSeconds(32));

        Assert.Equal("lost as Turgur's Insects landed", Assert.Single(log.Snapshot()).Cause);
    }

    // ---- pure debuffs, which used to land in complete silence (#120) ----

    /// <summary>
    /// Frankthetankk's documented case, verbatim: Malaise lands, Elemental Shield goes.
    ///
    /// Malaise drops strength and AC. It deals no damage, it is not a slow, and its only
    /// trace in the log is the flavor line "You feel somewhat vulnerable." — so before
    /// DebuffLandingCatalog existed there was nothing for the loss history to correlate
    /// and the buff was recorded as merely "faded". His whole point was that a permanent
    /// buff's fade is itself the anomaly, and it deserves a named cause to put in an EQL
    /// bug report.
    /// </summary>
    [Fact]
    public void APureDebuffLandingNamesItselfAsTheCause()
    {
        var log = new BuffLossLog(Slows);
        log.Observe([Active("Elemental Shield")], T0);
        log.Apply(LogParser.Parse(
            "[Fri Aug 14 21:00:30 2026] You feel somewhat vulnerable.")!);
        log.Apply(new BuffFadeEvent(T0.AddSeconds(31), "Elemental Shield", ["Elemental Shield"]));
        log.Observe([Missing("Elemental Shield")], T0.AddSeconds(32));

        Assert.Equal("lost as Malaise landed", Assert.Single(log.Snapshot()).Cause);
    }

    /// <summary>The parser must actually produce the event — the catalog is only useful
    /// if a real log line reaches it.</summary>
    [Fact]
    public void TheDebuffFlavorLineParsesToItsOwnEvent()
    {
        var evt = Assert.IsType<DebuffLandedEvent>(LogParser.Parse(
            "[Fri Aug 14 21:00:30 2026] You feel somewhat vulnerable."));

        Assert.Equal("You feel somewhat vulnerable.", evt.Message);
        Assert.Equal("Malaise", DebuffLandingCatalog.Default.Find(evt.Message)!.Label);
    }

    /// <summary>A debuff landing well before the fade explains nothing. The window is
    /// deliberately tight: a wrong cause in a bug report is worse than no cause.</summary>
    [Fact]
    public void AnOldDebuffLandingIsNotBlamed()
    {
        var log = new BuffLossLog(Slows);
        log.Observe([Active("Elemental Shield")], T0);
        log.Apply(LogParser.Parse(
            "[Fri Aug 14 21:00:00 2026] You feel somewhat vulnerable.")!);
        log.Apply(new BuffFadeEvent(T0.AddSeconds(31), "Elemental Shield", ["Elemental Shield"]));
        log.Observe([Missing("Elemental Shield")], T0.AddSeconds(32));

        Assert.Equal("faded", Assert.Single(log.Snapshot()).Cause);
    }

    [Fact]
    public void DeathOwnsTheLoss_FadeOrExpiryAlike()
    {
        // The game strips buffs on death — blaming a timer or a debuff would be wrong.
        var faded = new BuffLossLog(Slows);
        faded.Observe([Active("Temperance")], T0);
        faded.Apply(new DeathEvent(T0.AddSeconds(30), "a swamp rat"));
        faded.Apply(new BuffFadeEvent(T0.AddSeconds(31), "Temperance", ["Temperance"]));
        faded.Observe([Missing("Temperance")], T0.AddSeconds(32));
        Assert.Equal("lost on death", Assert.Single(faded.Snapshot()).Cause);

        var expired = new BuffLossLog(Slows);
        expired.Observe([Active("Temperance")], T0);
        expired.Apply(new DeathEvent(T0.AddSeconds(30), "a swamp rat"));
        expired.Observe([Missing("Temperance")], T0.AddSeconds(31));
        Assert.Equal("lost on death", Assert.Single(expired.Snapshot()).Cause);
    }

    [Fact]
    public void AnEntryAddedToTheSetWhileAlreadyMissingIsNotALoss()
    {
        // Editing "Aegolism" into the set after it faded is configuration, not a
        // transition — no prior claim, no entry.
        var log = new BuffLossLog(Slows);
        log.Observe([Active("Temperance")], T0);
        log.Observe([Active("Temperance"), Missing("Aegolism")], T0.AddSeconds(1));
        Assert.Empty(log.Snapshot());
    }

    [Fact]
    public void TheFirstLookRecordsReplayedFades_ButNeverStampsStaleExpiries()
    {
        // Log switch / startup replay: a fade the replay showed is a real loss at its
        // own log time; a buff already Missing with no fade line predates the watch
        // and stays silent — "expired at wall-clock now" would be a lie.
        var log = new BuffLossLog(Slows);
        log.Apply(new BuffFadeEvent(T0.AddSeconds(-300), "Temperance", ["Temperance"]));
        log.Observe([Missing("Temperance"), Missing("Aegolism")], T0);

        var e = Assert.Single(log.Snapshot());
        Assert.Equal("Temperance", e.Spell);
        Assert.Equal("faded", e.Cause);
        Assert.Equal(T0.AddSeconds(-300), e.Time);
    }

    [Fact]
    public void AStaleFadeDoesNotExplainALaterExpiry()
    {
        // Faded (recorded), re-landed, then the timer ran out: the old fade line must
        // not be reused — the second loss is an expiry in its own right.
        var log = new BuffLossLog(Slows);
        log.Observe([Active("Temperance")], T0);
        log.Apply(new BuffFadeEvent(T0.AddSeconds(10), "Temperance", ["Temperance"]));
        log.Observe([Missing("Temperance")], T0.AddSeconds(11));
        log.Observe([Active("Temperance")], T0.AddSeconds(20));      // re-buffed
        log.Observe([Missing("Temperance")], T0.AddSeconds(120));    // timer ran out

        var entries = log.Snapshot();
        Assert.Equal(2, entries.Count);
        Assert.Equal("expired", entries[0].Cause);   // newest first
        Assert.Equal("faded", entries[1].Cause);
    }

    [Fact]
    public void TheHistoryCapsAtAHundred_NewestFirst()
    {
        var log = new BuffLossLog(Slows);
        for (var i = 0; i < BuffLossLog.Cap + 5; i++)
        {
            log.Observe([Active($"Buff{i}")], T0.AddSeconds(2 * i));
            log.Observe([Missing($"Buff{i}")], T0.AddSeconds(2 * i + 1));
        }
        var entries = log.Snapshot();
        Assert.Equal(BuffLossLog.Cap, entries.Count);
        Assert.Equal($"Buff{BuffLossLog.Cap + 4}", entries[0].Spell);
    }

    [Fact]
    public void ResetSessionClearsAndStartsBlind()
    {
        // The character-switch story: nothing leaks across, and the fresh log makes
        // no claims about states it never saw transition.
        var log = new BuffLossLog(Slows);
        log.Observe([Active("Temperance")], T0);
        log.Observe([Missing("Temperance")], T0.AddSeconds(1));
        Assert.Single(log.Snapshot());

        log.ResetSession();
        Assert.Equal(0, log.Count);
        log.Observe([Missing("Temperance")], T0.AddSeconds(2));   // first look, no fade
        Assert.Empty(log.Snapshot());
    }

    [Fact]
    public void ChangedFiresOnARecordedLoss_TheSurfacesRepaint()
    {
        var log = new BuffLossLog(Slows);
        var fired = 0;
        log.Changed += () => fired++;
        log.Observe([Active("Temperance")], T0);
        Assert.Equal(0, fired);   // nothing recorded yet
        log.Observe([Missing("Temperance")], T0.AddSeconds(1));
        Assert.Equal(1, fired);
    }

    [Fact]
    public void ExportTextReadsChronologically_WithCharacterHeader()
    {
        var log = new BuffLossLog(Slows);
        log.Observe([Active("Temperance"), Active("Aegolism")], T0);
        log.Apply(new BuffFadeEvent(T0.AddSeconds(10), "Temperance", ["Temperance"]));
        log.Observe([Missing("Temperance"), Active("Aegolism")], T0.AddSeconds(11));
        log.Observe([Missing("Temperance"), Missing("Aegolism")], T0.AddSeconds(60));

        var text = log.ExportText("Dranak");
        var lines = text.Split('\n').Select(l => l.TrimEnd()).ToArray();
        Assert.Equal("EQBuddy buff losses — Dranak", lines[0]);
        Assert.Contains("Temperance — faded", lines[1]);   // oldest first for the report
        Assert.Contains("Aegolism — expired", lines[2]);
    }

    [Fact]
    public void TheEvaluatorCarriesTheEstimatedFlagThrough()
    {
        // The loss log's "(est)" hedge rides the entry state, set from the matched
        // BuffState — a wiki-base countdown running out is a weaker claim.
        var est = new BuffState("Temperance", ["Temperance"], "You",
            T0.AddSeconds(-100), T0.AddSeconds(50), Estimated: true);
        var s = Assert.Single(BuffSetEvaluator.Evaluate(
            ["Temperance"], [est], new HashSet<string>(), new HashSet<string>(), T0, 60));
        Assert.True(s.Estimated);
    }
}
