using System.Reflection;
using System.Text.Json;

namespace EQBuddy.Core;

/// <summary>
/// Raid-target tracking (Companion-parity, our honest cut): the boss list comes from
/// the game's own achievements dump (the "EverQuest: Raids" Conqueror criteria), a
/// per-character ledger records every kill the log witnesses with its date, and the
/// achievements import marks bosses cleared before EQBuddy existed. Difficulty tiers
/// are deliberately ABSENT: neither the log nor the dump names the instance tier, and
/// a badge the data can't back would be a fabrication — if a tier signal turns up in
/// a real log, it slots in then.
/// </summary>
public sealed class RaidTargetCatalog
{
    public sealed class ZoneEntry
    {
        public string Zone { get; set; } = "";
        public string[] Bosses { get; set; } = [];
    }

    private sealed class Root { public List<ZoneEntry> Zones { get; set; } = []; }

    public IReadOnlyList<ZoneEntry> Zones { get; }
    private readonly HashSet<string> _bossNames;

    public RaidTargetCatalog(IEnumerable<ZoneEntry> zones)
    {
        Zones = zones.ToList();
        _bossNames = Zones.SelectMany(z => z.Bosses)
            .Select(Fold)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public int BossCount => Zones.Sum(z => z.Bosses.Length);

    public bool IsRaidBoss(string creature) =>
        _bossNames.Contains(Fold(creature));

    /// <summary>The achievements dump hyphenates where logs and the wiki use spaces
    /// ("Cazic-Thule" vs "Cazic Thule") — fold the difference so a witnessed kill and
    /// the #109 spawn-catalog cross-mark both land regardless of which form arrives.</summary>
    private static string Fold(string name) =>
        LogParser.Normalize(name).Replace('-', ' ');

    public static RaidTargetCatalog LoadEmbedded()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("EQBuddy.Core.Data.RaidTargets.json")
            ?? throw new InvalidOperationException("RaidTargets.json missing from resources");
        var root = JsonSerializer.Deserialize<Root>(stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("RaidTargets.json unreadable");
        return new RaidTargetCatalog(root.Zones);
    }

    public static RaidTargetCatalog Default { get; } = LoadEmbedded();
}

/// <summary>One boss's personal record: kills the log saw, plus the achievement flag
/// the dump import set (covers clears from before EQBuddy).</summary>
public sealed class RaidBossRecord
{
    public int Kills { get; set; }
    public DateTime? FirstKill { get; set; }
    public DateTime? LastKill { get; set; }
    public bool AchievementComplete { get; set; }
    /// <summary>Witnessed kills by instance difficulty, keyed by
    /// <see cref="InstanceTier.StoreKey"/> ("d0".."d4", "open", "instance",
    /// "unknown"). Kills recorded before tiers existed aren't here at all — the
    /// replay high-water skips them, and backfilling would be guessing. The badge
    /// shows the highest difficulty PROVEN, never inferred.</summary>
    public Dictionary<string, int> TierKills { get; set; } = new();

    /// <summary>Highest difficulty (0–4) with a witnessed kill, or null when no
    /// tiered kill has been recorded (old records, open-world kills, imports).</summary>
    public int? HighestDifficulty()
    {
        for (var t = 4; t >= 0; t--)
            if (TierKills.TryGetValue($"d{t}", out var n) && n > 0) return t;
        return null;
    }
}

/// <summary>
/// Per-character raid-kill ledger, fed the parsed event stream like every tracker
/// (replay-safe: a time high-water mark keeps the startup replay from double-counting
/// kills already recorded). A kill counts when the log SEES the boss die — you were
/// there; who landed the blow is a raid's business, not a scoreboard's.
/// </summary>
public sealed class RaidKillLedger
{
    private readonly RaidTargetCatalog _catalog;
    private readonly string? _path;
    private Dictionary<string, RaidBossRecord> _records = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _highWater = DateTime.MinValue;
    /// <summary>Difficulty of the zone the log is currently inside, from the last
    /// zone-enter line — <see cref="InstanceTier.Unknown"/> until one is seen.
    /// Rebuilt by replay like everything else; only KILLS are high-water gated.</summary>
    private int _currentTier = InstanceTier.Unknown;
    private readonly object _lock = new();

