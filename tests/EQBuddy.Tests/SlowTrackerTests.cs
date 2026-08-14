using EQBuddy.Core;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>
/// The slow alert (#94, Frankthetankk): attack-speed debuffs landing on YOU, keyed
/// on cast-on-you lines from the harvest catalog, cleared by fade lines / duration /
/// zoning / death, with cure guidance by counter type and a raid-chatter-based
/// raid-only gate. All through real log lines — the parser is part of the contract.
/// </summary>
public class SlowTrackerTests
{
    private static readonly DateTime T0 = DateTime.Parse("2026-08-11T21:00:00");

    private static GameEvent Ev(int seconds, string message) =>
        LogParser.Parse($"[{T0.AddSeconds(seconds):ddd MMM d HH:mm:ss yyyy}] {message}")!;

    private static SlowTracker Replay(params GameEvent[] events)
    {
        var t = new SlowTracker();
        foreach (var e in events) t.Apply(e);
        return t;
    }

    // ---- the group-member bard case (David's Hugzee session, 2026-08-13:
    // grouped with two bards, no sing/forget lines ever log for OTHER players's
    // songs — the only tell is the cluster of first-person song fades). ----

    [Fact]
    public void ASlowLineAmidASongFadeClusterIsTheHasteLapsingNotASlow()
    {
        // Hugzee's exact shape: four flavor fades, "You slow down." six seconds
        // later (the next server tick).
        var t = Replay(
            Ev(0, "Your wounds stop healing."),
            Ev(0, "Your surge of strength fades."),
            Ev(6, "You slow down."));
        Assert.Empty(t.Snapshot(T0.AddSeconds(7)));
    }

    [Fact]
    public void FadesArrivingAfterTheChipRetroClearIt()
    {
        // Tick alignment sometimes prints the shared line FIRST: the chip appears,
        // then the cluster betrays it — take it back.
        var t = Replay(Ev(0, "You slow down."));
        Assert.Single(t.Snapshot(T0.AddSeconds(1)));
        t.Apply(Ev(4, "Your surge of strength fades."));
        Assert.Empty(t.Snapshot(T0.AddSeconds(5)));
    }

    [Fact]
    public void ARealSlowAwayFromAnyFadeClusterStillLands()
    {
        // "You feel lethargic." shares no line with any haste — the cluster rule
        // must not touch it even mid-cluster; and "You slow down." far from any
        // fade still lands as the real slow it may be.
        var t = Replay(
            Ev(0, "Your surge of strength fades."),
            Ev(2, "You feel lethargic."),
            Ev(60, "You slow down."));
        Assert.Equal(2, t.Snapshot(T0.AddSeconds(61)).Count);
    }

    [Fact]
    public void ASlowLineAfterASelosPulseIsTheSongLapsing()
    {
        // David's suggestion, verified against Hugzee's two-bard log: every false
        // "You slow down." arrived 12–44s after a "Your feet move faster." pulse.
        var t = Replay(
            Ev(0, "Your feet move faster."),
            Ev(44, "You slow down."));
        Assert.Empty(t.Snapshot(T0.AddSeconds(45)));
    }

    [Fact]
    public void AFreshPulseAfterTheChipRetroClearsIt()
    {
        // Re-twist ordering: the shared line prints, then the next pulse lands —
        // proof it was song churn.
        var t = Replay(Ev(30, "You slow down."));
        Assert.Single(t.Snapshot(T0.AddSeconds(31)));
        t.Apply(Ev(40, "Your feet move faster."));
        Assert.Empty(t.Snapshot(T0.AddSeconds(41)));
    }

    [Fact]
    public void ASlowLineLongAfterTheLastPulseStillLands()
    {
        // The bard left the group ten minutes ago; a real Deeds-line slow must
        // still announce.
        var t = Replay(
            Ev(0, "Your feet move faster."),
            Ev(600, "You slow down."));
        Assert.Single(t.Snapshot(T0.AddSeconds(601)));
    }

