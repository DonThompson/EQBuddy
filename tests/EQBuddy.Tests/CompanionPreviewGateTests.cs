using EQBuddy.Companion;
using EQBuddy.Core;

namespace EQBuddy.Tests;

/// <summary>The second screen is merged but must not reach players until David says go
/// (2026-08-14). These pin the gate itself: the property that hides it, and the guard
/// that keeps a socket closed even when a settings file asks for one.</summary>
public class CompanionPreviewGateTests
{
    [Fact]
    public void PreviewIsOffWithoutAnExplicitOptIn()
    {
        // The test host sets neither the env var nor the marker file, which is exactly
        // a player's machine. If this fails, released builds are surfacing the feature.
        Assert.Null(Environment.GetEnvironmentVariable(CompanionPreview.EnvVar));
        Assert.False(File.Exists(CompanionPreview.MarkerPath));
        Assert.False(CompanionPreview.Enabled);
    }

    [Fact]
    public void TheMarkerLivesBesideSettingsSoItSurvivesHoweverTheAppWasLaunched()
    {
        // The point of the file gate: a process reads the environment it INHERITED, so
        // an installer-relaunched app never saw a variable set afterwards. A path under
        // the profile folder has no such dependency — and it follows EQBUDDY_APPDATA,
        // so an isolated profile gets its own answer.
        Assert.Equal(AppPaths.File(CompanionPreview.MarkerFile), CompanionPreview.MarkerPath);
        Assert.EndsWith("mobile-preview.enabled", CompanionPreview.MarkerPath);
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
