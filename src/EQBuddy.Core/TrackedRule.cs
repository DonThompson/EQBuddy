using System.Text.Json.Serialization;

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
    /// <summary>Your spells wearing off ("Your X spell has worn off of Y.").
    /// <see cref="TrackedRule.SpellFilter"/> chooses between one named spell and a whole
    /// class of spells.</summary>
    SpellFade = 5,
}

/// <summary>
/// Which spells a <see cref="WatchKind.SpellFade"/> rule covers: one named spell, or a
/// class of them. Class filters need no match text and keep working as a character levels
/// into new spells and ranks, so one rule replaces one-rule-per-spell.
/// </summary>
public enum SpellFilter
{
    /// <summary>Match the rule's text against the spell name (the original behavior).</summary>
    ByName = 0,
    /// <summary>Every spell of yours that wears off, including buffs.</summary>
    AnySpell = 1,
    /// <summary>Charm, mez, root, lull or stun.</summary>
    AnyCrowdControl = 2,
    Charm = 3,
    Mesmerize = 4,
    Root = 5,
    Lull = 6,
    Stun = 7,
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
    /// <summary>For SpellFade rules: one named spell, or a class of spells. Ignored by
    /// every other kind.</summary>
    public SpellFilter SpellFilter { get; set; } = SpellFilter.ByName;
    public bool Enabled { get; set; } = true;
    /// <summary>Pinned rules get a chip in the mini dashboard.</summary>
    public bool Pinned { get; set; }
    public bool AlertBanner { get; set; } = true;
    public bool AlertSound { get; set; }

    /// <summary>Rules whose name is a label rather than a pattern, so an empty pattern
    /// means "match everything of this kind" instead of falling back to the name. A
    /// SpellFade rule filtered by class needs no match text either.</summary>
    [JsonIgnore]
    public bool IsMatchAllKind =>
        Kind is WatchKind.Death or WatchKind.Milestone
        || (Kind is WatchKind.SpellFade && SpellFilter != SpellFilter.ByName);

    /// <summary>The spell class this rule covers, or null when it matches by name or by
    /// a group with no single category (Any spell / Any crowd control).</summary>
    [JsonIgnore]
    public SpellCategory? FilterCategory => SpellFilter switch
    {
        SpellFilter.Charm => SpellCategory.Charm,
        SpellFilter.Mesmerize => SpellCategory.Mesmerize,
        SpellFilter.Root => SpellCategory.Root,
        SpellFilter.Lull => SpellCategory.Lull,
        SpellFilter.Stun => SpellCategory.Stun,
        _ => null,
    };

    /// <summary>The text actually matched against: the pattern, falling back to the name
    /// when only the name box was filled in (a common way to enter rules). Match-all kinds
    /// never fall back — their name is a label and an empty pattern means match-all.</summary>
    [JsonIgnore]
    public string EffectivePattern =>
        Pattern.Length > 0 ? Pattern
        : IsMatchAllKind ? ""
        : Name;

    /// <summary>Match-all kinds match everything when the pattern is empty.</summary>
    public bool Matches(string text) =>
        EffectivePattern.Length > 0
            ? text.Contains(EffectivePattern, StringComparison.OrdinalIgnoreCase)
            : IsMatchAllKind;
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
