namespace EQBuddy.UI.Shared;

/// <summary>How long until a cue fires, written for a glance at a mini bar.</summary>
public static class Countdown
{
    /// <summary>m:ss while a minute or more remains, bare seconds after that — a respawn
    /// timer wants "8:12" and the tail of a cast cue wants "3s". Rounds up, so a cue never
    /// reads "0s" while it's still pending, and never goes negative.</summary>
    public static string Format(TimeSpan left)
    {
        if (left < TimeSpan.Zero) left = TimeSpan.Zero;
        return left.TotalMinutes >= 1
            ? $"{(int)left.TotalMinutes}:{left.Seconds:D2}"
            : $"{Math.Max(1, (int)Math.Ceiling(left.TotalSeconds))}s";
    }
}
