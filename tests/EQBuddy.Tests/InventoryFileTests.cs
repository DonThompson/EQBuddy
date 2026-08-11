using EQBuddy.Core;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>The /outputfile inventory parse (David's ask, 2026-08-11), against his real
/// Dranak dump: tab format, Empty rows, +N tier folding, the trailing-* marker, stack
/// counts, and container structure ("General 1-Slot2" lives inside "General 1").</summary>
public class InventoryFileTests
{
    private static string[] Fixture() =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "fixtures", "inventory", "dranak.txt"));

    [Fact]
    public void CountsFoldTiersHonorStacksAndIgnoreEmpties()
    {
        var counts = InventoryFile.Parse(Fixture());

        // 9 flagged "Bone Chips*" + a 134 stack = 143, one key.
        Assert.Equal(143, counts["Bone Chips"]);
        // "Leather Whip +9" folds to the base name every quest page uses.
        Assert.True(counts.ContainsKey("Leather Whip"));
        Assert.False(counts.Keys.Any(k => k.Contains('+')), "no tier suffixes survive");
        Assert.False(counts.ContainsKey("Empty"));
        // Both Enchanted Fine Steel Morning Stars (primary + secondary) count.
        Assert.Equal(2, counts["Enchanted Fine Steel Morning Star"]);
    }

    [Fact]
    public void EntriesKeepWornSlotsAndBagStructure()
    {
        var entries = InventoryFile.ParseEntries(Fixture());

        var head = Assert.Single(entries, e => e.Location == "Head");
        Assert.Equal("Raw-Hide Skullcap +2", head.Name);
        Assert.False(head.InContainer);

        var bagged = entries.Where(e => e.ContainerSlot == "General 1" && e.InContainer).ToList();
        Assert.Contains(bagged, e => e.Name == "Bone Chips" && e.Count == 134);
        // The bag itself is a top-level entry at the same slot.
        Assert.Contains(entries, e => e is { Location: "General 1", Name: "Lightweight Bag" });
    }
}
