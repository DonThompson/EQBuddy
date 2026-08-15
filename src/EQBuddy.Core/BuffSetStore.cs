namespace EQBuddy.Core;

/// <summary>One class's slice of a character's assembled buff set — the unit the
/// stage-2 editors and the Buff Set breakout render (#120, Frankthetankk).</summary>
public sealed record BuffSetSection(string Class, IReadOnlyList<string> Spells);

/// <summary>
/// Stage-2 buff-set storage (#120, Frankthetankk — his design): sets live PER CLASS
/// underneath and are assembled by the character's ACTIVE class combination, so
/// swapping Warrior for Rogue keeps the other classes' picks untouched. The
/// "(any class)" bucket is always part of the assembled set, whatever the combination;
/// stage 1's flat per-character lists migrate into it, so nothing anyone configured is
/// lost or demoted (see <see cref="Migrate"/>).
///
/// The shape is settings JSON (character "name_server" → class → spell names), and
/// deserialization rebuilds dictionaries with the default comparer — so every helper
/// here compares class keys case-insensitively itself instead of trusting the maps.
/// </summary>
public static class BuffSetStore
{
    /// <summary>The bucket assembled into every combination. A real class can never
    /// collide with it — parentheses aren't part of any class name.</summary>
    public const string AnyClass = "(any class)";

    /// <summary>One-time stage-1 → stage-2 migration: flat per-character sets move
    /// under the "(any class)" bucket, which the assembled set ALWAYS includes — so a
    /// player's stage-1 picks keep exactly the reach they had, never dropped, never
    /// demoted to a weaker state. Merging preserves anything already in the bucket.
    /// Idempotent: <paramref name="flat"/> is emptied, and an empty one is a no-op.</summary>
    public static bool Migrate(
        Dictionary<string, List<string>> flat,
        Dictionary<string, Dictionary<string, List<string>>> byClass)
    {
        if (flat.Count == 0) return false;
        foreach (var (character, spells) in flat)
            foreach (var spell in spells)
                Add(byClass, character, AnyClass, spell);
        flat.Clear();
        return true;
    }

    /// <summary>Add a spell to one class bucket. False when already present in that
    /// bucket — identity is case-insensitive, matching the evaluator's.</summary>
    public static bool Add(
        Dictionary<string, Dictionary<string, List<string>>> byClass,
        string character, string cls, string spell)
    {
        if (character.Length == 0 || cls.Length == 0 || spell.Length == 0) return false;
        if (!byClass.TryGetValue(character, out var classes))
            byClass[character] = classes = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var key = classes.Keys.FirstOrDefault(k => k.Equals(cls, StringComparison.OrdinalIgnoreCase));
        if (key is null) classes[key = cls] = [];
        var list = classes[key];
        if (list.Contains(spell, StringComparer.OrdinalIgnoreCase)) return false;
        list.Add(spell);
        return true;
    }

    /// <summary>Remove a spell from one class bucket, pruning emptied structures so
    /// settings JSON never accumulates hollow entries. False when nothing matched.</summary>
    public static bool Remove(
        Dictionary<string, Dictionary<string, List<string>>> byClass,
        string character, string cls, string spell)
    {
        if (!byClass.TryGetValue(character, out var classes)) return false;
        var key = classes.Keys.FirstOrDefault(k => k.Equals(cls, StringComparison.OrdinalIgnoreCase));
        if (key is null) return false;
        var list = classes[key];
        var removed = list.RemoveAll(s => s.Equals(spell, StringComparison.OrdinalIgnoreCase)) > 0;
        if (list.Count == 0) classes.Remove(key);
        if (classes.Count == 0) byClass.Remove(character);
        return removed;
    }

