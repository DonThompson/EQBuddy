using Avalonia.Headless.XUnit;
using Avalonia.Media;
using EQBuddy.UI.Shared;

namespace EQBuddy.Avalonia.Tests;

/// <summary>
/// Every icon path, through a REAL geometry parser.
///
/// <c>IconPaths</c> lives in UI.Shared, which is deliberately toolkit-free
/// (ArchitectureTests), so its own test project has nothing to parse with and can only
/// check the string's shape. This is the other half: both UIs hand the same data to a
/// path parser at window-construction time, and a typo there is an exception in front of
/// a player rather than a red build.
/// </summary>
[Collection("avalonia")]
public class IconGeometryTests
{
    public static TheoryData<string> IconNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in IconPaths.Names) data.Add(name);
        return data;
    }

    [AvaloniaTheory]
    [MemberData(nameof(IconNames))]
    public void EveryIconParses(string name)
    {
        var bounds = StreamGeometry.Parse(IconPaths.Path(name)).Bounds;
        Assert.True(bounds.Width > 0 && bounds.Height > 0, $"{name} parsed to nothing.");
    }

    /// <summary>Everything is drawn on one 24×24 grid, so a mixed set renders at mixed
    /// weights unless they agree. An icon that overflows the box gets clipped or scaled
    /// down beside its neighbours; one that occupies a corner of it renders as a speck.
    /// Both read as "the icons are broken" rather than as a bad path.</summary>
    [AvaloniaTheory]
    [MemberData(nameof(IconNames))]
    public void EveryIconFillsItsGridWithoutOverflowing(string name)
    {
        var bounds = StreamGeometry.Parse(IconPaths.Path(name)).Bounds;
        const double box = IconPaths.ViewBox;

        Assert.True(bounds.X >= -0.5 && bounds.Y >= -0.5
            && bounds.Right <= box + 0.5 && bounds.Bottom <= box + 0.5,
            $"{name} draws outside the {box}×{box} grid: {bounds}");
        // Half the box in the larger dimension. Below that an icon is visibly lighter
        // than the set it sits in.
        Assert.True(Math.Max(bounds.Width, bounds.Height) >= box / 2,
            $"{name} only fills {bounds.Width:0.#}×{bounds.Height:0.#} of {box}×{box} — " +
            "it will render as a speck beside the others.");
    }
}
