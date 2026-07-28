using EQBuddy.Core;

namespace EQBuddy.Tests;

/// <summary>
/// Spell tracking: cast lines, classification, and the charm lifecycle.
///
/// The charm sequences below are transcribed from a real EQ Legends log
/// (eqlog_Douglas_qeynos, 2026-07-20) — both the success path and the interrupted cast
/// that must NOT produce a pet.
/// </summary>
public class SpellTrackingTests
{
    private const string Ts = "[Sat Jul 18 15:39:13 2026] ";

    private static SessionStats Replay(params string[] lines)
    {
        var stats = new SessionStats { CharacterName = "Douglas" };
        foreach (var line in lines)
        {
            var evt = LogParser.Parse(line);
            if (evt is not null) stats.Apply(evt);
        }
        return stats;
    }

    private static string At(int mm, int ss, string msg) =>
        $"[Sat Jul 18 15:{mm:D2}:{ss:D2} 2026] {msg}";

    // ---- parsing ----

    [Theory]
    [InlineData("You begin casting Stinging Swarm.", "Stinging Swarm")]
    [InlineData("You begin casting Befriend Animal.", "Befriend Animal")]
    [InlineData("You begin casting Stinging Swarm V.", "Stinging Swarm V")]
    [InlineData("You begin casting Succor: East Karana.", "Succor: East Karana")]
    [InlineData("You begin singing Chords of Dissonance.", "Chords of Dissonance")]
    public void CastStartParsed(string msg, string spell) =>
        Assert.Equal(spell, Assert.IsType<SpellCastEvent>(LogParser.Parse(Ts + msg)).Spell);

    [Fact]
    public void CastInterruptedParsed() =>
        Assert.Equal("Stinging Swarm", Assert.IsType<SpellInterruptedEvent>(
            LogParser.Parse(Ts + "Your Stinging Swarm spell is interrupted.")).Spell);

    [Fact]
    public void FizzleCarriesSpellName() =>
        Assert.Equal("Befriend Animal", Assert.IsType<FizzleEvent>(
            LogParser.Parse(Ts + "Your Befriend Animal spell fizzles!")).Spell);

    [Fact]
    public void ResistCarriesSpellName() =>
        Assert.Equal("Denon's Disruptive Discord", Assert.IsType<ResistEvent>(
            LogParser.Parse(Ts + "A willowisp resisted your Denon's Disruptive Discord!")).Spell);

    [Fact]
    public void DotTicksAreFlaggedOverTimeAndDirectHitsAreNot()
    {
        Assert.True(Assert.IsType<DamageDealtEvent>(
            LogParser.Parse(Ts + "Orc centurion has taken 10 damage from your Stinging Swarm.")).OverTime);
        Assert.False(Assert.IsType<DamageDealtEvent>(
            LogParser.Parse(Ts + "You hit orc centurion for 13 points of fire damage by Burn.")).OverTime);
    }

    /// <summary>Cast lines for other entities are deliberately not parsed — EQBuddy stays
    /// a single-character tool, so another player's cast line is ignored.
    /// (Names sanitized per CONTRIBUTING — these lines are real in shape only.)</summary>
    [Fact]
    public void OtherEntitiesCastsAreIgnored()
    {
        Assert.Null(LogParser.Parse(Ts + "Otherchar begins casting Tame Spirit."));
        Assert.Null(LogParser.Parse(Ts + "Otherchar`s warder begins casting Minor Healing."));
    }

    // ---- classification ----

    [Theory]
    [InlineData("Stinging Swarm V", "Stinging Swarm")]
    [InlineData("Light Healing V", "Light Healing")]
    [InlineData("Heroic Leap I", "Heroic Leap")]
    [InlineData("Befriend Animal", "Befriend Animal")]
    [InlineData("Chords of Dissonance", "Chords of Dissonance")]
    public void RankSuffixesCollapseOntoTheBaseName(string spell, string expected) =>
        Assert.Equal(expected, SpellCatalog.BaseName(spell));

    [Fact]
    public void RankedCharmStillClassifiesAsCharm()
    {
        var catalog = new SpellCatalog();
        Assert.Equal(SpellCategory.Charm, catalog.Classify("Befriend Animal"));
        Assert.Equal(SpellCategory.Charm, catalog.Classify("Befriend Animal III"));
        Assert.True(catalog.IsCrowdControl("Befriend Animal III"));
    }

