namespace EQBuddy.UI.Shared;

/// <summary>
/// The title-bar CPU/memory readout (#112): the arithmetic, the text, and — the part
/// that matters — the promise that the text is always the SAME SHAPE.
///
/// Why the shape is load-bearing. The widget sizes itself to its content
/// (<c>SizeToContent</c>) and the readout sits in an <c>Auto</c> column of the title
/// bar, so its measured width IS the window's width in the minimized pill. A readout
/// that grows a character when memory crosses 100 MB therefore resizes a real,
/// always-on-top, transparent native window — every few seconds, forever, whether or
/// not anything else in the app changed. On Windows that is invisible. On X11 it is a
/// geometry change on a window stacked above a fullscreen game, and KoboldCoterie's
/// report (#173, CachyOS) is that turning this option on costs the game its keyboard.
///
/// So the readout is padded to a fixed width for every realistic value, and the view
/// gives its label a fixed <see cref="ReservedWidth"/>. Both together mean a new
/// sample changes pixels and nothing else: no measure change, no window resize, no
/// call into the windowing system at all.
/// </summary>
public static class PerfReadout
{
    /// <summary>Width to reserve for the label, in the widget's pre-scale units at the
    /// readout's 10px font. Sized for the widest realistic string ("100.0% · 1234 MB")
    /// in DejaVu Sans, the usual Linux default and the widest of the fonts the two
    /// builds actually meet; Segoe UI needs about 80. The point is that it is a
    /// CONSTANT — the exact number only decides how much title bar the readout costs.
    /// </summary>
    public const double ReservedWidth = 100;

    /// <summary>Share of the WHOLE machine, so 100% is every core busy. Clamped at both
    /// ends: a backwards clock step (NTP, resume from suspend) makes the elapsed span
    /// negative or tiny and the naive ratio explode, and a readout that says 4000% is
    /// worse than one that says 100%.</summary>
    public static double CpuPercent(TimeSpan cpuDelta, TimeSpan elapsed, int cores)
    {
        if (elapsed <= TimeSpan.Zero || cores <= 0) return 0;
        var pct = cpuDelta.TotalMilliseconds / (elapsed.TotalMilliseconds * cores) * 100;
        return double.IsFinite(pct) ? Math.Clamp(pct, 0, 100) : 0;
    }

    /// <summary>Character count of every string <see cref="Format"/> returns for a
    /// plausible sample (0–100%, 0–9999 MB). Asserted by the tests — it is the
    /// invariant, not a decoration.</summary>
    public const int FixedLength = 16;

    /// <summary>"  0.3% ·   84 MB" — right-padded to <see cref="FixedLength"/> so the
    /// digits neither jitter nor change the measured width. Above 9999 MB (about 10 GB
    /// of working set, which would itself be the bug worth reporting) the string grows
    /// and the label trims; correctness of the number wins there.</summary>
    public static string Format(double cpuPercent, long workingSetBytes)
    {
        var cpu = Math.Clamp(cpuPercent, 0, 100);
        var mb = Math.Max(0, workingSetBytes) / (1024.0 * 1024.0);
        return $"{cpu,5:0.0}% · {mb,4:0} MB";
    }
}
