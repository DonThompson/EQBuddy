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

    /// <summary>Fold label for the next-milestone preview:
    /// "▸ At level 35: 2 new AA abilities".</summary>
    public static string NextLabel(int level, int count, bool expanded) =>
        $"{(expanded ? "▾" : "▸")} At level {level}: {count} new AA " +
        (count == 1 ? "ability" : "abilities");

    /// <summary>Right-column value: where the row comes from — its class, or its
    /// class-agnostic category (Archetype rows are labeled, never guessed per class:
    /// the wiki doesn't say which classes they cover) — plus the rank count when the
    /// ability has more than one to buy.</summary>
    public static string RowValue(AaCatalogEntry a) =>
        (a.Class is { Length: > 0 } cls ? cls : a.Category)
        + (a.MaxRank > 1 ? $" · {a.MaxRank} ranks" : "");
}
