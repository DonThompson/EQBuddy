using EQBuddy.Companion;
using EQBuddy.Core;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>
/// EQBuddy Mobile launched 2026-08-14 and its preview gate is gone. That gate used to be
/// the belt beside the braces; <see cref="AppSettings.CompanionEnabled"/> is now the ONLY
/// thing standing between a dormant feature and a listening port, which makes pinning it
/// more important than it was, not less.
///
/// Constructing a host does not save settings — these write nothing to any profile.
/// </summary>
public class CompanionEnableTests
{
    [Fact]
    public void OffByDefaultMeansNoSocketAtAll()
    {
        // The shape of a fresh install. Nobody who hasn't asked for a second screen
        // should have a port open on their machine because they updated EQBuddy.
        var settings = new AppSettings();
        Assert.False(settings.CompanionEnabled);

        using var host = new CompanionHost(settings, "test");
        Assert.False(host.Running);
        Assert.Equal(0, host.ClientCount);
        Assert.Null(host.PairingUrl);
    }

    [Fact]
    public void TurningItOnIsWhatOpensThePort()
    {
        var settings = new AppSettings { CompanionEnabled = true, CompanionPort = 0 };
        using var host = new CompanionHost(settings, "test");

        Assert.True(host.Running);
        Assert.Null(host.LastError);
        // A token is minted on first listen, and the pairing URL carries it in the
        // FRAGMENT so it never appears in an HTTP request line.
        Assert.NotNull(settings.CompanionToken);
        Assert.Contains("#" + settings.CompanionToken, host.PairingUrl);
    }

    [Fact]
    public void TickCostsNothingWhileNobodyIsPaired()
    {
        // The perf contract the whole feature rests on: with it off, a tick is a couple
        // of field reads and builds no projection, so a player who never uses this pays
        // nothing for it once a second forever.
        var settings = new AppSettings();
        using var host = new CompanionHost(settings, "test");
        var timers = new SpawnTimers(SpawnCatalog.LoadEmbedded(),
            new SpawnOverrides(), Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json"));

        var ex = Record.Exception(() => host.Tick(null, timers, "Dranak", DateTime.Now));
        Assert.Null(ex);
        Assert.False(host.Running);
    }
}
