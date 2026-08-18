using EQBuddy.Core;
using EQBuddy.UI.Shared;

namespace EQBuddy.Tests;

/// <summary>
/// The "Epic complete" master check as it touches SETTINGS — the half that was missing.
///
/// <c>EpicCompleteToggleTests</c> beside this file has covered the row helpers since #138,
/// and every one of those tests passed for the two days the feature was UNREACHABLE:
/// nothing in either UI called them, and <see cref="AppSettings.EpicQuestCompleted"/> had
/// a reader nobody called and a writer nowhere at all (found by liminalwarmth in #210).
///
/// So these assert the write path specifically — settings in, settings out — because a
/// green suite over an orphaned helper is exactly as green as one over a working feature,
/// and that is the whole reason the regression survived a release.
/// </summary>
public class EpicCompleteWritePathTests
{
    private static AppSettings Settings(params EpicQuestChecklistItem[] items)
    {
        var s = new AppSettings();
        s.EpicQuestChecklist.AddRange(items);
        return s;
    }

    private static EpicQuestChecklistItem Row(string id, string cls, bool acquired = false,
        bool classic = true) => new()
        {
            Id = id, ClassName = cls, QuestItem = id, Acquired = acquired,
            AvailableInClassic = classic,
        };

    [Fact]
    public void MarkCompleteWritesTheSettingAndTicksEveryRow()
    {
        var s = Settings(Row("a", "Bard", acquired: true), Row("b", "Bard"), Row("c", "Bard"));
        var items = EpicCompleteToggle.ItemsFor(s.EpicQuestChecklist, "Bard", classicOnly: false);

        EpicCompleteToggle.MarkComplete(s, "Bard", items);

        Assert.True(EpicCompleteToggle.IsComplete(s, "Bard"));
        Assert.All(s.EpicQuestChecklist, i => Assert.True(i.Acquired));
    }

    [Fact]
    public void MarkCompleteSnapshotsWhatItIsAboutToOverwrite()
    {
        // Taken BEFORE the bulk check, or the undo restores the state it was meant to
        // preserve — every row acquired — and does nothing at all.
        var s = Settings(Row("a", "Bard", acquired: true), Row("b", "Bard"));
        var items = EpicCompleteToggle.ItemsFor(s.EpicQuestChecklist, "Bard", classicOnly: false);

        EpicCompleteToggle.MarkComplete(s, "Bard", items);

        Assert.Equal(["a"], s.EpicQuestPreCompleteAcquired["Bard"]);
    }

    [Fact]
    public void ReopenClearsTheSettingAndPutsTheRowsBack()
    {
        var s = Settings(Row("a", "Bard", acquired: true), Row("b", "Bard"), Row("c", "Bard"));
        var items = EpicCompleteToggle.ItemsFor(s.EpicQuestChecklist, "Bard", classicOnly: false);
        EpicCompleteToggle.MarkComplete(s, "Bard", items);

        EpicCompleteToggle.Reopen(s, "Bard");
        Assert.True(EpicCompleteToggle.RestoreFrom(s, "Bard", items));

        Assert.False(EpicCompleteToggle.IsComplete(s, "Bard"));
        // The player's own tick returns; the two the master check placed do not. Unlike
        // the Sky turn-in, undoing here SHOULD move rows — the master check is what moved
        // them in the first place.
        Assert.True(s.EpicQuestChecklist.Single(i => i.Id == "a").Acquired);
        Assert.False(s.EpicQuestChecklist.Single(i => i.Id == "b").Acquired);
        Assert.False(s.EpicQuestChecklist.Single(i => i.Id == "c").Acquired);
        Assert.DoesNotContain("Bard", s.EpicQuestPreCompleteAcquired.Keys);
    }

    [Fact]
    public void ReopeningAClassCompletedBeforeSnapshotsExistedLeavesRowsAlone()
    {
        // The documented fallback: no snapshot means the completion predates the undo,
        // and silently unticking a whole epic would be the destructive reading.
        var s = Settings(Row("a", "Bard", acquired: true), Row("b", "Bard", acquired: true));
        s.EpicQuestCompleted.Add("Bard");
        var items = EpicCompleteToggle.ItemsFor(s.EpicQuestChecklist, "Bard", classicOnly: false);

        EpicCompleteToggle.Reopen(s, "Bard");

        Assert.False(EpicCompleteToggle.RestoreFrom(s, "Bard", items));
        Assert.All(s.EpicQuestChecklist, i => Assert.True(i.Acquired));
    }

