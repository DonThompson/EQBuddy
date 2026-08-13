using EQBuddy.Core;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>
/// The embedded item catalog (the item-knowledge layer, 2026-08-13): ~11k items
/// from Category:Items, parsed at build time through the app's own parsers. These
/// are the same style of gates every other catalog carries — size floors and
/// spot-checks that catch a bad regeneration before it ships.
/// </summary>
public class ItemCatalogTests
{
    [Fact]
    public void EmbeddedCatalogLoadsAndIsComprehensive()
    {
        var cat = ItemCatalog.Default;
        Assert.True(cat.Count >= 9000, $"suspiciously small item catalog: {cat.Count}");
    }

    [Fact]
    public void KnownItemsResolveWithStatsAndKnowledge()
    {
        // A classic weapon: stats block parsed into comparable numbers.
        var rusty = ItemCatalog.Default.Find("Rusty Broad Sword");
        Assert.NotNull(rusty);
        Assert.Contains("PRIMARY", rusty!.Slots);
        Assert.NotNull(rusty.Dmg);
        Assert.NotNull(rusty.Delay);

        // Upgrade suffixes fold to the base name the wiki titles.
        Assert.NotNull(ItemCatalog.Default.Find("Rusty Broad Sword +4"));

        // A quest staple carries its knowledge: what it's for.
        var chips = ItemCatalog.Default.Find("Bone Chips");
        Assert.NotNull(chips);
        Assert.True(chips!.QuestFlagged || chips.Quests is { Count: > 0 },
            "Bone Chips should know it's a quest item");
    }

    [Fact]
    public void TheCachedPeekFallsBackToTheCatalog()
    {
        // A service with an empty live cache and a fetcher that always misses must
        // still answer from the catalog — the instant/offline layer.
        var dir = Directory.CreateTempSubdirectory("eqbuddy-itemcat-").FullName;
        try
        {
            var service = new EqlWikiItemService(dir, _ => Task.FromResult<string?>(null));
            var info = service.CachedInfo("Rusty Broad Sword");
            Assert.NotNull(info);
            Assert.Contains(info!.StatsLines, l => l.StartsWith("Slot:", StringComparison.OrdinalIgnoreCase));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task AWikiMissLandsOnTheCatalogWithHonestProvenance()
    {
        var dir = Directory.CreateTempSubdirectory("eqbuddy-itemcat2-").FullName;
        try
        {
            var service = new EqlWikiItemService(dir, _ => Task.FromResult<string?>(null));
            var result = await service.LookupAsync("Rusty Broad Sword");
            Assert.Equal(ItemLookupState.Catalog, result.State);
            Assert.NotNull(result.Item);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
