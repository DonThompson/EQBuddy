using EQBuddy.Core;

namespace EQBuddy.UI.Shared;

/// <summary>#138 (aodgizmo): the "Epic complete" master check flips a whole class's
/// rows in one click — and one stray click left unchecking every row by hand as the
/// only way back. The bulk check now snapshots the state it overwrites so unchecking
/// the master restores it. This owns only the state; the confirmation dialog stays
/// in the views.</summary>
public static class EpicCompleteToggle
{
    /// <summary>Rows the bulk check would actually flip — the number the confirmation
    /// names. Zero means nothing gets overwritten and no dialog is warranted.</summary>
    public static int CountUnchecked(IEnumerable<EpicQuestChecklistItem> items) =>
        items.Count(i => !i.Acquired);

    /// <summary>Capture BEFORE the bulk check: ids of the rows already acquired.
    /// Ids, not indexes — catalog refreshes reorder and reseed rows.</summary>
    public static List<string> Snapshot(IEnumerable<EpicQuestChecklistItem> items) =>
        [.. items.Where(i => i.Acquired).Select(i => i.Id)];

    public static void CheckAll(IEnumerable<EpicQuestChecklistItem> items)
    {
        foreach (var i in items) i.Acquired = true;
    }

    /// <summary>Put every row back the way the snapshot saw it — the snapshot wins
    /// over any edit made since the bulk check (rows are disabled while complete, so
    /// only the bulk check itself can have moved them). A row the snapshot never saw
    /// (added by a catalog refresh in between) restores to unchecked: it was not
    /// acquired when the master was checked.</summary>
    public static void Restore(IEnumerable<EpicQuestChecklistItem> items, List<string> acquiredIds)
    {
        var acquired = new HashSet<string>(acquiredIds, StringComparer.Ordinal);
        foreach (var i in items) i.Acquired = acquired.Contains(i.Id);
    }
}
