using EQBuddy.Core;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>#99 (wizen): the per-class "Plane of Sky Tests" wiki pages are AGGREGATES,
/// and treating each as one quest demanded every class item at once. The split turns
/// each into one quest per reward, driven by the Sky card's own reward↔items data.</summary>
public class SkyTestSplitTests
{
    [Fact]
    public void AggregatePagesAreGoneAndPerRewardQuestsExist()
    {
        var cat = QuestCatalog.LoadEmbedded();

        Assert.DoesNotContain(cat.Quests, q =>
            q.Name.EndsWith("Plane of Sky Tests", StringComparison.OrdinalIgnoreCase));

        // Spot-check a known reward from the Sky defaults (Wizard: Nargon's Staff,
        // three turn-in items, from Wizard Schrock).
        var nargon = Assert.Single(cat.Quests, q => q.Name == "Wizard Sky Test: Nargon's Staff");
        Assert.Equal("Wizard Schrock", nargon.QuestGiver);
        Assert.Equal(3, nargon.Items.Count);
        Assert.Contains(nargon.Items, i => i.Name == "Efreeti War Staff");
        Assert.Equal("Plane of Sky", nargon.StartZone);
        Assert.Equal("Sky", nargon.Era);
        Assert.Equal("Wizard", nargon.Classes);
        Assert.Contains("Nargon's Staff", nargon.Rewards);
    }

    [Fact]
    public void EverySplitQuestIsSmallAndClassScoped()
    {
        var cat = QuestCatalog.LoadEmbedded();
        var split = cat.Quests.Where(q => q.Name.Contains(" Sky Test: ")).ToList();

        // 14+ classes × several tests each — and no test needs more than a handful
        // of items (the aggregate disease was dozens-at-once).
        Assert.True(split.Count >= 60, $"only {split.Count} split quests");
        Assert.All(split, q =>
        {
            Assert.InRange(q.Items.Count, 1, 5);
            Assert.True(q.Classes.Length > 0, $"{q.Name} lost its class");
            Assert.Single(q.Rewards);
        });

        // The class filter respects the scoping: a Wizard selection sees Wizard
        // tests, not Monk ones.
        var wizardOnly = split.Where(q => QuestClassFilter.MatchesAny(q.Classes, ["Wizard"])).ToList();
        Assert.NotEmpty(wizardOnly);
        Assert.DoesNotContain(wizardOnly, q => q.Name.StartsWith("Monk "));
    }
}
