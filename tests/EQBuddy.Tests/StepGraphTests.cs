using EQBuddy.UI.Shared;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>The character-progress step charts: values hold until the next
/// observation, flat histories draw nothing, zeros mean unknown.</summary>
public class StepGraphTests
{
    private static readonly DateTime T0 = DateTime.Parse("2026-08-01T12:00:00");

    [Fact]
    public void StepsHoldUntilTheNextObservation()
    {
        var g = HistoryPresentation.BuildStepGraph(
            [(T0, 50), (T0.AddDays(1), 51), (T0.AddDays(2), 53)], 200, 100)!;

        // 3 observations → 5 points: each later observation adds a hold + a step.
        Assert.Equal(5, g.Points.Count);
        Assert.Equal(g.Points[0].Y, g.Points[1].Y, 3);       // held at 50 until day 1
        Assert.Equal(g.Points[1].X, g.Points[2].X, 3);       // vertical ding
        Assert.Equal(0, g.Points[^1].Y, 3);                  // max value tops the chart
        Assert.Equal(100, g.Points[0].Y, 3);                 // min value sits at the floor
    }

    [Fact]
    public void FlatOrTinyHistoriesDrawNothing()
    {
        Assert.Null(HistoryPresentation.BuildStepGraph([(T0, 50)], 200, 100));
        Assert.Null(HistoryPresentation.BuildStepGraph(
            [(T0, 214), (T0.AddDays(3), 214)], 200, 100));   // never changed
    }

    [Fact]
    public void ZerosAreUnknownNotProgress()
    {
        // A session that saw no AA event reports 0 — it must not drag the chart floor.
        var g = HistoryPresentation.BuildStepGraph(
            [(T0, 100), (T0.AddDays(1), 0), (T0.AddDays(2), 120)], 200, 100)!;
        Assert.Equal(3, g.Points.Count);   // two real observations, one hold+step
    }
}
