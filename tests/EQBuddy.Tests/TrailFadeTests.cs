using EQBuddy.UI.Shared;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>The breadcrumb fade clock (MapWindow trail): wall-clock age drives
/// alpha, so a camped player's route dissolves without any new /loc arriving.
/// The trail is the last minute of movement, fading continuously from the moment
/// a crumb lands (David's field tests, 2026-08-10).</summary>
public class TrailFadeTests
{
    [Fact]
    public void BrandNewCrumbsDrawAtFullStrength()
    {
        Assert.Equal(TrailFade.FullAlpha, TrailFade.Alpha(TimeSpan.Zero));
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
    public void FadeIsContinuousNoPlateau()
    {
        // "Fade continuously": strictly dimmer every few seconds of age, from the
        // very first — no full-strength plateau at the head of the tail.
        var prev = (int)TrailFade.FullAlpha + 1;
        for (var age = TimeSpan.Zero; age < TrailFade.Horizon; age += TimeSpan.FromSeconds(5))
        {
            int alpha = TrailFade.Alpha(age);
            Assert.True(alpha < prev, $"alpha {alpha} at {age} did not dim (was {prev})");
            Assert.True(alpha > 0, $"alpha hit zero at {age}, inside the horizon");
            prev = alpha;
        }
    }

    [Fact]
    public void MidFadeIsVisiblyDimmerButStillThere()
    {
        int alpha = TrailFade.Alpha(TrailFade.Horizon / 2);
        Assert.InRange(alpha, TrailFade.FullAlpha / 2 - 5, TrailFade.FullAlpha / 2 + 5);
    }
}
