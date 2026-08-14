namespace EQBuddy.Core;

/// <summary>One level's unlocks with AAs and spells held apart — the UI labels the
/// two groups differently (rank counts vs class lists), so the model never mixes
/// them into one row type.</summary>
public sealed record LevelUnlockSet(
    IReadOnlyList<AaCatalogEntry> Aas,
    IReadOnlyList<SpellUnlock> Spells)
{
    public static LevelUnlockSet Empty { get; } = new([], []);
    public int Count => Aas.Count + Spells.Count;
}

/// <summary>
/// Answers the ding — which AA abilities and spells become available at exactly a
/// given level for a set of classes. AA category rules follow the wiki's own tables:
///
///   Class     — carries a class name; included only when it matches a picked class.
///   General   — class-agnostic by the wiki's sectioning; always included.
///   Archetype — applies to a SUBSET of classes, but the wiki tables never say which,
///               so rows are included for everyone and labeled "Archetype" rather
///               than silently guessed per class.
///   Special   — progression-gated (Slayer achievements); included and labeled.
///
/// Spells (SpellLevelCatalog, the promoted eqlwiki spells harvest) are always class
/// rows — no picked class, no spell unlocks. Empty classes therefore still answer
/// with the class-agnostic AA categories — a player who never picked classes sees
/// the General ding rewards, not a blank card — but never a spell.
/// </summary>
public static class LevelUnlocks
{
    // Class rows lead — "my class got a new button" is the ding's headline — then the
    // class-agnostic categories, names alphabetical within each group.
    private static int CategoryRank(AaCatalogEntry a) => a.Category switch
    {
        "Class" => 0,
        "Archetype" => 1,
        "General" => 2,
        _ => 3,
    };

    /// <summary>Unlocks whose level requirement is exactly <paramref name="level"/>
    /// and whose class (if any) is in <paramref name="classes"/>. Entries the wiki
    /// gives no level for never match — unknown is not level anything.</summary>
    public static LevelUnlockSet UnlocksAt(
        IReadOnlyCollection<string> classes, int level) =>
        new(AaCatalog.All
                .Where(a => a.LevelRequirement == level
                    && (a.Class is not { Length: > 0 } cls
                        || classes.Contains(cls, StringComparer.OrdinalIgnoreCase)))
                .OrderBy(CategoryRank)
                .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            SpellLevelCatalog.Default.UnlocksAt(classes, level));

    /// <summary>The next level ABOVE <paramref name="afterLevel"/> that unlocks
    /// anything for these classes, with its unlocks — the "At 35: …" preview. AA
    /// requirements are sparse but caster spell levels are dense (a cleric gains a
    /// spell at every level to 60), so with classes picked this usually answers
    /// afterLevel + 1. Null past the last milestone the catalogs know.</summary>
    public static (int Level, LevelUnlockSet Unlocks)? Next(
        IReadOnlyCollection<string> classes, int afterLevel)
    {
        foreach (var level in AaCatalog.All
                     .Where(a => a.LevelRequirement > afterLevel)
                     .Select(a => a.LevelRequirement!.Value)
                     .Concat(SpellLevelCatalog.Default.LevelsFor(classes)
                         .Where(l => l > afterLevel))
                     .Distinct().OrderBy(l => l))
        {
            var unlocks = UnlocksAt(classes, level);
            if (unlocks.Count > 0) return (level, unlocks);
        }
        return null;
    }
}
