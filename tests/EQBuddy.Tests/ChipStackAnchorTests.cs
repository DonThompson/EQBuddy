using EQBuddy.UI.Shared;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>
/// The grow-up chip stack's anchor. Both drift bugs players reported (#122 and #152)
/// were arithmetic in here rather than anything to do with rendering, so this is the
/// level they can be held down at.
/// </summary>
public class ChipStackAnchorTests
{
    [Fact]
    public void AStackHoldsItsBottomEdgeAsChipsComeAndGo()
    {
        var a = new ChipStackAnchor();
        a.Observe(top: 500, height: 60);      // two chips, bottom at 560

        Assert.Equal(560, a.Bottom);
        Assert.Equal(470, a.TopFor(90));      // a third chip pushes the TOP up
        Assert.Equal(530, a.TopFor(30));      // one expires; the bottom stays put
    }

    [Fact]
    public void AClosingWindowCannotCollapseTheAnchorOntoItsTopEdge()
    {
        // THE #152 BUG. A closing window reports a height of zero. Believing it made the
        // saved "bottom" equal the TOP edge, so the next open subtracted a chip's height
        // from it and the stack climbed the screen — one chip per cycle, exactly as
        // Snagglefern measured.
        var a = new ChipStackAnchor();
        a.Observe(top: 500, height: 60);
        Assert.Equal(560, a.Bottom);

        a.Observe(top: 500, height: 0);       // the stack empties and the window closes
        Assert.Equal(560, a.Bottom);          // ...and the anchor is unmoved
    }

    [Fact]
    public void TheStackReturnsToTheSameSpotAcrossManyReopenCycles()
    {
        // The player's actual complaint, in full: empty the stack, mez again, repeat.
        // Any drift at all compounds, so this asserts the exact figure five cycles later.
        const double parkedBottom = 900;
        var persisted = parkedBottom;

        for (var cycle = 0; cycle < 5; cycle++)
        {
            var a = new ChipStackAnchor(persisted);

            // Opens with one chip and repositions on its first layout — the top it was
            // restored at belongs to whatever chip count it closed with.
            Assert.True(a.ShouldReposition(growsUp: true, heightChanged: false));
            var top = a.TopFor(30)!.Value;
            Assert.Equal(870, top);
            a.Observe(top, 30);

            // Grows to three chips, then back down to one as they expire.
            top = a.TopFor(90)!.Value; a.Observe(top, 90);
            top = a.TopFor(30)!.Value; a.Observe(top, 30);

            // The window closes: persist the ANCHOR, never the dead window's geometry.
            a.Observe(top, 0);
            persisted = a.Bottom;
            Assert.Equal(parkedBottom, persisted);
        }
    }

    [Fact]
    public void ADragMovesTheAnchorWithIt()
    {
        var a = new ChipStackAnchor(900);
        a.Observe(top: 870, height: 30);
        Assert.Equal(900, a.Bottom);

        a.Observe(top: 400, height: 30);     // the player drags it somewhere else
        Assert.Equal(430, a.Bottom);         // and that is the new parked edge
    }

    [Fact]
    public void AFreshStackWithNoSavedPositionSimplyGrowsFromWhereItOpens()
    {
        var a = new ChipStackAnchor();
        Assert.False(a.HasAnchor);
        Assert.Null(a.TopFor(30));
        Assert.False(a.ShouldReposition(growsUp: true, heightChanged: true));
        Assert.False(a.FirstLayoutPending);
    }

    [Fact]
    public void AGrowDownStackIsNeverRepositioned()
    {
        var a = new ChipStackAnchor(900);
        Assert.False(a.ShouldReposition(growsUp: false, heightChanged: true));
    }

    [Fact]
    public void OnlyTheFirstLayoutAndRealHeightChangesReposition()
    {
        var a = new ChipStackAnchor(900);
        Assert.True(a.ShouldReposition(growsUp: true, heightChanged: false));  // restored pass
        a.Observe(870, 30);
        // A move with no height change must not drag the window around under the player.
        Assert.False(a.ShouldReposition(growsUp: true, heightChanged: false));
        Assert.True(a.ShouldReposition(growsUp: true, heightChanged: true));
    }
}
