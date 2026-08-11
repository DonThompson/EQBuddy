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
        {
            if (q.Name.EndsWith("Quests", StringComparison.OrdinalIgnoreCase)
                || ExtraCollections.Contains(q.Name))
                q.Collection = true;

            // Item-name repair (2026-08-11 accuracy audit): the {{:Item}} transclusion
            // idiom leaks a leading ':' on some pages (Dreadscale's ":Banded Bracer"),
            // and wiki source occasionally double-spaces — either way the name can
            // never match a loot line. Normalize, then merge duplicates the page
            // listed twice (Bard Epic's Symphony pages), keeping the larger Qty so
            // counts stay honest.
            foreach (var item in q.Items)
                item.Name = System.Text.RegularExpressions.Regex
                    .Replace(item.Name.TrimStart(':').Trim(), @"\s{2,}", " ");
            q.Items = q.Items
                .Where(i => i.Name.Length > 0)
                .GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(i => i.Qty).First())
                .ToList();
        }
    }
}