    /// <summary>
    /// Every section an EDITOR must show: the active combination (from
    /// <see cref="Sections"/>), then any class with stored picks that isn't in it,
    /// flagged as parked.
    ///
    /// Both editors have to use this. Since v1.82.0 the class pickers offer EVERY
    /// class — that was the fix for only being able to build a set for the class
    /// EQBuddy had guessed — which means a player can add to a bucket that isn't in
    /// their active combination. Options showed those parked buckets; the breakout
    /// composed its own list from <see cref="Sections"/> alone and didn't. So a pick
    /// added there was stored correctly, correctly excluded from the next search
    /// (it IS in the bucket), and rendered nowhere: it read as a click that did
    /// nothing and then ate the buff (#120, Frankthetankk, building a Paladin set).
    /// One method, so the two editors cannot disagree about this again.
    /// </summary>
    public static List<(BuffSetSection Section, bool Parked)> EditableSections(
        IReadOnlyDictionary<string, List<string>>? characterSets,
        IEnumerable<string> activeClasses)
    {
        var result = Sections(characterSets, activeClasses)
            .Select(sec => (Section: sec, Parked: false)).ToList();
        var active = result.Select(r => r.Section.Class).ToHashSet(StringComparer.OrdinalIgnoreCase);
        result.AddRange(StoredClasses(characterSets)
            .Where(c => !active.Contains(c))
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .Select(c => (Section: new BuffSetSection(c, SpellsFor(characterSets, c)), Parked: true)));
        return result;
    }

    /// <summary>One class bucket's stored picks (copy); empty when there are none.</summary>
    public static List<string> SpellsFor(
        IReadOnlyDictionary<string, List<string>>? characterSets, string cls) =>
        characterSets?.FirstOrDefault(kv => kv.Key.Equals(cls, StringComparison.OrdinalIgnoreCase))
            .Value is { } list ? [.. list] : [];

    /// <summary>The assembled view for an active class combination: the "(any class)"
    /// bucket first — always included, even with no classes known — then one section
    /// per active class in the order given (empty when that class has no picks yet:
    /// the editors render the empty section as the place to add). Stored classes NOT
    /// in the combination stay out on purpose — that is the requester's design: their
    /// picks wait, untouched, for the next swap back.</summary>
    public static List<BuffSetSection> Sections(
        IReadOnlyDictionary<string, List<string>>? characterSets,
        IEnumerable<string> activeClasses)
    {
        var result = new List<BuffSetSection> { new(AnyClass, SpellsFor(characterSets, AnyClass)) };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { AnyClass };
        foreach (var cls in activeClasses)
            if (cls.Length > 0 && seen.Add(cls))
                result.Add(new BuffSetSection(cls, SpellsFor(characterSets, cls)));
        return result;
    }

    /// <summary>The evaluator's flat input: every section's spells in section order,
    /// first spelling wins on duplicates (the same buff picked under two classes is
    /// one claim on the card, exactly as stage 1's rank folding already guarantees).</summary>
    public static List<string> Assemble(
        IReadOnlyDictionary<string, List<string>>? characterSets,
        IEnumerable<string> activeClasses)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return Sections(characterSets, activeClasses)
            .SelectMany(s => s.Spells).Where(seen.Add).ToList();
    }

    /// <summary>Every class holding stored picks for this character, "(any class)"
    /// included — the Options editor lists these even when not currently active,
    /// because it is the one place a parked pick can still be removed.</summary>
    public static List<string> StoredClasses(
        IReadOnlyDictionary<string, List<string>>? characterSets) =>
        characterSets?.Where(kv => kv.Value.Count > 0).Select(kv => kv.Key).ToList() ?? [];
}

/// <summary>The set editors' shared search ranking (#120): buffs the player was seen
/// casting first, then the whole attributable-buff catalog; entries already in the
/// target bucket stay out (the same buff under a DIFFERENT class stays offered — the
/// assembly dedups, and pre-configuring a future class is legitimate).</summary>
public static class BuffSetSearch
{
    public static List<(string Spell, bool Seen)> Rank(
        string query, IReadOnlyCollection<string> seenCasts,
        IReadOnlyCollection<string> exclude, IEnumerable<string> catalog, int max = 14)
    {
        var inBucket = new HashSet<string>(exclude, StringComparer.OrdinalIgnoreCase);
        var seenMatches = seenCasts
            .Where(s => s.Contains(query, StringComparison.OrdinalIgnoreCase) && !inBucket.Contains(s))
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
        var seenSet = new HashSet<string>(seenMatches, StringComparer.OrdinalIgnoreCase);
        var catalogMatches = catalog
            .Where(s => s.Contains(query, StringComparison.OrdinalIgnoreCase)
                && !inBucket.Contains(s) && !seenSet.Contains(s))
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase);
        return seenMatches.Select(s => (s, true))
            .Concat(catalogMatches.Select(s => (s, false))).Take(max).ToList();
    }
}
