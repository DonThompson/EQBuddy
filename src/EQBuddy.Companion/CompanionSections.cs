namespace EQBuddy.Companion;

// The wire shape of every surface. One file so the protocol reads top to bottom;
// each record is pre-chewed for a phone (numbers already formatted where the phone
// can't do better, seconds rather than absolute times so a clock-skewed device still
// counts down correctly, semantic flags rather than colors so the theme decides).

// ---------------- spawns / session (Phase 1) ----------------

public sealed record CompanionSpawnSection(IReadOnlyList<CompanionSpawnTimer> Timers);

/// <summary>One spawn countdown, pre-chewed for a phone: the page ticks
/// <see cref="RemainingSeconds"/> down locally between pushes, so it is the remaining
/// time AT <see cref="CompanionSnapshot.SentAtUtc"/>. Null remaining = the kill was
/// seen but no respawn duration is known ("killed, duration unknown").</summary>
public sealed record CompanionSpawnTimer(
    string Name,
    string Zone,
    double? RemainingSeconds,
    bool Due,
    bool Imminent,
    double? DurationSeconds);

/// <summary>Session basics for the footer strip.</summary>
public sealed record CompanionSessionSection(
    int Kills,
    double XpPerHour,
    double SessionSeconds,
    double SessionDps);

// ---------------- map ----------------

/// <summary>
/// The zone map. Split deliberately: <see cref="Geometry"/> is the STATIC picture
/// (thousands of segments from the map pack) and is sent once per zone per device —
/// the server withholds it while a device already holds that <see cref="GeometryStamp"/> —
/// while the marker and circles ride every push. The page keeps the last geometry it
/// received and re-attaches it whenever the stamp still matches.
/// </summary>
public sealed record CompanionMapSection(
    string Zone,
    /// <summary>The catalog zone the spawn archive lives under — what a curation edit
    /// must name. Not the same string as <see cref="Zone"/> in tiered zones, and
    /// aiming an edit at the wrong one would quietly curate a different archive.</summary>
    string TimerZone,
    string GeometryStamp,
    CompanionMapGeometry? Geometry,
    string? Missing,
    CompanionMapMarker? You,
    IReadOnlyList<CompanionMapCircle> Circles,
    IReadOnlyList<CompanionMapCrumb> Trail,
    IReadOnlyList<CompanionMapNamed> Named);

/// <summary>Map-space geometry. Coordinates are rounded to whole map units (roughly
/// game feet) — the pack's sub-unit precision is invisible on a phone and doubles the
/// payload.</summary>
public sealed record CompanionMapGeometry(
    string Stamp,
    int MinX, int MinY, int MaxX, int MaxY,
    IReadOnlyList<CompanionMapStroke> Strokes,
    IReadOnlyList<CompanionMapPoi> Pois,
    bool Truncated);

/// <summary>All segments of one color, flattened to x1,y1,x2,y2,… — the page turns
/// each stroke into a single SVG path, exactly as the desktop makes one Path per color.</summary>
public sealed record CompanionMapStroke(string Color, IReadOnlyList<int> Segments);

public sealed record CompanionMapPoi(int X, int Y, string Color, string Label);

/// <summary>Where the player's last /loc put them, in map space.</summary>
public sealed record CompanionMapMarker(double X, double Y, double AgeSeconds);

/// <summary>One breadcrumb of the /loc trail, in map space, oldest first — the same
/// list the desktop map draws its comet tail from (thinned to 25 map units apart by
/// SessionStats, so the tail spans real ground rather than one corridor).
///
/// The AGE rides the wire, not an alpha: the page fades locally against
/// <see cref="EQBuddy.UI.Shared.TrailFade"/>'s curve every second, so the tail keeps
/// burning down smoothly between pushes exactly as it does on the desktop — and a
/// crumb merely fading never counts as a change worth waking a device for.</summary>
public sealed record CompanionMapCrumb(double X, double Y, double AgeSeconds);

/// <summary>One running spawn timer in the shown zone — the row the desktop map's named
/// panel draws, and the camp pin it plants, from a single answer. They are the same
/// question asked twice on the desktop too (UpdateNamedPanel builds both from one
/// resolved list), and splitting them here would let a pin and its row disagree.
///
/// <see cref="X"/>/<see cref="Y"/> are the camp in map space, null when no camp is known
/// yet — those named still get a ROW (with the desktop's "/loc during the fight" nudge),
/// they just get no pin. <see cref="FromWiki"/> is the desktop's "~": approximate,
/// and said out loud rather than passed off as your own observation.
///
/// <see cref="DueSeconds"/> is the countdown at send time; the page ticks it locally like
/// every other clock, so a running timer doesn't wake a device once a second.</summary>
public sealed record CompanionMapNamed(
    string Name,
    double? DueSeconds,
    bool Due,
    double? DurationSeconds,
    double? X, double? Y,
    bool FromWiki);