    [Fact]
    public void MarkCompleteIsIdempotent()
    {
        // A second click must not re-snapshot: by then every row is acquired, and storing
        // THAT as the undo state would quietly make the undo a no-op.
        var s = Settings(Row("a", "Bard", acquired: true), Row("b", "Bard"));
        var items = EpicCompleteToggle.ItemsFor(s.EpicQuestChecklist, "Bard", classicOnly: false);

        EpicCompleteToggle.MarkComplete(s, "Bard", items);
        EpicCompleteToggle.MarkComplete(s, "Bard", items);

        Assert.Single(s.EpicQuestCompleted);
        Assert.Equal(["a"], s.EpicQuestPreCompleteAcquired["Bard"]);
    }

    [Fact]
    public void CompletionIsPerClassAndNeverTouchesAnother()
    {
        var s = Settings(Row("a", "Bard"), Row("b", "Ranger"));
        var bard = EpicCompleteToggle.ItemsFor(s.EpicQuestChecklist, "Bard", classicOnly: false);

        EpicCompleteToggle.MarkComplete(s, "Bard", bard);

        Assert.False(EpicCompleteToggle.IsComplete(s, "Ranger"));
        Assert.False(s.EpicQuestChecklist.Single(i => i.Id == "b").Acquired);
    }

    [Fact]
    public void ItemsForHonoursTheClassicEraLens()
    {
        // The master check must flip exactly what the player can SEE. With the classic
        // lens on, ticking rows hidden behind it is a silent edit to a filtered-out part
        // of the checklist.
        var s = Settings(Row("a", "Bard"), Row("b", "Bard", classic: false));

        Assert.Equal(["a"],
            EpicCompleteToggle.ItemsFor(s.EpicQuestChecklist, "Bard", classicOnly: true)
                .Select(i => i.Id));
        Assert.Equal(2,
            EpicCompleteToggle.ItemsFor(s.EpicQuestChecklist, "Bard", classicOnly: false).Count);
    }

    [Fact]
    public void ClassNamesMatchCaseInsensitively()
    {
        var s = Settings(Row("a", "Bard"));
        s.EpicQuestCompleted.Add("bard");

        Assert.True(EpicCompleteToggle.IsComplete(s, "Bard"));
        EpicCompleteToggle.Reopen(s, "BARD");
        Assert.Empty(s.EpicQuestCompleted);
    }

    // ---- the confirmation, which is a decision and not a dialog ----

    [Fact]
    public void ConfirmPromptCountsOnlyWhatWouldBeOverwritten()
    {
        var items = new List<EpicQuestChecklistItem>
        {
            Row("a", "Bard", acquired: true), Row("b", "Bard"), Row("c", "Bard"),
        };
        Assert.Equal("Mark all 2 remaining Bard steps complete?",
            EpicCompleteToggle.ConfirmPrompt("Bard", items));
    }

    [Fact]
    public void ConfirmPromptIsSingularForOneRow()
    {
        Assert.Equal("Mark all 1 remaining Bard step complete?",
            EpicCompleteToggle.ConfirmPrompt("Bard", [Row("b", "Bard")]));
    }

    [Fact]
    public void NothingToOverwriteWarrantsNoDialog()
    {
        // Every row already ticked by hand: the master check changes no row, so a prompt
        // could only be answered one way and teaches nothing.
        Assert.Null(EpicCompleteToggle.ConfirmPrompt("Bard",
            [Row("a", "Bard", acquired: true), Row("b", "Bard", acquired: true)]));
    }

    [Fact]
    public void TheTwoChecklistsOfferOneVocabulary()
    {
        // The asymmetry between these two is what let the Sky turn-in go missing for two
        // days and this one for three. They say the same word for the same act.
        Assert.Equal(SkyCompleteToggle.ButtonLabel(true), EpicCompleteToggle.ButtonLabel(true));
    }
}
