using System.IO;
using System.Text.Json;

namespace EQBuddy.Core;

/// <summary>One running (or just-expired) spawn countdown.</summary>
public sealed record SpawnTimerState(
    string Server, string Zone, string Name,
    DateTime KilledAt, double? DurationSeconds)
{
    public DateTime? DueAt => DurationSeconds is { } d ? KilledAt.AddSeconds(d) : null;
    public bool IsDue(DateTime now) => DueAt is { } due && now >= due;
}

/// <summary>
/// Tracks when named mobs (or their placeholders) were seen killed and counts down to
/// their respawn (SPAWN-003). Fed the same parsed event stream as SessionStats, so
/// timestamps come from the log: a restart replays the log and re-derives running
/// countdowns exactly like delayed watch cues do. Timers longer than a log's lifetime
/// (raid targets; auto-emptied logs) survive via a persistence file instead.
///
/// Kill matching is zone-gated: names repeat across zones ("an ice giant"), and a kill
/// line names no zone, so the current zone comes from the "You have entered" lines the
/// same way the Travels card learns them. No zone seen yet means no automatic matching —
/// the ▶ button in the Spawns window is the fallback, not a guess.
///
/// Timers are per-server (freeport's Frenzy is not qeynos's), keyed server|zone|name.
/// A repeat kill restarts the clock; replaying the same kill is a no-op.
/// </summary>
public sealed class SpawnTimers
{
    private readonly SpawnCatalog _catalog;
    private readonly SpawnOverrides _overrides;
    private readonly string? _persistPath;
    private readonly object _lock = new();
    private readonly Dictionary<string, SpawnTimerState> _timers =
        new(StringComparer.OrdinalIgnoreCase);

    private SpawnZone? _currentZone;

    public string Server { get; set; } = "";
    public SpawnZone? CurrentZone { get { lock (_lock) return _currentZone; } }

    public SpawnTimers(SpawnCatalog catalog, SpawnOverrides overrides, string? persistPath = null)
    {
        _catalog = catalog;
        _overrides = overrides;
        _persistPath = persistPath;
        LoadPersisted();
    }

    /// <summary>Fed alongside SessionStats.Apply from the watcher thread.</summary>
    public void Apply(GameEvent evt)
    {
        switch (evt)
        {
            case ZoneEvent z:
                lock (_lock) _currentZone = _catalog.FindZone(z.Zone);
                break;
            case KillEvent k:
                OnKill(k);
                break;
        }
    }

    private void OnKill(KillEvent k)
    {
        lock (_lock)
        {
            if (_currentZone is not { } zone) return;

            foreach (var entry in zone.Named)
            {
                var o = _overrides.Find(zone.Zone, entry.Name);
                var placeholder = o?.Placeholder ?? entry.Placeholder;
                if (!SpawnCatalog.NameMatches(entry.Name, k.Target)
                    && !SpawnCatalog.NameMatches(placeholder, k.Target)) continue;

                var duration = o?.RespawnSeconds ?? SpawnCatalog.EffectiveSeconds(zone, entry);
                Upsert(new SpawnTimerState(Server, zone.Zone, entry.Name, k.Time, duration));
                return;
            }

            foreach (var (name, o) in _overrides.CustomFor(zone.Zone))
            {
                if (!SpawnCatalog.NameMatches(name, k.Target)
                    && !SpawnCatalog.NameMatches(o.Placeholder ?? "", k.Target)) continue;
                Upsert(new SpawnTimerState(Server, zone.Zone, name, k.Time, o.RespawnSeconds));
                return;
            }
        }
    }

    /// <summary>The ▶ button: the player saw (or heard about) the kill themselves.
    /// <paramref name="elapsed"/> covers "it died five minutes ago".</summary>
    public void StartManual(string zone, string name, double? durationSeconds, TimeSpan elapsed = default)
    {
        lock (_lock)
            Upsert(new SpawnTimerState(Server, zone, name, DateTime.Now - elapsed, durationSeconds));
    }

    /// <summary>Re-derives the countdown after a duration edit, from the original kill.</summary>
    public void SetDuration(string zone, string name, double? durationSeconds)
    {
        lock (_lock)
        {
            if (_timers.TryGetValue(Key(Server, zone, name), out var t))
                Upsert(t with { DurationSeconds = durationSeconds });
        }
    }

    public void Clear(string zone, string name)
    {
        lock (_lock)
        {
            if (_timers.Remove(Key(Server, zone, name))) SavePersisted();
        }
    }

    /// <summary>Current timers for this server, expired ones pruned. A due timer lingers
    /// (one respawn cycle, clamped to 1–24 h) so "due" is visible rather than vanishing,
    /// then drops — the cycle almost certainly ran without us seeing a kill.</summary>
    public List<SpawnTimerState> Snapshot(DateTime now)
    {
        lock (_lock)
        {
            var stale = _timers.Values.Where(t => IsStale(t, now)).ToList();
            if (stale.Count > 0)
            {
                foreach (var t in stale) _timers.Remove(Key(t.Server, t.Zone, t.Name));
                SavePersisted();
            }
            return _timers.Values
                .Where(t => string.Equals(t.Server, Server, StringComparison.OrdinalIgnoreCase))
                .OrderBy(t => t.DueAt ?? DateTime.MaxValue)
                .ToList();
        }
    }

    private static bool IsStale(SpawnTimerState t, DateTime now)
    {
        if (t.DueAt is not { } due)
            // No duration known: the row only says "killed N ago" — keep it a day.
            return now - t.KilledAt > TimeSpan.FromHours(24);
        var linger = TimeSpan.FromSeconds(Math.Clamp(t.DurationSeconds!.Value, 3600, 86400));
        return now - due > linger;
    }

    private static string Key(string server, string zone, string name) => $"{server}|{zone}|{name}";

    private void Upsert(SpawnTimerState t)
    {
        var key = Key(t.Server, t.Zone, t.Name);
        // Replays hand us the same kill again — identical state must not thrash the
        // persistence file. An OLDER kill never overwrites a newer one (a truncated log
        // replayed after a manual start, for example).
        if (_timers.TryGetValue(key, out var existing))
        {
            if (existing == t) return;
            if (t.KilledAt < existing.KilledAt) return;
        }
        _timers[key] = t;
        SavePersisted();
    }

    // -- persistence: for timers that outlive the log (raid targets, auto-emptied logs) --

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private void LoadPersisted()
    {
        if (_persistPath is null || !File.Exists(_persistPath)) return;
        try
        {
            var list = JsonSerializer.Deserialize<List<SpawnTimerState>>(
                File.ReadAllText(_persistPath), JsonOpts);
            if (list is null) return;
            foreach (var t in list)
                _timers[Key(t.Server, t.Zone, t.Name)] = t;
        }
        catch { /* corrupt file loses timers, not the feature */ }
    }

    private void SavePersisted()
    {
        if (_persistPath is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_persistPath)!);
            File.WriteAllText(_persistPath, JsonSerializer.Serialize(_timers.Values.ToList(), JsonOpts));
        }
        catch { /* read-only disk: timers just won't survive a restart */ }
    }
}
