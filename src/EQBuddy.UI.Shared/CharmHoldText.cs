namespace EQBuddy.UI.Shared;

/// <summary>
/// The running charm-hold clock (#130, bjstrange): "how long has this charm held".
/// Shared between the WPF and Avalonia pet-breakout headers; the break-time
/// announcement rides the fade alert's label instead (SessionStats.FadeLabel).
/// </summary>
public static class CharmHoldText
{
    /// <summary>" · charmed 4:32" while a charm is holding; "" otherwise.</summary>
    public static string Suffix(DateTime? charmedSince, DateTime now)
    {
        if (charmedSince is not { } since || now < since) return "";
        var t = now - since;
        var text = t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes}:{t.Seconds:00}";
        return $" · charmed {text}";
    }
}
