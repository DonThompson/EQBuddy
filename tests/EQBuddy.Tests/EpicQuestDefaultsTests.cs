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
    public void CarriesClassicAvailabilityFromWikiMetadata()
    {
        var bardItems = EpicQuestDefaults.Items().Where(i => i.ClassName == "Bard").ToList();

        Assert.Contains(bardItems, i => i.QuestItem.Contains("Torch of Misty") && !i.AvailableInClassic);
        Assert.Contains(bardItems, i => i.QuestItem.Contains("Konia Swiftfoot") && i.AvailableInClassic);
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
    public void ResolvesItemNamesFromTheClassEpicCatalog()
    {
        var bardItems = EpicQuestDefaults.Items().Where(i => i.ClassName == "Bard").ToList();

        // A loot step names its drop; a multi-item turn-in step names each piece.
        var backbone = bardItems.Single(i => i.QuestItem.Contains("Phinigel Autropos"));
        Assert.Contains("Kedge Backbone", backbone.ItemNames);
        var turnIn = bardItems.Single(i => i.QuestItem.StartsWith("Give Forpar's Note to Himself"));
        Assert.Contains("Forpar's Note to Himself", turnIn.ItemNames);
        Assert.Contains("Kedge Backbone", turnIn.ItemNames);
        Assert.Contains("Amygdalan Tendril", turnIn.ItemNames);
    }

    [Fact]
    public void ProseOnlyStepsGetNoItemNames()
    {
        var items = EpicQuestDefaults.Items();

        // "loot his head" never says "Maligar's Head"; a hail mentions no item at
        // all — neither can be proven by a loot line, so neither auto-ticks.
        Assert.Empty(items.Single(i => i.ClassName == "Bard" &&
            i.QuestItem.Contains("Enraged Doppleganger")).ItemNames);
        Assert.Empty(items.Single(i => i.ClassName == "Shadow Knight" &&
            i.QuestItem.Contains("Hail Kurron Ni")).ItemNames);
    }

    [Fact]
    public void MostStepsResolveAtLeastOneItemName()
    {
        // The 2026-08-13 baseline: 392 of 486 rows mention a catalog item. Guard the
        // ballpark, not the exact count — data refreshes may move it a little, but a
        // matcher regression would crater it.
        var items = EpicQuestDefaults.Items();
        Assert.True(items.Count(i => i.ItemNames.Count > 0) > 350,
            $"only {items.Count(i => i.ItemNames.Count > 0)} of {items.Count} rows matched");
    }

    [Fact]
    public void ANameInsideALongerMatchedNameIsTheLongerNamesMention()
    {
        var names = EpicQuestDefaults.MatchItemNames(
            "Give An Undead Bard your Mystical Lute Body.",
            ["Mystical Lute", "Mystical Lute Body"]);

        Assert.Equal(new[] { "Mystical Lute Body" }, names);
    }

    [Fact]
    public void CaseVariantCatalogDuplicatesCollapseToOne()
    {
        var names = EpicQuestDefaults.MatchItemNames(
            "receive Maestro's Symphony Page 24 Top",
            ["Maestro's Symphony Page 24 Top", "Maestro's Symphony Page 24 top"]);

        Assert.Equal(new[] { "Maestro's Symphony Page 24 Top" }, names);
    }

    [Fact]
    public void AGluedSubstringIsNotAMention()
    {
        // Boundary check: "Woe" must not match inside "Woes".
        Assert.Empty(EpicQuestDefaults.MatchItemNames("A tale of many Woes", ["Woe"]));
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
