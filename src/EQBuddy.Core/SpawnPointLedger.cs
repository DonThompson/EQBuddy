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
        /// <summary>The player attested this location is right (map right-click,
        /// David 2026-08-13). A confirmed centroid stops refining — kills still
        /// count and timers still run, but the dot stays where it was put.
        /// Travels in share strings like everything else the player set.</summary>
        public bool Confirmed { get; set; }
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
        /// <summary>How many kills are archived AT the HighWater second. Log stamps
        /// have 1-second resolution, so an AoE finishing two mobs in one second must
        /// not lose the second kill — replay skips exactly this many boundary kills
        /// and live observation counts past them (review 2026-08-13).</summary>
        public int HighWaterCount { get; set; }
        public List<SpawnPoint> Points { get; set; } = [];
    }

    /// <summary>The nearest archived point within <see cref="ClusterRadius"/>, or null.
    /// NEAREST, not first-found: with camps 30–60 units apart, first-match attaches a
    /// kill to whichever point happens to be older in the list and smears centroids
    /// together. One shared helper so live clustering and share-string merges can
    /// never diverge (both bugs from the 2026-08-13 review).</summary>
    public static SpawnPoint? FindCluster(List<SpawnPoint> points, double locY, double locX)
    {
        SpawnPoint? best = null;
        var bestDist = double.MaxValue;
        foreach (var p in points)
        {
            var dy = p.LocY - locY;
            var dx = p.LocX - locX;
            var dist = Math.Sqrt(dy * dy + dx * dx);
            if (dist <= ClusterRadius && dist < bestDist) { best = p; bestDist = dist; }
        }
        return best;
    }

    private readonly string? _dir;
    private readonly SpawnCatalog _catalog;
    private readonly Dictionary<string, ZoneArchive> _zones = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private string _currentZone = "";
    private LocationEvent? _lastLoc;
    // Kills seen at the current HighWater second THIS process — compared against the
    // archive's persisted HighWaterCount so replay skips exactly the boundary kills
    // it already has (log replay is deterministic and ordered).
    private readonly Dictionary<string, int> _seenAtHighWater = new(StringComparer.OrdinalIgnoreCase);
    // Dirty archives + a wall-clock debounce: a full-log ingest observes thousands of
    // kills in seconds, and a JSON rewrite per kill is O(archive²) disk traffic on the
    // parser thread (review 2026-08-13). The log is the source of truth — anything an
    // unflushed crash loses, the next replay re-derives via the high-water mark.
    private readonly HashSet<string> _dirty = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastSaveWall = DateTime.MinValue;
    private static readonly TimeSpan SaveDebounce = TimeSpan.FromSeconds(2);

    /// <summary>Bumped on every archive mutation (observation or import). The map's
    /// 1 Hz tick compares this integer instead of deep-cloning the archive to detect
    /// change — Snapshot only runs when something actually happened.</summary>
    public int Revision { get; private set; }

    /// <summary>Same freshness window as camp pins: you were standing at the fight.</summary>
    private static readonly TimeSpan LocWindow = TimeSpan.FromMinutes(3);

    public SpawnPointLedger(string? dir, SpawnCatalog catalog)
    {
        _dir = dir;
        _catalog = catalog;
    }

    /// <summary>A replay is starting over — LogWatcher.Select calls this before every
    /// (re)ingest. The in-process boundary counters must restart with it: they count
    /// kills seen at each zone's HighWater second THIS replay, and a counter carried
    /// over from the previous pass has already "used up" the persisted
    /// HighWaterCount, so the re-replayed boundary kills would count as new and
    /// re-archive on every auto-follow away-and-back or review enter/exit (audit
    /// finding 3). Explicit hook rather than inferring a rewind from an
    /// older-than-HighWater kill: a restart whose first surviving kill lands exactly
    /// AT the boundary second sends nothing older to infer from.</summary>
    public void ReplayStarting()
    {
        lock (_lock) _seenAtHighWater.Clear();
    }

    public void Apply(GameEvent evt)
    {
        switch (evt)
        {
            case ZoneEvent z:
                lock (_lock)
                {
                    FlushLocked();   // leaving a zone is the natural save point
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
        if (k.Time < zone.HighWater) return;   // replayed history, already archived
        if (k.Time == zone.HighWater)
        {
            // The boundary second: replay must skip exactly the kills already
            // archived there, but a LIVE second kill in the same log second (AoE,
            // two group kills) is new data and must land.
            var seen = _seenAtHighWater.GetValueOrDefault(zone.Zone) + 1;
            _seenAtHighWater[zone.Zone] = seen;
            if (seen <= zone.HighWaterCount) return;
            zone.HighWaterCount = seen;
        }
        else
        {
            zone.HighWater = k.Time;
            zone.HighWaterCount = 1;
            _seenAtHighWater[zone.Zone] = 1;
        }

        var point = FindCluster(zone.Points, loc.LocY, loc.LocX);
        if (point is null)
            zone.Points.Add(point = new SpawnPoint { LocY = loc.LocY, LocX = loc.LocX });
        else if (!point.Confirmed)
        {
            // Centroid refines toward the new observation, weighted by history —
            // the archive's "only gets better over time" contract. A CONFIRMED
            // point is the exception: the player attested the spot, so it holds.
            var n = point.TotalKills();
            point.LocY = (point.LocY * n + loc.LocY) / (n + 1);
            point.LocX = (point.LocX * n + loc.LocX) / (n + 1);
        }

        var name = FoldPetName(LogParser.Normalize(k.Target));
        var mob = point.Mobs.TryGetValue(name, out var m) ? m : point.Mobs[name] = new MobSeen();
        mob.Kills++;
        mob.LastKill = k.Time;
        Revision++;
        MarkDirty(zone);
    }

    private void MarkDirty(ZoneArchive zone)
    {
        _dirty.Add(zone.Zone);
        var now = DateTime.UtcNow;
        if (now - _lastSaveWall < SaveDebounce) return;
        _lastSaveWall = now;
        FlushLocked();
    }

    /// <summary>Write every dirty archive to disk. Callers hold <see cref="_lock"/>.</summary>
    private void FlushLocked()
    {
        foreach (var name in _dirty)
            if (_zones.TryGetValue(name, out var zone))
                Save(zone);
        _dirty.Clear();
    }

    /// <summary>Flush pending archives — the app-shutdown hook. Anything lost to a
    /// crash instead is re-derived from the log on the next replay.</summary>
    public void Flush()
    {
        lock (_lock) FlushLocked();
    }

    /// <summary>Remove the archived point nearest (locY, locX) — the map's
    /// right-click (David, 2026-08-13). Durable against replay: the removed kills
    /// sit behind the high-water mark, so restarts don't resurrect the point.
    /// Fresh kills near the spot legitimately re-learn it, which is the honest
    /// semantic for "that dot is wrong" — the log always outranks an edit.</summary>
    public bool RemovePoint(string zone, double locY, double locX)
    {
        lock (_lock)
        {
            var archive = Load(zone);
            var point = FindCluster(archive.Points, locY, locX);
            if (point is null) return false;
            archive.Points.Remove(point);
            Revision++;
            Save(archive);   // a user edit saves immediately, not on the debounce
            _dirty.Remove(archive.Zone);
            return true;
        }
    }

    /// <summary>Wipe a zone's entire spawn-point archive (the map's clear-all,
    /// David 2026-08-13). Returns how many points went. Same durability story as
    /// RemovePoint: replay can't resurrect them, fresh kills rebuild honestly.</summary>
    public int ClearZone(string zone)
    {
        lock (_lock)
        {
            var archive = Load(zone);
            var count = archive.Points.Count;
            if (count == 0) return 0;
            archive.Points.Clear();
            Revision++;
            Save(archive);
            _dirty.Remove(archive.Zone);
            return count;
        }
    }

    /// <summary>Set or clear the player's location attestation on the point nearest
    /// (locY, locX) — the map's other right-click. Returns the new state, or null
    /// when nothing was near.</summary>
    public bool? ConfirmPoint(string zone, double locY, double locX, bool confirmed)
    {
        lock (_lock)
        {
            var archive = Load(zone);
            var point = FindCluster(archive.Points, locY, locX);
            if (point is null) return null;
            point.Confirmed = confirmed;
            Revision++;
            Save(archive);
            _dirty.Remove(archive.Zone);
            return point.Confirmed;
        }
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
                HighWaterCount = z.HighWaterCount,
                Points = z.Points.Select(p => new SpawnPoint
                {
                    LocY = p.LocY, LocX = p.LocX, Confirmed = p.Confirmed,
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
    /// preview was computed against a snapshot clone) and persist. Points merge
    /// under the ledger lock; the timer overrides apply OUTSIDE it — SpawnOverrides
    /// guards itself, and holding this lock through its disk writes would stall the
    /// log pipeline behind the import (review 2026-08-13).</summary>
    public void ApplyImport(ZoneShare.Preview preview, SpawnOverrides overrides, bool includeFlagged)
    {
        lock (_lock)
        {
            var zone = Load(preview.Payload.Zone);
            ZoneShare.ApplyPoints(preview, zone);
            Revision++;
            Save(zone);
        }
        ZoneShare.ApplyTimers(preview, _catalog.FindZone(preview.Payload.Zone),
            overrides, includeFlagged);
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
        // Hygiene for archives written by older builds or hand-edited files: a
        // mob-less point would crash LastKilled() on the map tick, and a legacy
        // archive without HighWaterCount had exactly the <= semantics — one kill
        // at the boundary second — so say so rather than re-ingesting it.
        archive.Points.RemoveAll(p => p.Mobs is not { Count: > 0 });
        if (archive.HighWaterCount == 0 && archive.HighWater != default)
            archive.HighWaterCount = 1;
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
