namespace EQBuddy.UI.Shared;

/// <summary>
/// Fritsch–Carlson monotone-cubic interpolation — the curve behind every smoothed
/// chart line (the 2026-08-13 approved pass). The property that makes it the honest
/// choice: between any two samples the curve stays within their range, so a smooth
/// line can never invent a peak or dip the data doesn't contain.
/// </summary>
public static class MonotoneCurve
{
    /// <summary>Per-point tangents for the Hermite/Bézier form.</summary>
    public static double[] Tangents(double[] xs, double[] ys)
    {
        var n = xs.Length;
        var m = new double[n - 1];
        for (var i = 0; i < n - 1; i++)
            m[i] = (ys[i + 1] - ys[i]) / Math.Max(1e-9, xs[i + 1] - xs[i]);
        var t = new double[n];
        t[0] = m[0]; t[n - 1] = m[n - 2];
        for (var i = 1; i < n - 1; i++)
            t[i] = m[i - 1] * m[i] <= 0 ? 0
                : 3 * (xs[i] - xs[i - 1] + xs[i + 1] - xs[i])
                  / ((2 * (xs[i + 1] - xs[i]) + (xs[i] - xs[i - 1])) / m[i - 1]
                     + ((xs[i + 1] - xs[i]) + 2 * (xs[i] - xs[i - 1])) / m[i]);
        return t;
    }

    /// <summary>The curve evaluated at <paramref name="samplesPerSegment"/> points per
    /// segment (cubic Hermite) — for surfaces that draw dense polylines rather than
    /// Béziers. Returns interleaved (x, y) pairs including the original samples.</summary>
    public static List<(double X, double Y)> Sample(double[] xs, double[] ys, int samplesPerSegment = 6)
    {
        var t = Tangents(xs, ys);
        var pts = new List<(double, double)> { (xs[0], ys[0]) };
        for (var i = 0; i < xs.Length - 1; i++)
        {
            var h = xs[i + 1] - xs[i];
            for (var k = 1; k <= samplesPerSegment; k++)
            {
                var u = (double)k / samplesPerSegment;
                var h00 = (1 + 2 * u) * (1 - u) * (1 - u);
                var h10 = u * (1 - u) * (1 - u);
                var h01 = u * u * (3 - 2 * u);
                var h11 = u * u * (u - 1);
                pts.Add((xs[i] + u * h,
                    h00 * ys[i] + h10 * h * t[i] + h01 * ys[i + 1] + h11 * h * t[i + 1]));
            }
        }
        return pts;
    }
}
