using EQBuddy.Core;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>
/// The Sky checklist's loot auto-tick scoping: shared items respect the player's
/// class filter / active tab (#98), single-class items tick their class no matter
/// what tab is showing (#106 — both reported by bjstrange, five days apart).
/// </summary>
public class SkyLootAutoCheckTests
{
    private static List<SkyQuestChecklistItem> Checklist() =>
    [
        new() { ClassName = "Berserker", QuestItem = "Great Staff", Reward = "Test of Fury" },
        new() { ClassName = "Druid", QuestItem = "Wind Rune Azia", Reward = "Test of Nature" },
        new() { ClassName = "Monk", QuestItem = "Wind Rune Azia", Reward = "Test of Fists" },
        new() { ClassName = "Wizard", QuestItem = "Wind Rune Azia", Reward = "Test of Frost" },
    ];

    [Fact]
    public void ASingleClassItemTicksItsClassWhateverTabIsActive()
    {
        // #106 verbatim: active tab Druid, no class filter, Berserker staff drops.
        var list = Checklist();
        var changed = SkyLootAutoCheck.Apply(list, "Great Staff", 1, [], activeTab: "Druid");

        Assert.True(changed);
        Assert.True(list.Single(i => i.ClassName == "Berserker").Acquired);
    }

    [Fact]
    public void ASharedItemStaysScopedToTheActiveTab()
    {
        // One physical rune must not tick three class plans (#98's careful side).
        var list = Checklist();
        SkyLootAutoCheck.Apply(list, "Wind Rune Azia", 1, [], activeTab: "Druid");

        Assert.True(list.Single(i => i.ClassName == "Druid" && i.QuestItem == "Wind Rune Azia").Acquired);
        Assert.False(list.Single(i => i.ClassName == "Monk").Acquired);
        Assert.False(list.Single(i => i.ClassName == "Wizard").Acquired);
    }

    [Fact]
    public void ASharedItemTicksEveryClassInThePlayersFilter()
    {
        // #98's shipped behavior: the multiclass set all tick, one slot each.
        var list = Checklist();
        SkyLootAutoCheck.Apply(list, "Wind Rune Azia", 1, ["Druid", "Monk"], activeTab: "Wizard");

        Assert.True(list.Single(i => i.ClassName == "Druid" && i.QuestItem == "Wind Rune Azia").Acquired);
        Assert.True(list.Single(i => i.ClassName == "Monk").Acquired);
        Assert.False(list.Single(i => i.ClassName == "Wizard").Acquired);
    }

    [Fact]
    public void TheLootBudgetCapsSlotsPerClass()
    {
        var list = new List<SkyQuestChecklistItem>
        {
            new() { ClassName = "Berserker", QuestItem = "Great Staff", Reward = "Test of Fury" },
            new() { ClassName = "Berserker", QuestItem = "Great Staff", Reward = "Test of Rage" },
        };
        SkyLootAutoCheck.Apply(list, "Great Staff", 1, [], activeTab: "Druid");

        Assert.Equal(1, list.Count(i => i.Acquired));   // one staff, one tick
    }

    // ---- #106 round two: an item exactly TWO classes want, neither in the lens ----

    private static List<SkyQuestChecklistItem> TwoClassChecklist() =>
    [
        new() { ClassName = "Berserker", QuestItem = "Twisted Staff", Reward = "Test of Fury" },
        new() { ClassName = "Necromancer", QuestItem = "Twisted Staff", Reward = "Test of Decay" },
        new() { ClassName = "Druid", QuestItem = "Wind Rune Azia", Reward = "Test of Nature" },
    ];

    [Fact]
    public void AMultiClassItemNobodyClaimsParksOneFlaggedTick()
    {
        // bjstrange's staff: wanted by Berserker AND Necromancer, looted on the Druid
        // tab with no filter. Previously lost entirely; now the first owning class
        // gets the tick, flagged for the player to move.
        var list = TwoClassChecklist();
        var changed = SkyLootAutoCheck.Apply(list, "Twisted Staff", 1, [], activeTab: "Druid");

        Assert.True(changed);
        var ticked = Assert.Single(list.Where(i => i.Acquired));
        Assert.Equal("Berserker", ticked.ClassName);       // first owning class, deterministic
        Assert.True(ticked.AcquiredUnassigned);            // wears the *
    }

    [Fact]
    public void ALensMatchTicksNormallyWithoutTheFlag()
    {
        var list = TwoClassChecklist();
        SkyLootAutoCheck.Apply(list, "Twisted Staff", 1, ["Necromancer"], activeTab: "Druid");

        var ticked = Assert.Single(list.Where(i => i.Acquired));
        Assert.Equal("Necromancer", ticked.ClassName);
        Assert.False(ticked.AcquiredUnassigned);           // a real match is no guess
    }

    [Fact]
    public void TheParkedTickRespectsTheLootBudget()
    {
        var list = TwoClassChecklist();
        SkyLootAutoCheck.Apply(list, "Twisted Staff", 2, [], activeTab: "Druid");

        // Two staffs looted: both open slots get one, both flagged.
        Assert.Equal(2, list.Count(i => i.Acquired));
        Assert.All(list.Where(i => i.Acquired), i => Assert.True(i.AcquiredUnassigned));
    }
}
