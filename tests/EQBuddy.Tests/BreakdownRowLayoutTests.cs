using EQBuddy.UI.Shared;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>
/// #182 (Ladylag): "I can't read the names of any of the abilities in the combat
/// breakdown because they are all truncated after a few letters."
///
/// Her screenshot showed rows named <c>.</c>, <c>..</c> and one named nothing at all,
/// beside a row named "Damage shield" in full. It reads like a parser drawing a failure
/// as data — I said so in the thread, and I was wrong. Every one of those rows knew its
/// own name. The name was the only flexible column and the stat context beside it was
/// <c>Auto</c>, so a long context ("×1993 · avg 103.3 · 26.5 dps · 13% crit · 49% miss")
/// took what it liked and left the name a few pixels of ellipsis. "Damage shield"
/// survived because its context is short.
///
/// The invariant below is the one that screenshot violated.
/// </summary>
public class BreakdownRowLayoutTests
{
    /// <summary>A row's NAME is what it is; the context is a remark about it. However
    /// little room there is, the identity gets the larger share of it.</summary>
    [Theory]
    [InlineData(400)]
    [InlineData(185)]   // a 272px breakout after the headline takes its width
    [InlineData(60)]
    [InlineData(12)]
    public void TheNameNeverGetsLessRoomThanTheCommentaryBesideIt(double flexible)
    {
        var name = BreakdownRowLayout.NameWidth(flexible);
        Assert.True(name >= flexible / 2,
            $"{name:0.#} of {flexible:0.#} went to the name — the row can be starved again.");
        Assert.True(name <= flexible);
    }

    /// <summary>Scaling down does not eventually flip the rule. This is the case the bug
    /// actually lived in: a narrow window, not a wide one.</summary>
    [Fact]
    public void TheShareIsAProportionNotAFixedWidth()
    {
        Assert.Equal(BreakdownRowLayout.NameWidth(300) / 3,
            BreakdownRowLayout.NameWidth(100), 6);
        Assert.Equal(0, BreakdownRowLayout.NameWidth(0));
        Assert.Equal(0, BreakdownRowLayout.NameWidth(-50));
    }

    [Fact]
    public void TheNameOutweighsTheContext() =>
        Assert.True(BreakdownRowLayout.NameWeight > BreakdownRowLayout.ContextWeight);

    /// <summary>The resize band on a frameless window has to be findable by a hand. Six
    /// device-independent pixels of unmarked edge is not — which is why "drag only works
    /// on the bottom edge" was reported of a window that has resized from every edge
    /// since 2026-08-06: the bottom is the edge with a grip drawn on it.</summary>
    [Fact]
    public void TheResizeBandIsWideEnoughToHit()
    {
        Assert.True(BreakdownRowLayout.ResizeEdge >= 8);
        Assert.True(BreakdownRowLayout.ResizeCorner > BreakdownRowLayout.ResizeEdge);
    }

    /// <summary>And the zone math still answers with the wider band — an edge that grew
    /// past the corner size, or a corner that swallowed the title row, would trade one
    /// unusable window for another.</summary>
    [Theory]
    [InlineData(2, 100, ResizeZones.Left)]
    [InlineData(9, 100, ResizeZones.Left)]
    [InlineData(263, 100, ResizeZones.Right)]
    [InlineData(100, 2, ResizeZones.Top)]
    [InlineData(100, 337, ResizeZones.Bottom)]
    [InlineData(4, 4, ResizeZones.TopLeft)]
    [InlineData(266, 336, ResizeZones.BottomRight)]
    [InlineData(136, 170, ResizeZones.None)]
    [InlineData(30, 20, ResizeZones.None)]   // the title row's controls keep their hits
    public void TheWiderBandStillHitsTheRightZone(double x, double y, int expected) =>
        Assert.Equal(expected, ResizeZones.Hit(x, y, 272, 340,
            BreakdownRowLayout.ResizeEdge, BreakdownRowLayout.ResizeCorner));

    // ---- what hovering a row gives back ----

    /// <summary>The name comes back in full whether or not it fitted, which is the
    /// promise: "a truncated name should show its full text on hover regardless".</summary>
    [Fact]
    public void HoverAlwaysStartsWithTheFullName() =>
        Assert.StartsWith("Tuyen's Chant of Flame",
            BreakdownRowLayout.HoverText("Tuyen's Chant of Flame",
                "×1993 · avg 103.3 · 26.5 dps", null), StringComparison.Ordinal);

    /// <summary>The stat line comes back too. It is the column that yields now, so the
    /// crit and miss percentages Ladylag could read before must still be reachable.</summary>
    [Fact]
    public void HoverCarriesTheStatLineTheRowNoLongerHasRoomFor()
    {
        var text = BreakdownRowLayout.HoverText("Kick", "×50 · avg 22.3 · 32% miss", null);
        Assert.Contains("32% miss", text);
        Assert.Contains(Environment.NewLine, text);
    }

    /// <summary>A caller's own tooltip is APPENDED, never replaced — the burst
    /// breakdown, the last item seen and the fight total are richer than a name, and
    /// putting this on the row instead of on the name is what keeps both.</summary>
    [Fact]
    public void ACallersTooltipSurvives()
    {
        var text = BreakdownRowLayout.HoverText("Crush", "×158 · avg 30.6", "burst 4,837 over 12s");
        Assert.Contains("Crush", text);
        Assert.Contains("×158", text);
        Assert.Contains("burst 4,837 over 12s", text);
    }

    [Fact]
    public void NothingIsRepeatedAndEmptyPartsAreSkipped()
    {
        Assert.Equal("Kick", BreakdownRowLayout.HoverText("Kick", "", null));
        Assert.Equal("Kick", BreakdownRowLayout.HoverText("Kick", "", "Kick"));
        Assert.Equal("", BreakdownRowLayout.HoverText("", "", null));
    }
}
