namespace EQBuddy.Core;

/// <summary>Session-seen buff evidence, snapshotted from <see cref="BuffTracker.SetSights"/>.
/// All three sets hold rank-folded spell names, compared case-insensitively.</summary>
public sealed record BuffSetSights(
    IReadOnlySet<string> Landings, IReadOnlySet<string> Fades, IReadOnlySet<string> OwnCasts);

/// <summary>One buff-set entry's honesty state. Missing and NotSeen are deliberately
/// different claims and are never merged — see <see cref="BuffSetEvaluator"/>.</summary>
public enum BuffSetStatus
{
    /// <summary>Seen landing this session, still counting down outside the warn window.</summary>
    Active,
    /// <summary>Still up, but inside the Buffs card's warn threshold — rebuff soon.</summary>
    Expiring,
    /// <summary>Known gone: seen fading this session, or its countdown ran out.</summary>
    Missing,
    /// <summary>No landing line observed this session. The buff may well be up from
    /// before the log was watched — EQBuddy cannot know, and says so instead of
    /// claiming it's missing.</summary>
    NotSeen,
}

/// <summary>Estimated carries the matched state's flag (wiki-base duration, not yet
/// learned) so the loss log can say "expired (est)" — an estimate running out is a
/// weaker claim than a learned duration doing so (#120 stage 3).</summary>
public sealed record BuffSetEntryState(
    string Spell, BuffSetStatus Status, double? RemainingSeconds, bool Estimated = false);

/// <summary>
/// Evaluates a player-defined buff set against what the session's log actually showed
/// (discussion #120, Frankthetankk — building on skulldugger's #64 ask). The set is
/// player-built only, per character; nothing is ever auto-added to it.
///
/// Identity: a set entry matches a <see cref="BuffState"/> when its rank-folded name
/// (<see cref="SpellCatalog.BaseName"/>) equals the state's Label or any of its
/// Candidates — the same identity the tracker itself uses. So "Temperance" covers a
/// "Temperance II" landing, and an unresolved landing counts for every spell it might
/// have been (both here and in the seen-landing evidence).
///
/// Honesty: Missing is a claim — we saw the buff fade this session, or its countdown
/// ran out. NotSeen is the absence of one — no landing line this session, so the buff
/// may be up from before EQBuddy was watching. An expired countdown reads Missing even
/// though the card's chip lingers at 0:00 (estimates are floors): the chip hedges, the
/// set line answers "should I rebuff?", and past the timer the answer is yes.
/// </summary>
public static class BuffSetEvaluator
{
    /// <summary>Per-entry states, in set order, duplicate entries rank-folded away.
    /// An empty set yields an empty list — no set, no line, no claims.</summary>
    public static List<BuffSetEntryState> Evaluate(
        IEnumerable<string> set,
        IReadOnlyList<BuffState> active,
        IReadOnlySet<string> seenLandings,
        IReadOnlySet<string> seenFades,
        DateTime now,
        double warnSeconds)
    {
        // Local case-insensitive copies: callers (tests included) may hand in
        // default-comparer sets, and identity here is case-insensitive everywhere.
        var landings = new HashSet<string>(seenLandings, StringComparer.OrdinalIgnoreCase);
        var fades = new HashSet<string>(seenFades, StringComparer.OrdinalIgnoreCase);
        var folded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<BuffSetEntryState>();
        foreach (var raw in set)
        {
            var entry = SpellCatalog.BaseName(raw);
            if (entry.Length == 0 || !folded.Add(entry)) continue;
            var match = active.FirstOrDefault(b => Matches(entry, b));
            if (match is not null)
            {
                var remaining = match.RemainingSeconds(now);
                result.Add(new BuffSetEntryState(raw, remaining switch
                {
                    null => BuffSetStatus.Active,        // no countdown known — it's up
                    <= 0 => BuffSetStatus.Missing,       // timer ran out (linger is a hedge, this is the claim)
                    var r when r <= warnSeconds => BuffSetStatus.Expiring,
                    _ => BuffSetStatus.Active,
                }, remaining, match.Estimated));
            }
            else if (landings.Contains(entry) || fades.Contains(entry))
                result.Add(new BuffSetEntryState(raw, BuffSetStatus.Missing, null));
            else
                result.Add(new BuffSetEntryState(raw, BuffSetStatus.NotSeen, null));
        }
        return result;
    }

    private static bool Matches(string entryBase, BuffState b) =>
        entryBase.Equals(SpellCatalog.BaseName(b.Label), StringComparison.OrdinalIgnoreCase)
        || b.Candidates.Any(c => entryBase.Equals(SpellCatalog.BaseName(c), StringComparison.OrdinalIgnoreCase));
}
