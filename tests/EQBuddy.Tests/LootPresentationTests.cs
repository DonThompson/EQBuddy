using EQBuddy.Core;
using EQBuddy.UI.Shared;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>
/// Everything AROUND the loot rows: which strips are up, which chip is lit, whether
/// "recent" is offered, and what an empty slice says.
///
/// These rules used to live twice — the widget's RenderLoot and the breakout's
/// UpdateLoot each derived them from the same four snapshot lists — and the copies had
/// already drifted. Gate 4 moved them here, which is what makes them assertable at all:
/// the WPF layer has no test project (docs/TestPlan.md §5).
/// </summary>
public class LootPresentationTests
{
    private static readonly List<LootDetail> Mixed =
    [
        new("Bone Chips", 5, "a decaying skeleton"),
        new("Vegetables", 3, "Forage"),
    ];

    private static readonly List<LootPickup> Recent =
    [
        new(new DateTime(2026, 8, 16, 17, 2, 0), "Vegetables", 1, "Forage"),
        new(new DateTime(2026, 8, 16, 17, 1, 0), "Bone Chips", 2, "a decaying skeleton"),
    ];

    private static LootPresentation.Plan Build(
        IReadOnlyList<LootDetail> loot, string view = "all", string sort = "count",
        IReadOnlyList<NameCount>? merged = null, IReadOnlyList<NameCount>? fashioned = null,
        IReadOnlyList<LootPickup>? recent = null) =>
        LootPresentation.Build(loot, merged ?? [], fashioned ?? [], recent ?? Recent, view, sort);

    // ---- the settings round-trip ----

    [Theory]
    [InlineData("all", "all")]
    [InlineData("looted", "looted")]
    [InlineData("other", "other")]
    [InlineData("made", "other")]           // the pre-#198 spelling, still on disk
    [InlineData("", "all")]
    [InlineData(null, "all")]
    [InlineData("nonsense", "all")]         // a hand-edited profile must not hide loot
    public void NormalizeViewMapsEveryStoredValue(string? stored, string expected) =>
        Assert.Equal(expected, LootPresentation.NormalizeView(stored));

    [Theory]
    [InlineData("count", "count")]
    [InlineData("name", "name")]
    [InlineData("recent", "recent")]
    [InlineData("", "count")]
    [InlineData(null, "count")]
    public void NormalizeSortMapsEveryStoredValue(string? stored, string expected) =>
        Assert.Equal(expected, LootPresentation.NormalizeSort(stored));

    [Fact]
    public void PlanCarriesTheNormalizedSelection()
    {
        var plan = Build(Mixed, view: "made", sort: "bogus");
        Assert.Equal("other", plan.View);
        Assert.Equal("count", plan.Sort);
    }

    // ---- strip visibility ----

    [Fact]
    public void NoLootAtAllHidesBothStrips()
    {
        var plan = Build([], recent: []);
        Assert.False(plan.ShowViewStrip);
        Assert.False(plan.ShowSortStrip);
        Assert.Equal("No loot seen yet.", plan.EmptyNote);
    }

    /// <summary>LW, 2026-08-17: the show strip stays up whenever the card holds any loot,
    /// even when one slice is empty — otherwise a player cannot tell the filter is there.</summary>
    [Fact]
    public void OneSidedLootStillOffersTheShowStrip()
    {
        var plan = Build([new("Bone Chips", 5, "a decaying skeleton")]);
        Assert.True(plan.ShowViewStrip);
    }

    [Fact]
    public void ASingleRowHidesTheSortStrip()
    {
        var plan = Build([new("Bone Chips", 5, "a decaying skeleton")]);
        Assert.Single(plan.Rows);
        Assert.False(plan.ShowSortStrip);
    }

    [Fact]
    public void TwoRowsOfferTheSortStrip()
    {
        var plan = Build(Mixed);
        Assert.Equal(2, plan.Rows.Count);
        Assert.True(plan.ShowSortStrip);
    }

    /// <summary>Made-only rows (merges and crafts) count toward the sort strip too —
    /// they are rows, and they can be ordered.</summary>
    [Fact]
    public void MergesAndCraftsCountAsRows()
    {
        var plan = Build([], view: "other", merged: [new("Crushbone Belt +5", 1)],
            fashioned: [new("Elixir of Concentration", 4)], recent: []);
        Assert.Equal(2, plan.Rows.Count);
        Assert.True(plan.ShowViewStrip);
        Assert.True(plan.ShowSortStrip);
    }

