using EQBuddy.Core;

namespace EQBuddy.UI.Shared;

public static class WatchAlertText
{
    // The label goes to a banner AND the voice — past three names the alert stops
    // being one. The rest collapse to "+N more" (the watch card still lists them all).
    private const int MaxNamedItems = 3;

    public static string MatchLabel(TrackedRule rule, TrackedRuleResult result, int delta)
    {
        var item = result.LastItem ?? "match";
        var label = rule.Kind == WatchKind.SpellFade ? SpellFadeLabel(item) : item;
        // × matches every other count in the app; SpokenAlerts rewrites it for the voice.
        return delta > 1 ? $"{label} ×{delta}" : label;
    }

    /// <summary>#137 (bjstrange): one refresh can catch SEVERAL distinct items for a
    /// rule ("Wind Rune" matching Heda and Meda off one corpse) — "{last} ×2" named
    /// the wrong drop twice. Given the previous per-item counts, every item whose
    /// count grew is named (largest first, the Items order), each with its own ×n
    /// only when its own growth exceeds one. Falls back to the last-item label when
    /// previous counts are unavailable (fresh baseline, rules added mid-session).</summary>
    public static string MatchLabel(TrackedRule rule, TrackedRuleResult result, int delta,
        IReadOnlyDictionary<string, int>? previousItems)
    {
        if (previousItems is null) return MatchLabel(rule, result, delta);
        var grown = new List<(string Item, int Grew)>();
        foreach (var it in result.Items)
        {
            var grew = it.Count - previousItems.GetValueOrDefault(it.Name);
            if (grew > 0) grown.Add((it.Name, grew));
        }
        // A total that moved without any item growing (history trim, rollover skew)
        // has nothing to name — the last-item label is the honest fallback.
        if (grown.Count == 0) return MatchLabel(rule, result, delta);

        var named = grown.Take(MaxNamedItems).Select(g =>
        {
            var label = rule.Kind == WatchKind.SpellFade ? SpellFadeLabel(g.Item) : g.Item;
            return g.Grew > 1 ? $"{label} ×{g.Grew}" : label;
        });
        var text = string.Join(", ", named);
        return grown.Count > MaxNamedItems ? $"{text} +{grown.Count - MaxNamedItems} more" : text;
    }

    /// <summary>The per-item counts a caller keeps as its baseline for the overload
    /// above. Case-insensitive to match the snapshot builder's item keying.</summary>
    public static Dictionary<string, int> ItemCounts(TrackedRuleResult result)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var it in result.Items) counts[it.Name] = it.Count;
        return counts;
    }

    private static string SpellFadeLabel(string item)
    {
        var open = item.LastIndexOf(" (", StringComparison.Ordinal);
        if (open > 0 && item.EndsWith(")", StringComparison.Ordinal))
        {
            var spell = item[..open];
            var target = item[(open + 2)..^1];
            if (target.Length > 0)
                return $"{spell} faded off {target}";
        }

        return $"{item} faded off you";
    }
}
