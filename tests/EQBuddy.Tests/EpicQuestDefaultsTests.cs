using EQBuddy.Core;

namespace EQBuddy.Tests;

public sealed class EpicQuestDefaultsTests
{
    [Fact]
    public void BuildsEpicChecklistFromQuestCatalog()
    {
        var items = EpicQuestDefaults.Items();

        Assert.Contains(items, i => i.ClassName == "Monk" && i.Reward.Contains("Celestial Fists"));
        Assert.Contains(items, i => i.ClassName == "Shadow Knight" && i.Reward.Contains("Innoruuk's Curse"));
        Assert.Contains(items, i => i.ClassName == "Wizard" && i.QuestItem == "Staff of Gabstik");
        Assert.DoesNotContain(items, i => i.ClassName == "Beastlord");
        Assert.DoesNotContain(items, i => i.ClassName == "Berserker");
    }

    [Fact]
    public void DedupesCaseOnlyCatalogVariants()
    {
        var bardItems = EpicQuestDefaults.Items()
            .Where(i => i.ClassName == "Bard")
            .Select(i => i.QuestItem)
            .ToList();

        Assert.Equal(bardItems.Count, bardItems.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
