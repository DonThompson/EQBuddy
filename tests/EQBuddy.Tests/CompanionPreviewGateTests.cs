using EQBuddy.Companion;
using EQBuddy.Core;

namespace EQBuddy.Tests;

/// <summary>The second screen is merged but must not reach players until David says go
/// (2026-08-14). These pin the gate itself: the property that hides it, and the guard
/// that keeps a socket closed even when a settings file asks for one.</summary>
public class CompanionPreviewGateTests
{
    [Fact]
    public void PreviewIsOffUnlessTheEnvironmentAsksForIt()
    {
        // The test host does not set EQBUDDY_COMPANION, which is exactly a player's
        // machine. If this ever fails, released builds are surfacing the feature.
        Assert.Null(Environment.GetEnvironmentVariable(CompanionPreview.EnvVar));
        Assert.False(CompanionPreview.Enabled);
    }

    [Fact]
    public void EnabledSettingsStillOpenNoSocketWhileGated()
    {
        // The dangerous case: a settings.json copied from a field-test machine, or
        // hand-edited, carrying CompanionEnabled=true into a released build.
        var settings = new AppSettings { CompanionEnabled = true, CompanionPort = 47999 };
        using var host = new CompanionHost(settings, "test");

        Assert.False(host.Running);
        Assert.Equal(0, host.ClientCount);
    }

    [Fact]
    public void TurningItOnAtRuntimeAlsoOpensNoSocketWhileGated()
    {
        var settings = new AppSettings { CompanionPort = 47998 };
        using var host = new CompanionHost(settings, "test");

        host.SetEnabled(true);   // what the Options toggle calls

        Assert.False(host.Running);
    }
}
