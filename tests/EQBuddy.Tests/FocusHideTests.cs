using EQBuddy.UI.Shared;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>The hide-the-overlay decision (#41 hide-when-unfocused, #114
/// hide-when-not-running): the full truth table, since the two opt-ins compose and
/// each has a deliberate carve-out that reads as a bug when violated.</summary>
public class FocusHideTests
{
    // (unfocused, notRunning, fgSelf, fgGame, gameRunning) → hide?
    [Theory]
    // Neither opt-in: never hide, whatever the world looks like.
    [InlineData(false, false, false, false, false, false)]
    [InlineData(false, false, false, false, true, false)]
    // Unfocused-only (the shipped #41 behavior, unchanged): hides only when the
    // game runs behind some third app; game closed keeps the widget visible.
    [InlineData(true, false, false, false, true, true)]
    [InlineData(true, false, false, false, false, false)]
    [InlineData(true, false, false, true, true, false)]     // playing: show
    [InlineData(true, false, true, false, true, false)]     // using EQBuddy: show
    // Not-running-only (#114): hides exactly when the game is closed; a running
    // game keeps the widget up even when unfocused (that's the OTHER toggle).
    [InlineData(false, true, false, false, false, true)]
    [InlineData(false, true, false, false, true, false)]
    [InlineData(false, true, true, false, false, false)]    // escape hatch: EQBuddy focused
    [InlineData(false, true, false, true, true, false)]     // playing: show
    // Both on: the overlay exists only while the game is focused or EQBuddy is used.
    [InlineData(true, true, false, false, true, true)]
    [InlineData(true, true, false, false, false, true)]
    [InlineData(true, true, false, true, true, false)]
    [InlineData(true, true, true, false, false, false)]
    public void DecideWalksTheTruthTable(
        bool unfocused, bool notRunning, bool fgSelf, bool fgGame, bool running, bool hide) =>
        Assert.Equal(hide, FocusHide.Decide(unfocused, notRunning, fgSelf, fgGame, running));
}
