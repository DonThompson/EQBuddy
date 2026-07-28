using System.Text.RegularExpressions;

namespace EQBuddy.Core;

/// <summary>What a spell does, as far as we can tell from the log.</summary>
public enum SpellCategory
{
    Unknown = 0,
    Charm,
    Mesmerize,
    Root,
    Lull,
    Stun,
    Heal,
    DirectDamage,
    DamageOverTime,
}

/// <summary>
/// Maps spell names to what they do. Two sources feed it:
///
/// 1. A small seed table of crowd-control spells (below). CC is the only category we
///    cannot learn from the log, because charms, mezzes, roots, lulls and stuns produce
///    no numbers — there is nothing to observe. The seed covers the cold start.
/// 2. Observation. Damage and heal spells label themselves: a spell that shows up in a
///    "has taken N damage from your X" line IS a damage-over-time spell, so a hardcoded
///    list for those categories would be pure maintenance debt. <see cref="Learn"/> is
///    called as those lines are parsed. Charm also self-labels via the cast → blink →
///    "Attacking … Master." sequence, which lets us extend the seed at runtime.
///
/// A miss returns <see cref="SpellCategory.Unknown"/> and every caller degrades to its
/// previous behavior, so an unrecognised spell is never worse than not having this class.
///
/// Instance state (not static) so sessions and tests never leak learned spells into
/// each other.
/// </summary>
public sealed partial class SpellCatalog
{
    // Spell ranks appear as roman numerals in EQ Legends ("Stinging Swarm V",
    // "Light Healing V", "Heroic Leap I"), so rank variants must collapse onto the base
    // name before lookup. Matching exact names without this silently misses every
    // ranked spell a character learns past the first one.
    [GeneratedRegex(@"\s+[IVX]{1,6}$")]
    private static partial Regex RankSuffixRx();

    /// <summary>
    /// Crowd-control seed list, adapted from Spyxy's DPS Meter
    /// (https://github.com/khadesh/SpyxysDPSMeter, MIT — see NOTICE) which classifies
    /// spells from equivalent tables. Names are stored without rank suffixes.
    ///
    /// Only "Befriend Animal" is confirmed against a real EQ Legends log so far; the rest
    /// are long-standing EverQuest spell lines carried over. Wrong entries are worse than
    /// missing ones, so keep this conservative and let observation fill the gaps.
    /// </summary>
    private static readonly Dictionary<string, SpellCategory> Seed =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // -- Charm --
            ["Charm"] = SpellCategory.Charm,
            ["Beguile"] = SpellCategory.Charm,
            ["Cajoling Whispers"] = SpellCategory.Charm,
            ["Allure"] = SpellCategory.Charm,
            ["Dominate"] = SpellCategory.Charm,
            ["Boltran's Agacerie"] = SpellCategory.Charm,
            ["Befriend Animal"] = SpellCategory.Charm,   // verified: eqlog_Douglas_qeynos
            ["Charm Animals"] = SpellCategory.Charm,
            ["Beguile Animals"] = SpellCategory.Charm,
            ["Allure of the Wild"] = SpellCategory.Charm,
            ["Call of Karana"] = SpellCategory.Charm,
            ["Dominate Undead"] = SpellCategory.Charm,
            ["Cajole Undead"] = SpellCategory.Charm,
            ["Enslave Death"] = SpellCategory.Charm,
            ["Thrall of Bones"] = SpellCategory.Charm,

            // -- Mesmerize --
            ["Mesmerize"] = SpellCategory.Mesmerize,
            ["Mesmerization"] = SpellCategory.Mesmerize,
            ["Enthrall"] = SpellCategory.Mesmerize,
            ["Entrance"] = SpellCategory.Mesmerize,
            ["Dazzle"] = SpellCategory.Mesmerize,
            ["Rapture"] = SpellCategory.Mesmerize,
            ["Kelin's Lucid Lullaby"] = SpellCategory.Mesmerize,

            // -- Root --
            ["Root"] = SpellCategory.Root,
            ["Ensnaring Roots"] = SpellCategory.Root,
            ["Engulfing Roots"] = SpellCategory.Root,
            ["Engorging Roots"] = SpellCategory.Root,
            ["Grasping Roots"] = SpellCategory.Root,
            ["Paralyzing Earth"] = SpellCategory.Root,
            ["Immobilize"] = SpellCategory.Root,

            // -- Lull --
            ["Lull"] = SpellCategory.Lull,
            ["Soothe"] = SpellCategory.Lull,
            ["Calm"] = SpellCategory.Lull,
            ["Pacify"] = SpellCategory.Lull,
            ["Harmony"] = SpellCategory.Lull,
            ["Wake of Tranquility"] = SpellCategory.Lull,
            ["Mollify"] = SpellCategory.Lull,
            ["Alliance"] = SpellCategory.Lull,

            // -- Stun --
            ["Stun"] = SpellCategory.Stun,
            ["Divine Stun"] = SpellCategory.Stun,
            ["Color Flux"] = SpellCategory.Stun,
            ["Color Shift"] = SpellCategory.Stun,
            ["Color Skew"] = SpellCategory.Stun,
            ["Scintillating Colors"] = SpellCategory.Stun,
        };

    private readonly Dictionary<string, SpellCategory> _learned =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Strips a trailing roman-numeral rank so "Stinging Swarm V" and
    /// "Stinging Swarm" are the same spell.</summary>
    public static string BaseName(string spell)
    {
        spell = spell.Trim();
        var stripped = RankSuffixRx().Replace(spell, "");
        // Never strip away the whole name (a spell literally called "V" isn't a rank).
        return stripped.Length > 0 ? stripped : spell;
    }

    public SpellCategory Classify(string spell)
    {
        var name = BaseName(spell);
        if (Seed.TryGetValue(name, out var seeded)) return seeded;
        return _learned.TryGetValue(name, out var learned) ? learned : SpellCategory.Unknown;
    }

    /// <summary>
    /// Record what a spell was observed doing. The seed table always wins — observation
    /// can add spells but never reclassify a known one, so a stray damage line can't turn
    /// a charm into a nuke. Returns true when this taught us something new.
    /// </summary>
    public bool Learn(string spell, SpellCategory category)
    {
        if (category == SpellCategory.Unknown) return false;
        var name = BaseName(spell);
        if (name.Length == 0 || Seed.ContainsKey(name)) return false;
        if (_learned.TryGetValue(name, out var existing) && existing == category) return false;
        _learned[name] = category;
        return true;
    }

    public static bool IsCrowdControl(SpellCategory category) =>
        category is SpellCategory.Charm or SpellCategory.Mesmerize or SpellCategory.Root
            or SpellCategory.Lull or SpellCategory.Stun;

    public bool IsCrowdControl(string spell) => IsCrowdControl(Classify(spell));

    /// <summary>Human-readable category name for UI and watch-rule labels.</summary>
    public static string Describe(SpellCategory category) => category switch
    {
        SpellCategory.Charm => "Charm",
        SpellCategory.Mesmerize => "Mez",
        SpellCategory.Root => "Root",
        SpellCategory.Lull => "Lull",
        SpellCategory.Stun => "Stun",
        SpellCategory.Heal => "Heal",
        SpellCategory.DirectDamage => "Direct damage",
        SpellCategory.DamageOverTime => "Damage over time",
        _ => "Unknown",
    };
}
