namespace EQBuddy.Core;

/// <summary>
/// A user-defined loot watch: case-insensitive substring match against looted item names
/// (TRACK-002, TRACK-021 — no regex). Persisted in settings.
/// </summary>
public sealed class TrackedRule
{
    public string Name { get; set; } = "";
    public string Pattern { get; set; } = "";
    public bool Enabled { get; set; } = true;
    /// <summary>Pinned rules get a chip in the mini dashboard.</summary>
    public bool Pinned { get; set; }
    public bool AlertBanner { get; set; } = true;
    public bool AlertSound { get; set; }

    public bool Matches(string itemName) =>
        Pattern.Length > 0 &&
        itemName.Contains(Pattern, StringComparison.OrdinalIgnoreCase);
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
