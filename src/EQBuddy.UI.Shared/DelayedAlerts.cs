using EQBuddy.Core;

namespace EQBuddy.UI.Shared;

/// <summary>One alert waiting for its moment. Opaque to callers except for
/// <see cref="DueAt"/>, which tells the host when to set its timer for. Carries the
/// generations it was created in so cancellation can invalidate it without hunting down the
/// host's timer — see <see cref="DelayedAlerts"/>.</summary>
public sealed record PendingAlert(
    int Generation, int CombatGeneration, bool IsCombatCue,
    TrackedRule Rule, string RuleName, string Label, DateTime DueAt);

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

    /// <summary>Bumped by <see cref="CancelCombatCues"/> as well as <see cref="CancelAll"/>,
    /// so dying can drop the "cast now" cues without touching a respawn timer.</summary>
    private int _combatGeneration;

    private readonly Dictionary<string, int> _inFlight = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per rule, because a chain announces repeatedly and each call is its own cue.
    /// A cap all the same: a pattern matching something chatty shouldn't be able to queue
    /// hundreds of sounds that then all arrive.</summary>
    public const int MaxInFlightPerRule = 8;

    public int InFlight => _inFlight.Values.Sum();

    /// <summary>When each rule's next cue is due, for showing a live countdown. Keyed by rule
    /// name; a rule with several cues in flight reports the soonest, since that's the one
    /// about to matter. Cancelled generations are dropped — a countdown that keeps ticking
    /// after you died would be worse than none.</summary>
    private readonly List<PendingAlert> _pending = [];

    public IReadOnlyDictionary<string, DateTime> NextDueByRule(DateTime now)
    {
        _pending.RemoveAll(a => a.DueAt <= now || !IsLive(a));
        var due = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in _pending)
            if (!due.TryGetValue(a.RuleName, out var soonest) || a.DueAt < soonest)
                due[a.RuleName] = a.DueAt;
        return due;
    }

    private bool IsLive(PendingAlert a) =>
        a.Generation == _generation && (!a.IsCombatCue || a.CombatGeneration == _combatGeneration);

    /// <summary>Claim a slot for a delayed alert. Null when this rule already has
    /// <see cref="MaxInFlightPerRule"/> waiting, in which case the match is simply not
    /// cued — the count still moved, and the alert the user cares about is the one already
    /// on its way.</summary>
    public PendingAlert? Schedule(TrackedRule rule, string ruleName, string label, DateTime now)
    {
        var count = _inFlight.TryGetValue(ruleName, out var c) ? c : 0;
        if (count >= MaxInFlightPerRule) return null;
        _inFlight[ruleName] = count + 1;
        var pending = new PendingAlert(_generation, _combatGeneration, rule.IsCombatCue,
            rule, ruleName, label, now.AddSeconds(rule.AlertDelaySeconds));
        _pending.Add(pending);
        return pending;
    }

    /// <summary>Called when a cue's timer goes off. False means it was cancelled while
    /// waiting and must stay silent. Releases the slot either way.</summary>
    public bool Claim(PendingAlert alert)
    {
        if (_inFlight.TryGetValue(alert.RuleName, out var c))
            _inFlight[alert.RuleName] = Math.Max(0, c - 1);
        if (alert.Generation != _generation) return false;
        return !alert.IsCombatCue || alert.CombatGeneration == _combatGeneration;
    }

    /// <summary>Abandon everything in flight — the session rolled over on an idle gap, or the
    /// widget followed a different character, so nothing pending belongs to the situation any
    /// more. Slots are released immediately so the new situation starts with a clean budget,
    /// and the generation bump silences the timers still out there.</summary>
    public void CancelAll()
    {
        _generation++;
        _combatGeneration++;
        _inFlight.Clear();
        _pending.Clear();
    }

    /// <summary>
    /// Abandon only the cues tied to the fight you were in (<see cref="TrackedRule.CombatCueSeconds"/>
    /// or shorter) — what dying should do. A "cast now" cue landing on your corpse is noise;
    /// a respawn timer is not, since dying has no bearing on when a mob pops.
    ///
    /// Slots stay claimed until each timer fires and calls <see cref="Claim"/>, which
    /// releases them; there's nothing to hunt down.
    /// </summary>
    public void CancelCombatCues() => _combatGeneration++;
}
