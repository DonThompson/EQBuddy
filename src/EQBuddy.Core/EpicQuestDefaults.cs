namespace EQBuddy.Core;

public static class EpicQuestDefaults
{
    public static List<EpicQuestChecklistItem> Items()
    {
        var questCatalog = QuestCatalog.LoadEmbedded();
        var checklist = EpicQuestChecklistCatalog.LoadEmbedded();
        var items = new List<EpicQuestChecklistItem>();

        foreach (var classChecklist in checklist.Classes)
        {
            var className = classChecklist.ClassName;
            var quest = FindQuest(questCatalog, className);
            var reward = quest is null ? "" :
                string.Join(", ", quest.Rewards.Where(r => r.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));
            var questItems = quest?.Items.Select(i => i.Name).ToList() ?? [];

            foreach (var row in classChecklist.Rows.OrderBy(r => r.Order))
            {
                items.Add(new EpicQuestChecklistItem
                {
                    Id = row.Id,
                    ClassName = className,
                    QuestName = quest?.Name ?? $"{className} Epic Quest",
                    Reward = reward,
                    Section = row.Section.Length > 0 ? row.Section : "Checklist",
                    QuestItem = row.Text,
                    Qty = 1,
                    Order = row.Order,
                    AvailableInClassic = row.AvailableInClassic,
                    ItemNames = MatchItemNames(row.Text, questItems),
                });
            }
        }

        return items;
    }

    /// <summary>Which of the class's epic turn-in items does this prose step mention?
    /// The loot auto-tick's match key (#121). Boundary-checked containment — a name
    /// counts only when it isn't glued to surrounding letters/digits — with two data
    /// cleanups the catalog forces: case-variant duplicates ("Page 24 Top" / "Page 24
    /// top") collapse to the first spelling, and a name found ONLY inside a longer
    /// matched name is dropped ("Mystical Lute" inside "Mystical Lute Body" is the
    /// Body's mention, not the Lute's). Names are stored tier-folded
    /// (QuestCatalog.BaseItemName) so loot-time equality folds symmetrically.</summary>
    public static List<string> MatchItemNames(string text, IEnumerable<string> catalogItems)
    {
        var found = new List<(string Name, List<(int Start, int End)> Spans)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in catalogItems)
        {
            var name = QuestCatalog.BaseItemName(raw);
            if (name.Length == 0 || !seen.Add(name)) continue;
            var spans = Occurrences(text, name);
            if (spans.Count > 0) found.Add((name, spans));
        }

        return found
            .Where(f => f.Spans.Any(s => !found.Any(o =>
                o.Name.Length > f.Name.Length &&
                o.Spans.Any(os => os.Start <= s.Start && s.End <= os.End))))
            .Select(f => f.Name)
            .ToList();
    }

    private static List<(int Start, int End)> Occurrences(string text, string name)
    {
        var spans = new List<(int, int)>();
        for (var at = 0; ; at++)
        {
            at = text.IndexOf(name, at, StringComparison.OrdinalIgnoreCase);
            if (at < 0) return spans;
            var end = at + name.Length;
            if ((at == 0 || !char.IsLetterOrDigit(text[at - 1])) &&
                (end == text.Length || !char.IsLetterOrDigit(text[end])))
                spans.Add((at, end));
        }
    }

    public static QuestEntry? FindQuest(QuestCatalog catalog, string className) =>
        catalog.Quests.FirstOrDefault(q =>
            q.Name.Equals($"{className} Epic Quest", StringComparison.OrdinalIgnoreCase));

    public static string SourceLine(QuestEntry quest)
    {
        var start = quest.StartZone.Length > 0
            ? quest.QuestGiver.Length > 0 ? $"{quest.StartZone}: {quest.QuestGiver}" : quest.StartZone
            : quest.QuestGiver;
        var zones = quest.Zones.Where(z => z.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToList();
        var zoneText = zones.Count == 0 ? "" : "Zones: " + string.Join(", ", zones);
        return start.Length > 0 && zoneText.Length > 0 ? $"{start} | {zoneText}" : start + zoneText;
    }
}
