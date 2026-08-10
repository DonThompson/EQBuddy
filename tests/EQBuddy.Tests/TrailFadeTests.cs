using EQBuddy.UI.Shared;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>The breadcrumb fade clock (MapWindow trail): wall-clock age drives
/// alpha, so a camped player's route in dissolves without any new /loc arriving
/// (David's field test, 2026-08-10).</summary>
public class TrailFadeTests
{
    [Fact]
    public void FreshCrumbsKeepFullStrength()
    {
        Assert.Equal(TrailFade.FullAlpha, TrailFade.Alpha(TimeSpan.Zero));
        Assert.Equal(TrailFade.FullAlpha, TrailFade.Alpha(TrailFade.FreshFor));
    }

    [Fact]
    public void FutureTimestampsClampToFull()
    {
        // Log replay can hand the UI a crumb stamped slightly ahead of DateTime.Now.
        Assert.Equal(TrailFade.FullAlpha, TrailFade.Alpha(TimeSpan.FromSeconds(-30)));
    }

    [Fact]
    public void CrumbsAtTheHorizonAreGone()
    {
        Assert.Equal(0, TrailFade.Alpha(TrailFade.Horizon));
        Assert.Equal(0, TrailFade.Alpha(TrailFade.Horizon + TimeSpan.FromHours(2)));
    }

    [Fact]
    public void FadeIsMonotonicBetweenFreshAndHorizon()
    {
        var prev = (int)TrailFade.FullAlpha;
        for (var age = TrailFade.FreshFor; age <= TrailFade.Horizon; age += TimeSpan.FromSeconds(30))
        {
            int alpha = TrailFade.Alpha(age);
            Assert.True(alpha <= prev, $"alpha rose from {prev} to {alpha} at {age}");
            prev = alpha;
        }
        Assert.Equal(0, prev);
    }

    [Fact]
    public void MidFadeIsVisiblyDimmerButStillThere()
    {
        var mid = TrailFade.FreshFor + (TrailFade.Horizon - TrailFade.FreshFor) / 2;
        int alpha = TrailFade.Alpha(mid);
        Assert.InRange(alpha, 1, TrailFade.FullAlpha - 1);
    }
}
