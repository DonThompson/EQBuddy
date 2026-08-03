using EQBuddy.Core;
using Xunit;

namespace EQBuddy.Tests;

// Guards the off-screen-restore fix (field report 2026-08-03): a position saved on a
// since-removed monitor must be rejected so the window falls back to its default spot,
// while positions anywhere on the current layout — including secondary monitors at
// negative coordinates — must be kept.
public class WindowPlacementTests
{
    // Single 1920×1080 primary screen.
    private static bool OnSingle(double left, double top,
        double width = double.NaN, double height = double.NaN) =>
        WindowPlacement.IsReachable(left, top, 0, 0, 1920, 1080, width, height);

    [Fact]
    public void FirstLaunchNaNIsNotReachable()
    {
        Assert.False(OnSingle(double.NaN, double.NaN));
        Assert.False(OnSingle(100, double.NaN));
        Assert.False(OnSingle(double.NaN, 100));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1560, 40)]      // widget's own default corner
    [InlineData(1880, 1040)]    // bottom-right, grab margin exactly visible
    public void PositionsOnScreenAreKept(double left, double top) =>
        Assert.True(OnSingle(left, top));

    [Theory]
    [InlineData(2200, 40)]      // right of a monitor that's gone
    [InlineData(-500, 40)]      // left of the screen, size unknown
    [InlineData(40, 1200)]      // below the bottom edge
    [InlineData(40, -300)]      // above the top edge
    [InlineData(1900, 40)]      // corner on-screen but < 40px of grab area left
    public void PositionsOffScreenAreRejected(double left, double top) =>
        Assert.False(OnSingle(left, top));

    [Fact]
    public void SecondMonitorAtNegativeCoordinatesIsKept()
    {
        // 1920×1080 laptop with a monitor arranged to its left: virtual screen
        // starts at -1920. The same saved spot dies when that monitor unplugs.
        Assert.True(WindowPlacement.IsReachable(-1400, 200, -1920, 0, 3840, 1080));
        Assert.False(WindowPlacement.IsReachable(-1400, 200, 0, 0, 1920, 1080));
    }

    [Fact]
    public void KnownWidthLetsATuckedWindowKeepItsSpot()
    {
        // Widget parked with its left half past the screen edge: rejected when the
        // size is unknown (top-left corner is off-screen), kept when the width
        // proves 60px of draggable body remains visible.
        Assert.False(OnSingle(-300, 40));
        Assert.True(OnSingle(-300, 40, width: 360, height: 200));
        // But a window fully past the edge is gone no matter its size.
        Assert.False(OnSingle(-500, 40, width: 360, height: 200));
    }

    [Fact]
    public void SubMarginSizesAreTreatedAsTheGrabArea()
    {
        // A 10px-tall saved height must not shrink the required visible area
        // below the drag-rescuable minimum.
        Assert.False(OnSingle(40, 1075, width: 100, height: 10));
    }
}
