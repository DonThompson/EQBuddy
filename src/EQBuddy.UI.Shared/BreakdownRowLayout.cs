namespace EQBuddy.UI.Shared;

/// <summary>
/// How a breakdown row divides the width it has: identity first, commentary second
/// (#182, Ladylag).
///
/// The row is <c>[name] [badge] [context] [headline]</c> — "Tuyen's Chant of Flame",
/// "×1993 · avg 103.3 · 26.5 dps · 13% crit · 49% miss", "205,878". The name was the
/// only flexible column and the context took whatever it wanted, so a long context
/// squeezed the name to nothing and the ellipsis was all that rendered. Ladylag's
/// screenshot shows the end state exactly: rows reading <c>.</c>, <c>..</c> and one
/// reading nothing at all — while "Damage shield", whose context happens to be short,
/// printed in full. **It was never a parser failure.** Every one of those rows knew its
/// own name and had no room to say it.
///
/// So both are flexible now and the name outweighs the context. A name is what the row
/// IS; the context is what can be said about it, and it is already the smallest, dimmest
/// text on the row. When something has to give, that is the thing that gives.
///
/// Framework-free because both UIs draw this row and both had the defect — a WPF
/// <c>GridLength</c> and an Avalonia column string are different spellings of one
/// decision, and the two spellings are how it would drift back.
/// </summary>
public static class BreakdownRowLayout
{
    /// <summary>The name's share of the flexible width.</summary>
    public const double NameWeight = 2;

    /// <summary>The context's share. Lower on purpose — see above.</summary>
    public const double ContextWeight = 1;

    /// <summary>What the name gets out of whatever is left after the badge and the
    /// headline have taken their own widths. The property that matters is not the exact
    /// ratio but that this can never approach zero, which is what produced a row called
    /// "." in a window a player could not widen.</summary>
    public static double NameWidth(double flexibleWidth) =>
        flexibleWidth <= 0 ? 0 : flexibleWidth * NameWeight / (NameWeight + ContextWeight);

    /// <summary>How wide the invisible resize band on a frameless window is.
    ///
    /// It was 6, and 6 device-independent pixels of unmarked edge is not something a
    /// person finds: Ladylag reported that only the bottom edge resized, and the bottom
    /// edge is the one with a ⤡ grip drawn on it. The band is not the affordance — the
    /// grip is — but a band you have to hit within 6px makes the grip the ONLY way in,
    /// which is how "there is no way to widen the window" ends up being true of a window
    /// that has resized from every edge since 2026-08-06.
    ///
    /// Windows' own sizing border is about 4px plus 4px of invisible padding; this is a
    /// little more generous than that because the window is small, borderless, and sits
    /// over a game.
    /// </summary>
    public const double ResizeEdge = 10;

    /// <summary>Corner band, wider than the edge so a diagonal grab is easy to land.
    /// Kept clear of the title row's controls by being short.</summary>
    public const double ResizeCorner = 16;

    /// <summary>
    /// What hovering a row says: the full name, the full stat line, and whatever the
    /// caller wanted to add — in that order, deduplicated, one per line.
    ///
    /// Trimming a row is right: a long ability name must never push the numbers off the
    /// end. A trimmed name with no way to read it is not, which is the second half of
    /// #182. This sits on the ROW rather than on the name itself so it cannot shadow the
    /// caller's own tooltip (the burst breakdown, the last item seen, the damage total) —
    /// that text is appended rather than replaced.
    /// </summary>
    public static string HoverText(string name, string context, string? callerTooltip)
    {
        var parts = new List<string>(3);
        if (name.Length > 0) parts.Add(name);
        if (context.Length > 0) parts.Add(context);
        if (callerTooltip is { Length: > 0 } extra && !parts.Contains(extra)) parts.Add(extra);
        return string.Join(Environment.NewLine, parts);
    }
}
