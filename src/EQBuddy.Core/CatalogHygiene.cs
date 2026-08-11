namespace EQBuddy.Core;

/// <summary>
/// The catalog-wide sequel to <see cref="SkyTestSplit"/> (David's ask, 2026-08-11):
/// the 917-page harvest faithfully mirrors the wiki, and the wiki has three kinds of
/// page that are not one quest.
///
/// INDEX pages ("Popular Quests by Level", 347 rewards) are navigation, not quests —
/// they polluted search and the reward map, and are dropped outright.
///
/// COLLECTION pages ("Bard Skyshrine Armor Quests", "Coldain Ring Quests" — 77 of
/// them end in "Quests", plus a curated handful of armor-set and key pages) document
/// several quests at once. Unlike Sky, no local table knows their per-quest turn-in
/// splits, so they stay — searchable, item-badged, openable — but flagged: a
/// collection never computes a progress fraction, never flips "ready", and never
/// enters the held tab, because a union of six quests' items is not a quest you can
/// finish. The durable fix (harvest-side section splitting) is queued; this stops
/// the lying today.
///
/// Deliberately untouched: the Epic quests (40+ items is the truth of an epic) and
/// real single steps that merely sit near collections ("10th Coldain Ring Quest").
/// </summary>
public static class CatalogHygiene
{
    /// <summary>Pure navigation pages — not quests by any reading.</summary>
    private static readonly HashSet<string> IndexPages = new(StringComparer.OrdinalIgnoreCase)
    {
        "Popular Quests by Level",
        "Class Race Quest List",
        "Velious Class Armor Comparisons",
        "Faction Quests",
        "All Positive Faction Quests",
    };

    /// <summary>Aggregate pages whose names don't end in "Quests" but document a set
    /// all the same (armor sets, key roundups, multi-test gauntlets).</summary>
    private static readonly HashSet<string> ExtraCollections = new(StringComparer.OrdinalIgnoreCase)
    {
        "Plane of Sky Keys",
        "Custom Plate Helms - Kael Drakkel",
        "Custom Plate Helms - Skyshrine",
        "Custom Plate Helms - Thurgadin",
        "Trooper Scale Armor",
        "Dreadscale Armor",
        "Animal Skin Armor",
        "Crusader's Tests",
        "Emerald Warriors' Items",
    };

    public static void Apply(QuestCatalog catalog)
    {
        catalog.Quests.RemoveAll(q => IndexPages.Contains(q.Name));
        foreach (var q in catalog.Quests)
            if (q.Name.EndsWith("Quests", StringComparison.OrdinalIgnoreCase)
                || ExtraCollections.Contains(q.Name))
                q.Collection = true;
    }
}
