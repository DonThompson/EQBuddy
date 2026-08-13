using System.Text.RegularExpressions;

namespace EQBuddy.Core;

/// <summary>
/// Instance difficulty decoded from a "You have entered X." zone name — the only
/// place EQ Legends states it (#109; the shapes come from a community 1.4M-line log
/// survey, 2026-08-13, and match our own observed tier variants):
///
///   "The Plane of Hate"                    → open world
///   "The Plane of Hate - Solo"             → base instance (D0)
///   "Nagafen's Lair - Group 3 (Fused)"     → D3
///   "Najena 4 (Refined)"                   → D4 (no Solo/Group word)
///
/// An instance whose adjective we don't recognize is still unmistakably an instance
/// (the base form never prints a parenthetical) but is NOT called D0 — that would
/// invent the one fact the line failed to state.
/// </summary>
public static partial class InstanceTier
{
    /// <summary>Not an instance at all.</summary>
    public const int OpenWorld = -1;
    /// <summary>An instance with an adjective this build doesn't know.</summary>
    public const int UnknownAdjective = -2;
    /// <summary>No zone line seen yet — nothing is known either way.</summary>
    public const int Unknown = -3;

    public static int FromZoneName(string zone)
    {
        if (TieredRx().IsMatch(zone))
            return AdjectiveRx().Match(zone).Groups[1].Value.ToLowerInvariant() switch
            {
                "awakened" => 1,
                "adaptive" => 2,
                "fused" => 3,
                "refined" => 4,
                _ => UnknownAdjective,
            };
        if (SoloGroupRx().IsMatch(zone)) return 0;
        return OpenWorld;
    }

    public static bool IsInstance(int tier) => tier >= 0 || tier == UnknownAdjective;

    /// <summary>Short badge for the Raids card; only real difficulties get one.</summary>
    public static string Badge(int tier) => tier is >= 0 and <= 4 ? $"D{tier}" : "";

    /// <summary>Ledger key for per-tier kill counts — stable strings, never ints,
    /// so the JSON stays readable and old builds' unknown values stay distinct.</summary>
    public static string StoreKey(int tier) => tier switch
    {
        >= 0 and <= 4 => $"d{tier}",
        OpenWorld => "open",
        UnknownAdjective => "instance",
        _ => "unknown",
    };

    [GeneratedRegex(@"\s\d+\s*\([^)]*\)\s*$")]
    private static partial Regex TieredRx();
    [GeneratedRegex(@"\(([A-Za-z]+)\)\s*$")]
    private static partial Regex AdjectiveRx();
    [GeneratedRegex(@"\s-\s*(Solo|Group)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SoloGroupRx();
}
