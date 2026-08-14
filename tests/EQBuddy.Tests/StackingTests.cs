using EQBuddy.Core;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>
/// "Blocked by" stacking capture: "Your X spell did not take hold. (Blocked by Y.)"
/// means the cast COMPLETED but the buff never landed — another buff owns its slot.
/// The line shape is documented in the wild by EQ Legends Companion's research corpus
/// (fact, not code). Covers the parser (both line shapes), the SessionStats outcome
/// and disarm paths, the BuffTracker's pending-window disarm, and the per-character
/// stacking ledger's replay-safe persistence.
/// </summary>
public class StackingTests
{
    private const string Ts = "[Sat Jul 18 15:39:13 2026] ";
    private static readonly DateTime T0 = DateTime.Parse("2026-08-12T21:00:00");

    private static string At(int mm, int ss, string msg) =>
        $"[Sat Jul 18 15:{mm:D2}:{ss:D2} 2026] {msg}";

    private static SessionStats Replay(params string[] lines)
    {
        var stats = new SessionStats { CharacterName = "Douglas", ServerName = "qeynos" };
        foreach (var line in lines)
        {
            var evt = LogParser.Parse(line);
            if (evt is not null) stats.Apply(evt);
        }
        return stats;
    }

    private static GameEvent Ev(int seconds, string message) =>
        LogParser.Parse($"[{T0.AddSeconds(seconds):ddd MMM d HH:mm:ss yyyy}] {message}")!;

    private static string TempStorePath() =>
        Path.Combine(Path.GetTempPath(), $"stacking-ledger-{Guid.NewGuid():N}.json");

    // ---- parsing ----

    [Theory]
    [InlineData("Your Chloroplast spell did not take hold. (Blocked by Regrowth.)",
        "Chloroplast", "Regrowth")]
    // Apostrophes on either side must survive the lazy captures.
    [InlineData("Your Kazumi's Note of Preservation spell did not take hold. (Blocked by McVaxius' Rousing Rondo.)",
        "Kazumi's Note of Preservation", "McVaxius' Rousing Rondo")]
    // Colons and hyphens too — spell names use the full punctuation set.
    [InlineData("Your Talisman: Wolf spell did not take hold. (Blocked by Form of the Great Wolf.)",
        "Talisman: Wolf", "Form of the Great Wolf")]
    public void BlockedLineCarriesBothSpells(string msg, string spell, string blocker)
    {
        var e = Assert.IsType<SpellBlockedEvent>(LogParser.Parse(Ts + msg));
        Assert.Equal(spell, e.Spell);
        Assert.Equal(blocker, e.BlockedBy);
    }

    [Fact]
    public void BlockerlessLineStillParsesRatherThanDropping()
    {
        // The game sometimes omits the parenthetical; the failed cast still counts.
        var e = Assert.IsType<SpellBlockedEvent>(LogParser.Parse(
            Ts + "Your Chloroplast spell did not take hold."));
        Assert.Equal("Chloroplast", e.Spell);
        Assert.Equal("", e.BlockedBy);
    }

    // ---- SessionStats outcomes ----

    [Fact]
    public void BlockedCastsCountPerSpellAndInTotal()
    {
        var s = Replay(
            At(0, 0, "You begin casting Chloroplast."),
            At(0, 3, "Your Chloroplast spell did not take hold. (Blocked by Regrowth.)"),
            At(0, 10, "You begin casting Chloroplast II."),
            At(0, 13, "Your Chloroplast II spell did not take hold. (Blocked by Regrowth.)"),
            At(0, 20, "You begin casting Burst of Strength.")).Snapshot();

        Assert.Equal(2, s.Blocked);
        var chloro = Assert.Single(s.SpellResists, r => r.Spell == "Chloroplast");
        Assert.Equal(2, chloro.Casts);      // ranks fold onto the base name
        Assert.Equal(2, chloro.Blocked);
        Assert.Equal(0, chloro.Resists);
        Assert.DoesNotContain(s.SpellResists, r => r.Spell == "Burst of Strength");
    }

    [Fact]
    public void ABlockedCastIsNotACastingFailure()
    {
        // The cast completed — mana spent, no interrupt line — so completion stays 100%.
        var s = Replay(
            At(0, 0, "You begin casting Chloroplast."),
            At(0, 3, "Your Chloroplast spell did not take hold. (Blocked by Regrowth.)")).Snapshot();

        Assert.Equal(1, s.CastsStarted);
        Assert.Equal(0, s.CastsInterrupted);
        Assert.Equal(1.0, s.CastCompletion!.Value, 3);
    }

    [Fact]
    public void ABlockedCharmCastCannotConfirmAPetFromALaterBlink()
    {
        // The block proves OUR charm never landed, so the blink two seconds later is a
        // bystander's — it must degrade to the provisional "Pet?" baseline (the same
        // state a blink with no cast at all produces), never a confirmed claim.
        var s = Replay(
            At(44, 6, "You begin casting Befriend Animal."),
            At(44, 9, "Your Befriend Animal spell did not take hold. (Blocked by Alluring Whispers.)"),
            At(44, 11, "a giant spider blinks."),
            At(44, 13, "A giant spider hits orc pawn for 14 points of damage.")).Snapshot();

        Assert.DoesNotContain(s.DamageBySource, d => d.Name == "Pet (Giant spider)");
        Assert.Single(s.DamageBySource, d => d.Name == "Pet? (Giant spider)");
    }

