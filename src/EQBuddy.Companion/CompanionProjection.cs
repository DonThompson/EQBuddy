using EQBuddy.Core;

namespace EQBuddy.Companion;

/// <summary>
/// Projects the desktop's already-built state (the per-tick shared StatsSnapshot plus
/// SpawnTimers' state list) into the wire DTO. Pure functions: the host owns when to
/// call, the server owns when (and to whom) to send.
/// </summary>
public static class CompanionProjection
{
    /// <summary>A timer with this much or less left counts as "imminent" — the page
    /// pulses those rows. Matches the "get ready to camp" horizon, not a lore number.</summary>
    public const double ImminentSeconds = 120;

    /// <summary>Build the full offered snapshot. <paramref name="offered"/> is the
    /// desktop gate (Options → EQBuddy Mobile): sections outside it are never even
    /// projected, so gated data doesn't exist in memory to leak, let alone send.</summary>
    public static CompanionSnapshot Build(
        StatsSnapshot? stats,
        IReadOnlyList<SpawnTimerState> timers,
        string character,
        string appVersion,
        DateTime now,
        IReadOnlyList<string>? offered = null)
    {
        offered ??= CompanionSurfaces.All;
        var offerSpawns = offered.Contains(CompanionSurfaces.Spawns, StringComparer.OrdinalIgnoreCase);
        var offerSession = offered.Contains(CompanionSurfaces.Session, StringComparer.OrdinalIgnoreCase);

        return new CompanionSnapshot
        {
            Version = stats?.Version ?? 0,
            SentAtUtc = now.ToUniversalTime(),
            Identity = new CompanionIdentity(character, stats?.CurrentZone ?? "", appVersion),
            Offered = offered,
            Spawns = offerSpawns ? new CompanionSpawnSection(BuildTimers(timers, now)) : null,
            Session = offerSession
                ? new CompanionSessionSection(
                    Kills: stats?.YourKillCount ?? 0,
                    XpPerHour: stats?.XpPerHour ?? 0,
                    SessionSeconds: stats?.Elapsed.TotalSeconds ?? 0,
                    SessionDps: stats?.SessionDps ?? 0)
                : null,
        };
    }

    private static List<CompanionSpawnTimer> BuildTimers(IReadOnlyList<SpawnTimerState> timers, DateTime now)
    {
        var rows = new List<CompanionSpawnTimer>(timers.Count);
        foreach (var t in timers)
        {
            double? remaining = t.DueAt is { } due ? Math.Max(0, (due - now).TotalSeconds) : null;
            var isDue = t.IsDue(now);
            rows.Add(new CompanionSpawnTimer(
                t.Name, t.Zone, remaining,
                Due: isDue,
                Imminent: !isDue && remaining is { } r && r <= ImminentSeconds,
                DurationSeconds: t.DurationSeconds));
        }
        // Soonest first; due rows (remaining 0) naturally float to the top; unknown
        // durations sink to the bottom — the page renders in this order as-is.
        rows.Sort((a, b) => (a.RemainingSeconds ?? double.MaxValue)
            .CompareTo(b.RemainingSeconds ?? double.MaxValue));
        return rows;
    }

    /// <summary>
    /// What "the version moved" means for push decisions: a stable string over the
    /// DATA identity, deliberately excluding the values that drift every single tick
    /// (remaining seconds, session length, xp rate) — the page ticks those locally.
    /// Included: the stats event counter (kills/loot/zone changes bump it), the offer
    /// list (gate edits must push), the roster of timers, and each row's due/imminent
    /// flags (those flips are exactly the transitions the phone must repaint for).
    /// Phase 2 note: when more surfaces land, fingerprint PER SECTION so a mez-only
    /// phone isn't pushed for loot changes.
    /// </summary>
    public static string Fingerprint(CompanionSnapshot snap)
    {
        var sb = new System.Text.StringBuilder(128);
        sb.Append(snap.Version).Append('|')
          .Append(snap.Identity.Character).Append('|')
          .Append(snap.Identity.Zone).Append('|')
          .Append(string.Join(',', snap.Offered));
        foreach (var t in snap.Spawns?.Timers ?? [])
            sb.Append('|').Append(t.Zone).Append('/').Append(t.Name)
              .Append(':').Append(t.Due ? 'D' : t.Imminent ? 'I' : '-')
              .Append(':').Append(t.DurationSeconds ?? -1);
        return sb.ToString();
    }
}
