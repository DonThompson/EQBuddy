namespace EQBuddy.Core;

/// <summary>A new-buff-unlock suggestion (#120 stage 3, Frankthetankk): a spell the
/// ding just made available, buff-shaped, not covered by the assembled set. Class is
/// the bucket ✓ adds to — the (first) active class that gained the spell at this
/// level, so accepting lands the pick where the unlock came from.</summary>
public sealed record BuffSuggestion(string Spell, string Class);

/// <summary>
/// New-buff-unlock suggestions (#120 stage 3, Frankthetankk — the final committed
/// stage). On a level-up the Progress card already knows which spells just became
/// available (LevelUnlocks); the ones that are BUFFS the tracker can attribute
/// (<see cref="BuffDurationCatalog.IsBuffSpell"/> — a landing line with a known
/// duration, the same restriction both set editors enforce) and that the assembled
/// set doesn't cover become suggestions. Suggested, never auto-added: the
/// requester's principle is "player decides everything".
///
/// Rank-up folding — the requester's hard design point — is answered structurally,
/// not with suggestion plumbing: sets store base names and every comparison here and
/// in <see cref="BuffSetEvaluator"/> is rank-folded (<see cref="SpellCatalog.BaseName"/>),
/// so a new RANK of a set spell folds into the same slot and is already covered.
/// Only a genuinely new base line suggests.
/// </summary>
public static class BuffSuggestions
{
    /// <summary>Suggestions for the current ding: buff-shaped unlocks minus what the
    /// assembled set covers (rank-folded), minus per-character dismissals, one row
    /// per base line even when a ding lists several ranks at once.</summary>
    public static List<BuffSuggestion> Compute(
        IReadOnlyList<SpellUnlock> dingSpells,
        IEnumerable<string> assembledSet,
        IEnumerable<string> dismissed,
        BuffDurationCatalog? catalog = null)
    {
        var cat = catalog ?? BuffDurationCatalog.Default;
        var covered = assembledSet.Select(SpellCatalog.BaseName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var declined = dismissed.Select(SpellCatalog.BaseName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<BuffSuggestion>();
        foreach (var unlock in dingSpells)
        {
            var baseName = SpellCatalog.BaseName(unlock.Name);
            if (baseName.Length == 0 || unlock.Classes.Count == 0) continue;
            if (!cat.IsBuffSpell(unlock.Name)) continue;
            if (covered.Contains(baseName) || declined.Contains(baseName)) continue;
            covered.Add(baseName);
            result.Add(new BuffSuggestion(unlock.Name, unlock.Classes[0]));
        }
        return result;
    }

    /// <summary>Record a ✕: per character, per rank-folded BASE spell name — a
    /// dismissed line stays dismissed across its ranks and across sessions, never
    /// re-asked. False when it was already dismissed (nothing to save).</summary>
    public static bool Dismiss(
        Dictionary<string, List<string>> dismissed, string character, string spell)
    {
        var baseName = SpellCatalog.BaseName(spell);
        if (character.Length == 0 || baseName.Length == 0) return false;
        var key = dismissed.Keys.FirstOrDefault(k =>
            k.Equals(character, StringComparison.OrdinalIgnoreCase));
        if (key is null) dismissed[key = character] = [];
        var list = dismissed[key];
        if (list.Contains(baseName, StringComparer.OrdinalIgnoreCase)) return false;
        list.Add(baseName);
        return true;
    }

    /// <summary>This character's dismissed base names. Settings JSON rebuilds the
    /// outer dictionary with the default comparer, so the character key is matched
    /// case-insensitively here rather than trusting the map.</summary>
    public static IReadOnlyList<string> DismissedFor(
        Dictionary<string, List<string>> dismissed, string character) =>
        dismissed.FirstOrDefault(kv =>
            kv.Key.Equals(character, StringComparison.OrdinalIgnoreCase)).Value ?? [];
}
