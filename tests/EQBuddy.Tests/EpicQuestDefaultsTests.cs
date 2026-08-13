using EQBuddy.Core;

namespace EQBuddy.Tests;

public sealed class EpicQuestDefaultsTests
{
    [Fact]
    public void BuildsEpicChecklistFromWikiChecklist()
    {
        var items = EpicQuestDefaults.Items();

        Assert.Contains(items, i => i.ClassName == "Monk" && i.QuestItem.Contains("Demon Fangs"));
        Assert.Contains(items, i => i.ClassName == "Shadow Knight" && i.QuestItem.Contains("Cell Key") && i.QuestItem.Contains("Caradon"));
        Assert.Contains(items, i => i.ClassName == "Wizard" && i.Section == "Post-Revamp Checklist");
        Assert.DoesNotContain(items, i => i.ClassName == "Beastlord");
        Assert.DoesNotContain(items, i => i.ClassName == "Berserker");
    }

    [Fact]
    public void PreservesWikiSections()
    {
        var bardItems = EpicQuestDefaults.Items().Where(i => i.ClassName == "Bard").ToList();

        Assert.Contains(bardItems, i => i.Section == "Maestro's Symphony Page 24 Top" &&
                                        i.QuestItem.Contains("Konia Swiftfoot"));
        Assert.Contains(bardItems, i => i.Section == "Maestro's Symphony Page 24 Bottom" &&
                                        i.QuestItem.Contains("Baenar Swiftsong"));
    }

    [Fact]
    public void IncludesEveryClassicEpicClassWithChecklistRows()
    {
        var items = EpicQuestDefaults.Items();
        var classes = new[]
        {
            "Bard", "Cleric", "Druid", "Enchanter", "Magician", "Monk", "Necromancer",
            "Paladin", "Ranger", "Rogue", "Shadow Knight", "Shaman", "Warrior", "Wizard",
        };

        foreach (var className in classes)
            Assert.True(items.Count(i => i.ClassName == className) > 0, className);
    }
}
