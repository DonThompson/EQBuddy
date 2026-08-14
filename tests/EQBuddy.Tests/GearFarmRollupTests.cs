using EQBuddy.Core;
using EQBuddy.UI.Shared;

namespace EQBuddy.Tests;

public sealed class GearFarmRollupTests
{
    private static GearChecklistItem Wish(string item, bool acquired = false) =>
        new() { Slot = "Slot", Item = item, Acquired = acquired };

    private static Func<string, ItemCatalog.Record?> Catalog(params ItemCatalog.Record[] records)
    {
        var byName = records.ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);
        return name => byName.TryGetValue(name, out var r) ? r : null;
    }

    [Fact]
    public void BucketsDropsQuestsCraftedAndUnknown()
    {
        var dropper = Wish("Steel Helm");
        var questReward = Wish("Testament of Vanear");
        var flaggedOnly = Wish("Torn Page");
        var craftOnly = Wish("Silver Chitin Wristband");
        var mystery = Wish("Nameless Trinket");

        var groups = GearFarmRollup.Build(
            [dropper, questReward, flaggedOnly, craftOnly, mystery],
            Catalog(
                new ItemCatalog.Record { Name = "Steel Helm", DropZones = ["Unrest"] },
                new ItemCatalog.Record { Name = "Testament of Vanear", Quests = ["A Cleric's Test"] },
                new ItemCatalog.Record { Name = "Torn Page", QuestFlagged = true },
                new ItemCatalog.Record { Name = "Silver Chitin Wristband", Recipes = ["Jewelcraft"] }));

        Assert.Collection(groups,
            g => { Assert.Equal("Unrest", g.Zone); Assert.Equal([dropper], g.Items); },
            g => { Assert.Equal(GearFarmRollup.QuestsHeading, g.Zone); Assert.Equal([questReward, flaggedOnly], g.Items); },
            g => { Assert.Equal(GearFarmRollup.CraftedHeading, g.Zone); Assert.Equal([craftOnly], g.Items); },
            g => { Assert.Equal(GearFarmRollup.NoDataHeading, g.Zone); Assert.Equal([mystery], g.Items); });
    }

    [Fact]
    public void MultiZoneItemAppearsUnderEveryZoneItDropsIn()
    {
        var wish = Wish("Runed Bolster Belt");

        var groups = GearFarmRollup.Build([wish], Catalog(
            new ItemCatalog.Record { Name = "Runed Bolster Belt", DropZones = ["Lake Rathetear", "Guk", "guk"] }));

        // Duplicate zone names in the record fold; the item itself repeats per zone.
        Assert.Equal(["Guk", "Lake Rathetear"], groups.Select(g => g.Zone));
        Assert.All(groups, g => Assert.Equal([wish], g.Items));
    }

    [Fact]
    public void AcquiredWishesNeverAppear()
    {
        var open = Wish("Steel Helm");
        var done = Wish("Steel Helm", acquired: true);
        var catalog = Catalog(new ItemCatalog.Record { Name = "Steel Helm", DropZones = ["Unrest"] });

        var groups = GearFarmRollup.Build([open, done], catalog);
        Assert.Equal([open], Assert.Single(groups).Items);

        // A fully acquired list rolls up to nothing — the caller draws the "done" text.
        Assert.Empty(GearFarmRollup.Build([done], catalog));
    }

    [Fact]
    public void UpgradeTierWishesJoinOnTheBaseItemName()
    {
        var wish = Wish("Crushbone Belt +5");
        string? asked = null;

        var groups = GearFarmRollup.Build([wish],
            name =>
            {
                asked = name;
                return new ItemCatalog.Record { Name = "Crushbone Belt", DropZones = ["Crushbone"] };
            });

        Assert.Equal("Crushbone Belt", asked);
        Assert.Equal([wish], Assert.Single(groups).Items);
    }

    [Fact]
    public void ZonesOrderNearestFirstWithUnrankableLastThenBuckets()
    {
        var catalog = Catalog(
            new ItemCatalog.Record { Name = "Helm", DropZones = ["Far Zone"] },
            new ItemCatalog.Record { Name = "Belt", DropZones = ["Near Zone"] },
            new ItemCatalog.Record { Name = "Ring", DropZones = ["Off-Graph Zone"] },
            new ItemCatalog.Record { Name = "Mask", Quests = ["A Quest"] });
        var checklist = new[] { Wish("Helm"), Wish("Belt"), Wish("Ring"), Wish("Mask") };
        var hops = new Dictionary<string, int?>
        {
            ["Far Zone"] = 4, ["Near Zone"] = 1, ["Off-Graph Zone"] = null,
        };

        var ranked = GearFarmRollup.Build(checklist, catalog, z => hops[z]);
        Assert.Equal(["Near Zone", "Far Zone", "Off-Graph Zone", GearFarmRollup.QuestsHeading],
            ranked.Select(g => g.Zone));

        // No current zone to measure from: alphabetical, buckets still last.
        var flat = GearFarmRollup.Build(checklist, catalog);
        Assert.Equal(["Far Zone", "Near Zone", "Off-Graph Zone", GearFarmRollup.QuestsHeading],
            flat.Select(g => g.Zone));
    }

    [Fact]
    public void HeadingsCarryCountAndDistance()
    {
        var items = new[] { Wish("Helm"), Wish("Belt") };
        Assert.Equal("Unrest (2) · you're here",
            GearFarmRollup.Heading(new GearFarmRollup.ZoneGroup("Unrest", 0, items)));
        Assert.Equal("Unrest (2) · 1 zone away",
            GearFarmRollup.Heading(new GearFarmRollup.ZoneGroup("Unrest", 1, items)));
        Assert.Equal("Unrest (2) · 3 zones away",
            GearFarmRollup.Heading(new GearFarmRollup.ZoneGroup("Unrest", 3, items)));
        Assert.Equal("Quests (2)",
            GearFarmRollup.Heading(new GearFarmRollup.ZoneGroup("Quests", null, items)));
    }
}
