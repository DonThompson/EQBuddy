using EQBuddy.Core;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>
/// Buff countdowns: cast + cast-on-you correlation starts the timer, the fade line
/// ends it, natural fades teach real durations (ranks lengthen; the wiki base is a
/// floor), zoning keeps buffs and dying doesn't. Real log lines throughout.
/// </summary>
public class BuffTrackerTests
{
    private static readonly DateTime T0 = DateTime.Parse("2026-08-12T21:00:00");

    private static GameEvent Ev(int seconds, string message) =>
        LogParser.Parse($"[{T0.AddSeconds(seconds):ddd MMM d HH:mm:ss yyyy}] {message}")!;

    private static BuffTracker Replay(params GameEvent[] events)
    {
        var t = new BuffTracker();
        foreach (var e in events) t.Apply(e);
        return t;
    }

    [Fact]
    public void YourOwnCastPlusLandingStartsAnExactCountdown()
    {
        var t = Replay(
            Ev(0, "You begin casting Armor of Faith."),
            Ev(3, "You feel the favor of the gods upon you."));

        var b = Assert.Single(t.Snapshot(T0.AddSeconds(4)));
        Assert.Equal("Armor of Faith", b.Label);
        Assert.Equal("You", b.Caster);
        Assert.True(b.Estimated);   // wiki base until a natural fade teaches better
        Assert.Equal(3780 - 1, b.RemainingSeconds(T0.AddSeconds(4))!.Value, 0);
    }

    [Fact]
    public void SomeoneElsesBuffOnYouIsAttributedThroughTheirCastLine()
    {
        var t = Replay(
            Ev(0, "Sanctari begins casting Armor of Faith."),
            Ev(3, "You feel the favor of the gods upon you."));

        var b = Assert.Single(t.Snapshot(T0.AddSeconds(4)));
        Assert.Equal("Sanctari", b.Caster);
    }

    [Fact]
    public void AnUnexplainedLandingKeepsAllCandidatesAndSaysEst()
    {
        // A clicky or an out-of-range caster: nobody visibly cast anything.
        var t = Replay(Ev(0, "You feel the favor of the gods upon you."));

        var b = Assert.Single(t.Snapshot(T0.AddSeconds(1)));
        Assert.True(b.Estimated);
        Assert.Equal("", b.Caster);
    }

    [Fact]
    public void TheFadeLineClearsTheBuff()
    {
        // FadeMessages knows Armor of Faith's wear-off line.
        var fadeLine = FadeMessageCatalog.Default.FindBySpell("Armor of Faith");
        Assert.NotNull(fadeLine);   // the test's own premise, checked
        var t = Replay(
            Ev(0, "You begin casting Armor of Faith."),
            Ev(3, "You feel the favor of the gods upon you."),
            Ev(600, fadeLine!.Message));

        Assert.Empty(t.Snapshot(T0.AddSeconds(601)));
    }

    [Fact]
    public void ANaturalFadeTeachesTheRealDurationForNextTime()
    {
        var fadeLine = FadeMessageCatalog.Default.FindBySpell("Armor of Faith")!;
        var t = new BuffTracker();
        // Rank-lengthened: fades at 4200s, wiki says 3780. Learn 4200 (tick-floored).
        t.Apply(Ev(0, "You begin casting Armor of Faith."));
        t.Apply(Ev(2, "You feel the favor of the gods upon you."));
        t.Apply(Ev(2 + 4201, fadeLine.Message));

        Assert.Equal(4200, t.LearnedDurations["Armor of Faith"], 0);

        // The next cast counts down from the learned number, no longer estimated.
        t.Apply(Ev(5000, "You begin casting Armor of Faith."));
        t.Apply(Ev(5002, "You feel the favor of the gods upon you."));
        var b = Assert.Single(t.Snapshot(T0.AddSeconds(5003)));
        Assert.False(b.Estimated);
        Assert.Equal(4200 - 1, b.RemainingSeconds(T0.AddSeconds(5003))!.Value, 0);
    }

    [Fact]
    public void AnEarlyFadeIsADispelNotADuration()
    {
        var fadeLine = FadeMessageCatalog.Default.FindBySpell("Armor of Faith")!;
        var t = Replay(
            Ev(0, "You begin casting Armor of Faith."),
            Ev(2, "You feel the favor of the gods upon you."),
            Ev(300, fadeLine.Message));   // way short of 3780s — dispelled/clicked off

        Assert.Empty(t.Snapshot(T0.AddSeconds(301)));
        Assert.False(t.LearnedDurations.ContainsKey("Armor of Faith"));
    }

    [Fact]
    public void ZoningKeepsBuffsAndDyingClearsThem()
    {
        var zoned = Replay(
            Ev(0, "You begin casting Armor of Faith."),
            Ev(2, "You feel the favor of the gods upon you."),
            Ev(10, "You have entered The Plane of Sky."));
        Assert.Single(zoned.Snapshot(T0.AddSeconds(11)));

        var slain = Replay(
            Ev(0, "You begin casting Armor of Faith."),
            Ev(2, "You feel the favor of the gods upon you."),
            Ev(10, "You have been slain by Master Yael!"));
        Assert.Empty(slain.Snapshot(T0.AddSeconds(11)));
    }

    [Fact]
    public void RebuffingRefreshesInsteadOfDuplicating()
    {
        var t = Replay(
            Ev(0, "You begin casting Armor of Faith."),
            Ev(2, "You feel the favor of the gods upon you."),
            Ev(100, "You begin casting Armor of Faith."),
            Ev(102, "You feel the favor of the gods upon you."));

        var b = Assert.Single(t.Snapshot(T0.AddSeconds(103)));
        Assert.Equal(T0.AddSeconds(102), b.LandedAt);
    }
}
