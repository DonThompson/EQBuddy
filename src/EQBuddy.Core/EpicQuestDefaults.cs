namespace EQBuddy.Core;

public static class EpicQuestDefaults
{
    public static List<EpicQuestChecklistItem> Items()
    {
        var catalog = QuestCatalog.LoadEmbedded();
        var items = new List<EpicQuestChecklistItem>();

        foreach (var className in QuestClassFilter.Classes)
        {
            var quest = FindQuest(catalog, className);
            if (quest is null) continue;

            var reward = string.Join(", ", quest.Rewards.Where(r => r.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));
            var grouped = quest.Items
                .Where(i => i.Name.Length > 0)
                .GroupBy(i => QuestCatalog.BaseItemName(i.Name), StringComparer.OrdinalIgnoreCase)
                .Select(g => new QuestItemNeed { Name = g.First().Name, Qty = g.Max(i => Math.Max(1, i.Qty)) })
                .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (var i = 0; i < grouped.Count; i++)
            {
                var need = grouped[i];
                items.Add(new EpicQuestChecklistItem
                {
                    Id = $"epic-{QuestClassFilter.Abbrev(className).ToLowerInvariant()}-{i:000}-{StableKey(need.Name)}",
                    ClassName = className,
                    QuestName = quest.Name,
                    Reward = reward,
                    QuestItem = need.Name,
                    Qty = need.Qty,
                    Source = SourceLine(quest),
                });
            }
        }

        return items;
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

    private static string StableKey(string value)
    {
        var chars = value.ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        var compact = new string(chars);
        while (compact.Contains("--", StringComparison.Ordinal))
            compact = compact.Replace("--", "-", StringComparison.Ordinal);
        return compact.Trim('-');
    }
}
