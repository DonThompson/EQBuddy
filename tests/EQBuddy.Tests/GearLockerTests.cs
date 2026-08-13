using EQBuddy.Core;
using EQBuddy.UI.Shared;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>
/// The Gear Locker (#104): stats-block parsing, slot grouping, and the dominance
/// rule. "Outclassed" must be arithmetic — at least as good on every number both
/// items carry, strictly better on one — and class-locked items never outclass
/// anything the player can't trade them for.
/// </summary>
public class GearLockerTests
{
    // ---- the stats-block parser ----

    [Fact]
    public void ParsesAWeaponBlock()
    {
        var s = ItemStatsBlock.Parse(
        [
            "MAGIC ITEM LORE ITEM NO DROP",
            "Slot: PRIMARY",
            "Skill: 1H Slashing Atk Delay: 26",
            "DMG: 20",
            "STR: +15 WIS: +15",
            "WT: 3.0 Size: MEDIUM",
            "Class: PAL",
            "Race: ALL",
        ]);
        Assert.Equal(["PRIMARY"], s.Slots);
        Assert.Equal(20, s.Dmg);
        Assert.Equal(26, s.Delay);
        Assert.Equal(0.77, s.Ratio!.Value, 2);
        Assert.Equal("1H Slashing", s.Skill);
        Assert.Equal(15, s.Attributes["STR"]);
        Assert.Equal(["PAL"], s.Classes);
        Assert.True(s.Magic);
        Assert.True(s.Lore);
        Assert.True(s.NoDrop);
        Assert.True(s.Wearable);
    }

    [Fact]
    public void ParsesArmorAndMultiSlotJewelry()
    {
        var s = ItemStatsBlock.Parse(
        [
            "Slot: EAR",
            "AC: 4 HP: +25 MANA: +25",
            "SV FIRE: +5 SV COLD: +5",
            "Class: ALL",
        ]);
        Assert.Equal(4, s.Ac);
        Assert.Equal(25, s.Hp);
        Assert.Equal(25, s.Mana);
        Assert.Equal(5, s.Attributes["SV FIRE"]);
        Assert.Empty(s.Classes);            // ALL = unrestricted

        var multi = ItemStatsBlock.Parse(["Slot: PRIMARY SECONDARY"]);
        Assert.Equal(["PRIMARY", "SECONDARY"], multi.Slots);
    }

    [Fact]
    public void ASlotlessItemIsNotWearable()
    {
        var s = ItemStatsBlock.Parse(["QUEST ITEM", "WT: 0.1 Size: SMALL"]);
        Assert.False(s.Wearable);
        Assert.Null(s.Ac);
    }

    // ---- the Locker's grouping and dominance ----

    private static InventoryFile.Entry E(string loc, string name, int count = 1) =>
        new(loc, name, count);

