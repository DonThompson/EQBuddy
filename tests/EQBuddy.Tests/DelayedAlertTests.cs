using EQBuddy.Core;
using EQBuddy.UI.Shared;

namespace EQBuddy.Tests;

/// <summary>
/// Delayed alerts (discussion #22): a rule can hold its alert back N seconds so it lands as
/// a cue — "cast now" 2.5 s after a complete-heal call, or "recast" 25 s into a 30 s mez.
///
/// The timers themselves belong to each UI; what's tested here is the policy neither UI
/// should be reinventing: how many cues can be in flight, and when a cue must stay silent
/// because the situation it belonged to is gone.
/// </summary>
public class DelayedAlertTests
{
    private static readonly DateTime T0 = new(2026, 7, 30, 20, 0, 0);

    private static TrackedRule Rule(double delay = 2.5) =>
        new() { Name = "chain", Pattern = "CH -->", Kind = WatchKind.Text, AlertDelaySeconds = delay };

    [Fact]
    public void ScheduledAlertIsDueAfterTheDelay()
    {
        var pending = new DelayedAlerts().Schedule(Rule(2.5), "chain", "CH --> Tank", T0);

        Assert.NotNull(pending);
        Assert.Equal(T0.AddSeconds(2.5), pending!.DueAt);
    }

    [Fact]
    public void AClaimedAlertFires()
    {
        var alerts = new DelayedAlerts();
        var pending = alerts.Schedule(Rule(), "chain", "CH --> Tank", T0)!;

        Assert.True(alerts.Claim(pending));
        Assert.Equal(0, alerts.InFlight);
    }

    /// <summary>A chain announces repeatedly, so overlapping cues are the normal case rather
    /// than a mistake — each call gets its own.</summary>
    [Fact]
    public void SeveralCuesCanBeInFlightAtOnce()
    {
        var alerts = new DelayedAlerts();
        var rule = Rule();

        for (var i = 0; i < 3; i++)
            Assert.NotNull(alerts.Schedule(rule, "chain", $"call {i}", T0.AddSeconds(i)));

        Assert.Equal(3, alerts.InFlight);
    }

    /// <summary>But not without limit: a pattern matching something chatty must not be able
    /// to queue a wall of sounds that then all arrive.</summary>
    [Fact]
    public void InFlightCuesAreCappedPerRule()
    {
        var alerts = new DelayedAlerts();
        var rule = Rule();

        for (var i = 0; i < DelayedAlerts.MaxInFlightPerRule; i++)
            Assert.NotNull(alerts.Schedule(rule, "chain", "call", T0));

        Assert.Null(alerts.Schedule(rule, "chain", "one too many", T0));
        Assert.Equal(DelayedAlerts.MaxInFlightPerRule, alerts.InFlight);
    }

    /// <summary>The cap is per rule — a busy rule mustn't starve a quiet one.</summary>
    [Fact]
    public void TheCapIsPerRuleNotGlobal()
    {
        var alerts = new DelayedAlerts();
        var chain = Rule();
        for (var i = 0; i < DelayedAlerts.MaxInFlightPerRule; i++)
            alerts.Schedule(chain, "chain", "call", T0);

        Assert.NotNull(alerts.Schedule(Rule(25), "mez", "recast", T0));
    }

    /// <summary>Two rules sharing a display name are still two rules. The budget is keyed
    /// by the rule's id, so filling one "Asaka" rule's slots must not silence the other —
    /// same-named rules used to share one cap, one cooldown and one countdown purely
    /// because everything was keyed by the name string.</summary>
    [Fact]
    public void SameNamedRulesHaveSeparateBudgets()
    {
        var alerts = new DelayedAlerts();
        var first = Rule();
        var second = Rule();   // same Name/Pattern, distinct Id
        for (var i = 0; i < DelayedAlerts.MaxInFlightPerRule; i++)
            alerts.Schedule(first, "chain", "call", T0);

        Assert.Null(alerts.Schedule(first, "chain", "capped", T0));
        Assert.NotNull(alerts.Schedule(second, "chain", "independent", T0));
    }

