using System.Text.RegularExpressions;

namespace EQBuddy.Core;

/// <summary>
/// The Gear card's auto-done: an imported shopping-list row ticks itself when the
/// player's own log (loot, merges) or inventory dump shows the item obtained — the
/// Sky/Epic auto-check DNA applied to the one-owner gear list. (Idea convergent with
/// EQ Legends Companion's wish auto-done — concept only; the machinery is ours.)
///
/// No class-lens or AcquiredUnassigned machinery here: a gear list belongs to the one
/// character it was exported for, so a name match simply ticks (the Sky #106 ambiguity
/// rules exist because five classes can want one rune — that cannot happen on this card).
///
/// The one wrinkle is UPGRADE TIERS. A wish may name the upgraded form ("Crushbone
/// Belt +5") while the corpse drops the base item. The rule is AT-OR-ABOVE: an
/// obtained "+7" satisfies a "+5" or base wish, but a base drop never ticks a "+5"
/// wish — the wish is the upgraded item, and ticking it on the first base drop would
/// silently lie about four merges of work still to do. Both loot lines and the
/// inventory dump's raw rows carry the "+N" suffix, so the signal is reliable; the
/// dump's base-folded Counts map is exactly why <see cref="ApplyInventory"/> reads
/// Entries instead.
/// </summary>
public static partial class GearLootAutoCheck
{
    /// <summary>"Crushbone Belt +5" → ("Crushbone Belt", 5); no suffix → tier 0.
    /// The base half matches <see cref="QuestCatalog.BaseItemName"/>'s folding.</summary>
    public static (string BaseName, int Tier) SplitTier(string itemName)
    {
        // TryParse, not Parse: wish names arrive from an imported HTML export and
        // obtained names from dump rows — a "+" tail of absurd digits must read as
        // no-tier, not throw on the UI tick that evaluates every row.
        var m = TierSuffixRegex().Match(itemName.Trim());
        return m.Success && int.TryParse(m.Groups["tier"].Value, out var tier)
            ? (m.Groups["base"].Value, tier)
            : (itemName.Trim(), 0);
    }

    /// <summary>Ticks up to <paramref name="newlyObtained"/> un-acquired rows whose
    /// item matches <paramref name="obtainedName"/> at-or-above the wished tier.
    /// Case-insensitive. Returns true if anything ticked. The count cap matters:
    /// one looted ring is one tick even when EAR 1 and EAR 2 both wish for it.</summary>
    public static bool Apply(IReadOnlyList<GearChecklistItem> checklist, string obtainedName,
        int newlyObtained)
    {
        if (newlyObtained <= 0) return false;
        var (obtainedBase, obtainedTier) = SplitTier(obtainedName);

        var changed = false;
        foreach (var item in checklist
                     .Where(i => !i.Acquired && Satisfies(i, obtainedBase, obtainedTier))
                     .Take(newlyObtained))
        {
            item.Acquired = true;
            changed = true;
        }
        return changed;
    }

    /// <summary>One pass over an inventory dump's raw rows (names keep their "+N",
    /// unlike the base-folded Counts map): owning N of an item at-or-above the wished
    /// tier ticks up to N matching rows. Returns true if anything ticked.</summary>
    public static bool ApplyInventory(IReadOnlyList<GearChecklistItem> checklist,
        IEnumerable<InventoryFile.Entry> entries)
    {
        var changed = false;
        foreach (var group in entries.GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
            changed |= Apply(checklist, group.Key, group.Sum(e => e.Count));
        return changed;
    }

    private static bool Satisfies(GearChecklistItem item, string obtainedBase, int obtainedTier)
    {
        var (wishBase, wishTier) = SplitTier(item.Item);
        return wishBase.Equals(obtainedBase, StringComparison.OrdinalIgnoreCase)
            && obtainedTier >= wishTier;
    }

    [GeneratedRegex(@"^(?<base>.+?)\s*\+(?<tier>\d+)$")]
    private static partial Regex TierSuffixRegex();
}