    public event Action? Changed;

    /// <summary>Whose log is being followed — records key on "character|boss" so the
    /// family sharing one install each keep their own clears. Empty = not yet known
    /// (kills land unscoped-but-unshown rather than on the wrong character).</summary>
    public Func<string>? CharacterKey { get; set; }

    private string Key(string boss) =>
        $"{CharacterKey?.Invoke() ?? ""}|{LogParser.Normalize(boss)}";

    public RaidKillLedger(string? path, RaidTargetCatalog? catalog = null)
    {
        _catalog = catalog ?? RaidTargetCatalog.Default;
        _path = path;
        try
        {
            if (path is not null && File.Exists(path))
            {
                var stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(path));
                if (stored is not null)
                {
                    _records = new Dictionary<string, RaidBossRecord>(
                        stored.Records, StringComparer.OrdinalIgnoreCase);
                    _highWater = stored.HighWater;
                }
            }
        }
        catch { /* corrupt store: rebuilt from the log's own replay */ }
    }

    private sealed class Stored
    {
        public Dictionary<string, RaidBossRecord> Records { get; set; } = new();
        public DateTime HighWater { get; set; }
    }

    public void Apply(GameEvent evt)
    {
        if (evt is ZoneEvent z)
        {
            lock (_lock) _currentTier = InstanceTier.FromZoneName(z.Zone);
            return;
        }
        if (evt is not KillEvent kill) return;
        if (!_catalog.IsRaidBoss(kill.Target)) return;
        var key = Key(kill.Target);
        lock (_lock)
        {
            if (kill.Time <= _highWater) return;   // replayed history, already counted
            _highWater = kill.Time;
            var rec = _records.TryGetValue(key, out var r) ? r : _records[key] = new RaidBossRecord();
            rec.Kills++;
            rec.FirstKill ??= kill.Time;
            rec.LastKill = kill.Time;
            var tierKey = InstanceTier.StoreKey(_currentTier);
            rec.TierKills[tierKey] = rec.TierKills.GetValueOrDefault(tierKey) + 1;
            Save();
        }
        Changed?.Invoke();
    }

    /// <summary>Mark bosses the achievements dump says are cleared. Import only ever
    /// adds — an achievement can't be un-earned, and neither can a witnessed kill.</summary>
    public int MarkAchievements(IEnumerable<AchievementEntry> achievements)
    {
        var marked = 0;
        lock (_lock)
        {
            foreach (var a in achievements)
            {
                if (a.Section != "EverQuest: Raids"
                    || !a.Name.StartsWith("Conqueror of ", StringComparison.Ordinal)) continue;
                foreach (var (boss, complete) in a.Criteria)
                {
                    if (!complete) continue;
                    var key = Key(boss);
                    var rec = _records.TryGetValue(key, out var r) ? r : _records[key] = new RaidBossRecord();
                    if (!rec.AchievementComplete) { rec.AchievementComplete = true; marked++; }
                }
            }
            if (marked > 0) Save();
        }
        if (marked > 0) Changed?.Invoke();
        return marked;
    }

    public RaidBossRecord? For(string boss)
    {
        lock (_lock)
            return _records.TryGetValue(Key(boss), out var r) ? r : null;
    }

    /// <summary>Defeated = the log saw it die or the achievement says so —
    /// for the current character only.</summary>
    public int DefeatedCount()
    {
        var prefix = $"{CharacterKey?.Invoke() ?? ""}|";
        lock (_lock)
            return _records.Count(kv => kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && (kv.Value.Kills > 0 || kv.Value.AchievementComplete));
    }

    private void Save()
    {
        if (_path is not { } path) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(
                new Stored { Records = _records, HighWater = _highWater }));
        }
        catch { /* best-effort */ }
    }
}
