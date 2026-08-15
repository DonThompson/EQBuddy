using EQBuddy.Core;

namespace EQBuddy.UI.Shared;

/// <summary>
/// The Loot card's third sort: every drop in the order it happened, newest first.
///
/// wizen was farming HQ Lion Skins and wanted to know whether anything else had dropped
/// while he ground the same thing hundreds of times (#160). The aggregate view cannot
/// answer that — a rare pelt is one row among many, and its count ticking from 0 to 1
/// looks exactly like a skin count ticking from 200 to 201. Seen in arrival order it is
/// obvious, because it is the row that isn't a Lion Skin.
///
/// Deliberately NOT a "last looted" sort of the aggregate, which is what was asked for:
/// that still collapses 200 skins into one row and still buries the interesting drop by
/// showing it below whatever was picked up most recently.
/// </summary>
public static class RawLootView
{
    /// <summary>The value column: the clock time it landed, and the stack size when it
    /// was more than one. Time rather than "3 minutes ago" because the card repaints
    /// once a second and a drifting relative age would rewrite every row every tick —
    /// and because a clock time can be matched against the log itself.</summary>
    public static string Detail(LootPickup pickup) =>
        pickup.Count > 1
            ? $"×{pickup.Count}  {pickup.Time:h:mm:ss tt}"
            : pickup.Time.ToString("h:mm:ss tt");

    /// <summary>Rows for the list, newest first. Consecutive identical drops collapse
    /// into one row carrying the newest time — an eight-in-a-row skin streak is one line
    /// saying ×8, so the drop that ISN'T a skin stays visible instead of being pushed
    /// off the end by its own commonness.</summary>
    public static List<(string Item, string Detail)> Rows(IReadOnlyList<LootPickup> newestFirst)
    {
        var rows = new List<(string, string)>();
        for (var i = 0; i < newestFirst.Count;)
        {
            var head = newestFirst[i];
            var count = head.Count;
            var j = i + 1;
            while (j < newestFirst.Count
                   && newestFirst[j].Item.Equals(head.Item, StringComparison.OrdinalIgnoreCase))
            {
                count += newestFirst[j].Count;
                j++;
            }
            rows.Add((head.Item, Detail(head with { Count = count })));
            i = j;
        }
        return rows;
    }
}
