using EQBuddy.Core;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>
/// The Gear card's auto-done: single-owner list, so a name match ticks — the only
/// judgment call is upgrade tiers, and the rule is AT-OR-ABOVE (a base drop never
/// ticks a "+5" wish; an obtained "+7" satisfies a "+5" or base wish).
/// </summary>
public class GearLootAutoCheckTests
{
    private static List<GearChecklistItem> Checklist() =>
    [
        new() { Slot = "Head", Item = "Carmine Turban" },
        new() { Slot = "Waist", Item = "Crushbone Belt +5" },
        new() { Slot = "Chest", IsExaltation = true, Item = "Haste Gem",
                ExaltationEffect = "Enhancement Haste I" },
    ];

    [Fact]
    public void ALootedItemTicksItsRowCaseInsensitively()
    {
        var list = Checklist();
        var changed = GearLootAutoCheck.Apply(list, "carmine turban", 1);

        Assert.True(changed);
        Assert.True(list.Single(i => i.Item == "Carmine Turban").Acquired);
        Assert.False(list.Single(i => i.Item == "Crushbone Belt +5").Acquired);
    }

    [Fact]
    public void AnUnrelatedItemTicksNothing()
    {
        var list = Checklist();
        Assert.False(GearLootAutoCheck.Apply(list, "Rusty Dagger", 3));
        Assert.All(list, i => Assert.False(i.Acquired));
    }

    [Fact]
    public void ABaseDropDoesNotTickAnUpgradedWish()
    {
        // The wish IS the "+5" form: ticking on the first base drop would silently
        // lie about the merges still to do.
        var list = Checklist();
        Assert.False(GearLootAutoCheck.Apply(list, "Crushbone Belt", 1));
        Assert.False(list.Single(i => i.Item == "Crushbone Belt +5").Acquired);
    }

    [Fact]
    public void AnAtOrAboveTierSatisfiesTheWish()
    {
        var atList = Checklist();
        Assert.True(GearLootAutoCheck.Apply(atList, "Crushbone Belt +5", 1));
        Assert.True(atList.Single(i => i.Item == "Crushbone Belt +5").Acquired);

        var aboveList = Checklist();
        Assert.True(GearLootAutoCheck.Apply(aboveList, "Crushbone Belt +7", 1));
        Assert.True(aboveList.Single(i => i.Item == "Crushbone Belt +5").Acquired);
    }

    [Fact]
    public void AnUpgradedDropSatisfiesABaseWish()
    {
        // Owning the better tier is owning the item.
        var list = Checklist();
        Assert.True(GearLootAutoCheck.Apply(list, "Carmine Turban +3", 1));
        Assert.True(list.Single(i => i.Item == "Carmine Turban").Acquired);
    }

    [Fact]
    public void AnExaltationArrivingByMergeTicksItsRow()
    {
        // Exaltations reach the player via CraftEvent (snapshot.Crafted) as often as
        // by loot; the helper only sees names, so the same call covers both streams.
        var list = Checklist();
        Assert.True(GearLootAutoCheck.Apply(list, "Haste Gem", 1));
        Assert.True(list.Single(i => i.IsExaltation).Acquired);
    }

    [Fact]
    public void TheObtainedCountCapsRowsTicked()
    {
        // One looted ring is one tick even when EAR 1 and EAR 2 both wish for it.
        var list = new List<GearChecklistItem>
        {
            new() { Slot = "Ear 1", Item = "Ivandyr's Hoop" },
            new() { Slot = "Ear 2", Item = "Ivandyr's Hoop" },
        };
        GearLootAutoCheck.Apply(list, "Ivandyr's Hoop", 1);

        Assert.Equal(1, list.Count(i => i.Acquired));
    }

    [Fact]
    public void AlreadyAcquiredRowsAreLeftAloneAndReportNoChange()
    {
        var list = Checklist();
        list[0].Acquired = true;
        Assert.False(GearLootAutoCheck.Apply(list, "Carmine Turban", 1));
    }

    [Fact]
    public void AnInventoryDumpTicksOwnedItemsAtOrAboveTheWishedTier()
    {
        // Raw dump rows keep their "+N" (the base-folded Counts map is why the pass
        // reads Entries): a held "+6" belt proves the "+5" wish done; two turbans in
        // two rows still honor the count cap.
        var list = new List<GearChecklistItem>
        {
            new() { Slot = "Head", Item = "Carmine Turban" },
            new() { Slot = "Waist", Item = "Crushbone Belt +5" },
            new() { Slot = "Legs", Item = "Ravenscale Leggings +2" },
        };
        var changed = GearLootAutoCheck.ApplyInventory(list,
        [
            new InventoryFile.Entry("General 1-Slot1", "Carmine Turban", 1),
            new InventoryFile.Entry("Waist", "Crushbone Belt +6", 1),
            new InventoryFile.Entry("General 2-Slot3", "Ravenscale Leggings", 1),
        ]);

        Assert.True(changed);
        Assert.True(list.Single(i => i.Item == "Carmine Turban").Acquired);
        Assert.True(list.Single(i => i.Item == "Crushbone Belt +5").Acquired);
        // The base leggings do not satisfy the "+2" wish.
        Assert.False(list.Single(i => i.Item == "Ravenscale Leggings +2").Acquired);
    }
}
