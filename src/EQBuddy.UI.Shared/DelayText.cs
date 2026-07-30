using System.Globalization;
using EQBuddy.Core;

namespace EQBuddy.UI.Shared;

/// <summary>
/// Reading and writing the per-rule delay box.
///
/// The box was seconds-only while delays were combat cues, where seconds is the natural
/// unit. Respawn timers pushed the ceiling to 30 minutes, and making someone work out that
/// eight minutes is 480 — every time they read the rule back, not just when typing it — is
/// the kind of small friction that gets a feature quietly abandoned. So minutes are
/// accepted and, where they're exact, shown.
/// </summary>
public static class DelayText
{
    /// <summary>
    /// Parse what the user typed. A bare number is seconds ("2.5"); a trailing m/min is
    /// minutes ("8m", "8 min"); "m:ss" is minutes and seconds ("1:30"). Anything
    /// unrecognisable is 0 — the box is narrow, it reformats on the spot, and a rule that
    /// alerts immediately is a better failure than one that never alerts at all.
    /// The result is clamped by <see cref="TrackedRule.AlertDelaySeconds"/>.
    /// </summary>
    public static double Parse(string? input)
    {
        var text = (input ?? "").Trim().ToLowerInvariant();
        if (text.Length == 0) return 0;

        if (text.Contains(':'))
        {
            var parts = text.Split(':', 2);
            return double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var mm)
                && double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var ss)
                ? mm * 60 + ss
                : 0;
        }

        var minutes = false;
        foreach (var suffix in (string[])["minutes", "minute", "mins", "min", "m"])
        {
            if (!text.EndsWith(suffix, StringComparison.Ordinal)) continue;
            text = text[..^suffix.Length].TrimEnd();
            minutes = true;
            break;
        }
        if (text.EndsWith('s')) text = text[..^1].TrimEnd();

        if (!double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)) return 0;
        return minutes ? value * 60 : value;
    }

    /// <summary>How a stored delay is shown back. Empty for none, whole minutes as "8m",
    /// anything else as seconds — so what someone typed is usually what they see.</summary>
    public static string Format(double seconds)
    {
        if (seconds <= 0) return "";
        if (seconds >= 60 && seconds % 60 == 0)
            return (seconds / 60).ToString("0", CultureInfo.InvariantCulture) + "m";
        return seconds.ToString("0.#", CultureInfo.InvariantCulture);
    }
}
