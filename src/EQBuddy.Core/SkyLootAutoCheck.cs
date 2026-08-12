namespace EQBuddy.Core;

/// <summary>
/// The Sky checklist's loot auto-tick, extracted from the widget so the class-scoping
/// rules are testable. Two rules, layered:
///
/// 1. SHARED items (five classes want a Wind Rune Azia) tick only the player's own
///    classes — the quest tracker's class filter, or the active Sky tab when no
///    filter is set (#98, bjstrange). One physical rune cannot honestly tick five
///    class plans the player doesn't play.
/// 2. UNAMBIGUOUS items — wanted by exactly ONE class in the whole checklist — tick
///    that class no matter what tab or filter is active (#106, bjstrange again: a
///    Berserker-only staff looted on the Druid tab can't be for anyone else's test).
///    Same philosophy as the tracker's "loot outranks the class lens".
/// </summary>
public static class SkyLootAutoCheck
{
    /// <summary>Ticks up to <paramref name="newlyLooted"/> unacquired slots per
    /// eligible class for <paramref name="itemName"/>. Returns true if anything ticked.</summary>
    public static bool Apply(IReadOnlyList<SkyQuestChecklistItem> checklist, string itemName,
        int newlyLooted, IReadOnlyList<string> myClasses, string activeTab)
    {
        if (newlyLooted <= 0) return false;

        bool ClassTicks(string className) => myClasses.Count > 0
            ? myClasses.Any(c => c.Equals(className, StringComparison.OrdinalIgnoreCase))
            : activeTab.Length == 0 || string.Equals(className, activeTab, StringComparison.Ordinal);

        var slots = checklist
            .Where(i => string.Equals(i.QuestItem, itemName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var owningClasses = slots.Select(i => i.ClassName)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var changed = false;
        foreach (var classGroup in slots
                     .Where(i => !i.Acquired && (ClassTicks(i.ClassName) || owningClasses.Count == 1))
                     .GroupBy(i => i.ClassName))
            foreach (var item in classGroup.Take(newlyLooted))
            {
                item.Acquired = true;
                changed = true;
            }
        return changed;
    }
}