    [Fact]
    public void UnknownSpellsClassifyAsUnknownRatherThanGuessing() =>
        Assert.Equal(SpellCategory.Unknown, new SpellCatalog().Classify("Tame Spirit"));

    [Fact]
    public void ObservationCannotReclassifyASeededCrowdControlSpell()
    {
        var catalog = new SpellCatalog();
        Assert.False(catalog.Learn("Befriend Animal", SpellCategory.DirectDamage));
        Assert.Equal(SpellCategory.Charm, catalog.Classify("Befriend Animal"));
    }

    [Fact]
    public void LearnedSpellsAreRankInsensitive()
    {
        var catalog = new SpellCatalog();
        Assert.True(catalog.Learn("Stinging Swarm", SpellCategory.DamageOverTime));
        Assert.Equal(SpellCategory.DamageOverTime, catalog.Classify("Stinging Swarm V"));
    }

    // ---- charm lifecycle (real log sequence) ----

    [Fact]
    public void CharmCastBeforeBlinkConfirmsThePetImmediately()
    {
        // Real sequence: cast at 44:06, blink at 44:10. Because the cast is a known charm
        // the pet is certain, so damage lands under "Pet (…)" with no provisional stage.
        var s = Replay(
            At(44, 6, "You begin casting Befriend Animal."),
            At(44, 10, "a giant spider blinks."),
            At(44, 12, "A giant spider hits orc pawn for 14 points of damage.")).Snapshot();

        var pet = Assert.Single(s.DamageBySource, d => d.Name == "Pet (Giant spider)");
        Assert.Equal(14, pet.Total);
        Assert.DoesNotContain(s.DamageBySource, d => d.Name.StartsWith("Pet?"));
    }

    [Fact]
    public void BlinkWithoutACharmCastStaysProvisional()
    {
        // No cast in flight — fall back to the original blink-only guess so this can never
        // be worse than the previous behavior.
        var s = Replay(
            At(0, 0, "a puma blinks."),
            At(0, 2, "A puma hits orc pawn for 9 points of damage.")).Snapshot();

        Assert.Single(s.DamageBySource, d => d.Name == "Pet? (Puma)");
    }

    [Fact]
    public void AnInterruptedCharmNeverClaimsAPet()
    {
        // Real sequence: cast at 03:47, interrupted at 03:51. Nothing was charmed, so a
        // nearby creature's damage must not be credited to the player.
        var s = Replay(
            At(3, 47, "You begin casting Befriend Animal."),
            At(3, 51, "Your Befriend Animal spell is interrupted."),
            At(3, 55, "A giant spider hits orc pawn for 14 points of damage.")).Snapshot();

        Assert.DoesNotContain(s.DamageBySource, d => d.Name.StartsWith("Pet"));
        Assert.Equal(0, s.DamageDealt);
    }

    [Fact]
    public void CharmWearingOffDropsThePetImmediately()
    {
        // Real sequence: charmed at 44:10, worn off at 46:01. Damage after the break
        // belongs to the creature, not to us.
        var s = Replay(
            At(44, 6, "You begin casting Befriend Animal."),
            At(44, 10, "a giant spider blinks."),
            At(44, 12, "A giant spider hits orc pawn for 14 points of damage."),
            At(46, 1, "Your Befriend Animal spell has worn off of a giant spider."),
            At(46, 5, "A giant spider hits orc pawn for 99 points of damage.")).Snapshot();

        Assert.Equal(14, s.DamageDealt);
    }

    [Fact]
    public void AnUnknownCharmSpellIsLearnedFromTheMasterTell()
    {
        // "Tame Spirit" isn't in the seed table. Cast → blink → "Master" tell proves it is
        // a charm, so the next cast of it confirms a pet with no provisional stage.
        var stats = Replay(
            At(0, 0, "You begin casting Tame Spirit."),
            At(0, 4, "an asp blinks."),
            At(0, 9, "An asp told you, 'Attacking orc pawn Master.'"),
            At(1, 0, "Your Tame Spirit spell has worn off of an asp."),
            At(2, 0, "You begin casting Tame Spirit."),
            At(2, 4, "a puma blinks."),
            At(2, 6, "A puma hits orc pawn for 21 points of damage."));

        var s = stats.Snapshot();
        Assert.Single(s.DamageBySource, d => d.Name == "Pet (Puma)");
    }

