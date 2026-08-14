namespace EQBuddy.Core;

/// <summary>A slow currently believed active on YOU, for the chip and the alert.</summary>
/// <param name="SharedWithHaste">The landing line doubles as a haste song's
/// wear-off ("You slow down." = Selo's fading) — the states eligible for the
/// fade-cluster retro-clear.</param>
public sealed record SlowState(
    string Message, string Label, DateTime LandedAt, DateTime? ExpiresAt,
    int PctMin, int PctMax, string CounterType, int CounterMin, int CounterMax,
    string[] Spells, bool SharedWithHaste = false)
{
    /// <summary>"40%" when the candidates agree, "23–75%" when the landing line
    /// is shared and the log can't tell which rank hit — honest range, never a guess.</summary>
    public string PctText => PctMin == PctMax ? $"{PctMax}%" : $"{PctMin}–{PctMax}%";

    public string CounterText => CounterType.Length == 0 ? ""
        : CounterMin == CounterMax
            ? $"{CounterMax} {CounterType.ToLowerInvariant()} counters"
            : $"{CounterMin}–{CounterMax} {CounterType.ToLowerInvariant()} counters";

    public double? RemainingSeconds(DateTime now) =>
        ExpiresAt is { } e ? Math.Max(0, (e - now).TotalSeconds) : null;
}

/// <summary>
/// Tracks attack-speed debuffs on the player (#94). Same consumer shape as
/// <see cref="MezTracker"/>: fed the parsed event stream, replay-safe because
/// everything keys on log timestamps. Far simpler state, though — the landing
/// line is self-targeted by construction ("You feel lethargic."), so there is
/// no attribution problem, only lifetime: a slow ends when its fade line prints
/// (FadeMessages already knows "You are no longer lethargic."), when its longest
/// candidate duration runs out, or when you zone or die.
/// </summary>
public sealed class SlowTracker
{
    /// <summary>An expired chip stays visible at 0:00 this long — same rationale
    /// as the mez linger: a silent vanish mid-debuff reads as a bug.</summary>
    public static readonly TimeSpan ExpiryLinger = TimeSpan.FromSeconds(8);
    /// <summary>Cap for slows whose wiki page documents no duration: the longest
    /// documented hostile slow runs 3.5 min, so nothing honest outlives this.</summary>
    public static readonly TimeSpan UnknownDurationCap = TimeSpan.FromMinutes(6);
    /// <summary>How long raid-channel chatter proves "in a raid". Raid chat is the
    /// only raid signal the log carries; it only exists while you're in one.</summary>
    public static readonly TimeSpan RaidChatterMemory = TimeSpan.FromMinutes(10);

    /// <summary>How long after "You forget Selo's Accelerando." its wear-off line
    /// ("You slow down.") is read as the haste ending rather than a slow landing
    /// (#116). A dropped song's effect lingers ~12 s before fading (11 s in
    /// Fennec-Halas's log); 45 s covers the linger with margin. The cost of the
    /// window: a REAL Deeds-line slow landing within it goes unannounced once —
    /// rare, and the next pulse or re-land alerts normally.</summary>
    public static readonly TimeSpan HasteForgetWindow = TimeSpan.FromSeconds(45);

    /// <summary>The GROUP-MEMBER side of #116 (David's Hugzee session, grouped
    /// with two bards): other players' songs never log at all — no sing line, no
    /// forget line — so when a bard's twist lapses, the recipient's log shows only
    /// a CLUSTER of first-person flavor fades ("Your wounds stop healing.", "Your
    /// surge of strength fades.") with "You slow down." within seconds of them.
    /// A shared slow line that close to other song fades is the haste lapsing, in
    /// either direction: landing after the fades = suppressed; fades printing
    /// after the chip appeared = the chip retro-clears. 22 of 28 false alerts in
    /// the diagnosing log sat inside this window; the chip's dismiss handles the
    /// stragglers.</summary>
    public static readonly TimeSpan FadeClusterWindow = TimeSpan.FromSeconds(10);

