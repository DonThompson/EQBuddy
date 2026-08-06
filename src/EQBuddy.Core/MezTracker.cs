using System.Reflection;
using System.Text.Json;

namespace EQBuddy.Core;

/// <summary>One entry in the embedded mez catalog (Data/MezSpells.json).</summary>
public sealed class MezSpellInfo
{
    public string Name { get; set; } = "";
    public double? DurationSeconds { get; set; }
    public bool Aoe { get; set; }
    public string Landing { get; set; } = "mesmerized";
    public string Source { get; set; } = "";
}

/// <summary>A currently-active (believed) mez, for the chip stack.</summary>
public sealed record MezState(
    string Target, string Spell, string Caster, DateTime LandedAt, DateTime? ExpiresAt)
{
    /// <summary>Seconds until wake-up; null while the duration is unknown (the chip
    /// shows the mez without a countdown — it still clears on break).</summary>
    public double? RemainingSeconds(DateTime now) =>
        ExpiresAt is { } e ? Math.Max(0, (e - now).TotalSeconds) : null;
}

/// <summary>
/// Tracks who is mezzed, for how much longer — from ANY group member's log, not just the
/// caster's. The landing line ("X has been mesmerized.") is bystander-visible, and other
/// players' casts log with spell name and rank ("Shack begins casting Shield of Thistles
/// IV."), so every EQBuddy in the group derives the same state from its own log; no
/// networking involved. Mirrors the charm trust rules: a landing line only counts when a
/// recent cast KNOWN to be a mez explains it — an unexplained landing is someone outside
/// the log's view and is ignored.
///
/// Durations: catalog first (Data/MezSpells.json — null until researched), overridden by
/// learned values. Only the CASTER's log sees "Your X spell has worn off of Y.", so only
/// the caster's EQBuddy can measure real durations; it learns the LONGEST land→fade gap
/// per exact spell name (rank included — ranks lengthen mezzes), because early breaks
/// shorten gaps but nothing lengthens them. Learned values persist via
/// <see cref="AttachStore"/> and flow to the rest of the group through catalog updates.
/// </summary>
public sealed class MezTracker
{
    /// <summary>A landing this long after the cast began no longer belongs to it
    /// (covers cast time + travel + log flushing).</summary>
    public static readonly TimeSpan CastToLand = TimeSpan.FromSeconds(8);
    /// <summary>Without a known duration, a mez chip that nothing ever breaks is
    /// dropped after this long — mezzes don't last minutes.</summary>
    public static readonly TimeSpan UnknownDurationCap = TimeSpan.FromSeconds(120);
    /// <summary>An expired chip stays visible at 0:00 this long before dropping —
    /// rank-lengthened mezzes outlive the base duration, and a chip that silently
    /// vanishes mid-mez reads as a bug (issue #32).</summary>
    public static readonly TimeSpan ExpiryLinger = TimeSpan.FromSeconds(8);
    /// <summary>How long a woken creature keeps explaining damage lines for its name.
    /// Refreshed by activity; long enough to cover a fight, short enough that a name
    /// doesn't stay break-immune forever (issue #35).</summary>
    public static readonly TimeSpan AwakeMemory = TimeSpan.FromSeconds(45);
    /// <summary>EQ effects run on 6-second server ticks, and the worn-off message fires
    /// at the tick boundary — up to several seconds AFTER the mez actually released.
    /// True durations are tick multiples, so rounding an observation DOWN to the tick
    /// recovers the exact duration and strips the message lag (field report from
    /// Aenari: learned timers ran 2-3s past the real wake — the dangerous direction).</summary>
    public const double ServerTickSeconds = 6;

    // No AA correction on purpose: the full eqlwiki AA sweep (2026-08-06, AaCatalog)
    // found NO EQ Legends AA that extends detrimental mez/charm durations — unlike live
    // EQ's Mesmerization Mastery. Adamant Will only moves resist chance, which never
    // shifts a landed mez's clock. Learned durations here are therefore character-true
    // without reading the AA ledger. (Beneficial-duration AAs like Spell Casting
    // Reinforcement matter to future BUFF countdowns, not to this tracker.)

