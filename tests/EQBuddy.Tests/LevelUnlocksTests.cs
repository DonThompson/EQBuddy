using EQBuddy.Core;
using EQBuddy.UI.Shared;

namespace EQBuddy.Tests;

/// <summary>Over the real embedded AA catalog — these assert facts the eqlwiki harvest
/// recorded (2026-08-06), so a regenerated catalog that loses a level requirement or a
/// class column fails here, not on someone's ding.</summary>
public class LevelUnlocksTests
{
    [Fact]
    public void ClassAbilityAppearsAtItsLevelForItsClassOnly()
    {
        // Lay on Hands: Paladin, level 6 — the archetypal "what did I just unlock".
        Assert.Contains(LevelUnlocks.UnlocksAt(["Paladin"], 6), a => a.Name == "Lay on Hands");
        Assert.DoesNotContain(LevelUnlocks.UnlocksAt(["Warrior"], 6), a => a.Name == "Lay on Hands");
        // Case-insensitive like every class list in the app.
        Assert.Contains(LevelUnlocks.UnlocksAt(["paladin"], 6), a => a.Name == "Lay on Hands");
    }

    [Fact]
    public void MultiClassUnionSeesEveryPickedClass()
    {
        // Legends allows up to three active classes; level 12 is the "Unbound" wave.
        var unlocks = LevelUnlocks.UnlocksAt(["Warrior", "Cleric"], 12);
        Assert.Contains(unlocks, a => a.Name == "Heroic Leap");     // Warrior
        Assert.Contains(unlocks, a => a.Name == "Unbound Boon");    // Cleric
        Assert.DoesNotContain(unlocks, a => a.Name == "Unbound Nature");   // Druid — not picked
    }

    [Fact]
    public void ClassAgnosticCategoriesShowRegardlessOfClasses()
    {
        // Rampage is an Archetype row (level 30) with no class column in the wiki —
        // included even with no classes picked, labeled rather than guessed.
        Assert.Contains(LevelUnlocks.UnlocksAt([], 30), a => a.Name == "Rampage");
        var warrior = LevelUnlocks.UnlocksAt(["Warrior"], 30);
        Assert.Contains(warrior, a => a.Name == "Warrior's Endurance");
        Assert.Contains(warrior, a => a.Name == "Rampage");
    }

    [Fact]
    public void ClassRowsLeadTheList()
    {
        // Monk 15: two Class rows (Dragon Force, Purify Body), then Archetype
        // (Double Riposte) — the ding's headline is "my class got a new button".
        var unlocks = LevelUnlocks.UnlocksAt(["Monk"], 15);
        Assert.True(unlocks.Count >= 3);
        Assert.Equal("Class", unlocks[0].Category);
        Assert.Equal("Class", unlocks[1].Category);
        Assert.Contains(unlocks.Skip(2), a => a.Name == "Double Riposte");
    }

    [Fact]
    public void LevelWithNothingIsEmptyNotPadded()
    {
        // No AA in the catalog requires level 2 for anyone.
        Assert.Empty(LevelUnlocks.UnlocksAt(["Warrior", "Cleric", "Wizard"], 2));
    }

    [Fact]
    public void NextJumpsToTheNextRealMilestone()
    {
        // Paladin after the level-6 Lay on Hands ding: level 8 belongs to Rangers
        // alone, so the next Paladin milestone is 10 (Exodus, Archetype).
        var next = LevelUnlocks.Next(["Paladin"], 6);
        Assert.NotNull(next);
        Assert.Equal(10, next!.Value.Level);
        Assert.Contains(next.Value.Unlocks, a => a.Name == "Exodus");
    }

    [Fact]
    public void NextPastTheLastMilestoneIsNull()
    {
        // 50 is the catalog's highest level requirement — nothing to preview beyond it.
        Assert.Null(LevelUnlocks.Next(["Cleric"], 50));
    }

    [Fact]
    public void RowValueNamesTheSourceAndTheRankSpan()
    {
        // Class row, single rank: just the class.
        Assert.Equal("Warrior", LevelUnlockText.RowValue(AaCatalog.Find("Heroic Leap")!));
        // Class row with ranks to buy: class plus rank count.
        Assert.Equal("Paladin · 10 ranks", LevelUnlockText.RowValue(AaCatalog.Find("Lay on Hands")!));
        // Class-agnostic multi-rank row: category plus rank count.
        Assert.Equal("Archetype · 3 ranks", LevelUnlockText.RowValue(AaCatalog.Find("Healing Adept")!));
    }

    [Fact]
    public void NextLabelFoldsAndCounts()
    {
        Assert.Equal("▸ At level 35: 2 new AA abilities", LevelUnlockText.NextLabel(35, 2, expanded: false));
        Assert.Equal("▾ At level 6: 1 new AA ability", LevelUnlockText.NextLabel(6, 1, expanded: true));
    }
}