    /// <summary>Countdowns are reported per id too: each same-named rule shows its own
    /// next-due time rather than both collapsing onto whichever cue is soonest.</summary>
    [Fact]
    public void SameNamedRulesCountDownIndependently()
    {
        var alerts = new DelayedAlerts();
        var quick = Rule(5);
        var slow = Rule(480);
        alerts.Schedule(quick, "Asaka", "spotted", T0);
        alerts.Schedule(slow, "Asaka", "respawn", T0);

        var due = alerts.NextDueByRule(T0);
        Assert.Equal(T0.AddSeconds(5), due[quick.Id]);
        Assert.Equal(T0.AddSeconds(480), due[slow.Id]);
    }

    /// <summary>Firing a cue frees its slot, so a long session of a repeating chain doesn't
    /// silently reach the cap and stay there.</summary>
    [Fact]
    public void ClaimingReleasesTheSlot()
    {
        var alerts = new DelayedAlerts();
        var rule = Rule();
        var all = new List<PendingAlert>();
        for (var i = 0; i < DelayedAlerts.MaxInFlightPerRule; i++)
            all.Add(alerts.Schedule(rule, "chain", "call", T0)!);

        Assert.Null(alerts.Schedule(rule, "chain", "capped", T0));
        alerts.Claim(all[0]);
        Assert.NotNull(alerts.Schedule(rule, "chain", "room again", T0));
    }

    /// <summary>The cancellation case that matters: you died, or the session rolled over, or
    /// the widget followed another character. A cue already counting down must not fire —
    /// being told to cast something while dead is worse than silence.</summary>
    [Fact]
    public void CancelledCuesDoNotFire()
    {
        var alerts = new DelayedAlerts();
        var pending = alerts.Schedule(Rule(), "chain", "CH --> Tank", T0)!;

        alerts.CancelAll();

        Assert.False(alerts.Claim(pending));
        Assert.Equal(0, alerts.InFlight);
    }

    /// <summary>Cancelling frees the budget immediately, so the new situation starts clean
    /// rather than inheriting slots held by timers that will never fire.</summary>
    [Fact]
    public void CancellingFreesTheBudgetForWhatComesNext()
    {
        var alerts = new DelayedAlerts();
        var rule = Rule();
        for (var i = 0; i < DelayedAlerts.MaxInFlightPerRule; i++)
            alerts.Schedule(rule, "chain", "call", T0);

        alerts.CancelAll();

        Assert.Equal(0, alerts.InFlight);
        Assert.NotNull(alerts.Schedule(rule, "chain", "after the wipe", T0));
    }

    /// <summary>Cues scheduled after a cancellation are unaffected by it.</summary>
    [Fact]
    public void CuesScheduledAfterACancellationStillFire()
    {
        var alerts = new DelayedAlerts();
        alerts.Schedule(Rule(), "chain", "old", T0);
        alerts.CancelAll();

        var fresh = alerts.Schedule(Rule(), "chain", "new", T0.AddSeconds(30))!;

        Assert.True(alerts.Claim(fresh));
    }

    // ---- what dying cancels ----

    /// <summary>Dying drops the cue that says "cast now" — landing it on your corpse is
    /// noise.</summary>
    [Fact]
    public void DyingCancelsCombatCues()
    {
        var alerts = new DelayedAlerts();
        var pending = alerts.Schedule(Rule(2.5), "chain", "CH --> Tank", T0)!;

        alerts.CancelCombatCues();

        Assert.False(alerts.Claim(pending));
    }

    /// <summary>But not a respawn timer. Dying has no bearing on when a mob pops, and losing
    /// an eight-minute timer because you took a dirt nap is worse than useless — testers use
    /// these to camp.</summary>
    [Fact]
    public void DyingLeavesLongTimersAlone()
    {
        var alerts = new DelayedAlerts();
        var respawn = alerts.Schedule(Rule(8 * 60), "spawn", "PH down", T0)!;

        alerts.CancelCombatCues();

        Assert.True(alerts.Claim(respawn));
    }

