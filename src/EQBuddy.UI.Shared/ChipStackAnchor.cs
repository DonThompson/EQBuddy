namespace EQBuddy.UI.Shared;

/// <summary>
/// The bookkeeping behind a chip stack that grows UPWARD, with no window attached so it
/// can be tested.
///
/// A self-sizing window normally holds its top edge and grows down. A grow-up stack has
/// to hold its BOTTOM edge instead, because its top moves every time a chip is added or
/// expires — the bottom is the edge the player actually parked against something.
///
/// Two ways that has gone wrong, both of which reached players:
///
/// - #122: the saved TOP was restored on reopen. A stack's top depends on how many chips
///   it held at close, so restoring it walked the stack upward across sessions.
/// - #152: the bottom was measured from a window that had already closed, where the
///   height reads as zero — so the "bottom" that got saved was really the top edge, and
///   the next open subtracted a chip's height from it. Exactly one chip of drift per
///   disappear/reappear cycle, which is what Snagglefern measured.
///
/// Hence the rule this class enforces: <see cref="Observe"/> only believes a window that
/// has actually laid out, and <see cref="Bottom"/> is the value to persist — never a
/// measurement taken at close.
/// </summary>
public sealed class ChipStackAnchor
{
    private double _bottom;

    /// <param name="restoredBottom">The bottom edge persisted at last close, or null for
    /// a stack with no remembered position (which simply grows from wherever it opens).</param>
    public ChipStackAnchor(double? restoredBottom = null)
    {
        _bottom = restoredBottom ?? double.NaN;
        // A restored stack must be repositioned on its FIRST layout as well as on later
        // height changes: it opens at the saved top, which belongs to the old chip count.
        FirstLayoutPending = restoredBottom is not null;
    }

    /// <summary>The anchored bottom edge, or NaN before anything has laid out. This is
    /// what gets persisted — reading the window's own geometry at close is the bug.</summary>
    public double Bottom => _bottom;

    public bool HasAnchor => !double.IsNaN(_bottom);

    public bool FirstLayoutPending { get; private set; }

    /// <summary>Where the top edge must go for a stack of this height to keep its bottom
    /// where it is. Null when there is no anchor to hold to.</summary>
    public double? TopFor(double height) => HasAnchor ? _bottom - height : null;

    /// <summary>Should this layout pass reposition the window? Only for a grow-up stack
    /// that has an anchor, and only when the height actually changed or this is the
    /// restored first pass.</summary>
    public bool ShouldReposition(bool growsUp, bool heightChanged) =>
        growsUp && HasAnchor && (heightChanged || FirstLayoutPending);

    /// <summary>Record where the window now is. **Heights of zero or less are ignored**:
    /// a closing or not-yet-measured window reports one, and believing it collapses the
    /// anchor onto the top edge — #152 exactly.</summary>
    public void Observe(double top, double height)
    {
        FirstLayoutPending = false;
        if (height > 0) _bottom = top + height;
    }
}
