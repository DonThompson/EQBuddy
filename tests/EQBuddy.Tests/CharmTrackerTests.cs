using EQBuddy.Core;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>
/// The charm state machine, reached directly (2026-08-18).
///
/// Every rule below was already covered through <see cref="SessionStats"/> by
/// <c>SpellTrackingTests</c>, and those tests stay — they are the integration level, and
/// they are what proved this extraction changed nothing. These are the other half: the
/// ones that could not be written before, because the only way into this code was a
/// switch statement inside a 2,500-line class. Six causes of one symptom were fixed in
/// here in three days; being able to ask it a question without building a session is the
/// point of the move.
/// </summary>
public class CharmTrackerTests
{
    private static DateTime At(int mm, int ss) => new(2026, 7, 18, 15, mm, ss);

    private static CharmTracker New(out List<string> confirmed)
    {
        var seen = new List<string>();
        confirmed = seen;
        return new CharmTracker(new SpellCatalog()) { PetConfirmedFirstTime = seen.Add };
    }

    /// <summary>Land a charm and have the pet prove it is ours — the shape every test
    /// below starts from, and the one an item clicky produces (no cast anywhere).</summary>
    private static CharmTracker Charmed(out List<string> confirmed, string pet = "a puma")
    {
        var charm = New(out confirmed);
        charm.OnCharmed(new CharmedEvent(At(0, 0), pet), pendingCast: null);
        charm.OnPetClaim(new PetClaimEvent(At(0, 1), pet), "Douglas");
        return charm;
    }

    // ---- the tracker owns no damage; it announces instead ----

    /// <summary>Its one reach back into the owner: provisional "Pet? (X)" rows become
    /// "Pet (X)" when a tell confirms. It fires ONCE — a second tell must not re-merge
    /// rows that have already moved.</summary>
    [Fact]
    public void ConfirmingAPetAnnouncesItExactlyOnce()
    {
        var charm = New(out var confirmed);
        charm.ConfirmPet("Puma");
        charm.ConfirmPet("Puma");
        Assert.Equal(["Puma"], confirmed);
    }

    // ---- a landing line is never proof on its own ----

    [Fact]
    public void ALandingWithNoCastClaimsNothingUntilThePetSpeaks()
    {
        var charm = New(out _);
        charm.OnCharmed(new CharmedEvent(At(0, 0), "a puma"), pendingCast: null);
        Assert.Null(charm.CharmedSince);
        Assert.Null(charm.PetName);

        charm.OnPetClaim(new PetClaimEvent(At(0, 3), "a puma"), "Douglas");
        Assert.Equal(At(0, 0), charm.CharmedSince);   // the LANDING's time, not the tell's
    }

    /// <summary>The guard that keeps the promotion safe: the tell has to name the
    /// creature the landing named, or a stranger's charm beside our own pet's attack
    /// order hands us their pet.</summary>
    [Fact]
    public void ATellAboutSomethingElseNeverPromotesALanding()
    {
        var charm = New(out _);
        charm.OnCharmed(new CharmedEvent(At(0, 0), "a puma"), null);
        charm.OnPetClaim(new PetClaimEvent(At(0, 3), "a wolf"), "Douglas");
        Assert.Null(charm.CharmedSince);
    }

    /// <summary>A pet tell naming somebody ELSE as leader is the one line that disproves
    /// ownership (#177, chrstahl). It drops the claim rather than merely not helping.</summary>
    [Fact]
    public void ATellNamingAnotherLeaderDisprovesOwnership()
    {
        var charm = Charmed(out _);
        Assert.NotNull(charm.CharmedSince);

        charm.OnPetClaim(new PetClaimEvent(At(0, 30), "a puma", Leader: "Someoneelse"), "Douglas");
        Assert.Null(charm.PetName);
        Assert.Null(charm.CharmedSince);
    }

    // ---- an incoming hit asks three questions ----

    [Fact]
    public void AHitInsideTheSettleWindowIsTheMobsOwnInFlightSwing()
    {
        var charm = Charmed(out _);
        Assert.False(charm.OnIncomingHit(new DamageTakenEvent(At(0, 2), "a puma", 100, Melee: true)));
        Assert.NotNull(charm.CharmedSince);
    }

    [Fact]
    public void ADotTickIsNotADecisionToAttack()
    {
        var charm = Charmed(out _);
        var tick = new DamageTakenEvent(At(0, 30), "a puma", 12, Melee: false, OverTime: true);
        Assert.False(charm.OnIncomingHit(tick));
        Assert.NotNull(charm.CharmedSince);
    }

    [Fact]
    public void AHeldPetDidNotStartThisAttackSoTheAttackerIsSomebodyElse()
    {
        var charm = Charmed(out _);
        charm.OnPetHold(new PetHoldEvent(At(0, 5), "a puma", Holding: true));

        Assert.False(charm.OnIncomingHit(new DamageTakenEvent(At(0, 40), "a puma", 100, true)));
        Assert.NotNull(charm.CharmedSince);
    }

