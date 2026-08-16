using EQBuddy.UI.Shared;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>
/// The title-bar CPU/memory readout (#112), and the reason it is a shared type at all:
/// #173 (KoboldCoterie, CachyOS) reports that turning it on takes EverQuest's keyboard
/// away on Linux. The readout sits in an Auto column of a SizeToContent window, so a
/// string that changes width every three seconds resizes a real always-on-top window
/// over a fullscreen X11 game, forever, on a timer. Constant width is therefore a
/// contract, not a nicety, and this is where it is held.
/// </summary>
public class PerfReadoutTests
{
    /// <summary>Every plausible sample formats to the same number of characters. The
    /// sweep is deliberately dense across the boundaries that change digit counts —
    /// 9→10%, 99→100 MB, 999→1000 MB — because those are exactly the moments the old
    /// inline format grew the window.</summary>
    [Fact]
    public void EveryPlausibleSampleIsTheSameLength()
    {
        var lengths = new SortedSet<int>();
        for (var cpu = 0.0; cpu <= 100.0; cpu += 0.1)
            foreach (var mb in new[] { 0, 1, 9, 10, 84, 99, 100, 101, 999, 1000, 4096, 9999 })
                lengths.Add(PerfReadout.Format(cpu, (long)mb * 1024 * 1024).Length);

        Assert.Equal([PerfReadout.FixedLength], lengths);
    }

    [Fact]
    public void FormatReadsTheWayThePlayerWasPromised()
    {
        // The Options blurb advertises "0.3% · 84 MB"; padding is the only difference.
        Assert.Equal("  0.3% ·   84 MB", PerfReadout.Format(0.34, 84L * 1024 * 1024));
        Assert.Equal("100.0% · 1234 MB", PerfReadout.Format(100, 1234L * 1024 * 1024));
    }

    /// <summary>Absurd values are allowed to break the width rather than lie — but they
    /// must still not throw, and the label trims them.</summary>
    [Fact]
    public void AbsurdMemoryStillFormats()
    {
        Assert.Contains("MB", PerfReadout.Format(50, 64L * 1024 * 1024 * 1024));
        Assert.Equal("  0.0% ·    0 MB", PerfReadout.Format(0, -1));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3000, 100)]      // one core pinned for the whole 3 s window, 1 core
    [InlineData(1500, 50)]
    [InlineData(30, 1)]
    public void CpuIsAShareOfEveryCore(double cpuMs, double expected)
    {
        var pct = PerfReadout.CpuPercent(
            TimeSpan.FromMilliseconds(cpuMs), TimeSpan.FromSeconds(3), cores: 1);
        Assert.Equal(expected, pct, 3);
    }

    [Fact]
    public void MoreCoresDilutesTheSameCpuTime()
    {
        var busy = TimeSpan.FromSeconds(3);
        Assert.Equal(100, PerfReadout.CpuPercent(busy, busy, cores: 1), 3);
        Assert.Equal(12.5, PerfReadout.CpuPercent(busy, busy, cores: 8), 3);
    }

    /// <summary>A clock that steps backwards (NTP, resume from suspend) used to produce
    /// a negative or wildly inflated ratio. Neither reaches the title bar.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1000)]
    public void ANonPositiveWindowReportsZeroRatherThanNonsense(double elapsedMs)
    {
        Assert.Equal(0, PerfReadout.CpuPercent(
            TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(elapsedMs), cores: 4));
    }

    [Fact]
    public void CpuNeverExceedsTheWholeMachine()
    {
        // More CPU time than wall-clock across all cores can only come from a clock
        // artefact; 100% is the honest ceiling for "share of this machine".
        Assert.Equal(100, PerfReadout.CpuPercent(
            TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(3), cores: 4));
        Assert.Equal(0, PerfReadout.CpuPercent(
            TimeSpan.FromSeconds(-5), TimeSpan.FromSeconds(3), cores: 4));
    }
}
