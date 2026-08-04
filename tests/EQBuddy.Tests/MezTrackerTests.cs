using EQBuddy.Core;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>
/// The mez-target tracker: cast→landing correlation from ANY group member's log (the
/// landing line is bystander-visible and other players' casts log with spell and rank),
/// durations from the eqlwiki catalog, break-on-damage, and caster-side duration
/// learning from natural fades.
/// </summary>
public class MezTrackerTests
{
    private static readonly DateTime T0 = DateTime.Parse("2026-08-04T20:00:00");

    private static GameEvent Ev(int seconds, string message) =>
        LogParser.Parse($"[{T0.AddSeconds(seconds):ddd MMM d HH:mm:ss yyyy}] {message}")!;

    private static MezTracker Replay(params GameEvent[] events)
    {
        var t = new MezTracker();
        foreach (var e in events) t.Apply(e);
        return t;
    }

    [Fact]
    public void OwnMezCastPlusLandingStartsACountdown()
    {
        var t = Replay(
            Ev(0, "You begin casting Mesmerize."),
            Ev(2, "ice boned skeleton has been mesmerized."));

        var m = Assert.Single(t.Snapshot(T0.AddSeconds(3)));
        Assert.Equal("Ice boned skeleton", m.Target);
        Assert.Equal("You", m.Caster);
        Assert.Equal(23, m.RemainingSeconds(T0.AddSeconds(3))!.Value, 0);   // 24s catalog duration
    }

    [Fact]
    public void AGroupMembersMezIsTrackedFromABystanderLog()
    {
        // The whole point: Hugzee casts, THIS log belongs to someone else in the group.
        var t = Replay(
            Ev(0, "Hugzee begins casting Enthrall."),
            Ev(3, "an orc centurion has been enthralled."));

        var m = Assert.Single(t.Snapshot(T0.AddSeconds(4)));
        Assert.Equal("Hugzee", m.Caster);
        Assert.Equal("Enthrall", m.Spell);
        Assert.Equal(47, m.RemainingSeconds(T0.AddSeconds(4))!.Value, 0);   // 48s catalog duration
    }

    [Fact]
    public void ALandingNobodyVisiblyCastIsIgnored()
    {
        // Same trust rule as charm: an unexplained bystander-visible line claims nothing.
        var t = Replay(Ev(0, "a Teir`Dal rogue has been mesmerized."));
        Assert.Empty(t.Snapshot(T0.AddSeconds(1)));
    }

    [Fact]
    public void DamageWakesTheTargetAndClearsTheChip()
    {
        var t = Replay(
            Ev(0, "You begin casting Mesmerize."),
            Ev(2, "ice boned skeleton has been mesmerized."),
            Ev(5, "Twiddley slashes ice boned skeleton for 12 points of damage."));

        Assert.Empty(t.Snapshot(T0.AddSeconds(6)));
    }

    [Fact]
    public void AoeMezCoversEveryLandingFromOneCast()
    {
        var t = Replay(
            Ev(0, "You begin casting Mesmerization."),
            Ev(3, "an orc pawn has been mesmerized."),
            Ev(3, "an orc centurion has been mesmerized."));

        Assert.Equal(2, t.Snapshot(T0.AddSeconds(4)).Count);
    }

    [Fact]
    public void TheCasterLearnsTheRealDurationFromANaturalFade()
    {
        // Rank II lasts longer than the base 24s the catalog knows; the caster's own
        // worn-off line measures it, and the next cast uses the learned value.
        var t = Replay(
            Ev(0, "You begin casting Mesmerize II."),
            Ev(2, "an orc pawn has been mesmerized."),
            Ev(34, "Your Mesmerize II spell has worn off of an orc pawn."),   // 32s observed
            Ev(60, "You begin casting Mesmerize II."),
            Ev(62, "a gnoll has been mesmerized."));

        Assert.Equal(32, t.LearnedDurations["Mesmerize II"], 0);
        var m = Assert.Single(t.Snapshot(T0.AddSeconds(63)));
        Assert.Equal(31, m.RemainingSeconds(T0.AddSeconds(63))!.Value, 0);
    }

    [Fact]
    public void ExoticLandingLinesParse()
    {
        Assert.IsType<MezzedEvent>(Ev(0, "an orc pawn swoons in raptured bliss."));
        Assert.IsType<MezzedEvent>(Ev(0, "a gnoll begins to scream."));
        Assert.IsType<MezzedEvent>(Ev(0, "a gnoll's eyes glaze over."));
        Assert.IsType<MezzedEvent>(Ev(0, "an orc oracle has been mesmerized by the Glamour of Kintaz."));
        // And the cast line that isn't a landing stays a cast.
        Assert.IsType<OtherCastEvent>(Ev(0, "Shack begins casting Shield of Thistles IV."));
    }

    [Fact]
    public void ZoningClearsEverything()
    {
        var t = Replay(
            Ev(0, "You begin casting Entrance."),
            Ev(2, "an orc pawn has been entranced."),
            Ev(5, "You have entered Clan Crushbone."));

        Assert.Empty(t.Snapshot(T0.AddSeconds(6)));
    }

    [Fact]
    public void UnknownDurationChipsStillShowAndStillBreak()
    {
        // A mez spell missing from the catalog: chip appears with no countdown, and
        // damage still clears it. (Requires the spell to be cast-correlated — add the
        // name to MezSpells.json for it to track at all; this uses a catalog entry
        // with its duration nulled to simulate the pre-research state.)
        var t = new MezTracker([new MezSpellInfo { Name = "Mesmerize" }]);
        t.Apply(Ev(0, "You begin casting Mesmerize."));
        t.Apply(Ev(2, "an orc pawn has been mesmerized."));

        var m = Assert.Single(t.Snapshot(T0.AddSeconds(3)));
        Assert.Null(m.RemainingSeconds(T0.AddSeconds(3)));

        t.Apply(Ev(6, "Twiddley slashes an orc pawn for 5 points of damage."));
        Assert.Empty(t.Snapshot(T0.AddSeconds(7)));
    }
}