    /// <summary>Ending the session or switching character drops everything — a respawn timer
    /// from the camp you left isn't yours any more.</summary>
    [Fact]
    public void CancelAllTakesLongTimersToo()
    {
        var alerts = new DelayedAlerts();
        var respawn = alerts.Schedule(Rule(8 * 60), "spawn", "PH down", T0)!;

        alerts.CancelAll();

        Assert.False(alerts.Claim(respawn));
    }

    [Theory]
    [InlineData(2.5, true)]
    [InlineData(60, true)]      // on the boundary, still a fight cue
    [InlineData(61, false)]
    [InlineData(480, false)]
    [InlineData(0, false)]      // no delay at all is not a cue
    public void CombatCueBoundary(double seconds, bool expected) =>
        Assert.Equal(expected, new TrackedRule { AlertDelaySeconds = seconds }.IsCombatCue);

    // ---- the setting itself ----

    [Fact]
    public void DelayDefaultsToImmediate() =>
        Assert.Equal(0, new TrackedRule().AlertDelaySeconds);

    /// <summary>Clamped rather than rejected, so a hand-edited settings.json can't park an
    /// alert hours out or schedule one in the past.</summary>
    [Theory]
    [InlineData(2.5, 2.5)]
    [InlineData(0, 0)]
    [InlineData(-5, 0)]
    [InlineData(9999, TrackedRule.MaxAlertDelaySeconds)]
    public void DelayIsClamped(double set, double expected) =>
        Assert.Equal(expected, new TrackedRule { AlertDelaySeconds = set }.AlertDelaySeconds);

    /// <summary>A 30 s mez wants a warning at 25 s, so the range has to reach past the 10 s
    /// originally suggested.</summary>
    [Fact]
    public void TheRangeCoversAMezRecastWarning() =>
        Assert.Equal(25, new TrackedRule { AlertDelaySeconds = 25 }.AlertDelaySeconds);

    /// <summary>The cap was two minutes, sized for combat cues, and silently clamped a
    /// tester's eight-minute respawn timer down to two. Spawn timers are the real ceiling.</summary>
    [Fact]
    public void TheRangeCoversARespawnTimer()
    {
        Assert.Equal(480, new TrackedRule { AlertDelaySeconds = 480 }.AlertDelaySeconds);
        Assert.Equal(30 * 60, new TrackedRule { AlertDelaySeconds = 30 * 60 }.AlertDelaySeconds);
    }

    // ---- reading and writing the delay box ----

    [Theory]
    [InlineData("2.5", 2.5)]
    [InlineData("25", 25)]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("8m", 480)]
    [InlineData("8 m", 480)]
    [InlineData("8min", 480)]
    [InlineData("8 minutes", 480)]
    [InlineData("8M", 480)]
    [InlineData("30s", 30)]
    [InlineData("1:30", 90)]
    [InlineData("10:00", 600)]
    [InlineData("nonsense", 0)]
    public void DelayTextParses(string input, double expected) =>
        Assert.Equal(expected, DelayText.Parse(input));

    /// <summary>Whole minutes come back as minutes, so an "8m" respawn rule still reads "8m"
    /// next time it's opened rather than "480".</summary>
    [Theory]
    [InlineData(0, "")]
    [InlineData(2.5, "2.5")]
    [InlineData(25, "25")]
    [InlineData(90, "90")]
    [InlineData(480, "8m")]
    [InlineData(1800, "30m")]
    public void DelayTextFormats(double seconds, string expected) =>
        Assert.Equal(expected, DelayText.Format(seconds));

    /// <summary>What the user types survives a round trip through the box.</summary>
    [Theory]
    [InlineData("8m")]
    [InlineData("2.5")]
    [InlineData("25")]
    [InlineData("30m")]
    public void DelayTextRoundTrips(string typed) =>
        Assert.Equal(DelayText.Parse(typed), DelayText.Parse(DelayText.Format(DelayText.Parse(typed))));
}