    /// <summary>The strongest signal of the three (David's own suggestion): a
    /// Selo's pulse landing on you ("Your feet move faster.") means any "You slow
    /// down." within this window is the song lapsing, full stop. Pulses repeat
    /// every 12–18s while a bard twists, and the diagnosing log's 29 false alerts
    /// all arrived 12–44s after a landing — 60s covers with margin. Alone among
    /// the guards this one is CAUSAL, not circumstantial; the fade-cluster and
    /// forget-line rules stay as backstops.</summary>
    public static readonly TimeSpan HasteSongWindow = TimeSpan.FromSeconds(60);

    private readonly SlowDebuffCatalog _catalog;
    private readonly Dictionary<string, SlowState> _active = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _forgottenSongs = new(StringComparer.OrdinalIgnoreCase);
    private DateTime? _lastRaidChatter;
    private DateTime? _lastFlavorFade;
    private DateTime? _lastHasteLanding;
    private readonly object _lock = new();

    /// <summary>Raised when the set of active slows changes.</summary>
    public event Action? Changed;

    /// <summary>A slow just landed (or re-landed). <c>isNew</c> is false for a
    /// refresh of one already showing — the caller speaks only when it's true,
    /// so a chain-slowing NPC doesn't turn the voice into a metronome.</summary>
    public event Action<SlowState, bool>? Landed;

    public SlowTracker(SlowDebuffCatalog? catalog = null) =>
        _catalog = catalog ?? SlowDebuffCatalog.Default;

    public void Apply(GameEvent evt)
    {
        SlowState? landed = null;
        var isNew = false;
        var changed = false;
        lock (_lock)
        {
            switch (evt)
            {
                case SlowLandedEvent sl when _catalog.Find(sl.Message) is { } entry:
                    // A shared line whose haste just left the twist is the haste
                    // fading, not a slow arriving (#116) — no chip, no voice.
                    if (entry.FadeOf.Any(song => _forgottenSongs.TryGetValue(Fold(song), out var at)
                            && evt.Time >= at && evt.Time - at <= HasteForgetWindow))
                        break;
                    // Group-member case, causal form (David): a Selo's pulse landed
                    // on you recently — this shared line is that song lapsing.
                    if (entry.FadeOf.Length > 0 && _lastHasteLanding is { } hl
                            && evt.Time >= hl && evt.Time - hl <= HasteSongWindow)
                        break;
                    // Group-member case, circumstantial form: the line landed amid
                    // other songs' fades — a twist lapsing (see FadeClusterWindow).
                    if (entry.FadeOf.Length > 0 && _lastFlavorFade is { } ff
                            && evt.Time >= ff && evt.Time - ff <= FadeClusterWindow)
                        break;
                    var expires = entry.MaxDurationSeconds is { } d
                        ? evt.Time.AddSeconds(d) : (DateTime?)null;
                    isNew = !_active.TryGetValue(entry.Message, out var prior)
                        || evt.Time - prior.LandedAt > (prior.ExpiresAt is null
                            ? UnknownDurationCap
                            : prior.ExpiresAt.Value - prior.LandedAt + ExpiryLinger);
                    landed = new SlowState(entry.Message, entry.Label, evt.Time, expires,
                        entry.PctMin, entry.PctMax, entry.CounterType,
                        entry.Spells.Min(s => s.CounterMin), entry.Spells.Max(s => s.CounterMax),
                        [.. entry.Spells.Select(s => s.Name)],
                        SharedWithHaste: entry.FadeOf.Length > 0);
                    _active[entry.Message] = landed;
                    changed = true;
                    break;

                case BuffFadeEvent fade:
                    _lastFlavorFade = evt.Time;
                    changed = RemoveWhere(s => s.Spells.Intersect(
                        fade.Spells, StringComparer.OrdinalIgnoreCase).Any());
                    // The fades sometimes print AFTER the shared slow line (6 s
                    // server ticks): a haste-capable chip that appeared within the
                    // cluster window was the lapse — take it back.
                    changed |= RemoveWhere(s => s.SharedWithHaste
                        && evt.Time >= s.LandedAt
                        && evt.Time - s.LandedAt <= FadeClusterWindow);
                    break;

                // "Your X spell has worn off." can name a slow directly.
                case SpellWornOffEvent { Pet: false } worn:
                    changed = RemoveWhere(s => s.Spells.Contains(
                        SpellCatalog.BaseName(worn.Spell), StringComparer.OrdinalIgnoreCase));
                    break;

                case SongForgottenEvent forgot:
                    _forgottenSongs[Fold(forgot.Song)] = evt.Time;
                    break;

                case HasteSongLandedEvent:
                    _lastHasteLanding = evt.Time;
                    // A fresh pulse right after a shared slow line proves that line
                    // was song churn (re-twist) — take the chip back.
                    changed = RemoveWhere(s => s.SharedWithHaste
                        && evt.Time >= s.LandedAt
                        && evt.Time - s.LandedAt <= HasteSongWindow);
                    break;

                case RaidChatterEvent:
                    _lastRaidChatter = evt.Time;
                    break;

                case ZoneEvent or DeathEvent:
                    changed = _active.Count > 0;
                    _active.Clear();
                    break;
            }

            // Expired-and-lingered entries drop here rather than on a timer —
            // the tracker only moves when the log does, which keeps replay exact.
            foreach (var (key, s) in _active.Where(kv => Stale(kv.Value, evt.Time)).ToList())
            {
                _active.Remove(key);
                changed = true;
            }
        }
        if (changed) Changed?.Invoke();
        if (landed is not null) Landed?.Invoke(landed, isNew);
    }