/// <summary>An archived spawn point. <see cref="Named"/> points wear the accent and
/// carry a <see cref="Label"/>; ordinary ones sit dim. <see cref="DueSeconds"/> is the
/// countdown at send time (negative = already due), <see cref="Projected"/> marks the
/// ordinary-point estimate the desktop prints with a "~".
///
/// <see cref="LocY"/>/<see cref="LocX"/> are the point's own game coordinates, carried
/// so a device curating this circle echoes them back verbatim. The page must never
/// derive them from <see cref="X"/>/<see cref="Y"/>: getting that inversion subtly
/// wrong would aim a removal at the wrong dot, and nothing on screen would say so.</summary>
public sealed record CompanionMapCircle(
    double X, double Y,
    bool Named,
    string? Label,
    bool Confirmed,
    double? DueSeconds,
    bool Imminent,
    bool Projected,
    int Kills,
    string Mobs,
    double LocY, double LocX);

// ---------------- mez ----------------

public sealed record CompanionMezSection(IReadOnlyList<CompanionMezChip> Chips);

/// <summary>One mez chip, numbered and warned exactly as MezChipsWindow does.
/// <see cref="Fraction"/> is the ELAPSED share (the page draws 1 - fraction, a
/// draining gauge); null when the duration is unknown.</summary>
public sealed record CompanionMezChip(
    string Name,
    double? RemainingSeconds,
    bool Warning,
    double? Fraction,
    string Detail);

// ---------------- buffs ----------------

public sealed record CompanionBuffsSection(
    IReadOnlyList<CompanionBuffGroup> Groups,
    IReadOnlyList<CompanionBuffLoss> Lost);

public sealed record CompanionBuffGroup(string Class, IReadOnlyList<CompanionBuffRow> Rows);

/// <summary><see cref="Status"/> is the BuffSetEvaluator state lowercased
/// ("active"/"expiring"/"missing"/"notSeen") — a semantic the page colors itself, so
/// the desktop's theme decides what "expiring" looks like on the phone too.</summary>
public sealed record CompanionBuffRow(
    string Spell,
    string Status,
    double? RemainingSeconds,
    bool Estimated);

public sealed record CompanionBuffLoss(string Spell, string Cause, double AgoSeconds);

// ---------------- combat ----------------

/// <summary>The three breakout boards in one section; each carries BOTH scopes so the
/// page's fight/session toggle is instant and needs no round trip.</summary>
public sealed record CompanionCombatSection(IReadOnlyList<CompanionCombatBoard> Boards);

public sealed record CompanionCombatBoard(
    string Key,
    string Label,
    string FightHeader,
    string SessionHeader,
    IReadOnlyList<CompanionAbilityRow> Fight,
    IReadOnlyList<CompanionAbilityRow> Session);

/// <summary>One ability row. <see cref="Value"/> is the desktop's own line (total ·
/// ×hits · avg · rate), <see cref="Fraction"/> the bar width against the top row, and
/// <see cref="Percent"/> the share of the board's total.</summary>
public sealed record CompanionAbilityRow(
    string Name,
    string Value,
    double Fraction,
    double Percent,
    long Total,
    int Hits);

// ---------------- loot ----------------

public sealed record CompanionLootSection(
    int Total,
    int CraftedTotal,
    IReadOnlyList<CompanionCountRow> Items,
    IReadOnlyList<CompanionCountRow> Crafted,
    IReadOnlyList<CompanionWatchRow> Watch);

public sealed record CompanionCountRow(string Name, int Count);

/// <summary>A watch rule's running count — the same three numbers the desktop's watch
/// card shows (total · /hr · /active hr).</summary>
public sealed record CompanionWatchRow(
    string Name,
    int Total,
    double PerHour,
    double PerActiveHour,
    string? LastItem);

// ---------------- checklists (epics / sky / gear) ----------------

/// <summary>One checklist, shaped the same for Epics, Sky and Gear so the page has a
/// single renderer and the tick path has a single action.</summary>
public sealed record CompanionChecklistSection(
    int Done,
    int Total,
    IReadOnlyList<CompanionChecklistGroup> Groups);

public sealed record CompanionChecklistGroup(
    string Heading,
    string? Note,
    IReadOnlyList<CompanionChecklistRow> Rows);

/// <summary><see cref="Id"/> is what a tap sends back to tick the row — the stored
/// item's own id for Epics/Sky, slot|item for Gear (which has no id of its own).</summary>
public sealed record CompanionChecklistRow(
    string Id,
    string Text,
    string? Detail,
    bool Done);

// ---------------- progress ----------------

public sealed record CompanionProgressSection(
    double XpPercent,
    double XpPerHour,
    double XpPerActiveHour,
    double? HoursToLevel,
    int AaGained,
    int AaTotal,
    double AaPerHour,
    int? Level,
    string? UnlocksLabel,
    IReadOnlyList<CompanionUnlockRow> Unlocks);

public sealed record CompanionUnlockRow(string Name, string Value);
