using EQBuddy.UI.Shared;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>The smoothing behind every curved chart line: it must be smooth AND
/// honest — the interpolated curve can never leave the range of the two samples
/// it connects, so a pretty line cannot invent a spike the fight didn't have.</summary>
public class MonotoneCurveTests
{
    [Fact]
    public void TheCurveNeverOvershootsItsSamples()
    {
        double[] xs = [0, 1, 2, 3, 4, 5, 6];
        double[] ys = [0, 60, 5, 5, 40, 0, 64];   // violent swings, flats, spike at end
        var pts = MonotoneCurve.Sample(xs, ys, samplesPerSegment: 20);
        foreach (var (x, y) in pts)
        {
            var i = Math.Min(xs.Length - 2, (int)x);
            var lo = Math.Min(ys[i], ys[i + 1]) - 1e-6;
            var hi = Math.Max(ys[i], ys[i + 1]) + 1e-6;
            Assert.InRange(y, lo, hi);
        }
    }

    [Fact]
    public void FlatSegmentsStayFlat()
    {
        double[] xs = [0, 1, 2, 3];
        double[] ys = [10, 10, 10, 10];
        Assert.All(MonotoneCurve.Sample(xs, ys), p => Assert.Equal(10, p.Y, 6));
    }

    [Fact]
    public void TheCurvePassesThroughEveryRealSample()
    {
        double[] xs = [0, 2, 5, 9];
        double[] ys = [3, 44, 12, 30];
        var pts = MonotoneCurve.Sample(xs, ys, samplesPerSegment: 4);
        for (var i = 0; i < xs.Length; i++)
            Assert.Contains(pts, p => Math.Abs(p.X - xs[i]) < 1e-9 && Math.Abs(p.Y - ys[i]) < 1e-9);
    }
}
