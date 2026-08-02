using EQBuddy.Core;

namespace EQBuddy.UI.Shared;

/// <summary>One row in the Spawns window, ready to render.</summary>
public sealed record SpawnRow(
    string Zone,
    string Name,
    /// <summary>"Frenzied Ghoul — Placeholder (ghoul)" when a PH is known.</summary>
    string DisplayName,
    /// <summary>Editable duration as text ("22m", "3d 12h"); "" when unknown.</summary>
    string DurationText,
    /// <summary>"7:54", "due", "killed 12m ago" (no duration known), or "".</summary>
    string CountdownText,
    bool HasActiveTimer,
    bool IsDue,
    bool Alert,
    bool IsCustom,
    /// <summary>Variance/source/note rolled into tooltip text; "" when there's nothing.</summary>
    string Detail);

/// <summary>
/// Presentation and edit logic for the Spawns window, shared by both UIs. The window
/// itself stays a thin view: rows in, user actions forwarded here, persistence handled
/// by <see cref="SpawnOverrides"/>/<see cref="SpawnTimers"/> underneath.
/// </summary>
public sealed class SpawnsViewModel
{
    private readonly SpawnCatalog _catalog;
    private readonly SpawnOverrides _overrides;
    private readonly SpawnTimers _timers;
    // Keys already seen due (or due at first glance after launch) — an alert fires only
    // on the live transition, so a restart doesn't replay every overdue camp as news.
    private readonly HashSet<string> _alerted = new(StringComparer.OrdinalIgnoreCase);
    private bool _primed;

    public SpawnsViewModel(SpawnCatalog catalog, SpawnOverrides overrides, SpawnTimers timers)
    {
        _catalog = catalog;
        _overrides = overrides;
        _timers = timers;
    }

    public IReadOnlyList<string> ZoneNames { get; private set; } = [];

    /// <summary>The zone the log says the player is in, or null before any zone line.</summary>
    public string? CurrentZoneName => _timers.CurrentZone?.Zone;

