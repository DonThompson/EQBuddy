namespace EQBuddy.Core;

/// <summary>
/// #99 (wizen's diagnosis, verbatim right): the wiki documents each class's Plane of
/// Sky tests on ONE aggregate page ("Ranger Plane of Sky Tests"), and the harvest
/// faithfully turned each page into one quest — so the tracker demanded every ranger
/// item at once and reported progress against the union. The real per-test structure
/// already ships in <see cref="SkyQuestDefaults"/> (reward ↔ turn-in items ↔ NPC,
/// the Sky card's own data), so at load each aggregate entry is REPLACED with one
/// quest per reward. Pattern-based on the page names, so the weekly catalog harvest
/// keeps regenerating without re-breaking this — and if a class page is missing from
/// a harvest, its split simply doesn't run.
/// </summary>
public static class SkyTestSplit
{
    public static void Apply(QuestCatalog catalog)
    {
        foreach (var classGroup in SkyQuestDefaults.Items.GroupBy(i => i.ClassName))
        {
            var aggregate = catalog.Quests.FirstOrDefault(q =>
                q.Name.Equals($"{classGroup.Key} Plane of Sky Tests", StringComparison.OrdinalIgnoreCase));
            if (aggregate is null) continue;
            catalog.Quests.Remove(aggregate);

            foreach (var reward in classGroup.GroupBy(i => i.Reward))
                catalog.Quests.Add(new QuestEntry
                {
                    Name = $"{classGroup.Key} Sky Test: {reward.Key}",
                    // The aggregate page stays the walkthrough — that's where the
                    // wiki documents the individual test.
                    Url = aggregate.Url,
                    StartZone = "Plane of Sky",
                    QuestGiver = reward.First().Npc,
                    Classes = classGroup.Key,
                    Items = reward.Select(i => new QuestItemNeed { Name = i.QuestItem }).ToList(),
                    Rewards = [reward.Key],
                    Zones = ["Plane of Sky"],
                    Era = "Sky",
                });
        }
    }
}
