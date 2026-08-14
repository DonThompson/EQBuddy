using EQBuddy.Core;

namespace EQBuddy.UI.Shared;

/// <summary>
/// Text for the Progress card's level-unlock rows (LevelUnlocks in Core picks WHAT
/// shows; this says HOW), shared so the Avalonia card renders the same words when it
/// adopts the feature.
/// </summary>
public static class LevelUnlockText
{
    /// <summary>Section label over the ding's list: "New at level 30".</summary>
    public static string NewAtLevelLabel(int level) => $"New at level {level}";

    /// <summary>Fold label for the next-milestone preview, counting each group it
    /// holds in list order: "▸ At level 35: 2 new AA abilities, 3 new spells".</summary>
    public static string NextLabel(int level, int aaCount, int spellCount, bool expanded)
    {
        var parts = new List<string>(2);
        if (aaCount > 0)
            parts.Add($"{aaCount} new AA " + (aaCount == 1 ? "ability" : "abilities"));
        if (spellCount > 0)
            parts.Add($"{spellCount} new spell" + (spellCount == 1 ? "" : "s"));
        return $"{(expanded ? "▾" : "▸")} At level {level}: " + string.Join(", ", parts);
    }

    /// <summary>Right-column value: where the row comes from — its class, or its
    /// class-agnostic category (Archetype rows are labeled, never guessed per class:
    /// the wiki doesn't say which classes they cover) — plus the rank count when the
    /// ability has more than one to buy.</summary>
    public static string RowValue(AaCatalogEntry a) =>
        (a.Class is { Length: > 0 } cls ? cls : a.Category)
        + (a.MaxRank > 1 ? $" · {a.MaxRank} ranks" : "");

    /// <summary>Right-column value for a spell row: the picked classes gaining it at
    /// this level, plus the word that keeps spells apart from the AA rows —
    /// "Cleric spell", "Druid/Ranger spell".</summary>
    public static string SpellRowValue(SpellUnlock s) =>
        string.Join("/", s.Classes) + " spell";

    /// <summary>Tooltip for a spell row: every class that gets the spell and when —
    /// "Cleric 20 · Druid 29". Catalog facts only; effect text stays on the wiki,
    /// never invented here.</summary>
    public static string? SpellTooltip(SpellLevelEntry? e) =>
        e is null ? null
        : string.Join(" · ", e.Classes.Select(c => $"{c.Class} {c.Level}"));
}
