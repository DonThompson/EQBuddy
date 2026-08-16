using System.Text.RegularExpressions;

namespace EQBuddy.Core;

public sealed record MoteTierCount(string Item, int Count);

/// <summary><see cref="Potency"/> is the upgrade experience the session's motes are
/// worth, not how many dropped — the thing #154 (EzraSmith) asked for: "a hundred
/// Infinitesimal motes and a hundred Infinite motes are not the same hour."</summary>
public sealed record MotesSummary(
    int Total, double PerHour, IReadOnlyList<MoteTierCount> Tiers,
    int Potency = 0, double PotencyPerHour = 0)
{
    public static readonly MotesSummary Empty = new(0, 0, []);
}

/// <summary>
/// The "Mote of X Potential" upgrade-currency family, pulled out of the loot stream for
/// its own card (discussions #24, #44, #49 — flipwon: "more important than Travels &amp;
/// Deaths"). Only the Potential ladder counts here: named motes like Crystallized Fire
/// Mote are ordinary items and stay in Loot. Both log shapes land in the loot stream
/// already — "--You have looted a Mote...--" and the currency-stored variant — so this
/// is a pure derivation, no new parsing.
/// </summary>
public static partial class Motes
{
    [GeneratedRegex(@"^Mote of (?:(?<tier>\w+) )?Potential$", RegexOptions.IgnoreCase)]
    private static partial Regex MotePattern();

    /// <summary>
    /// The ladder and its experience values, verbatim from the wiki's Mote Guide table
    /// (verified 2026-08-16, columns "Mote Name" and "Exp per Mote").
    ///
    /// Two corrections landed with that verification. **Major outranks Greater** — we
    /// had them the other way round since 2026-08-07 — and the bare "Mote of Potential"
    /// is the FOURTH rung, worth 4, not the bottom of the ladder as we treated it. Both
    /// were invisible on the card because it only ever counted, never weighed.
    ///
    /// The 1, 1, 2, **4**, 5, 6, 7, 8, 9, 10 progression is the wiki's own; the jump
    /// from 2 to 4 skips 3 and the page does not explain it. Copied rather than
    /// derived, deliberately — a formula that looks tidier than the source is how you
    /// end up uniquely wrong (CLAUDE.md).
    ///
    /// NO TIER NUMBER is stored, and that is also from the verification: the wiki
    /// contradicts itself on the numbering. Mote Guide's own column is 0-based and is
    /// headed "Item Tier Limit" (the highest item tier a mote may be spent on, not the
    /// mote's rank); Item Upgrade System numbers the same motes 1-based; and
    /// Constructed Potential uses both inside one paragraph. Printing any single index
    /// would contradict some page of the source we mirror, so the card shows names and
    /// values and leaves numbering alone.
    /// </summary>
    private static readonly (string Tier, int Exp)[] Ladder =
    [
        ("Infinitesimal", 1), ("Minor", 1), ("Lesser", 2), ("", 4), ("Major", 5),
        ("Greater", 6), ("Superior", 7), ("Grand", 8), ("Ascendant", 9), ("Infinite", 10),
    ];

    /// <summary>The raid-only mote that carries no experience at all: it raises the
    /// item or spell one whole tier instead ("Only three of these can be earned each
    /// week" — Mote Guide). It counts on the card, and contributes nothing to potency,
    /// because its worth is a tier rather than a number of points. Note the name has no
    /// "Mote of" prefix, which is why the pattern above cannot see it.</summary>
    public const string VoidTouched = "Void-Touched Potential";

    public static bool IsMote(string itemName)
    {
        var name = itemName.Trim();
        return MotePattern().IsMatch(name)
            || name.Equals(VoidTouched, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The upgrade experience one of this mote is worth, or 0 for the
    /// Void-Touched mote and anything the ladder has not been taught.</summary>
    public static int PotencyOf(string itemName)
    {
        var m = MotePattern().Match(itemName.Trim());
        if (!m.Success) return 0;
        var tier = m.Groups["tier"].Value;
        var rung = Array.FindIndex(Ladder, l => l.Tier.Equals(tier, StringComparison.OrdinalIgnoreCase));
        return rung < 0 ? 0 : Ladder[rung].Exp;
    }

    public static MotesSummary Summarize(IEnumerable<LootDetail> loot, TimeSpan elapsed)
    {
        var rows = new List<(int Rank, string Item, int Count, int Exp)>();
        foreach (var l in loot)
        {
            var name = l.Item.Trim();
            if (name.Equals(VoidTouched, StringComparison.OrdinalIgnoreCase))
            {
                // Above the ladder: it is the strongest thing you can loot, and worth
                // no experience at all.
                rows.Add((Ladder.Length, l.Item, l.Count, 0));
                continue;
            }
            var m = MotePattern().Match(name);
            if (!m.Success) continue;
            var tier = m.Groups["tier"].Value;
            var rank = Array.FindIndex(Ladder,
                t => t.Tier.Equals(tier, StringComparison.OrdinalIgnoreCase));
            // A tier the wiki has not taught us sorts after the known ladder rather
            // than vanishing, and weighs nothing rather than guessing a value.
            var exp = rank < 0 ? 0 : Ladder[rank].Exp;
            if (rank < 0) rank = Ladder.Length;
            rows.Add((rank, l.Item, l.Count, exp));
        }
        if (rows.Count == 0) return MotesSummary.Empty;

        var tiers = rows
            .OrderBy(r => r.Rank).ThenBy(r => r.Item, StringComparer.OrdinalIgnoreCase)
            .Select(r => new MoteTierCount(r.Item, r.Count)).ToList();
        var total = rows.Sum(r => r.Count);
        var potency = rows.Sum(r => r.Count * r.Exp);
        var hours = Math.Max(elapsed.TotalHours, 1.0 / 60);
        return new MotesSummary(total, total / hours, tiers, potency, potency / hours);
    }
}
