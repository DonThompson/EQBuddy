using System.IO;
using System.Text.Json;

namespace EQBuddy.Core;

/// <summary>
/// The per-zone spawn-point archive (David's map brief, 2026-08-13): every kill that
/// lands near a fresh /loc becomes an observation, observations cluster into spawn
/// POINTS, and each zone's file only ever accretes and refines — one dataset per
/// zone you've played, improving forever. The map draws these as circles (named in
/// the theme accent, ordinary dim, pulsing when a respawn is imminent); the chip
/// stacks deliberately stay named-only.
///
/// Honesty rules: a point exists only where a /loc actually anchored a kill (the
/// same 3-minute freshness window camp pins use); ordinary-mob respawn projections
/// come from the zone's own clock and say "projected"; replay is idempotent via a
/// per-zone high-water mark, so restarts never double-count.
/// </summary>
public sealed class SpawnPointLedger
{
    /// <summary>Kills within this many loc units of a point's centroid refine that
    /// point; farther starts a new one. Loc units are roughly game feet — 30 covers
    /// a camp's wander without merging distinct camps.</summary>
    public const double ClusterRadius = 30;

    public sealed class MobSeen
    {
        public int Kills { get; set; }
        public DateTime LastKill { get; set; }
    }

    public sealed class SpawnPoint
    {
        public double LocY { get; set; }
        public double LocX { get; set; }
        /// <summary>Every mob name killed at this point, with counts — the hover's
        /// "which mobs share this spawn" answer.</summary>
        public Dictionary<string, MobSeen> Mobs { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public int TotalKills() => Mobs.Values.Sum(m => m.Kills);
        public (string Name, MobSeen Seen) LastKilled() =>
            Mobs.OrderByDescending(kv => kv.Value.LastKill)
                .Select(kv => (kv.Key, kv.Value)).First();
    }

    public sealed class ZoneArchive
    {
        public string Zone { get; set; } = "";
        public DateTime HighWater { get; set; }
        public List<SpawnPoint> Points { get; set; } = [];
    }

    private readonly string? _dir;
    private readonly SpawnCatalog _catalog;
    private readonly Dictionary<string, ZoneArchive> _zones = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private string _currentZone = "";
    private LocationEvent? _lastLoc;

    /// <summary>Same freshness window as camp pins: you were standing at the fight.</summary>
    private static readonly TimeSpan LocWindow = TimeSpan.FromMinutes(3);

    public SpawnPointLedger(string? dir, SpawnCatalog catalog)
    {
        _dir = dir;
        _catalog = catalog;
    }

    public void Apply(GameEvent evt)
    {
        switch (evt)
        {
            case ZoneEvent z:
                lock (_lock)
                {
                    _currentZone = _catalog.FindZone(z.Zone)?.Zone
                        ?? SpawnCatalog.StripTierVariant(z.Zone);
                    _lastLoc = null;
                }
                break;
            case LocationEvent loc:
                lock (_lock) _lastLoc = loc;
                break;
            case KillEvent k:
                lock (_lock) Observe(k);
                break;
        }
    }

    private void Observe(KillEvent k)
    {
        if (_currentZone.Length == 0) return;
        if (_lastLoc is not { } loc || k.Time - loc.Time > LocWindow || k.Time < loc.Time) return;

        var zone = Load(_currentZone);
        if (k.Time <= zone.HighWater) return;   // replayed history, already archived
        zone.HighWater = k.Time;

        var point = zone.Points.FirstOrDefault(p =>
            Math.Sqrt(Math.Pow(p.LocY - loc.LocY, 2) + Math.Pow(p.LocX - loc.LocX, 2)) <= ClusterRadius);
        if (point is null)
            zone.Points.Add(point = new SpawnPoint { LocY = loc.LocY, LocX = loc.LocX });
        else
        {
            // Centroid refines toward the new observation, weighted by history —
            // the archive's "only gets better over time" contract.
            var n = point.TotalKills();
            point.LocY = (point.LocY * n + loc.LocY) / (n + 1);
            point.LocX = (point.LocX * n + loc.LocX) / (n + 1);
        }

        var name = FoldPetName(LogParser.Normalize(k.Target));
        var seen = point.Mobs.TryGetValue(name, out var m) ? m : point.Mobs[name] = new MobSeen();
        seen.Kills++;
        seen.LastKill = k.Time;
        Save(zone);
    }

    /// <summary>"Royal guard pet" folds into "Royal guard" (David, 2026-08-13: pets
    /// roll into their owner names — an NPC's summon dying at the camp is the camp's
    /// business, not a separate creature worth archiving).</summary>
    internal static string FoldPetName(string name) =>
        name.EndsWith(" pet", StringComparison.OrdinalIgnoreCase)
            ? name[..^4].TrimEnd()
            : name;

