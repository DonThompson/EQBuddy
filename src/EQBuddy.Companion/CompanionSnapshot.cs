using System.Text.Json;
using System.Text.Json.Serialization;

namespace EQBuddy.Companion;

/// <summary>The surface registry: every section a snapshot can carry, by wire name.
/// Phase 1 ships two; mez/buffs/combat/loot/checklists join here as they land.</summary>
public static class CompanionSurfaces
{
    public const string Spawns = "spawns";
    public const string Session = "session";

    /// <summary>All surfaces this build knows, in default display order.</summary>
    public static readonly IReadOnlyList<string> All = [Spawns, Session];

    /// <summary>Human label for the desktop gate checkboxes (both UIs share it;
    /// the phone page carries its own copy in its SURFACE_META table).</summary>
    public static string Label(string surface) => surface switch
    {
        Spawns => "Spawn timers",
        Session => "Session stats",
        _ => surface,
    };
}

/// <summary>
/// The versioned wire envelope the phone receives. Lives in the Companion project (not
/// UI.Shared) because it IS the companion's wire contract: the server serializes it, the
/// embedded page consumes it, and its shape must move with the protocol — not with
/// desktop presentation helpers.
///
/// SECTIONED by design: each surface is its own nullable section property (null =
/// not subscribed, or gated off on the desktop — nulls vanish from the JSON), so
/// per-client projection is a cheap record copy that nulls what that phone didn't
/// ask for, and a mez-only pocket phone never pays for loot payloads. New surfaces
/// are new section properties plus a <see cref="CompanionSurfaces"/> name — the
/// envelope (kind/protocol/version/identity/offered) never changes shape.
///
/// Extensibility: every message carries a <see cref="Kind"/> ("snapshot" now,
/// "heartbeat" for keepalives; deltas bolt on as new kinds) and a
/// <see cref="Protocol"/> number the page checks before trusting the shape.
/// </summary>
public sealed record CompanionSnapshot
{
    public const int CurrentProtocol = 1;

    public string Kind { get; init; } = "snapshot";
    public int Protocol { get; init; } = CurrentProtocol;
    /// <summary>Moves when the underlying data moves (see CompanionProjection.Fingerprint);
    /// equal versions mean the phone may skip re-rendering.</summary>
    public long Version { get; init; }
    public DateTime SentAtUtc { get; init; }
    public CompanionIdentity Identity { get; init; } = new("", "", "");

    /// <summary>The surfaces the DESKTOP is willing to send (the owner's gate in
    /// Options → Second screen). The page builds its ⚙ Screens picker from this.</summary>
    public IReadOnlyList<string> Offered { get; init; } = [];

    /// <summary>Surfaces this client subscribed to that the desktop is NOT offering —
    /// the explicit "not offered" marker, so a gated surface reads as a told "no"
    /// on the phone rather than silence. Null (omitted) when there are none.</summary>
    public IReadOnlyList<string>? NotOffered { get; init; }

    // ---- sections, one nullable property per surface ----
    public CompanionSpawnSection? Spawns { get; init; }
    public CompanionSessionSection? Session { get; init; }

    /// <summary>This snapshot reduced to what one client subscribed to:
    /// null subscriptions = everything offered. Unknown or gated names land in
    /// <see cref="NotOffered"/>. A cheap record copy — safe per client per push.</summary>
    public CompanionSnapshot ForSubscription(IReadOnlyList<string>? surfaces)
    {
        if (surfaces is null) return this;
        var wanted = new HashSet<string>(surfaces, StringComparer.OrdinalIgnoreCase);
        var missing = surfaces
            .Where(s => !Offered.Contains(s, StringComparer.OrdinalIgnoreCase))
            .ToList();
        return this with
        {
            Spawns = wanted.Contains(CompanionSurfaces.Spawns) ? Spawns : null,
            Session = wanted.Contains(CompanionSurfaces.Session) ? Session : null,
            NotOffered = missing.Count > 0 ? missing : null,
        };
    }

    /// <summary>One serializer config for everything that crosses the wire — camelCase,
    /// so the page reads <c>msg.spawns.timers</c> without C#-casing surprises.</summary>
    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOpts);
}

/// <summary>Who and where the data comes from — the page's header line.</summary>
public sealed record CompanionIdentity(string Character, string Zone, string AppVersion);

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
