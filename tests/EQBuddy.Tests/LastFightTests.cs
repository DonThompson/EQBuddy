using EQBuddy.Core;

namespace EQBuddy.Tests;

/// <summary>
/// The "Last fight" block above the session totals on Combat and Healing. Mid-fight, a
/// session average tells you nothing about whether the pull in front of you is going badly.
/// </summary>
public class LastFightTests
{
    private static string At(int mm, int ss, string msg) =>
        $"[Sat Jul 18 15:{mm:D2}:{ss:D2} 2026] {msg}";

    private static SessionStats Replay(params string[] lines)
    {
        var stats = new SessionStats { CharacterName = "Kaybek" };
        foreach (var line in lines)
            if (LogParser.Parse(line) is { } evt) stats.Apply(evt);
        return stats;
    }

    [Fact]
    public void NoFightYetMeansNothingToShow() =>
        Assert.Null(Replay(At(0, 0, "You gain party experience! (10%)")).Snapshot().LastFight);

    /// <summary>While you're swinging, the fight in progress is the one that matters — even
    /// though an older fight has already finished.</summary>
    [Fact]
    public void TheFightInProgressWins()
    {
        var s = Replay(
            At(0, 0, "You slash orc pawn for 10 points of damage."),
            At(0, 5, "You have slain orc pawn!"),
            At(0, 20, "You slash orc centurion for 30 points of damage."),
            At(0, 30, "You slash orc centurion for 20 points of damage.")).Snapshot();

        var f = s.LastFight!;
        Assert.True(f.InProgress);
        Assert.Equal("Orc centurion", f.Name);
        Assert.Equal(50, f.DamageOut);
    }

    [Fact]
    public void OtherwiseTheLastFinishedFight()
    {
        var s = Replay(
            At(0, 0, "You slash orc pawn for 10 points of damage."),
            At(0, 4, "Orc pawn hits YOU for 3 points of damage."),
            At(0, 5, "You have slain orc pawn!")).Snapshot();

        var f = s.LastFight!;
        Assert.False(f.InProgress);
        Assert.Equal("Orc pawn", f.Name);
        Assert.Equal(10, f.DamageOut);
        Assert.Equal(3, f.DamageIn);
        Assert.Equal("Killed", f.Outcome);
    }

    /// <summary>Heals name a target, not a creature, so the only honest link to a fight is
    /// "whatever you were fighting at the time".</summary>
    [Fact]
    public void HealingDuringAFightIsCreditedToIt()
    {
        var s = Replay(
            At(0, 0, "You slash orc pawn for 10 points of damage."),
            At(0, 2, "You healed Kaybek for 40 hit points by Light Healing."),
            At(0, 5, "You have slain orc pawn!")).Snapshot();

        Assert.Equal(40, s.LastFight!.Healed);
    }

    /// <summary>Heals cast between pulls belong to no fight — they still count for the
    /// session, but attributing them to the last corpse would be invention.</summary>
    [Fact]
    public void HealingBetweenFightsIsSessionOnly()
    {
        var s = Replay(
            At(0, 0, "You slash orc pawn for 10 points of damage."),
            At(0, 5, "You have slain orc pawn!"),
            At(0, 30, "You healed Kaybek for 40 hit points by Light Healing.")).Snapshot();

        Assert.Equal(0, s.LastFight!.Healed);
        Assert.Equal(40, s.HealingDone);
    }

    /// <summary>A fight nobody finished still gets shown, marked for what it is.</summary>
    [Fact]
    public void ATimedOutFightKeepsItsOutcome()
    {
        var s = Replay(
            At(0, 0, "You slash orc pawn for 10 points of damage."),
            At(5, 0, "You gain party experience! (1%)")).Snapshot();

        Assert.Equal("Timeout", s.LastFight!.Outcome);
    }

    /// <summary>With an add in play, the fight you're actually engaged with is the one you
    /// touched last.</summary>
    [Fact]
    public void TheMostRecentlyTouchedFightWins()
    {
        var s = Replay(
            At(0, 0, "You slash orc pawn for 10 points of damage."),
            At(0, 10, "You slash orc centurion for 5 points of damage.")).Snapshot();

        Assert.Equal("Orc centurion", s.LastFight!.Name);
    }
}
