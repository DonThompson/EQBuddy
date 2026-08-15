using EQBuddy.UI.Shared;
using Xunit;

namespace EQBuddy.Tests;

/// <summary>
/// What ↻ Reset tells you before it runs (#159, Frankthetankk).
///
/// The button said "Reset session stats" while, with archiving on, also moving your live
/// log into Logs\archive. He raised it having just lost a session to the idle cleanup —
/// so the thing these tests actually protect is that the words match the file operation,
/// and that "nothing is deleted" gets said rather than left to be inferred by someone
/// already worried about losing data.
/// </summary>
public class ResetPromptTests
{
    [Fact]
    public void WithArchivingOnTheTooltipAdmitsItMovesTheLog()
    {
        var tip = ResetPrompt.Tooltip(archiveLogs: true);

        Assert.Contains("Logs\\archive", tip);
        Assert.Contains("Nothing is deleted", tip);
    }

    [Fact]
    public void WithArchivingOffTheTooltipPromisesTheLogIsUntouched()
    {
        var tip = ResetPrompt.Tooltip(archiveLogs: false);

        // It genuinely is untouched in this mode, so claiming a move would be its own lie.
        Assert.Contains("left alone", tip);
        Assert.DoesNotContain("moves to", tip);
    }

    [Fact]
    public void ArchivingOnAsksBeforeTouchingAFile()
    {
        var ask = ResetPrompt.Confirmation(archiveLogs: true);

        Assert.NotNull(ask);
        Assert.Contains("Logs\\archive", ask);
        Assert.Contains("Nothing is deleted", ask);
    }

    [Fact]
    public void ArchivingOffAsksNothing()
    {
        // A dialog in front of an action that cannot lose anything is a speed bump that
        // teaches people to click through dialogs — including the one that matters.
        Assert.Null(ResetPrompt.Confirmation(archiveLogs: false));
    }
}
