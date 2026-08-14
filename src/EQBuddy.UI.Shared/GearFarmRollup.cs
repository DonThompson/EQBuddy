using EQBuddy.Core;

namespace EQBuddy.UI.Shared;

/// <summary>
/// The Gear card's "by zone" view: the checklist says WHAT to get; this rollup says
/// WHERE TO GO. Un-acquired wishes join to the embedded ItemCatalog by name and pivot
/// to zone → [items], so a player can pick tonight's camp by which zone feeds the
/// most open wishes. (Idea convergent with a competitor's zone-grouped wish list —
/// concept only; the catalog join and the machinery are ours.)
///
/// An item that drops in several zones appears under EVERY one of them, not a chosen
/// "primary": the view answers "if I camp zone X tonight, what can it still give me",
/// and a per-zone list that hides valid camps makes its own header count lie. The
/// duplication is the honesty, and the acquired-row exclusion caps it — a tick
/// removes the item from all of its zones at once.
///
/// Items the catalog can't place in a drop zone go under honest buckets instead of
/// vanishing: quest-obtained under "Quests", recipe-only under "Crafted", and
/// no-record/no-data under "No drop data" — every un-acquired wish appears exactly
/// once across the non-zone buckets or N times across its N zones, never zero.
/// </summary>
public static class GearFarmRollup
{
    public const string QuestsHeading = "Quests";
    public const string CraftedHeading = "Crafted";
    public const string NoDataHeading = "No drop data";

    /// <summary>Hops is BFS zones-from-here (0 = you're standing in it), null for the
    /// non-zone buckets or when the graph can't place the zone.</summary>
    public sealed record ZoneGroup(string Zone, int? Hops, IReadOnlyList<GearChecklistItem> Items);

    /// <summary>Zone groups nearest-first when <paramref name="hopsFromHere"/> can
    /// rank them (unrankable zones after ranked ones, alphabetical within a tie —
    /// null delegate degrades to plain alphabetical), then the fixed buckets:
    /// Quests, Crafted, No drop data.</summary>
    public static IReadOnlyList<ZoneGroup> Build(
        IReadOnlyList<GearChecklistItem> checklist,
        Func<string, ItemCatalog.Record?> findRecord,
        Func<string, int?>? hopsFromHere = null)
    {
        var zones = new Dictionary<string, List<GearChecklistItem>>(StringComparer.OrdinalIgnoreCase);
        var quests = new List<GearChecklistItem>();
        var crafted = new List<GearChecklistItem>();
        var noData = new List<GearChecklistItem>();

        foreach (var item in checklist.Where(i => !i.Acquired))
        {
            // The same "+N" fold the auto-check join lives by: the wish names the
            // upgraded form ("Crushbone Belt +5"), the catalog titles the base item —
            // and the base item's corpses are where the farming happens.
            var record = findRecord(GearLootAutoCheck.SplitTier(item.Item).BaseName);
            if (record?.DropZones is { Count: > 0 } dropZones)
            {
                foreach (var zone in dropZones.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!zones.TryGetValue(zone, out var list)) zones[zone] = list = [];
                    list.Add(item);
                }
            }
            // QuestFlagged without a Quests list still means "a quest hands this out";
            // "Quests" is more honest than "No drop data" for it — the player's next
            // move is a quest log, not a corpse.
            else if (record is { } r && (r.Quests is { Count: > 0 } || r.QuestFlagged))
                quests.Add(item);
            else if (record?.Recipes is { Count: > 0 })
                crafted.Add(item);
            else
                noData.Add(item);
        }

        var groups = zones
            .Select(kv => new ZoneGroup(kv.Key, hopsFromHere?.Invoke(kv.Key), kv.Value))
            .OrderBy(g => g.Hops ?? int.MaxValue)
            .ThenBy(g => g.Zone, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (quests.Count > 0) groups.Add(new ZoneGroup(QuestsHeading, null, quests));
        if (crafted.Count > 0) groups.Add(new ZoneGroup(CraftedHeading, null, crafted));
        if (noData.Count > 0) groups.Add(new ZoneGroup(NoDataHeading, null, noData));
        return groups;
    }

    /// <summary>Header text: zone, open-wish count, and travel distance when known —
    /// the same "N zones away" phrasing the quest browser uses.</summary>
    public static string Heading(ZoneGroup group)
    {
        var text = $"{group.Zone} ({group.Items.Count})";
        return group.Hops switch
        {
            0 => text + " · you're here",
            { } hops => $"{text} · {hops} zone{(hops == 1 ? "" : "s")} away",
            null => text,
        };
    }
}
