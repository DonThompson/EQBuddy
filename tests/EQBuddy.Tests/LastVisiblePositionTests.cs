using EQBuddy.Core;
using Xunit;

namespace EQBuddy.Tests;

// Guards joma65's Linux report (#169): the main widget, the spawns window and both chip
// stacks came back at 0,0 on every launch. The X11/Wayland backends can report a zeroed
// position while a window is opening or tearing down, and four windows persisted that
// read straight into settings.json — where 0,0 is indistinguishable from a spot the
// player chose. The saved defaults are NaN, never 0, so a zero can only ever have been
// written by code; that is what pinned the cause here rather than on a lost settings file.
public class LastVisiblePositionTests
{
    [Fact]
    public void NothingSeenKeepsTheSavedSpot()
    {
        // The whole point: a window that never reported a position we can vouch for
        // must leave the player's saved spot exactly as it was.
        var seen = new LastVisiblePosition();
        Assert.False(seen.Have);
        Assert.Equal((1200d, 340d), seen.Or(1200, 340));
    }

    [Fact]
    public void AClosingWindowsZeroIsNeverObserved()
    {
        // The #169 shape: the player parked the widget, then the backend reported 0,0
        // while the window tore down. Visibility is already false by then, so the zero
        // never displaces the real position.
        var seen = new LastVisiblePosition();
        seen.Observe(1200, 340, visible: true);
        seen.Observe(0, 0, visible: false);
        Assert.Equal((1200d, 340d), seen.Or(double.NaN, double.NaN));
    }

    [Fact]
    public void TheLastOnScreenPositionWins()
    {
        // Ordinary dragging: the most recent on-screen report is the one to keep.
        var seen = new LastVisiblePosition();
        seen.Observe(100, 100, visible: true);
        seen.Observe(880, 420, visible: true);
        Assert.Equal((880d, 420d), seen.Or(0, 0));
    }

    [Fact]
    public void AnObservedZeroIsStillHonoured()
    {
        // 0,0 is a legitimate place to park a widget on a single-monitor desktop. The
        // guard is about WHEN a position was reported, not about the value — rejecting
        // the origin outright would strand anyone who genuinely tucks into the corner.
        var seen = new LastVisiblePosition();
        seen.Observe(0, 0, visible: true);
        Assert.True(seen.Have);
        Assert.Equal((0d, 0d), seen.Or(1200, 340));
    }

    [Fact]
    public void FallbackSurvivesAnInvisibleOnlyLifetime()
    {
        // A window opened and closed without ever being mapped (the hotkey-hidden
        // stacks, a headless test run) has observed nothing at all.
        var seen = new LastVisiblePosition();
        seen.Observe(0, 0, visible: false);
        seen.Observe(17, 42, visible: false);
        Assert.False(seen.Have);
        Assert.Equal((640d, 480d), seen.Or(640, 480));
    }
}
