using EQBuddy.UI.Shared;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>
/// One EQBuddy per profile, on every platform (#169). The Avalonia build's guard was a
/// Windows named mutex, so Linux and macOS ran a full second copy per launch — two
/// whole-file writers on one settings.json, where the last save silently reverts
/// everything the other copy changed.
///
/// The other half of the contract matters just as much: a lock nobody is behind must
/// not stop EQBuddy launching. That is why standing down requires a live copy to
/// actually answer.
/// </summary>
public class SingleInstanceTests : IDisposable
{
    private readonly string _profile =
        Directory.CreateTempSubdirectory("eqbuddy-instance-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_profile, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void TheSecondClaimOnAProfileIsRefused()
    {
        using var first = SingleInstance.TryClaim(_profile);
        Assert.NotNull(first);
        Assert.Null(SingleInstance.TryClaim(_profile));
    }

    [Fact]
    public void ReleasingTheClaimLetsTheNextCopyIn()
    {
        SingleInstance.TryClaim(_profile)!.Dispose();
        using var second = SingleInstance.TryClaim(_profile);
        Assert.NotNull(second);
    }

    /// <summary>Keyed on the profile directory, so the isolated EQBUDDY_APPDATA the
    /// tests and screenshots use still runs alongside a real EQBuddy.</summary>
    [Fact]
    public void ADifferentProfileIsADifferentClaim()
    {
        var other = Directory.CreateTempSubdirectory("eqbuddy-instance-other-").FullName;
        try
        {
            using var mine = SingleInstance.TryClaim(_profile);
            using var theirs = SingleInstance.TryClaim(other);
            Assert.NotNull(mine);
            Assert.NotNull(theirs);
        }
        finally
        {
            try { Directory.Delete(other, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>The running copy's tick picks the request up, exactly once, and that
    /// pickup is what tells the second launch somebody is home — so it stands down and
    /// the widget surfaces instead of a twin starting.</summary>
    [Fact]
    public void AnAnsweredRequestIsDeliveredOnceAndReportsSuccess()
    {
        Assert.False(SingleInstance.ConsumeShowRequest(_profile));

        var surfaced = 0;
        var answered = SingleInstance.AskRunningCopyToShow(_profile, TimeSpan.FromSeconds(2),
            _ =>   // stands in for the running copy's 1 s tick
            {
                if (SingleInstance.ConsumeShowRequest(_profile)) surfaced++;
            });

        Assert.True(answered);
        Assert.Equal(1, surfaced);
        Assert.False(SingleInstance.ConsumeShowRequest(_profile));
    }

    /// <summary>Nobody home. A stale instance.lock — a copy that was SIGKILLed on a
    /// filesystem with odd locking, say — must not be able to make EQBuddy unlaunchable,
    /// so an unanswered request fails and withdraws itself rather than lying in wait for
    /// some later launch to trip over.</summary>
    [Fact]
    public void AnUnansweredRequestFailsAndWithdrawsItself()
    {
        var answered = SingleInstance.AskRunningCopyToShow(
            _profile, TimeSpan.FromMilliseconds(120), _ => { });

        Assert.False(answered);
        Assert.False(File.Exists(Path.Combine(_profile, SingleInstance.ShowRequestFileName)));
        Assert.False(SingleInstance.ConsumeShowRequest(_profile));
    }
}