    [Fact]
    public void DismissDropsTheChipImmediately()
    {
        var t = Replay(Ev(0, "You feel lethargic."));
        var s = Assert.Single(t.Snapshot(T0.AddSeconds(1)));
        t.Dismiss(s.Message);
        Assert.Empty(t.Snapshot(T0.AddSeconds(2)));
    }

    [Fact]
    public void AKnownSlowLandingRaisesAChipWithTheExactNumbers()
    {
        // Sha's Lethargy: the #94 example — 40% slow, 12 disease counters, 2:30.
        var t = Replay(Ev(0, "You feel lethargic."));

        var s = Assert.Single(t.Snapshot(T0.AddSeconds(1)));
        Assert.Equal("40%", s.PctText);
        Assert.Equal("12 disease counters", s.CounterText);
        Assert.Equal(150, s.RemainingSeconds(T0.AddSeconds(0))!.Value, 0);
        Assert.Equal("Sha's Lethargy", Assert.Single(s.Spells));
    }

    [Fact]
    public void ASharedLandingLineShowsTheHonestRangeNotAGuess()
    {
        // "You feel drowsy." is the whole insect line — the log cannot name the rank.
        var t = Replay(Ev(0, "You feel drowsy."));

        var s = Assert.Single(t.Snapshot(T0.AddSeconds(1)));
        Assert.Contains("–", s.PctText);           // a range, e.g. 23–75%
        Assert.True(s.Spells.Length >= 5);
        Assert.Contains("Turgur's Insects", s.Spells);
        Assert.Contains("Walking Sleep", s.Spells);
    }

    [Fact]
    public void TheFadeLineClearsTheChip()
    {
        var t = Replay(
            Ev(0, "You feel lethargic."),
            Ev(30, "You are no longer lethargic."));   // FadeMessages knows this line

        Assert.Empty(t.Snapshot(T0.AddSeconds(31)));
    }

    [Fact]
    public void TheInsectLineFadeClearsTheInsectChip()
    {
        var t = Replay(
            Ev(0, "You feel drowsy."),
            Ev(30, "You feel less drowsy."));

        Assert.Empty(t.Snapshot(T0.AddSeconds(31)));
    }

    [Fact]
    public void ZoningAndDyingBothClearEverything()
    {
        var zoned = Replay(
            Ev(0, "You feel lethargic."),
            Ev(10, "You have entered The Plane of Sky."));
        Assert.Empty(zoned.Snapshot(T0.AddSeconds(11)));

        var slain = Replay(
            Ev(0, "You feel lethargic."),
            Ev(10, "You have been slain by Master Yael!"));
        Assert.Empty(slain.Snapshot(T0.AddSeconds(11)));
    }

    [Fact]
    public void TheChipExpiresWhenTheLongestCandidateDurationRunsOut()
    {
        var t = Replay(Ev(0, "You feel lethargic."));   // 150 s documented

        // Still visible inside the linger window, gone after it. Expiry is driven
        // by log time (the next event), not wall clock — replay-safe like mez.
        Assert.Single(t.Snapshot(T0.AddSeconds(155)));
        t.Apply(Ev(200, "You have slain a rat!"));
        Assert.Empty(t.Snapshot(T0.AddSeconds(200)));
    }

    [Fact]
    public void ARelandRefreshesInsteadOfDuplicatingAndOnlyTheFirstIsNew()
    {
        var announcements = new List<bool>();
        var t = new SlowTracker();
        t.Landed += (_, isNew) => announcements.Add(isNew);

        t.Apply(Ev(0, "You feel lethargic."));
        t.Apply(Ev(20, "You feel lethargic."));   // chain-slowing NPC re-lands it

        Assert.Single(t.Snapshot(T0.AddSeconds(21)));
        Assert.Equal([true, false], announcements);   // speak once, not twice
    }

