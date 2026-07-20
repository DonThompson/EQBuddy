namespace EQBuddy.Core;

/// <summary>What a watch rule matches against (WATCH-001: structured events, not raw text).</summary>
public enum WatchKind
{
    /// <summary>Looted item names (the original tracked-loot behavior).</summary>
    Loot = 0,
    /// <summary>Creatures killed by you or your pet.</summary>
    Kill = 1,
    /// <summary>Skill-up skill names.</summary>
    SkillUp = 2,
    /// <summary>Your deaths (pattern optionally filters the killer's name).</summary>
    Death = 3,
    /// <summary>Level-ups and AA points (pattern ignored).</summary>
    Milestone = 4,
}

/// <summary>
/// A user-defined watch: case-insensitive substring match (TRACK-002, TRACK-021 — no
/// regex) against the chosen event kind. Persisted in settings.
/// </summary>
public sealed class TrackedRule
{
    public string Name { get; set; } = "";
    public string Pattern { get; set; } = "";
    public WatchKind Kind { get; set; } = WatchKind.Loot;
    public bool Enabled { get; set; } = true;
    /// <summary>Pinned rules get a chip in the mini dashboard.</summary>
    public bool Pinned { get; set; }
    public bool AlertBanner { get; set; } = true;
    public bool AlertSound { get; set; }

    /// <summary>Death and Milestone rules match everything when the pattern is empty.</summary>
    public bool Matches(string text) =>
        Pattern.Length > 0
            ? text.Contains(Pattern, StringComparison.OrdinalIgnoreCase)
            : Kind is WatchKind.Death or WatchKind.Milestone;
}

public sealed record TrackedRuleResult(
    string Name,
    int TotalQuantity,
    List<NameCount> Items,
    double PerHour,
    double PerActiveHour,
    DateTime? FirstMatch,
    DateTime? LastMatch,
    string? LastItem);
