using EQBuddy.UI.Shared;

namespace EQBuddy.Tests;

/// <summary>
/// #153 (adndmike): the Options volume slider moved built-in alert sounds and did
/// nothing at all to a custom .wav — 10% and 100% were indistinguishable.
///
/// The cause was not the slider and not the player. A custom path that could not be
/// found fell through to the OS notification sound (SystemSounds.Asterisk on WPF,
/// Console.Beep on the Avalonia lane), which is the one route out of the play method
/// that the slider cannot reach — and it was reachable ONLY for custom paths, because
/// the seven built-ins ship with the OS and are always present. So the custom sound was
/// never playing; the system ding was, at the system's own volume.
///
/// These tests pin the rule that makes that impossible to reintroduce: every audible
/// outcome comes back carrying the volume, and a sound the player picked that has gone
/// missing is reported rather than quietly swapped.
/// </summary>
public class AlertSoundPlanTests
{
    private const string WindowsMedia = @"C:\Windows\Media\";

    /// <summary>A stand-in for Windows' Media folder: every built-in exists, nothing else does.</summary>
    private static AlertSoundPlan Plan(string choice, double volume = 0.1,
        IEnumerable<string>? alsoOnDisk = null, IEnumerable<string>? absentBuiltIns = null)
    {
        var absent = new HashSet<string>(absentBuiltIns ?? [], StringComparer.OrdinalIgnoreCase);
        var extra = new HashSet<string>(alsoOnDisk ?? [], StringComparer.OrdinalIgnoreCase);
        return AlertSoundPlanner.Plan(choice, volume,
            name => absent.Contains(name) ? "" : WindowsMedia + name + ".wav",
            path => path.StartsWith(WindowsMedia, StringComparison.OrdinalIgnoreCase) || extra.Contains(path));
    }

    // ---- the regression itself ----

    /// <summary>THE #153 TEST. A custom file that is gone must not produce a play that
    /// bypasses the volume — the old code's SystemSounds fall-through did exactly that.</summary>
    [Fact]
    public void AMissingCustomFileStillPlaysAtTheChosenVolume()
    {
        var plan = Plan(@"C:\EQL\triggers\rampage.wav", volume: 0.1);

        Assert.Equal(AlertSoundSource.Substitute, plan.Source);
        Assert.True(plan.CarriesVolume);
        Assert.Equal(0.1, plan.Volume);
    }

    /// <summary>...and it says so, rather than swapping the sound behind the player's
    /// back. A silent substitution is what made this look like a slider bug.</summary>
    [Fact]
    public void AMissingCustomFileIsReportedByName()
    {
        var plan = Plan(@"C:\EQL\triggers\rampage.wav");

        Assert.True(plan.ShouldReportMissingFile);
        Assert.Equal(@"C:\EQL\triggers\rampage.wav", plan.MissingFile);
        Assert.Contains(@"C:\EQL\triggers\rampage.wav", AlertSoundPlanner.MissingFileMessage(plan.MissingFile));
    }

    /// <summary>The heart of it: built-in and custom are the same deal. The reporter's
    /// evidence was that one obeyed the slider and the other didn't.</summary>
    [Theory]
    [InlineData("Ding")]
    [InlineData("Alarm")]
    [InlineData(@"C:\EQL\triggers\rampage.wav")]
    [InlineData(@"C:\EQL\triggers\gone.wav")]
    [InlineData("Asterisk")]           // legacy name
    [InlineData("")]                   // never set
    public void EveryAudiblePlanCarriesTheVolume(string choice)
    {
        foreach (var volume in new[] { 0.1, 0.5, 1.0 })
        {
            var plan = Plan(choice, volume, alsoOnDisk: [@"C:\EQL\triggers\rampage.wav"]);
            Assert.True(plan.CarriesVolume, $"{choice} at {volume} produced a play with no file");
            Assert.Equal(volume, plan.Volume);
        }
    }

    // ---- the ordinary routes ----

    [Fact]
    public void ABuiltInResolvesToThisPlatformsFile()
    {
        var plan = Plan("Chimes");

        Assert.Equal(AlertSoundSource.BuiltIn, plan.Source);
        Assert.Equal(WindowsMedia + "Chimes.wav", plan.FilePath);
        Assert.False(plan.ShouldReportMissingFile);
    }