    // ---- cast analytics ----

    [Fact]
    public void CastCompletionCountsInterruptsAndFizzles()
    {
        var s = Replay(
            At(0, 0, "You begin casting Stinging Swarm."),
            At(0, 4, "Orc centurion has taken 10 damage from your Stinging Swarm."),
            At(0, 10, "You begin casting Stinging Swarm."),
            At(0, 14, "Your Stinging Swarm spell is interrupted."),
            At(0, 20, "You begin casting Befriend Animal."),
            At(0, 24, "Your Befriend Animal spell fizzles!"),
            At(0, 30, "You begin casting Stinging Swarm."),
            At(0, 34, "Orc centurion has taken 10 damage from your Stinging Swarm.")).Snapshot();

        Assert.Equal(4, s.CastsStarted);
        Assert.Equal(1, s.CastsInterrupted);
        Assert.Equal(1, s.Fizzles);
        Assert.Equal(0.5, s.CastCompletion);
    }

    [Fact]
    public void CastCompletionIsNullBeforeAnyCast() =>
        Assert.Null(Replay(At(0, 0, "You slash orc pawn for 10 points of damage.")).Snapshot().CastCompletion);

    [Fact]
    public void DamageSplitsIntoDotAndDirect()
    {
        var s = Replay(
            At(0, 0, "Orc centurion has taken 10 damage from your Stinging Swarm."),
            At(0, 2, "Orc centurion has taken 10 damage from your Stinging Swarm."),
            At(0, 4, "You hit orc centurion for 13 points of fire damage by Burn."),
            At(0, 6, "You slash orc centurion for 25 points of damage.")).Snapshot();

        Assert.Equal(20, s.DotDamage);
        Assert.Equal(13, s.DirectSpellDamage);
        Assert.Equal(58, s.DamageDealt);   // melee stays out of both spell buckets
    }

    // ---- crowd-control watch rules ----

    private static readonly string[] FadeLines =
    [
        At(0, 0, "Your Befriend Animal spell has worn off of a puma."),   // charm
        At(0, 5, "Your Mesmerize spell has worn off of an asp."),         // mez
        At(0, 9, "Your Chords of Dissonance spell has worn off of a giant spider."), // damage song
    ];

    [Fact]
    public void AnyCrowdControlFilterNeedsNoMatchTextAndSkipsNonCcSpells()
    {
        var rule = new TrackedRule
        {
            Name = "CC broke", Kind = WatchKind.SpellFade, SpellFilter = SpellFilter.AnyCrowdControl,
        };
        var tracked = Assert.Single(Replay(FadeLines).Snapshot(recentWindow: null, rules: [rule]).Tracked);

        Assert.Equal(2, tracked.TotalQuantity);
        Assert.Contains(tracked.Items, i => i.Name == "Befriend Animal (Puma)");
        Assert.Contains(tracked.Items, i => i.Name == "Mesmerize (Asp)");
        Assert.DoesNotContain(tracked.Items, i => i.Name.StartsWith("Chords"));
    }

    [Fact]
    public void ASingleClassFilterMatchesOnlyThatClass()
    {
        var rule = new TrackedRule
        {
            Name = "Charm broke", Kind = WatchKind.SpellFade, SpellFilter = SpellFilter.Charm,
        };
        var tracked = Assert.Single(Replay(FadeLines).Snapshot(recentWindow: null, rules: [rule]).Tracked);

        Assert.Equal(1, tracked.TotalQuantity);
        Assert.Contains(tracked.Items, i => i.Name == "Befriend Animal (Puma)");
    }

    [Fact]
    public void AnySpellFilterCatchesEvenUnclassifiedSpellsLikeBuffs()
    {
        var rule = new TrackedRule
        {
            Name = "Anything dropped", Kind = WatchKind.SpellFade, SpellFilter = SpellFilter.AnySpell,
        };
        Assert.Equal(3, Assert.Single(
            Replay(FadeLines).Snapshot(recentWindow: null, rules: [rule]).Tracked).TotalQuantity);
    }

    [Fact]
    public void ByNameFilterKeepsTheOriginalSubstringBehaviour()
    {
        var rule = new TrackedRule { Name = "Charm only", Pattern = "Befriend", Kind = WatchKind.SpellFade };
        Assert.Equal(SpellFilter.ByName, rule.SpellFilter);   // the default, so old rules are unaffected
        Assert.Equal(1, Assert.Single(
            Replay(FadeLines).Snapshot(recentWindow: null, rules: [rule]).Tracked).TotalQuantity);
    }