    [Fact]
    public void ProofOfTwoCreaturesSharingTheNameMakesAnAttackerAmbiguous()
    {
        var charm = Charmed(out _);
        charm.NoteSameNameProof(At(0, 10));

        Assert.False(charm.OnIncomingHit(new DamageTakenEvent(At(0, 20), "a puma", 100, true)));
        Assert.NotNull(charm.CharmedSince);
    }

    [Fact]
    public void ReleasingTheHoldRestoresTheOrdinaryBreakRule()
    {
        var charm = Charmed(out _);
        charm.OnPetHold(new PetHoldEvent(At(0, 5), "a puma", Holding: true));
        charm.OnPetHold(new PetHoldEvent(At(0, 20), "a puma", Holding: false));

        Assert.True(charm.OnIncomingHit(new DamageTakenEvent(At(0, 40), "a puma", 100, true)));
        Assert.Null(charm.CharmedSince);
        Assert.Null(charm.PetName);
    }

    /// <summary>Every guard above has to stay narrow enough that a pet which really did
    /// round on you stops being credited.</summary>
    [Fact]
    public void APetThatGenuinelyTurnsStillBreaksTheClaim()
    {
        var charm = Charmed(out _);
        Assert.True(charm.OnIncomingHit(new DamageTakenEvent(At(1, 0), "a puma", 100, true)));
        Assert.Null(charm.CharmedSince);
    }

    // ---- the hold ledger ----

    [Fact]
    public void ABreakRecordsHowLongItHeldAndTheFadeLabelCarriesIt()
    {
        var charm = Charmed(out _);
        var before = charm.HoldRevision;

        charm.OnIncomingHit(new DamageTakenEvent(At(4, 32), "a puma", 100, true));

        Assert.True(charm.HoldRevision > before);   // the tracked scan must rebuild
        Assert.Contains("held 4:32",
            charm.FadeLabel(new SpellWornOffEvent(At(4, 32), "Allure", "a puma")));
    }

    /// <summary>The fade prints a few seconds after the attack that actually broke it, so
    /// an exact-time miss falls back within the skew window (#135, v1.76.0).</summary>
    [Fact]
    public void AFadeArrivingLateStillFindsItsHold()
    {
        var charm = Charmed(out _);
        charm.OnIncomingHit(new DamageTakenEvent(At(4, 32), "a puma", 100, true));

        Assert.Contains("held 4:32",
            charm.FadeLabel(new SpellWornOffEvent(At(4, 36), "Allure", "a puma")));
    }

    /// <summary>A fade far past the skew window is a different event entirely, and must
    /// not borrow an older break's duration.</summary>
    [Fact]
    public void AFadeOutsideTheSkewWindowBorrowsNothing()
    {
        var charm = Charmed(out _);
        charm.OnIncomingHit(new DamageTakenEvent(At(4, 32), "a puma", 100, true));

        Assert.DoesNotContain("held",
            charm.FadeLabel(new SpellWornOffEvent(At(5, 30), "Allure", "a puma")));
    }

    [Fact]
    public void ResetForgetsEverything()
    {
        var charm = Charmed(out _);
        charm.Reset();

        Assert.Null(charm.PetName);
        Assert.Null(charm.CharmedSince);
        Assert.Equal("Pet", charm.SourceLabel);
    }

    // ---- the damage-source label ----

    [Fact]
    public void TheLabelSaysWhetherThePetIsProvenOrOnlySuspected()
    {
        var charm = New(out _);
        Assert.Equal("Pet", charm.SourceLabel);

        charm.OnBlink(new PetBlinkEvent(At(0, 0), "a puma"), pendingCast: null);
        Assert.Equal("Pet? (Puma)", charm.SourceLabel);   // blink only — might be a stranger's

        charm.OnPetClaim(new PetClaimEvent(At(0, 3), "a puma"), "Douglas");
        Assert.Equal("Pet (Puma)", charm.SourceLabel);
    }

    /// <summary>A moan with no cast of ours in flight is ambient flavour — the necro
    /// charms' landing line is plausible enough as scenery that it never claims alone.</summary>
    [Fact]
    public void AWeakBlinkWithNoCastIsAmbientFlavour()
    {
        var charm = New(out _);
        var outcome = charm.OnBlink(new PetBlinkEvent(At(0, 0), "a puma", Weak: true), null);

        Assert.True(outcome.Ambient);
        Assert.Null(charm.PetName);
    }

    /// <summary>"Your pet" needs no prior identification — no other creature answers to
    /// it, and it covers a summoned pet that never got an attack order.</summary>
    [Fact]
    public void TheGenericPetNameIsAlwaysOurs()
    {
        var charm = New(out _);
        Assert.True(charm.IsPet("Your pet"));
        Assert.False(charm.IsPet("a puma"));
    }
}
