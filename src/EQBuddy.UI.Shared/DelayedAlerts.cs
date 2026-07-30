using EQBuddy.Core;

namespace EQBuddy.UI.Shared;

/// <summary>One alert waiting for its moment. Opaque to callers except for
/// <see cref="DueAt"/>, which tells the host when to set its timer for.</summary>
public sealed record PendingAlert(
    int Generation, TrackedRule Rule, string RuleName, string Label, DateTime DueAt);

/// <summary>
/// Bookkeeping for alerts that fire some seconds after their match
/// (<see cref="TrackedRule.AlertDelaySeconds"/>) — the cap on how many can be in flight,
/// and cancelling the ones that stop making sense.
///
/// Deliberately owns no timer. Each host schedules with its own dispatcher timer, which
/// keeps the cue accurate to milliseconds rather than to the 1 s UI refresh; the part worth
/// testing is the policy, which lives here so both UIs share one copy of it and it can be
/// tested without a UI at all.
/// </summary>
public sealed class DelayedAlerts
{
    /// <summary>
    /// A cue that no longer belongs to the current situation must not fire: the session
    /// rolled over, the character changed, or you died and whatever you were being reminded
    /// to cast is moot. Rather than hunt down live timers, everything scheduled carries the
    /// generation it was created in — <see cref="CancelAll"/> moves the generation on and
    /// any timer that survives finds itself stale when it asks to fire.
    /// </summary>
    private int _generation;

    private readonly Dictionary<string, int> _inFlight = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per rule, because a chain announces repeatedly and each call is its own cue.
    /// A cap all the same: a pattern matching something chatty shouldn't be able to queue
    /// hundreds of sounds that then all arrive.</summary>
    public const int MaxInFlightPerRule = 8;

    public int InFlight => _inFlight.Values.Sum();

    /// <summary>Claim a slot for a delayed alert. Null when this rule already has
    /// <see cref="MaxInFlightPerRule"/> waiting, in which case the match is simply not
    /// cued — the count still moved, and the alert the user cares about is the one already
    /// on its way.</summary>
    public PendingAlert? Schedule(TrackedRule rule, string ruleName, string label, DateTime now)
    {
        var count = _inFlight.TryGetValue(ruleName, out var c) ? c : 0;
        if (count >= MaxInFlightPerRule) return null;
        _inFlight[ruleName] = count + 1;
        return new PendingAlert(_generation, rule, ruleName, label,
            now.AddSeconds(rule.AlertDelaySeconds));
    }

    /// <summary>Called when a cue's timer goes off. False means it was cancelled while
    /// waiting and must stay silent. Releases the slot either way.</summary>
    public bool Claim(PendingAlert alert)
    {
        if (_inFlight.TryGetValue(alert.RuleName, out var c))
            _inFlight[alert.RuleName] = Math.Max(0, c - 1);
        return alert.Generation == _generation;
    }

    /// <summary>Abandon everything in flight. Slots are released immediately so a fresh
    /// situation starts with a clean budget, and the generation bump silences the timers
    /// that are still out there.</summary>
    public void CancelAll()
    {
        _generation++;
        _inFlight.Clear();
    }
}
