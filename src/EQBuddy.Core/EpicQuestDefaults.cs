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
}
