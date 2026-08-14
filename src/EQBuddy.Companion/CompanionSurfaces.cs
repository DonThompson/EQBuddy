namespace EQBuddy.Companion;

/// <summary>
/// The surface registry: every screen a phone can be shown, by wire name. ONE list —
/// the desktop's offer checkboxes, the per-device ⚙ picker, the per-section change
/// detection and the subscription filter all read it, so a surface added here appears
/// everywhere without a second edit.
///
/// Order is the page's default display order: the things you glance at while camping
/// first, the reference lists after.
/// </summary>
public static class CompanionSurfaces
{
    public const string Map = "map";
    public const string Spawns = "spawns";
    public const string Mez = "mez";
    public const string Buffs = "buffs";
    public const string Combat = "combat";
    public const string Session = "session";
    public const string Loot = "loot";
    public const string Progress = "progress";
    public const string Epics = "epics";
    public const string Sky = "sky";
    public const string Gear = "gear";

    /// <summary>All surfaces this build knows, in default display order.</summary>
    public static readonly IReadOnlyList<string> All =
        [Map, Spawns, Mez, Buffs, Combat, Session, Loot, Progress, Epics, Sky, Gear];

    /// <summary>Human label for the desktop gate checkboxes (both UIs share it;
    /// the phone page carries its own copy in its SURFACE_META table).</summary>
    public static string Label(string surface) => surface switch
    {
        Map => "Zone map",
        Spawns => "Spawn timers",
        Mez => "Mez chips",
        Buffs => "Buffs",
        Combat => "Damage, healing & pet",
        Session => "Session stats",
        Loot => "Loot & watches",
        Progress => "XP & AA",
        Epics => "Epic checklist",
        Sky => "Sky checklist",
        Gear => "Gear checklist",
        _ => surface,
    };

    /// <summary>One line for the gate's tooltip — what leaves the PC if this stays
    /// ticked. Untickers deserve to know exactly what they're refusing.</summary>
    public static string Describe(string surface) => surface switch
    {
        Map => "The zone's map picture, your last /loc marker, and your archived spawn points.",
        Spawns => "Named spawn countdowns for the zone you're in.",
        Mez => "Who's mezzed and how long is left.",
        Buffs => "Your buff set's state, and what you've lost this session.",
        Combat => "Ability breakdowns for damage, healing and your pet, last fight and session.",
        Session => "Kills, xp/hr, session length, dps.",
        Loot => "Session loot, what you've made, and your watch counters.",
        Progress => "XP and AA rates, and what unlocked at your level.",
        Epics => "Your Epic quest checklist — tappable from the phone.",
        Sky => "Your Sky quest checklist — tappable from the phone.",
        Gear => "Your gear checklist, by slot and by farm zone.",
        _ => "",
    };

    /// <summary>Surfaces whose rows a phone may TICK. Everything else is read-only —
    /// the phone gets no write the desktop doesn't already offer, and no write at all
    /// beyond these two checklists.</summary>
    public static bool AcceptsTicks(string surface) =>
        surface is Epics or Sky or Gear;
}
