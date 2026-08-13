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

    [Fact]
    public void ProvidesGuideAndItemSourceHints()
    {
        var guide = Assert.IsType<EpicQuestGuide>(EpicQuestDefaults.GuideFor("Shadow Knight"));

        Assert.Contains(guide.Steps, s => s.Contains("Corrupted Ghoulbane"));
        Assert.Contains(guide.Steps, s => s.Contains("Lhranc"));

        var items = EpicQuestDefaults.Items();
        Assert.Contains(items, i => i.ClassName == "Monk" &&
                                    i.QuestItem == "Demon Fangs" &&
                                    i.Source.Contains("Xenevorash"));
        Assert.Contains(items, i => i.ClassName == "Shaman" &&
                                    i.QuestItem == "Child's Tear" &&
                                    i.Source.Contains("Plane of Fear"));
        Assert.Contains(items, i => i.ClassName == "Shadow Knight" &&
                                    i.QuestItem == "Blade of Abrogation" &&
                                    i.Source.Contains("Plane of Sky"));
        Assert.Contains(items, i => i.ClassName == "Shadow Knight" &&
                                    i.QuestItem == "Cell Key" &&
                                    i.Source.Contains("mimic/chest") &&
                                    i.Source.Contains("The Hole"));
    }

    [Fact]
    public void OrdersEpicItemsByQuestRoute()
    {
        var items = EpicQuestDefaults.Items()
            .Where(i => i.ClassName == "Shadow Knight")
            .OrderBy(i => i.Order)
            .ThenBy(i => i.QuestItem, StringComparer.OrdinalIgnoreCase)
            .Select(i => i.QuestItem)
            .ToList();

        Assert.True(items.IndexOf("Darkforge Breastplate") < items.IndexOf("Cell Key"));
        Assert.True(items.IndexOf("Cell Key") < items.IndexOf("Innoruuk's Curse"));
    }
}
