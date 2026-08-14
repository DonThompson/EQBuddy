namespace EQBuddy.Core;

/// <summary>
/// Answers the ding — which AA abilities become available at exactly a given level for
/// a set of classes. AAs ONLY: the embedded catalogs carry no per-class spell levels
/// (the eqlwiki spells harvest has them, but it is not promoted into Core), so a spell
/// answer here would be invented. Category rules follow the wiki's own tables:
///
///   Class     — carries a class name; included only when it matches a picked class.
///   General   — class-agnostic by the wiki's sectioning; always included.
///   Archetype — applies to a SUBSET of classes, but the wiki tables never say which,
///               so rows are included for everyone and labeled "Archetype" rather
///               than silently guessed per class.
///   Special   — progression-gated (Slayer achievements); included and labeled.
///
/// Empty classes therefore still answer with the class-agnostic categories — a player
/// who never picked classes sees the General ding rewards, not a blank card.
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

    /// <summary>Abilities whose level requirement is exactly <paramref name="level"/>
    /// and whose class (if any) is in <paramref name="classes"/>. Entries the wiki
    /// gives no level for never match — unknown is not level anything.</summary>
    public static IReadOnlyList<AaCatalogEntry> UnlocksAt(
        IReadOnlyCollection<string> classes, int level) =>
        AaCatalog.All
            .Where(a => a.LevelRequirement == level
                && (a.Class is not { Length: > 0 } cls
                    || classes.Contains(cls, StringComparer.OrdinalIgnoreCase)))
            .OrderBy(CategoryRank)
            .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>The next level ABOVE <paramref name="afterLevel"/> that unlocks
    /// anything for these classes, with its unlocks — the "At 35: …" preview. Level
    /// requirements are sparse (most levels unlock nothing), so the preview jumps to
    /// the next real milestone instead of announcing an empty next level. Null past
    /// the last milestone the catalog knows.</summary>
    public static (int Level, IReadOnlyList<AaCatalogEntry> Unlocks)? Next(
        IReadOnlyCollection<string> classes, int afterLevel)
    {
        foreach (var level in AaCatalog.All
                     .Where(a => a.LevelRequirement > afterLevel)
                     .Select(a => a.LevelRequirement!.Value)
                     .Distinct().OrderBy(l => l))
        {
            var unlocks = UnlocksAt(classes, level);
            if (unlocks.Count > 0) return (level, unlocks);
        }
        return null;
    }
}