    /// <summary>The log writes "Selo`s" where the wiki writes "Selo's" — fold the
    /// backtick so the forget line matches catalog names either way.</summary>
    private static string Fold(string name) => name.Replace('`', '\'');

    private static bool Stale(SlowState s, DateTime now) =>
        s.ExpiresAt is { } e ? now - e > ExpiryLinger : now - s.LandedAt > UnknownDurationCap;

    private bool RemoveWhere(Func<SlowState, bool> match)
    {
        var doomed = _active.Where(kv => match(kv.Value)).Select(kv => kv.Key).ToList();
        foreach (var key in doomed) _active.Remove(key);
        return doomed.Count > 0;
    }

    /// <summary>Player dismissed a chip (David, 2026-08-13: "at least let me
    /// dismiss it if it's not relevant") — drop that slow now, no questions. A
    /// re-land of the same debuff starts a fresh chip normally.</summary>
    public void Dismiss(string message)
    {
        bool changed;
        lock (_lock) changed = _active.Remove(message);
        if (changed) Changed?.Invoke();
    }

    /// <summary>Cheap emptiness probe for per-tick gates — no list, no sort.</summary>
    public bool Any(DateTime now)
    {
        lock (_lock) return _active.Values.Any(s => !Stale(s, now));
    }

    /// <summary>Active slows, worst first; expired entries linger at 0:00 briefly.</summary>
    public List<SlowState> Snapshot(DateTime now)
    {
        lock (_lock)
            return _active.Values
                .Where(s => !Stale(s, now))
                .OrderByDescending(s => s.PctMax)
                .ToList();
    }

    /// <summary>Whether raid chat has been seen recently — the honest raid signal
    /// for the raid-only toggle. False in a raid nobody has typed in for 10 min;
    /// that's the documented trade, not a bug.</summary>
    public bool InRaid(DateTime now) =>
        _lastRaidChatter is { } t && now - t <= RaidChatterMemory;

    /// <summary>"Cure: Abolish Disease (4/cast), Counteract Disease (2/cast)" —
    /// the strongest few options for the state's counter type, or "" for
    /// counterless slows (bard songs just run out).</summary>
    public string CureLine(SlowState s, int max = 3)
    {
        var options = _catalog.CuresFor(s.CounterType);
        if (options.Count == 0) return "";
        var parts = options.Take(max).Select(o => o.PerCastMin == o.PerCastMax
            ? $"{o.Name} ({o.PerCastMax}/cast)"
            : $"{o.Name} ({o.PerCastMin}–{o.PerCastMax}/cast)");
        return "Cure: " + string.Join(", ", parts);
    }
}
