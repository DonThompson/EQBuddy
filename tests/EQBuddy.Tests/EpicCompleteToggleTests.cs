using EQBuddy.Core;
using EQBuddy.UI.Shared;

namespace EQBuddy.Tests;

/// <summary>#138 (aodgizmo): the "Epic complete" master check bulk-flips a whole
/// class's rows — the snapshot it takes first is the only way back, so the
/// round-trip has to be exact.</summary>
public class EpicCompleteToggleTests
{
    private static List<EpicQuestChecklistItem> Rows(params (string Id, bool Acquired)[] rows) =>
        [.. rows.Select(r => new EpicQuestChecklistItem { Id = r.Id, Acquired = r.Acquired })];

    [Fact]
    public void SnapshotThenRestoreRoundTripsEveryRow()
    {
        var items = Rows(("a", true), ("b", false), ("c", true), ("d", false));
        var snapshot = EpicCompleteToggle.Snapshot(items);

        EpicCompleteToggle.CheckAll(items);
        Assert.All(items, i => Assert.True(i.Acquired));

        EpicCompleteToggle.Restore(items, snapshot);
        Assert.Equal([true, false, true, false], items.Select(i => i.Acquired));
    }

    /// <summary>Rows are disabled while the class is complete, but if an edit sneaks
    /// in anyway (older settings file, a future auto-tick) the snapshot wins — the
    /// restore's contract is "the way it was", not "the way it drifted to".</summary>
    [Fact]
    public void RestoreWinsOverEditsMadeAfterTheSnapshot()
    {
        var items = Rows(("a", true), ("b", false));
        var snapshot = EpicCompleteToggle.Snapshot(items);
        EpicCompleteToggle.CheckAll(items);

        items[0].Acquired = false;   // drifted while complete
        EpicCompleteToggle.Restore(items, snapshot);

        Assert.True(items[0].Acquired);
        Assert.False(items[1].Acquired);
    }

    /// <summary>A catalog refresh can add rows between the bulk check and the undo.
    /// The snapshot never saw them, so they restore to unchecked — they were not
    /// acquired when the master was checked.</summary>
    [Fact]
    public void ARowAddedAfterTheSnapshotRestoresUnchecked()
    {
        var items = Rows(("a", true));
        var snapshot = EpicCompleteToggle.Snapshot(items);
        EpicCompleteToggle.CheckAll(items);

        items.Add(new EpicQuestChecklistItem { Id = "new", Acquired = true });
        EpicCompleteToggle.Restore(items, snapshot);

        Assert.True(items[0].Acquired);
        Assert.False(items[1].Acquired);
    }

    /// <summary>The confirmation names the number of rows the bulk check would flip;
    /// zero means nothing gets overwritten and the view shows no dialog.</summary>
    [Fact]
    public void CountUncheckedCountsOnlyTheRowsTheBulkCheckWouldFlip()
    {
        Assert.Equal(2, EpicCompleteToggle.CountUnchecked(Rows(("a", true), ("b", false), ("c", false))));
        Assert.Equal(0, EpicCompleteToggle.CountUnchecked(Rows(("a", true), ("b", true))));
    }
}
