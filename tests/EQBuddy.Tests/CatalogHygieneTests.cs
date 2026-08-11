using EQBuddy.Core;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>The catalog-wide aggregate-page audit (David's ask, 2026-08-11, following
/// #99): index pages dropped, collection pages flagged and excluded from all
/// progress/ready math, real quests — including the giant epics — untouched.</summary>
public class CatalogHygieneTests
{
    private static readonly QuestCatalog Cat = QuestCatalog.LoadEmbedded();

    [Fact]
    public void IndexPagesAreGone()
    {
        Assert.DoesNotContain(Cat.Quests, q => q.Name == "Popular Quests by Level");
        Assert.DoesNotContain(Cat.Quests, q => q.Name == "Class Race Quest List");
        Assert.DoesNotContain(Cat.Quests, q => q.Name == "Velious Class Armor Comparisons");
    }

    [Fact]
    public void CollectionPagesAreFlaggedRealQuestsAreNot()
    {
        Assert.True(Assert.Single(Cat.Quests, q => q.Name == "Bard Skyshrine Armor Quests").Collection);
        Assert.True(Assert.Single(Cat.Quests, q => q.Name == "Coldain Ring Quests").Collection);
        Assert.True(Assert.Single(Cat.Quests, q => q.Name == "Plane of Sky Keys").Collection);

        // The giants that are genuinely ONE quest keep their standing.
        Assert.False(Assert.Single(Cat.Quests, q => q.Name == "Druid Epic Quest").Collection);
        Assert.False(Assert.Single(Cat.Quests, q => q.Name == "10th Coldain Ring Quest").Collection);
        // The Sky split's products are real quests too.
        Assert.False(Assert.Single(Cat.Quests, q => q.Name == "Wizard Sky Test: Nargon's Staff").Collection);
    }

    [Fact]
    public void CollectionsNeverComputeProgressOrReadReady()
    {
        var collection = Cat.Quests.First(q => q.Collection && q.Items.Count >= 10);
        // Own every item on the union page — a real quest would scream "ready".
        var progress = collection.Items
            .Select(i => new QuestItemProgress(i.Name, i.Qty, i.Qty)).ToList();
        var m = new QuestMatch(collection, progress.Count, progress.Count, progress);

        Assert.False(m.Complete);
        Assert.Equal(0, m.Fraction);
        Assert.Equal(0, m.ReadyCount);
    }
}
