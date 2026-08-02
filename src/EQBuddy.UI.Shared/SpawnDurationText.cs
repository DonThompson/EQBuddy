using System.Globalization;

namespace EQBuddy.UI.Shared;

/// <summary>
/// Reading and writing spawn-timer durations. Distinct from <see cref="DelayText"/> on
/// purpose: rule delays live in seconds and cap at 30 minutes, spawn cycles live in
/// minutes and run to a week. A bare number here is MINUTES ("22" = 22 minutes), because
/// that is how every wiki writes them; seconds need an explicit "s" ("90s"). Suffixes
/// m/h/d, compounds ("3d 12h", "1h30m"), and colon forms ("6:40" = m:ss,
/// "1:00:00" = h:mm:ss) all parse.
/// </summary>
public static class SpawnDurationText
{
    /// <summary>Seconds, or null when the text holds no usable duration.</summary>
    public static double? Parse(string? input)
    {
        var text = (input ?? "").Trim().ToLowerInvariant();
        if (text.Length == 0) return null;

        if (text.Contains(':'))
        {
            var parts = text.Split(':');
            if (parts.Length is < 2 or > 3) return null;
            double total = 0;
            foreach (var p in parts)
            {
                if (!double.TryParse(p, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
                    return null;
                total = total * 60 + v;
            }
            return total > 0 ? total : null;
        }

        // Compounds: walk number+unit pairs ("3d 12h", "1h30m", "6m40s").
        double seconds = 0, number = 0;
        bool haveNumber = false, sawUnit = false;
        foreach (var ch in text)
        {
            if (char.IsDigit(ch) || ch == '.')
            {
                if (!haveNumber) { number = 0; haveNumber = true; }
                if (ch == '.') return ParseSimple(text); // decimals only in the simple form
                number = number * 10 + (ch - '0');
            }
            else if (ch is 'd' or 'h' or 'm' or 's')
            {
                if (!haveNumber) return null;
                seconds += number * (ch switch { 'd' => 86400, 'h' => 3600, 'm' => 60, _ => 1 });
                haveNumber = false;
                sawUnit = true;
            }
            else if (!char.IsWhiteSpace(ch)) return null;
        }
        if (haveNumber && sawUnit) return null;      // trailing bare number after a unit
        if (!sawUnit) return ParseSimple(text);      // no units at all: bare minutes
        return seconds > 0 ? seconds : null;

        static double? ParseSimple(string t) =>
            double.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) && v > 0
                ? v * 60
                : null;
    }

    /// <summary>"3d 12h" / "1h 30m" / "22m" / "6m 40s" / "90s" — largest two units.</summary>
    public static string Format(double? seconds)
    {
        if (seconds is not { } s || s <= 0) return "";
        var t = TimeSpan.FromSeconds(s);
        var parts = new List<string>(2);
        if (t.Days > 0) parts.Add($"{t.Days}d");
        if (t.Hours > 0) parts.Add($"{t.Hours}h");
        if (parts.Count < 2 && t.Minutes > 0) parts.Add($"{t.Minutes}m");
        if (parts.Count < 2 && t.Seconds > 0) parts.Add($"{t.Seconds}s");
        return parts.Count > 0 ? string.Join(" ", parts) : "0s";
    }

    /// <summary>Countdown display: "2d 3h" over a day, "1:03:22" with hours,
    /// "7:54" under an hour, "due" at or past zero.</summary>
    public static string Countdown(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero) return "due";
        if (remaining.TotalDays >= 1)
            return $"{remaining.Days}d {remaining.Hours}h";
        if (remaining.TotalHours >= 1)
            return $"{(int)remaining.TotalHours}:{remaining.Minutes:00}:{remaining.Seconds:00}";
        return $"{remaining.Minutes}:{remaining.Seconds:00}";
    }
}