    // ---- "recent" is only offered where a timestamp exists ----

    [Fact]
    public void RecentIsOfferedWhenTheChosenSliceHasSomethingInIt()
    {
        Assert.True(Build(Mixed, view: "all").ShowRecent);
        Assert.True(Build(Mixed, view: "looted").ShowRecent);
        Assert.True(Build(Mixed, view: "other").ShowRecent);
    }

    [Fact]
    public void RecentIsWithheldWhenTheChosenSliceIsEmpty()
    {
        var lootedOnly = new List<LootDetail> { new("Bone Chips", 5, "a decaying skeleton") };
        Assert.False(Build(lootedOnly, view: "other").ShowRecent);

        var foragedOnly = new List<LootDetail> { new("Vegetables", 3, "Forage") };
        Assert.False(Build(foragedOnly, view: "looted").ShowRecent);
    }

    // ---- the empty slice names itself ----

    [Fact]
    public void AnEmptySliceNamesItselfRatherThanBlanking()
    {
        var lootedOnly = new List<LootDetail> { new("Bone Chips", 5, "a decaying skeleton") };
        Assert.Equal("Nothing else yet.", Build(lootedOnly, view: "other").EmptyNote);

        var foragedOnly = new List<LootDetail> { new("Vegetables", 3, "Forage") };
        Assert.Equal("No looted items yet.", Build(foragedOnly, view: "looted").EmptyNote);
    }

    [Fact]
    public void RowsPresentMeansNoEmptyNote() => Assert.Null(Build(Mixed).EmptyNote);

    [Fact]
    public void EmptyNoteForNormalizesItsViewToo() =>
        Assert.Equal("Nothing else yet.", LootPresentation.EmptyNoteFor("made", hasAnyLoot: true));

    // ---- the two headers agree (#131) ----

    [Theory]
    [InlineData(0, 0, "0 items")]
    [InlineData(1, 0, "1 item")]
    [InlineData(12, 0, "12 items")]
    [InlineData(12, 3, "12 items (+3 made)")]
    public void HeaderCountsMadeItemsApartFromDrops(int loot, int made, string expected) =>
        Assert.Equal(expected, LootPresentation.Header(loot, made));

    [Theory]
    [InlineData(1, 0, "Session · 1 item looted")]
    [InlineData(12, 0, "Session · 12 items looted")]
    [InlineData(12, 3, "Session · 12 items looted · +3 made")]
    public void BreakoutSubtitleReportsTheSameTwoNumbers(int loot, int made, string expected) =>
        Assert.Equal(expected, LootPresentation.BreakoutSubtitle(loot, made));

    // ---- provenance ----

    [Fact]
    public void ProvenanceRidesAsAParentheticalOrNotAtAll()
    {
        Assert.Equal("(Foraged)", LootPresentation.Note("Foraged"));
        Assert.Null(LootPresentation.Note(null));
        Assert.Null(LootPresentation.Note(""));
    }

    [Fact]
    public void OtherIsEverythingACorpseDidNotHandYou()
    {
        Assert.True(LootPresentation.IsOther(LootRows.ForageSource));
        Assert.True(LootPresentation.IsOther(LootRows.ParcelSource));
        Assert.False(LootPresentation.IsOther("a decaying skeleton"));
    }

    // ---- the strips themselves ----

    [Fact]
    public void EveryStripOptionHasAKeyTheNormalizersAccept()
    {
        foreach (var option in LootPresentation.Views)
            Assert.Equal(option.Key, LootPresentation.NormalizeView(option.Key));
        foreach (var option in LootPresentation.Sorts)
            Assert.Equal(option.Key, LootPresentation.NormalizeSort(option.Key));
    }

    /// <summary>The tooltips are the reason this list is data rather than three chips
    /// typed into each of four surfaces: "other" is not self-explanatory, and the
    /// breakout's hand-built copy shipped without any hover copy at all.</summary>
    [Fact]
    public void EveryStripOptionExplainsItself()
    {
        foreach (var option in LootPresentation.Views.Concat(LootPresentation.Sorts))
        {
            Assert.False(string.IsNullOrWhiteSpace(option.Label));
            Assert.False(string.IsNullOrWhiteSpace(option.Tip));
        }
    }
}