    /// <summary>Merge any pet-named entries an archive accumulated before the fold
    /// existed (or that arrive via an older sharer's string) into their owners.</summary>
    private static void FoldPetEntries(ZoneArchive archive)
    {
        foreach (var point in archive.Points)
        {
            foreach (var petName in point.Mobs.Keys
                         .Where(n => !string.Equals(FoldPetName(n), n, StringComparison.OrdinalIgnoreCase))
                         .ToList())
            {
                var seen = point.Mobs[petName];
                point.Mobs.Remove(petName);
                var owner = FoldPetName(petName);
                var into = point.Mobs.TryGetValue(owner, out var o) ? o
                    : point.Mobs[owner] = new MobSeen();
                into.Kills += seen.Kills;
                if (seen.LastKill > into.LastKill) into.LastKill = seen.LastKill;
            }
        }
    }

    /// <summary>The archive for a zone (resolved name), loaded lazily. A snapshot
    /// deep enough for cross-thread walking — points are cloned.</summary>
    public ZoneArchive Snapshot(string zone)
    {
        lock (_lock)
        {
            var z = Load(zone);
            return new ZoneArchive
            {
                Zone = z.Zone,
                HighWater = z.HighWater,
                Points = z.Points.Select(p => new SpawnPoint
                {
                    LocY = p.LocY, LocX = p.LocX,
                    Mobs = p.Mobs.ToDictionary(kv => kv.Key,
                        kv => new MobSeen { Kills = kv.Value.Kills, LastKill = kv.Value.LastKill },
                        StringComparer.OrdinalIgnoreCase),
                }).ToList(),
            };
        }
    }

    /// <summary>The catalog named this point belongs to, when any mob seen here
    /// matches one — the map's "wear the accent" test AND its label text (David,
    /// 2026-08-13: named points show their name even without a running timer).</summary>
    public string? NamedPointName(string zone, SpawnPoint p)
    {
        var z = _catalog.FindZone(zone);
        if (z is null) return null;
        foreach (var name in p.Mobs.Keys)
            foreach (var e in z.Named)
                if (SpawnCatalog.NameMatches(e.Name, name)
                    || e.Aliases.Any(a => SpawnCatalog.NameMatches(a, name)))
                    return e.Name;
        return null;
    }

    /// <summary>True when any mob at the point is a catalog named for the zone.</summary>
    public bool IsNamedPoint(string zone, SpawnPoint p) => NamedPointName(zone, p) is not null;

    /// <summary>Projected next respawn at an ORDINARY point: last kill + the zone's
    /// own clock. Null when the zone documents no clock — "unknown" beats a guess.
    /// Named points don't use this; their real timers already exist.</summary>
    public DateTime? ProjectedRespawn(string zone, SpawnPoint p)
    {
        var z = _catalog.FindZone(zone);
        if (z?.NamedDefaultSeconds is not { } clock) return null;
        return p.LastKilled().Seen.LastKill.AddSeconds(clock);
    }

    /// <summary>Apply a previewed ZoneShare import into the LIVE archive (the
    /// preview was computed against a snapshot clone) and persist. Merge semantics
    /// live in <see cref="ZoneShare.Apply"/>; this is just the lock and the disk.</summary>
    public void ApplyImport(ZoneShare.Preview preview, SpawnOverrides overrides, bool includeFlagged)
    {
        lock (_lock)
        {
            var zone = Load(preview.Payload.Zone);
            ZoneShare.Apply(preview, zone, overrides, includeFlagged);
            Save(zone);
        }
    }

    private ZoneArchive Load(string zone)
    {
        if (_zones.TryGetValue(zone, out var cached)) return cached;
        var archive = new ZoneArchive { Zone = zone };
        try
        {
            if (_dir is not null && File.Exists(PathFor(zone)))
                archive = JsonSerializer.Deserialize<ZoneArchive>(File.ReadAllText(PathFor(zone)))
                    ?? archive;
        }
        catch { /* a corrupt archive restarts that zone's learning, not the app */ }
        FoldPetEntries(archive);   // migrate pre-fold archives and older sharers' data
        return _zones[zone] = archive;
    }

    private void Save(ZoneArchive zone)
    {
        if (_dir is null) return;
        try
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(PathFor(zone.Zone), JsonSerializer.Serialize(zone));
        }
        catch { /* read-only disk loses persistence, not the session's data */ }
    }

    private string PathFor(string zone) =>
        Path.Combine(_dir!, string.Concat(zone.Select(c =>
            char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_')) + ".json");
}