    public void RefreshZoneList() =>
        ZoneNames = _catalog.Zones.Select(z => z.Zone).OrderBy(z => z, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Rows for one zone: named with running timers first (soonest due on top),
    /// then the rest alphabetically. Custom (player-added) named merge in.</summary>
    public List<SpawnRow> RowsFor(string zoneName, DateTime now)
    {
        var zone = _catalog.FindZone(zoneName);
        if (zone is null) return [];

        var active = _timers.Snapshot(now)
            .Where(t => string.Equals(t.Zone, zone.Zone, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(t => t.Name, t => t, StringComparer.OrdinalIgnoreCase);

        var rows = new List<SpawnRow>();
        foreach (var entry in zone.Named)
            rows.Add(BuildRow(zone.Zone, entry.Name,
                _overrides.Find(zone.Zone, entry.Name), entry, zone, active, now));
        foreach (var (name, o) in _overrides.CustomFor(zone.Zone))
            rows.Add(BuildRow(zone.Zone, name, o, null, zone, active, now));

        return rows
            .OrderByDescending(r => r.HasActiveTimer)
            .ThenBy(r => active.TryGetValue(r.Name, out var t) ? t.DueAt ?? DateTime.MaxValue : DateTime.MaxValue)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private SpawnRow BuildRow(string zoneName, string name, SpawnOverride? o, SpawnEntry? entry,
        SpawnZone zone, Dictionary<string, SpawnTimerState> active, DateTime now)
    {
        var placeholder = o?.Placeholder ?? entry?.Placeholder ?? "";
        var duration = o?.RespawnSeconds
            ?? (entry is not null ? SpawnCatalog.EffectiveSeconds(zone, entry) : null);

        var display = placeholder.Length > 0 ? $"{name} — Placeholder ({placeholder})" : name;

        string countdown = "";
        var hasTimer = active.TryGetValue(name, out var timer);
        var isDue = false;
        if (hasTimer && timer is not null)
        {
            if (timer.DueAt is { } due)
            {
                countdown = SpawnDurationText.Countdown(due - now);
                isDue = timer.IsDue(now);
            }
            else
                countdown = $"killed {SpawnDurationText.Format((now - timer.KilledAt).TotalSeconds)} ago";
        }

        var detail = entry is null ? "Added by you" : string.Join(" · ", new[]
        {
            entry.Variance.Length > 0 ? $"variance {entry.Variance}" : "",
            entry.Note,
            entry.Source.Length > 0 ? $"source: {entry.Source}" : "",
        }.Where(s => s.Length > 0));

        return new SpawnRow(zoneName, name, display,
            SpawnDurationText.Format(duration), countdown,
            hasTimer, isDue, o?.Alert ?? true, o?.Custom ?? false, detail);
    }

    // ---- user actions ----

    /// <summary>Duration edited in a row. Persists as an override (catalog stays
    /// pristine) and re-derives any running countdown from its original kill time.</summary>
    public void SetDuration(string zone, string name, string durationText)
    {
        var seconds = SpawnDurationText.Parse(durationText);
        var o = _overrides.GetOrAdd(zone, name);
        o.RespawnSeconds = seconds;
        _overrides.Save();
        _timers.SetDuration(zone, name, seconds);
    }

    public void ToggleAlert(string zone, string name)
    {
        var o = _overrides.GetOrAdd(zone, name);
        o.Alert = !o.Alert;
        _overrides.Save();
    }

    /// <summary>▶ — the player marks the kill themselves; <paramref name="agoText"/>
    /// covers "died 5m ago" and uses the same syntax as durations.</summary>
    public void StartNow(string zone, string name, string agoText = "")
    {
        var elapsed = SpawnDurationText.Parse(agoText) is { } ago
            ? TimeSpan.FromSeconds(ago) : TimeSpan.Zero;
        var zoneEntry = _catalog.FindZone(zone);
        var entry = zoneEntry?.Named.FirstOrDefault(n =>
            string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase));
        var duration = _overrides.Find(zone, name)?.RespawnSeconds
            ?? (entry is not null && zoneEntry is not null
                ? SpawnCatalog.EffectiveSeconds(zoneEntry, entry) : null);
        _timers.StartManual(zone, name, duration, elapsed);
    }

    public void ClearTimer(string zone, string name) => _timers.Clear(zone, name);

    /// <summary>Player-added named for zones (or spawns) the catalog doesn't know.</summary>
    public bool AddCustom(string zone, string name, string durationText)
    {
        name = name.Trim();
        if (name.Length == 0) return false;
        var zoneEntry = _catalog.FindZone(zone);
        if (zoneEntry is not null && zoneEntry.Named.Any(n =>
                string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase)))
            return false; // already in the catalog — edit that row instead
        var o = _overrides.GetOrAdd(zone, name);
        o.Custom = true;
        o.RespawnSeconds = SpawnDurationText.Parse(durationText);
        _overrides.Save();
        return true;
    }

    public void RemoveCustom(string zone, string name)
    {
        _overrides.Remove(zone, name);
        _overrides.Save();
        _timers.Clear(zone, name);
    }

    /// <summary>Timers that crossed into "due" since the last call, for rows whose alert
    /// toggle is on. The first call after launch primes silently: a camp that came due
    /// while the app was closed shows as due but doesn't bang the banner on startup.</summary>
    public List<SpawnTimerState> ConsumeDueAlerts(DateTime now)
    {
        var due = _timers.Snapshot(now).Where(t => t.IsDue(now)).ToList();
        var fresh = new List<SpawnTimerState>();
        foreach (var t in due)
        {
            var key = $"{t.Zone}|{t.Name}|{t.KilledAt:O}";
            if (!_alerted.Add(key)) continue;
            if (_primed && (_overrides.Find(t.Zone, t.Name)?.Alert ?? true))
                fresh.Add(t);
        }
        _primed = true;
        return fresh;
    }
}