    /// <summary>Both UIs map dropdown indexes straight back to enum values, so a label
    /// array that drifts out of sync silently mislabels every rule.</summary>
    [Fact]
    public void DropdownLabelsStayAlignedWithTheirEnums()
    {
        Assert.Equal(Enum.GetValues<WatchKind>().Length,
            EQBuddy.UI.Shared.OptionsViewModel.KindNames.Length);
        Assert.Equal(Enum.GetValues<SpellFilter>().Length,
            EQBuddy.UI.Shared.OptionsViewModel.SpellFilterNames.Length);
    }

    // ---- the built-in CC alert ----

    [Fact]
    public void AFreshInstallShipsWithTheCrowdControlAlertEnabled()
    {
        var settings = new AppSettings();
        Assert.True(settings.ApplyDefaultRules());

        var rule = Assert.Single(settings.TrackedRules);
        Assert.Equal(WatchKind.SpellFade, rule.Kind);
        Assert.Equal(SpellFilter.AnyCrowdControl, rule.SpellFilter);
        Assert.True(rule.Enabled);
        Assert.True(rule.AlertBanner);
        Assert.True(rule.AlertSound);
    }

    /// <summary>The built-in rule is a starting point, not a fixture: every part of it has
    /// to be editable, and edits must survive the next launch's default-rules pass.</summary>
    [Fact]
    public void TheBuiltInRuleStaysFullyEditable()
    {
        var settings = new AppSettings();
        settings.ApplyDefaultRules();
        var rule = settings.TrackedRules[0];

        rule.AlertSound = false;
        rule.AlertBanner = false;
        rule.SpellFilter = SpellFilter.Charm;
        rule.Name = "My charm alarm";
        rule.Enabled = false;

        Assert.False(settings.ApplyDefaultRules());   // no second pass to undo the edits
        var after = Assert.Single(settings.TrackedRules);
        Assert.False(after.AlertSound);
        Assert.False(after.AlertBanner);
        Assert.False(after.Enabled);
        Assert.Equal(SpellFilter.Charm, after.SpellFilter);
        Assert.Equal("My charm alarm", after.Name);
    }

    [Fact]
    public void DefaultRulesAreNotAppliedTwice()
    {
        var settings = new AppSettings();
        settings.ApplyDefaultRules();
        Assert.False(settings.ApplyDefaultRules());
        Assert.Single(settings.TrackedRules);
    }

    /// <summary>Deleting the built-in rule has to stick, or it reappears every launch.</summary>
    [Fact]
    public void ADeletedDefaultRuleStaysDeleted()
    {
        var settings = new AppSettings();
        settings.ApplyDefaultRules();
        settings.TrackedRules.Clear();

        Assert.False(settings.ApplyDefaultRules());
        Assert.Empty(settings.TrackedRules);
    }

    /// <summary>The built-in rule must actually fire end to end, not just exist.</summary>
    [Fact]
    public void TheBuiltInRuleAlertsWhenACharmBreaks()
    {
        var settings = new AppSettings();
        settings.ApplyDefaultRules();

        var tracked = Assert.Single(Replay(
            At(0, 0, "You begin casting Befriend Animal."),
            At(0, 4, "a puma blinks."),
            At(1, 0, "Your Befriend Animal spell has worn off of a puma."))
            .Snapshot(recentWindow: null, rules: settings.TrackedRules).Tracked);

        Assert.Equal(1, tracked.TotalQuantity);
        Assert.Equal("Befriend Animal (Puma)", tracked.LastItem);
    }

    /// <summary>A class-filtered rule carries no match text, so the snapshot's
    /// "skip rules with no pattern" guard must not throw it away.</summary>
    [Fact]
    public void ClassFilteredRulesSurviveTheEmptyPatternGuard()
    {
        var rule = new TrackedRule
        {
            Name = "", Pattern = "", Kind = WatchKind.SpellFade, SpellFilter = SpellFilter.AnyCrowdControl,
        };
        Assert.True(rule.IsMatchAllKind);
        Assert.Equal(2, Assert.Single(
            Replay(FadeLines).Snapshot(recentWindow: null, rules: [rule]).Tracked).TotalQuantity);
    }
}