    [Fact]
    public void TwoDifferentSlowsStackAsTwoChips()
    {
        var t = Replay(
            Ev(0, "You feel lethargic."),
            Ev(5, "Strands of solid music bind your body."));

        Assert.Equal(2, t.Snapshot(T0.AddSeconds(6)).Count);
    }

    [Fact]
    public void RaidChatterProvesARaidAndGoesStaleAfterTenMinutes()
    {
        var t = Replay(Ev(0, "Cleric1 tells the raid, 'CH --> Tankname'"));

        Assert.True(t.InRaid(T0.AddMinutes(5)));
        Assert.False(t.InRaid(T0.AddMinutes(11)));

        var own = Replay(Ev(0, "You tell your raid, 'inc 3'"));
        Assert.True(own.InRaid(T0.AddMinutes(1)));
    }

    [Fact]
    public void TheCureLineNamesTheStrongestCuresFirstWithPerCastCounts()
    {
        var t = new SlowTracker();
        t.Apply(Ev(0, "You feel lethargic."));
        var line = t.CureLine(t.Snapshot(T0.AddSeconds(1))[0]);

        Assert.StartsWith("Cure:", line);
        Assert.Contains("Abolish Disease (36/cast)", line);
        // Strongest first: Legends' Abolish strips 36 counters, Counteract 8.
        Assert.True(line.IndexOf("Abolish Disease", StringComparison.Ordinal)
            < line.IndexOf("Counteract Disease", StringComparison.Ordinal));
    }

    [Fact]
    public void ACounterlessSlowOffersNoCureLine()
    {
        // Bard-song slows carry no counters — nothing cures them, they just end.
        var t = new SlowTracker();
        t.Apply(Ev(0, "Strands of solid music bind your body."));
        Assert.Equal("", t.CureLine(t.Snapshot(T0.AddSeconds(1))[0]));
    }

    // ---- catalog contract: the honesty rules live in the data ----

    [Fact]
    public void BeneficialSlowsAreNotInTheCatalog()
    {
        // Torpor is a shaman's own tradeoff buff; "you are slowed!" on it is noise.
        // Aura of Marr's landing line is the regen tick — excluded twice over.
        foreach (var entry in AllEntries())
            foreach (var spell in entry.Spells)
                Assert.DoesNotContain(spell.Name, new[] { "Torpor", "Rejuvenation", "Aura of Marr" });
        Assert.Null(SlowDebuffCatalog.Default.Find("Your wounds begin to heal."));
    }

    [Fact]
    public void SlowLandingLinesNeverCollideWithFadeOrRegenLines()
    {
        // Exact-match catalogs sharing the parser must partition the message space;
        // the harvest enforces it, this pins it against a bad regeneration.
        foreach (var entry in AllEntries())
            Assert.Null(FadeMessageCatalog.Default.Find(entry.Message));
    }

    [Fact]
    public void TheParserMapsLandingLinesToSlowEventsAndRaidChatToRaidEvents()
    {
        Assert.IsType<SlowLandedEvent>(Ev(0, "You feel lethargic."));
        Assert.IsType<RaidChatterEvent>(Ev(0, "Soandso tells the raid, 'hi'"));
        // And fade lines stay fades — catalog order in the parser must not shadow them.
        Assert.IsType<BuffFadeEvent>(Ev(0, "You feel less drowsy."));
    }

    // ---- #116 (Fennec-Halas): "You slow down." is ALSO how Selo's haste fades ----

    [Fact]
    public void TheParserReadsForgetLines()
    {
        var forgot = Assert.IsType<SongForgottenEvent>(Ev(0, "You forget Selo's Accelerando."));
        Assert.Equal("Selo's Accelerando", forgot.Song);
    }

