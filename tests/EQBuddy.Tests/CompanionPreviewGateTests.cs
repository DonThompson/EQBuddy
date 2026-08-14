using EQBuddy.Companion;
using EQBuddy.Core;

namespace EQBuddy.Tests;

/// <summary>The second screen is merged but must not reach players until David says go
/// (2026-08-14). These pin the gate itself: the property that hides it, and the guard
/// that keeps a socket closed even when a settings file asks for one.</summary>
public class CompanionPreviewGateTests
{
    [Theory]
    // A player's machine: no marker, no variable. This is the case that must stay false
    // or released builds surface the feature.
    [InlineData(null, false, false)]
    [InlineData("", false, false)]
    [InlineData("0", false, false)]
    [InlineData("no", false, false)]
    // Either opt-in alone is enough; the marker is the one that survives a relaunch.
    [InlineData(null, true, true)]
    [InlineData("1", false, true)]
    [InlineData("true", false, true)]
    [InlineData("YES", false, true)]
    public void OptInRequiresAMarkerOrAnExplicitVariable(string? env, bool marker, bool expected) =>
        Assert.Equal(expected, CompanionPreview.IsOptedIn(env, marker));

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

    // Asserts the INVARIANT rather than a machine's answer: a listener exists only where
    // the preview is opted in. On a player's box (and CI) that reads "enabled settings
    // still open no socket" — the dangerous case, a settings.json copied from a field-test
    // machine or hand-edited. On an opted-in box it reads "the gate lets it through".
    //
    // Constructing the host does NOT save, so this test writes nothing. Its sibling —
    // "does SetEnabled obey the gate" — is deliberately absent: SetEnabled calls
    // AppSettings.Save(), which writes the REAL profile's settings.json, and running it
    // here overwrote a live install's settings (2026-08-14). A test must never reach
    // outside its sandbox; the toggle's behavior is covered by the same gate this pins.
    [Fact]
    public void ASocketExistsOnlyWhereThePreviewIsOptedIn()
    {
        var settings = new AppSettings { CompanionEnabled = true, CompanionPort = 47999 };
        using var host = new CompanionHost(settings, "test");

        Assert.Equal(CompanionPreview.Enabled, host.Running);
        if (!CompanionPreview.Enabled) Assert.Equal(0, host.ClientCount);
    }
}
