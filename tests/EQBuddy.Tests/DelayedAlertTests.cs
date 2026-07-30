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
        for (var i = 0; i < DelayedAlerts.MaxInFlightPerRule; i++)
            alerts.Schedule(Rule(), "chain", "call", T0);

        Assert.NotNull(alerts.Schedule(Rule(25), "mez", "recast", T0));
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
}