    [Fact]
    public void SelosFadeDoesNotTriggerTheSlowAlert()
    {
        // Fennec-Halas's exact lines, 11 s apart: forget, then the wear-off.
        var t = Replay(
            Ev(0, "You forget Selo's Accelerando."),
            Ev(11, "You slow down."));
        Assert.Empty(t.Snapshot(T0.AddSeconds(12)));

        // The log's backtick spelling folds to the wiki's apostrophe.
        var t2 = Replay(
            Ev(0, "You forget Selo`s Accelerating Chorus."),
            Ev(11, "You slow down."));
        Assert.Empty(t2.Snapshot(T0.AddSeconds(12)));
    }

    [Fact]
    public void ARealDeedsSlowStillAlertsWithoutARecentForget()
    {
        // No forget line: "You slow down." is a genuine Deeds-line slow.
        var s = Assert.Single(Replay(Ev(0, "You slow down.")).Snapshot(T0.AddSeconds(1)));
        Assert.Contains("Languid Pace", s.Spells);

        // A forget long outside the window doesn't suppress either.
        var t = Replay(
            Ev(0, "You forget Selo's Accelerando."),
            Ev(120, "You slow down."));
        Assert.Single(t.Snapshot(T0.AddSeconds(121)));

        // And forgetting some unrelated song never suppresses.
        var t2 = Replay(
            Ev(0, "You forget Chant of Battle."),
            Ev(11, "You slow down."));
        Assert.Single(t2.Snapshot(T0.AddSeconds(12)));
    }

    // ---- #94 field report: type + count on the chip face, not just the tooltip ----

    [Fact]
    public void TheChipLabelCarriesCounterTypeAndCount()
    {
        var t = Replay(Ev(0, "You feel lethargic."));            // Sha's: disease 12
        Assert.Equal("Slowed 40% · disease 12",
            EQBuddy.UI.Shared.SlowChipText.Label(t.Snapshot(T0.AddSeconds(1)).Single()));

        var shared = Replay(Ev(0, "You feel drowsy."));          // insect line: a range
        var label = EQBuddy.UI.Shared.SlowChipText.Label(shared.Snapshot(T0.AddSeconds(1)).Single());
        Assert.StartsWith("Slowed ", label);
        Assert.Contains("disease", label);
        Assert.Contains("–", label);
    }

    [Fact]
    public void ACounterlessSlowChipStaysPlain()
    {
        // Bard-song slows carry no counters: no cure tag to show, and inventing one
        // ("magic 0"?) would be noise.
        var t = Replay(Ev(0, "Strands of solid music bind your body."));
        var label = EQBuddy.UI.Shared.SlowChipText.Label(t.Snapshot(T0.AddSeconds(1)).Single());
        Assert.DoesNotContain("·", label);
    }

    [Fact]
    public void TheCatalogCarriesTheFadeCollisionData()
    {
        var entry = SlowDebuffCatalog.Default.Find("You slow down.")!;
        Assert.Contains("Selo's Accelerando", entry.FadeOf);
        Assert.Contains("Selo's Accelerating Chorus", entry.FadeOf);
        // Unambiguous lines stay unencumbered.
        Assert.Empty(SlowDebuffCatalog.Default.Find("You feel lethargic.")!.FadeOf);
    }

    private static IEnumerable<SlowDebuffCatalog.Entry> AllEntries()
    {
        // The catalog exposes lookups, not enumeration — walk it via the harvest's
        // own landing lines, embedded as the source of truth.
        var catalog = SlowDebuffCatalog.Default;
        Assert.True(catalog.Count >= 20, $"suspiciously small slow catalog: {catalog.Count}");
        foreach (var msg in KnownLandingLines)
            if (catalog.Find(msg) is { } entry)
                yield return entry;
    }

    private static readonly string[] KnownLandingLines =
    [
        "You feel lethargic.", "You feel drowsy.", "You slow down.",
        "You feel your muscles lock.", "Strands of solid music bind your body.",
        "You have been deafened.", "Your limbs slow down!",
    ];
}