    [Fact]
    public void ACustomFileThatIsThereIsPlayedAsChosen()
    {
        const string mine = @"C:\EQL\triggers\rampage.wav";
        var plan = Plan(mine, alsoOnDisk: [mine]);

        Assert.Equal(AlertSoundSource.Custom, plan.Source);
        Assert.Equal(mine, plan.FilePath);
        Assert.False(plan.ShouldReportMissingFile);
    }

    [Fact]
    public void LegacySystemSoundNamesStillLandOnThePalette()
    {
        Assert.Equal(WindowsMedia + "Chord.wav", Plan("Beep").FilePath);
        Assert.Equal(AlertSoundSource.BuiltIn, Plan("Beep").Source);
    }

    [Fact]
    public void AnUnsetChoiceIsTheDefaultBuiltIn()
    {
        var plan = Plan("");

        Assert.Equal(AlertSoundSource.BuiltIn, plan.Source);
        Assert.Equal(WindowsMedia + "Ding.wav", plan.FilePath);
        Assert.False(plan.ShouldReportMissingFile);
    }

    /// <summary>"Off" is an answer, not a failure — no stand-in and nothing to report.</summary>
    [Fact]
    public void OffPlaysNothingAndComplainsAboutNothing()
    {
        var plan = Plan(AlertSoundCatalog.OffChoice);

        Assert.Equal(AlertSoundSource.Silent, plan.Source);
        Assert.Equal("", plan.FilePath);
        Assert.False(plan.ShouldReportMissingFile);
    }

    // ---- the platform's own gaps ----

    /// <summary>A Linux sound theme need not carry every freedesktop event. The player
    /// picked a sound EQBuddy offered, so that is ours to paper over silently — unlike a
    /// file of theirs that has gone away.</summary>
    [Fact]
    public void AGapInThePlatformsSoundThemeSubstitutesWithoutBlamingThePlayer()
    {
        var plan = Plan("Tada", absentBuiltIns: ["Tada"]);

        Assert.Equal(AlertSoundSource.Substitute, plan.Source);
        Assert.Equal(WindowsMedia + "Ding.wav", plan.FilePath);
        Assert.True(plan.CarriesVolume);
        Assert.False(plan.ShouldReportMissingFile);
    }

    /// <summary>Nothing playable anywhere: no file, and therefore no volume to carry.
    /// The caller logs — it must not invent a noise the slider cannot reach.</summary>
    [Fact]
    public void WithNoStandInLeftThePlanIsUnplayableRatherThanAnUncontrollableBeep()
    {
        var plan = AlertSoundPlanner.Plan(@"C:\EQL\triggers\gone.wav", 0.1, _ => "", _ => false);

        Assert.Equal(AlertSoundSource.Unplayable, plan.Source);
        Assert.Equal("", plan.FilePath);
        Assert.False(plan.CarriesVolume);
        Assert.True(plan.ShouldReportMissingFile);
    }

    // ---- volume hygiene ----

    [Theory]
    [InlineData(-1.0, 0.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(0.35, 0.35)]
    [InlineData(1.0, 1.0)]
    [InlineData(4.2, 1.0)]
    public void TheVolumeIsClampedToWhatAPlayerCanAccept(double stored, double expected) =>
        Assert.Equal(expected, Plan("Ding", stored).Volume);

    /// <summary>NaN survives Math.Clamp and would reach the player as NaN; a hand-edited
    /// settings.json is exactly where that comes from.</summary>
    [Fact]
    public void ANonsenseStoredVolumeFallsBackToFullRatherThanNaN()
    {
        var plan = Plan("Ding", double.NaN);

        Assert.False(double.IsNaN(plan.Volume));
        Assert.Equal(1.0, plan.Volume);
    }

    /// <summary>The catalog and the planner must agree on what "custom" means, or a
    /// built-in would be probed on disk as a path and reported missing.</summary>
    [Fact]
    public void EveryBuiltInNameIsPlannedAsABuiltIn()
    {
        foreach (var name in AlertSoundCatalog.Names)
            Assert.Equal(AlertSoundSource.BuiltIn, Plan(name).Source);
    }
}
