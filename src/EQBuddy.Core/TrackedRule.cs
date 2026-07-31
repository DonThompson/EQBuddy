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
    /// <summary>Any log line containing the text — the escape hatch from WATCH-001's
    /// structured-events rule. Everything else here matches a parsed event, which by
    /// definition can only cover lines EQBuddy has a pattern for. Text rules exist for
    /// lines nobody can pattern in advance: another player's raid-assist script announcing
    /// a heal rotation, a server's custom emotes, a guild's own chat conventions.
    /// Matches the message, never the "[Tue Jul 28 16:55:07 2026] " timestamp prefix.</summary>
    Text = 6,
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
    /// <summary>
    /// Show this rule as a chip in the mini dashboard. Defaults to on, because chips used to
    /// show every enabled rule: a new rule that silently never appears — with no hint that a
    /// pin exists — reads as the feature being broken. The pin is there to take rules *out*
    /// of a crowded mini bar, not to opt each one in.
    /// </summary>
    public bool Pinned { get; set; } = true;
    public bool AlertBanner { get; set; } = true;
    public bool AlertSound { get; set; }
    /// <summary>
    /// Which sound this rule plays: a built-in name, a full .wav/.mp3 path, or empty to
    /// use the shared <see cref="AppSettings.AlertSound"/>. Giving each rule its own
    /// sound is the point — you learn what happened from the audio without looking.
    /// Empty by default so rules saved before this existed keep the shared sound.
    /// Ignored when <see cref="AlertSound"/> is false.
    /// </summary>
    public string AlertSoundName { get; set; } = "";

    /// <summary>
    /// Hold the alert back this many seconds after the match, instead of firing at once.
    /// 0 (the default) is the original behaviour, so nothing changes for existing rules.
    ///
    /// This turns a rule into a cue timer: match the call in a complete-heal chain and
    /// sound at +2.5 s to say "cast now", or match your own mez cast and sound at +25 s to
    /// say "recast before it breaks". Requested in discussion #22.
    ///
    /// Only the banner and sound wait — the count, rates and rows update on the match, as
    /// they always did. To get both an immediate and a delayed alert, make two rules with
    /// the same match text and different sounds; that also gets you a quiet "heard it" cue
    /// and a loud "do it now" one, which a single rule couldn't.
    /// </summary>
    public double AlertDelaySeconds
    {
        get => _alertDelaySeconds;
        // Clamped rather than validated: a hand-edited settings.json shouldn't be able to
        // park an alert five hours in the future, or schedule one in the past.
        set => _alertDelaySeconds = Math.Clamp(value, 0, MaxAlertDelaySeconds);
    }
    private double _alertDelaySeconds;

    /// <summary>Thirty minutes. Started at two, which was sized for the combat cues the
    /// feature was requested for (a 2.5 s heal-chain call, a 25 s mez) — then testers used
    /// it for respawn timers, where eight minutes is unremarkable and the old cap silently
    /// clamped them down to two. Spawn timers are the natural ceiling on how far ahead
    /// anyone wants to be reminded.</summary>
    public const double MaxAlertDelaySeconds = 30 * 60;

    /// <summary>Cues at or under this are about the fight in front of you — "cast now",
    /// "recast before it breaks" — and are abandoned when you die, because being told to
    /// cast something while dead is noise. Longer ones are about the world (a respawn), and
    /// dying has no bearing on when a mob pops, so they survive.</summary>
    public const double CombatCueSeconds = 60;

    /// <summary>Whether this rule's cue belongs to the current fight rather than the world.
    /// See <see cref="CombatCueSeconds"/>.</summary>
    [JsonIgnore]
    public bool IsCombatCue => AlertDelaySeconds > 0 && AlertDelaySeconds <= CombatCueSeconds;

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