    [Fact]
    public void BlockedPairsLandInTheAttachedLedger()
    {
        var path = TempStorePath();
        var store = new StackingLedgerStore(path);
        var stats = new SessionStats
        {
            CharacterName = "Douglas", ServerName = "qeynos", StackingStore = store,
        };
        foreach (var line in new[]
        {
            At(0, 0, "You begin casting Chloroplast."),
            At(0, 3, "Your Chloroplast spell did not take hold. (Blocked by Regrowth.)"),
            At(0, 10, "You begin casting Chloroplast."),
            At(0, 13, "Your Chloroplast spell did not take hold. (Blocked by Regrowth.)"),
            // Blocker-less: counts as an outcome, teaches the ledger nothing.
            At(0, 20, "Your Chloroplast spell did not take hold."),
        })
            stats.Apply(LogParser.Parse(line)!);

        var pair = Assert.Single(store.BlockersFor(stats.LedgerCharacterKey, "Chloroplast"));
        Assert.Equal("Regrowth", pair.BlockedBy);
        Assert.Equal(2, pair.Count);
    }

    // ---- BuffTracker ----

    [Fact]
    public void ABlockedCastOpensNoBuffState()
    {
        // Landing lines are the only thing that opens state; the block means the
        // landing never comes.
        var t = new BuffTracker();
        t.Apply(Ev(0, "You begin casting Armor of Faith."));
        t.Apply(Ev(3, "Your Armor of Faith spell did not take hold. (Blocked by Resolution.)"));

        Assert.Empty(t.Snapshot(T0.AddSeconds(4)));
    }

    [Fact]
    public void ABlockedCastCannotClaimALaterUnrelatedLanding()
    {
        // Someone else's Armor of Faith lands on us seconds after ours was blocked:
        // without the disarm, our dead cast would resolve the landing to "You" with an
        // exact countdown. It must stay the unexplained-landing case instead.
        var t = new BuffTracker();
        t.Apply(Ev(0, "You begin casting Armor of Faith."));
        t.Apply(Ev(3, "Your Armor of Faith spell did not take hold. (Blocked by Resolution.)"));
        t.Apply(Ev(5, "You feel the favor of the gods upon you."));

        var b = Assert.Single(t.Snapshot(T0.AddSeconds(6)));
        Assert.Equal("", b.Caster);
        Assert.True(b.Estimated);
    }

    // ---- StackingLedgerStore ----

    [Fact]
    public void LedgerRoundTripsThroughDisk()
    {
        var path = TempStorePath();
        var store = new StackingLedgerStore(path);
        store.Record("douglas_qeynos", "Chloroplast", "Regrowth", T0);
        store.Record("douglas_qeynos", "Chloroplast", "Regrowth", T0.AddMinutes(5));
        store.Record("douglas_qeynos", "Chloroplast II", "Torpor", T0.AddMinutes(6));
        store.Flush();

        var reloaded = new StackingLedgerStore(path);
        var blockers = reloaded.BlockersFor("douglas_qeynos", "Chloroplast");
        Assert.Equal(2, blockers.Count);
        Assert.Equal(("Regrowth", 2), blockers[0]);   // most-observed first
        Assert.Equal(("Torpor", 1), blockers[1]);     // rank folded onto the base name

        var entry = reloaded.For("douglas_qeynos")["Chloroplast"]["Regrowth"];
        Assert.Equal(T0, entry.FirstSeen);
        Assert.Equal(T0.AddMinutes(5), entry.LastSeen);
    }

    [Fact]
    public void LedgerReplayReOffersBounceOffTheHighWaterMark()
    {
        // The startup full-log replay re-offers every event it has already recorded;
        // the per-pair time gate must swallow them without inflating counts.
        var path = TempStorePath();
        var store = new StackingLedgerStore(path);
        store.Record("douglas_qeynos", "Chloroplast", "Regrowth", T0);
        store.Record("douglas_qeynos", "Chloroplast", "Regrowth", T0);              // same stamp
        store.Record("douglas_qeynos", "Chloroplast", "Regrowth", T0.AddMinutes(-5)); // older

        var pair = Assert.Single(store.BlockersFor("douglas_qeynos", "Chloroplast"));
        Assert.Equal(1, pair.Count);
    }

    [Fact]
    public void LedgerKeepsCharactersApart()
    {
        var store = new StackingLedgerStore(TempStorePath());
        store.Record("douglas_qeynos", "Chloroplast", "Regrowth", T0);

        Assert.Empty(store.BlockersFor("dranak_legends", "Chloroplast"));
        Assert.Empty(store.For("dranak_legends"));
    }
}