    private readonly Dictionary<string, MezSpellInfo> _catalog;
    private readonly Dictionary<string, double> _learned = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<MezState> _active = [];
    private readonly List<(string Caster, string Spell, DateTime Time)> _recentCasts = [];
    // The awake ledger (issue #35): creatures of this name currently believed awake,
    // and when one last acted. Once a break wakes one twin, its ongoing fight keeps
    // generating damage lines for the shared name — those attribute HERE instead of
    // eating the still-mezzed siblings' chips. Kills decrement; inactivity expires it.
    private readonly Dictionary<string, (int Count, DateTime Last)> _awake =
        new(StringComparer.OrdinalIgnoreCase);
    private string? _storePath;
    private readonly object _lock = new();

    /// <summary>Raised when the set of active mezzes changes (not on every tick).</summary>
    public event Action? Changed;

    public MezTracker(IEnumerable<MezSpellInfo>? catalog = null)
    {
        _catalog = (catalog ?? LoadEmbedded()).ToDictionary(
            s => s.Name, s => s, StringComparer.OrdinalIgnoreCase);
    }

    public static List<MezSpellInfo> LoadEmbedded()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("EQBuddy.Core.Data.MezSpells.json")
            ?? throw new InvalidOperationException("MezSpells.json missing from resources");
        using var doc = JsonDocument.Parse(stream);
        return doc.RootElement.GetProperty("spells").Deserialize<List<MezSpellInfo>>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
    }

    /// <summary>Loads learned durations and saves after each new maximum —
    /// same pattern as SpellCatalog's store; tests don't attach one.</summary>
    public void AttachStore(string path)
    {
        _storePath = path;
        try
        {
            if (!File.Exists(path)) return;
            var stored = JsonSerializer.Deserialize<Dictionary<string, double>>(File.ReadAllText(path));
            if (stored is null) return;
            lock (_lock)
                foreach (var (spell, seconds) in stored)
                {
                    // Heal files written before the guards existed: tick-floor values
                    // that carry message lag (Aenari's 2-3s-long timers), and reject
                    // anything below the catalog base — break lengths recorded as
                    // "durations" (the {"Mesmerize": 7} incident).
                    var ticked = Math.Floor(seconds / ServerTickSeconds) * ServerTickSeconds;
                    var floor = _catalog.TryGetValue(SpellCatalog.BaseName(spell), out var info)
                        ? info.DurationSeconds ?? 0 : 0;
                    if (ticked is > 0 and < 600 && ticked >= floor) _learned.TryAdd(spell, ticked);
                }
        }
        catch { /* corrupt store: rewritten on next learn */ }
    }

    /// <summary>Second/third consumer of the parsed event stream (like SpawnTimers):
    /// replay-safe because everything keys on log timestamps.</summary>
    public void Apply(GameEvent evt)
    {
        var changed = false;
        lock (_lock)
        {
            switch (evt)
            {
                case SpellCastEvent own when IsMezSpell(own.Spell):
                    RememberCast("You", own.Spell, own.Time);
                    break;
                case OtherCastEvent other when IsMezSpell(other.Spell):
                    RememberCast(other.Caster, other.Spell, other.Time);
                    break;
                case MezzedEvent mez:
                    changed = OnLanding(mez);
                    break;
                // Any damage wakes a mezzed creature — from anyone, visible to everyone.
                case DamageDealtEvent dd:
                    changed = OnCreatureActivity(dd.Target, dd.Time);
                    break;
                case ThirdMeleeEvent tm:
                    // Damage TO the target breaks it; the target ATTACKING proves it woke.
                    changed = OnCreatureActivity(tm.Target, tm.Time) | OnCreatureActivity(tm.Attacker, tm.Time);
                    break;
                case ThirdDotEvent td:
                    changed = OnCreatureActivity(td.Target, td.Time);
                    break;
                case ThirdSchoolEvent tsch:
                    changed = OnCreatureActivity(tsch.Target, tsch.Time) | OnCreatureActivity(tsch.Attacker, tsch.Time);
                    break;
                // The creature acting proves it's awake — but a DoT tick doesn't count:
                // a dot cast before the mez keeps ticking on you while the mob sleeps.
                case DamageTakenEvent { Self: false, OverTime: false } dt:
                    changed = OnCreatureActivity(dt.Attacker, dt.Time);
                    break;
                case KillEvent k:
                    changed = OnKill(k.Target, k.Time);
                    break;
                case SpellWornOffEvent { Pet: false } wo when wo.Target.Length > 0 && IsMezSpell(wo.Spell):
                    // Caster-private natural fade: the exact end, and the one signal that
                    // can teach a real duration (see class summary).
                    changed = OnWornOff(wo);
                    break;
                case ZoneEvent:
                    changed = _active.Count > 0;
                    _active.Clear();
                    _recentCasts.Clear();
                    _awake.Clear();
                    break;
            }
            Prune(evt.Time);
        }
        if (changed) Changed?.Invoke();
    }

    /// <summary>Active mezzes at <paramref name="now"/>, soonest wake-up first;
    /// unknown-duration entries sort last (nothing to warn about yet). Entries past
    /// their expiry stay visible (at 0:00) for <see cref="ExpiryLinger"/> — the mez
    /// may genuinely still hold (rank-lengthened durations) and a silent vanish
    /// mid-mez reads as a bug.</summary>
    public List<MezState> Snapshot(DateTime now)
    {
        lock (_lock)
            return _active
                .Where(m => m.ExpiresAt is null || now - m.ExpiresAt < ExpiryLinger)
                .OrderBy(m => m.ExpiresAt ?? DateTime.MaxValue)
                .ToList();
    }

    /// <summary>Learned durations (exact spell name → seconds), for display/export.</summary>
    public IReadOnlyDictionary<string, double> LearnedDurations
    {
        get { lock (_lock) return new Dictionary<string, double>(_learned); }
    }

    private bool IsMezSpell(string spell) =>
        _catalog.ContainsKey(SpellCatalog.BaseName(spell));

    private void RememberCast(string caster, string spell, DateTime t)
    {
        _recentCasts.Add((caster, spell, t));
        if (_recentCasts.Count > 32) _recentCasts.RemoveRange(0, 16);
    }

    private bool OnLanding(MezzedEvent mez)
    {
        // Newest explaining cast wins. AoE mezzes land on several targets from one cast,
        // so the cast is NOT consumed — each landing within the window claims it.
        var cast = _recentCasts.LastOrDefault(c => mez.Time - c.Time <= CastToLand);
        if (cast.Spell is null || cast.Spell.Length == 0) return false;   // nobody we can see cast a mez

        var entry = new MezState(mez.Target, cast.Spell, cast.Caster, mez.Time,
            DurationFor(cast.Spell) is { } d ? mez.Time.AddSeconds(d) : null);

        // An AWAKE creature of this name getting mezzed is the classic re-mez after a
        // break: settle its ledger entry and ADD a chip — the sleeping siblings keep
        // theirs (issue #35).
        if (_awake.TryGetValue(entry.Target, out var awake) && awake.Count > 0
            && mez.Time - awake.Last <= AwakeMemory)
        {
            if (awake.Count == 1) _awake.Remove(entry.Target);
            else _awake[entry.Target] = (awake.Count - 1, mez.Time);
            _active.Add(entry);
            return true;
        }

        // Same-name handling (issue #32, reworked from the original keep-earliest rule):
        // chain-mezzing ONE target is the normal workflow, so a re-landing REFRESHES the
        // earliest-expiring same-name entry. The exception is several landings in the
        // same second (an AoE catching same-named mobs): those are distinct creatures
        // and get their own entries — the UI numbers them.
        var sameName = _active.Where(m =>
            m.Target.Equals(mez.Target, StringComparison.OrdinalIgnoreCase)).ToList();
        var refresh = sameName
            .Where(m => m.LandedAt != mez.Time)
            .OrderBy(m => m.ExpiresAt ?? DateTime.MaxValue)
            .FirstOrDefault();
        if (refresh is not null) _active.Remove(refresh);
        _active.Add(entry);
        return true;
    }

    private bool OnWornOff(SpellWornOffEvent wo)
    {
        // Among same-named entries the longest-asleep one fades first.
        var name = LogParser.Normalize(wo.Target);
        var entry = _active
            .Where(m => m.Target.Equals(name, StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => m.LandedAt)
            .FirstOrDefault();
        if (entry is null) return false;
        _active.Remove(entry);
        // A natural fade measures the full duration; learn the longest observed per
        // exact (ranked) spell name. Guards (field report, David 2026-08-04: a single
        // 7s early break got learned as Mesmerize's duration and shrank every chip on
        // the machine): the worn-off line ALSO fires on breaks, so an observation
        // SHORTER than the catalog base is a break — ranks only lengthen mezzes —
        // and a fade right after the name's awake ledger was touched is a break too.
        // Tick-floor the raw gap (see ServerTickSeconds): 32.8s observed = a 30s mez
        // plus message lag, never a 32.8s mez.
        var observed = Math.Floor((wo.Time - entry.LandedAt).TotalSeconds / ServerTickSeconds)
            * ServerTickSeconds;
        var baseFloor = _catalog.TryGetValue(SpellCatalog.BaseName(entry.Spell), out var info)
            ? info.DurationSeconds ?? 0 : 0;
        var brokeRecently = _awake.TryGetValue(name, out var aw)
            && (wo.Time - aw.Last).Duration() <= TimeSpan.FromSeconds(3);
        if (observed is > 3 and < 600 && observed >= baseFloor && !brokeRecently
            && (!_learned.TryGetValue(entry.Spell, out var known) || observed > known))
        {
            _learned[entry.Spell] = Math.Round(observed, 1);
            SaveStore();
        }
        return true;
    }

    private double? DurationFor(string spell) =>
        _learned.TryGetValue(spell, out var learned) ? learned
        : _catalog.TryGetValue(SpellCatalog.BaseName(spell), out var info) ? info.DurationSeconds
        : null;

    /// <summary>A damage/action line for this name. If a creature of the name is
    /// already believed awake (and recently active), the line is ITS fight — no chip
    /// is touched (issue #35: the woken twin's ongoing fight must not erase the
    /// still-mezzed siblings' chips). Otherwise this line IS the break: the
    /// earliest-expiring chip drops and the ledger records one awake creature.</summary>
    private bool OnCreatureActivity(string target, DateTime now)
    {
        var name = LogParser.Normalize(target);
        if (_awake.TryGetValue(name, out var a) && a.Count > 0 && now - a.Last <= AwakeMemory)
        {
            _awake[name] = (a.Count, now);   // the awake one is still fighting
            return false;
        }
        var victim = _active
            .Where(m => m.Target.Equals(name, StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => m.ExpiresAt ?? DateTime.MaxValue)
            .FirstOrDefault();
        if (victim is null) return false;
        _active.Remove(victim);
        _awake[name] = ((_awake.TryGetValue(name, out var prev) ? prev.Count : 0) + 1, now);
        return true;
    }

    /// <summary>A kill for this name. An awake creature dying decrements the ledger
    /// and touches no chip (the dead one is the one that was fighting); a kill with
    /// nothing awake means a mezzed one was killed outright (AoE) — drop its chip.</summary>
    private bool OnKill(string target, DateTime now)
    {
        var name = LogParser.Normalize(target);
        if (_awake.TryGetValue(name, out var a) && a.Count > 0 && now - a.Last <= AwakeMemory)
        {
            if (a.Count == 1) _awake.Remove(name);
            else _awake[name] = (a.Count - 1, now);
            return false;
        }
        var victim = _active
            .Where(m => m.Target.Equals(name, StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => m.ExpiresAt ?? DateTime.MaxValue)
            .FirstOrDefault();
        if (victim is null) return false;
        _active.Remove(victim);
        return true;
    }

    private void Prune(DateTime now)
    {
        _recentCasts.RemoveAll(c => now - c.Time > CastToLand);
        // Entries are RETAINED well past their visible expiry (Snapshot hides them
        // after ExpiryLinger): a rank-lengthened mez can fade long after the base
        // duration, and the natural-fade line must still find its entry to learn
        // from — pruning at the linger would make high ranks unlearnable. A stale
        // retained entry also absorbs the next break line first (earliest expiry),
        // which is the right guess: it's the likeliest-awake one.
        _active.RemoveAll(m => now - m.LandedAt > UnknownDurationCap);
    }

    private void SaveStore()
    {
        if (_storePath is not { } path) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(_learned));
        }
        catch { /* best-effort; in-memory learning still works */ }
    }
}