    private static readonly Dictionary<string, ItemStatsBlock> Stats = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Rusty Broad Sword"] = ItemStatsBlock.Parse(["Slot: PRIMARY", "Skill: 1H Slashing Atk Delay: 40", "DMG: 4"]),
        ["Fine Steel Long Sword"] = ItemStatsBlock.Parse(["Slot: PRIMARY", "Skill: 1H Slashing Atk Delay: 32", "DMG: 8"]),
        ["Cloth Cap"] = ItemStatsBlock.Parse(["Slot: HEAD", "AC: 2"]),
        ["Bronze Helm"] = ItemStatsBlock.Parse(["Slot: HEAD", "AC: 8", "STR: +2"]),
        ["Sages Circlet"] = ItemStatsBlock.Parse(["Slot: HEAD", "AC: 3", "MANA: +30", "Class: ENC MAG NEC WIZ"]),
        ["Bone Chips"] = ItemStatsBlock.Parse(["QUEST ITEM"]),
    };

    private static List<GearSlotGroup> Build(IReadOnlyList<string> classes, params InventoryFile.Entry[] entries) =>
        GearLocker.Build(entries, n => Stats.GetValueOrDefault(n), classes);

    [Fact]
    public void GroupsBySlotDropsUnwearablesAndFoldsDuplicates()
    {
        var groups = Build([],
            E("Primary", "Fine Steel Long Sword"),
            E("General 1-Slot1", "Rusty Broad Sword"),
            E("General 2-Slot3", "Rusty Broad Sword"),
            E("General 1-Slot2", "Bone Chips", 40));

        var primary = Assert.Single(groups, g => g.Slot == "PRIMARY");
        Assert.Equal(2, primary.Rows.Count);
        var rusty = primary.Rows.Single(r => r.Name == "Rusty Broad Sword");
        Assert.Equal(2, rusty.Count);                       // two bag copies, one row
        Assert.DoesNotContain(groups, g => g.Rows.Any(r => r.Name == "Bone Chips"));
    }

    [Fact]
    public void AStrictlyBetterWeaponOutclassesTheWorse()
    {
        var groups = Build([],
            E("Primary", "Fine Steel Long Sword"),
            E("General 1-Slot1", "Rusty Broad Sword"));

        var rows = groups.Single(g => g.Slot == "PRIMARY").Rows;
        Assert.Equal("", rows.Single(r => r.Name == "Fine Steel Long Sword").OutclassedBy);
        Assert.Equal("Fine Steel Long Sword",
            rows.Single(r => r.Name == "Rusty Broad Sword").OutclassedBy);
    }

    [Fact]
    public void DifferentStrengthsMeanNobodyDominates()
    {
        // Bronze Helm has the AC; Sage's Circlet has the mana. Neither wins on
        // everything, so neither is a dump candidate.
        var groups = Build([],
            E("Head", "Bronze Helm"),
            E("Bank 1", "Sages Circlet"));

        Assert.All(groups.Single(g => g.Slot == "HEAD").Rows,
            r => Assert.Equal("", r.OutclassedBy));
    }

    [Fact]
    public void AClassLockedItemNeverOutclassesForTheWrongClass()
    {
        // The Circlet beats Cloth Cap on every number — but a Warrior can't wear it,
        // so the Cap survives for a Warrior and falls for an Enchanter.
        var forWarrior = Build(["WAR"], E("Head", "Cloth Cap"), E("Bank 1", "Sages Circlet"));
        Assert.Equal("", forWarrior.Single(g => g.Slot == "HEAD")
            .Rows.Single(r => r.Name == "Cloth Cap").OutclassedBy);

        var forEnchanter = Build(["ENC"], E("Head", "Cloth Cap"), E("Bank 1", "Sages Circlet"));
        Assert.Equal("Sages Circlet", forEnchanter.Single(g => g.Slot == "HEAD")
            .Rows.Single(r => r.Name == "Cloth Cap").OutclassedBy);
    }

    [Fact]
    public void UnfetchedItemsLandInTheirOwnHonestGroup()
    {
        var groups = Build([], E("Primary", "Mystery Blade +3"));
        var unknown = Assert.Single(groups, g => g.Slot == "STATS NOT FETCHED YET");
        var row = Assert.Single(unknown.Rows);
        Assert.Equal("Mystery Blade +3", row.Name);
        Assert.Equal("Mystery Blade", row.BaseName);        // +N folds for the wiki key
    }

    [Fact]
    public void WhereLabelsReadLikeThePlayerThinks()
    {
        Assert.Equal("worn · Primary", GearLocker.WhereLabel(E("Primary", "x")));
        Assert.Equal("General 2", GearLocker.WhereLabel(E("General 2-Slot5", "x")));
        Assert.Equal("Bank 3", GearLocker.WhereLabel(E("Bank 3-Slot1", "x")));
    }

    [Fact]
    public void ClassNamesFoldToItemBlockCodes()
    {
        Assert.Equal("PAL", GearLocker.Code("Paladin"));
        Assert.Equal("SHD", GearLocker.Code("Shadow Knight"));
        Assert.Equal("PAL", GearLocker.Code("PAL"));
        Assert.Equal("NEWCLASS", GearLocker.Code("NewClass"));   // unknown passes through
    }
}
