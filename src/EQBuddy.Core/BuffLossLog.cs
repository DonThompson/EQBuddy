using System.Text;

namespace EQBuddy.Core;

/// <summary>One recorded loss: a set buff that went Missing, when, and the best cause
/// the log names — "expired" / "expired (est)", "faded", "lost as X landed (caster)",
/// "lost on death". Display-ready; the breakout fold and the ⧉ export print it as-is.</summary>
public sealed record BuffLossEntry(DateTime Time, string Spell, string Cause);

/// <summary>
/// The lost-buff history (#120 stage 3, Frankthetankk — the final committed stage):
/// every SET buff's transition to Missing, per character per session, with the cause,
/// best-effort from the log. The evaluator is pure, so the transition DETECTOR lives
/// here: <see cref="Apply"/> rides the same LogWatcher seam the trackers use and only
/// BUFFERS evidence (fade lines with their times, hostile spell landings on YOU,
/// deaths); <see cref="Observe"/> runs where per-tick evaluation already happens and
/// turns status transitions into entries, consulting the buffers for the cause.
///
/// Cause honesty, most to least specific:
///  - "lost on death" — a death within <see cref="DeathWindow"/>; the game strips
///    buffs on death, so blaming a timer would be wrong.
///  - "lost as X landed (caster)" — a hostile spell-land line on YOU at most
///    <see cref="OverwriteWindow"/> BEFORE the fade line: the requester's dev-report
///    scenario ("Buff X was active, NPC Y cast debuff Z, Buff X was gone"). The
///    evidence the log actually offers is a named non-melee DamageTakenEvent (nuke /
///    named DoT landing; ticks excluded — a tick minutes after its cast proves
///    nothing) and a SlowLandedEvent resolved through SlowDebuffCatalog. Deliberately
///    tight: a loose window would happily blame an unrelated nuke, and a wrong cause
///    is worse than the honest "faded". (A SpellBlockedEvent naming a set buff as the
///    blocked-BY party is evidence the buff is still UP blocking a cast — it never
///    explains a loss, so it plays no part here.)
///  - "faded" — the wear-off line was seen; the honest baseline.
///  - "expired" — the countdown ran out with no fade line ("(est)" when the duration
///    was still the wiki-base estimate).
///
/// In-memory only, capped at <see cref="Cap"/> newest-first; LogWatcher.Select resets
/// it alongside the trackers, so entries never leak across characters or reviews.
/// </summary>
public sealed class BuffLossLog
{
    public const int Cap = 100;
    /// <summary>Hostile landing at most this long before the fade names the cause.</summary>
    public static readonly TimeSpan OverwriteWindow = TimeSpan.FromSeconds(2);
    /// <summary>A death this close to the transition owns it outright.</summary>
    public static readonly TimeSpan DeathWindow = TimeSpan.FromSeconds(5);
    // A fade explains a transition only if it arrived since the previous tick's look
    // (small slack for the tick racing the watcher thread). An older fade already had
    // its transition — reusing it would mislabel a later expiry as "faded".
    private static readonly TimeSpan FadeSlack = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan HostileRetention = TimeSpan.FromSeconds(30);

    private readonly SlowDebuffCatalog _slows;
    private readonly List<BuffLossEntry> _entries = [];   // newest first
    private readonly List<(string Spell, string Attacker, DateTime Time)> _hostileLands = [];
    private readonly Dictionary<string, DateTime> _lastFade = new(StringComparer.OrdinalIgnoreCase);
    private DateTime? _lastDeath;
    private Dictionary<string, BuffSetStatus>? _previous;   // null until the first Observe
    private DateTime? _observedAt;
    private readonly object _lock = new();

    public event Action? Changed;

    public BuffLossLog(SlowDebuffCatalog? slows = null) =>
        _slows = slows ?? SlowDebuffCatalog.Default;

    /// <summary>Evidence intake, same consumer shape as the trackers. Never records a
    /// loss itself — whether a fade was a SET buff is Observe's call, made against the
    /// assembled set's evaluated states.</summary>
    public void Apply(GameEvent evt)
    {
        lock (_lock)
        {
            switch (evt)
            {
                // A named hostile spell landing on YOU — the overwrite correlation's
                // evidence. Melee, self-inflicted and DoT-tick lines stay out.
                case DamageTakenEvent { Melee: false, Self: false, OverTime: false, Ability.Length: > 0 } dt:
                    RememberHostile(dt.Ability, dt.Attacker, dt.Time);
                    break;
                // A slow's cast-on-you flavor line — self-targeted by construction
                // (#94); the catalog supplies the display label the line lacks.
                case SlowLandedEvent slow when _slows.Find(slow.Message) is { } entry:
                    RememberHostile(entry.Label, "", slow.Time);
                    break;
                case BuffFadeEvent fade:
                    foreach (var s in fade.Spells) _lastFade[SpellCatalog.BaseName(s)] = fade.Time;
                    break;
                // "Your X spell has worn off." with no target = YOUR buff ending.
                case SpellWornOffEvent { Pet: false, Target.Length: 0 } worn:
                    _lastFade[SpellCatalog.BaseName(worn.Spell)] = worn.Time;
                    break;
                case DeathEvent d:
                    _lastDeath = d.Time;
                    break;
            }
        }
    }

    /// <summary>The transition detector: called on the shared UI tick with the
    /// assembled set's evaluated states (visibility of the Buffs card must not gate
    /// it — a hidden card is not a blind history). A transition from Active/Expiring
    /// to Missing records a loss; from NotSeen only a fade can explain the flip (a
    /// landing would have made it Active first), so only fade-evidenced ones record.
    ///
    /// The FIRST look (start, character switch, review replay) has no previous
    /// states: fades the replay showed are real losses this session and record at
    /// their own log times; anything else already Missing predates the watching, and
    /// stamping it "expired" at wall-clock now would be a lie — so it stays silent.</summary>
    public void Observe(IReadOnlyList<BuffSetEntryState> states, DateTime now)
    {
        var changed = false;
        lock (_lock)
        {
            var current = new Dictionary<string, BuffSetStatus>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in states) current.TryAdd(SpellCatalog.BaseName(s.Spell), s.Status);

            foreach (var s in states)
            {
                if (s.Status != BuffSetStatus.Missing) continue;
                var key = SpellCatalog.BaseName(s.Spell);
                // First look has no prior tick: any buffered fade is fresh.
                var threshold = _observedAt is { } seen ? seen - FadeSlack : DateTime.MinValue;
                var fresh = _lastFade.TryGetValue(key, out var fadeAt) && fadeAt >= threshold;
                if (_previous is null)
                {
                    if (fresh) changed |= Record(fadeAt, s.Spell, CauseAt(fadeAt));
                    continue;
                }
                if (!_previous.TryGetValue(key, out var prev) || prev == BuffSetStatus.Missing)
                    continue;   // no prior claim, or the loss is already on record
                if (fresh)
                    changed |= Record(fadeAt, s.Spell, CauseAt(fadeAt));
                else if (prev is BuffSetStatus.Active or BuffSetStatus.Expiring)
                    changed |= Record(now, s.Spell,
                        _lastDeath is { } d && now - d <= DeathWindow ? "lost on death"
                        : s.Estimated ? "expired (est)" : "expired");
            }
            _previous = current;
            _observedAt = now;
        }
        if (changed) Changed?.Invoke();
    }

    private string CauseAt(DateTime fadeTime)
    {
        if (_lastDeath is { } d && fadeTime >= d && fadeTime - d <= DeathWindow)
            return "lost on death";
        var hit = _hostileLands.LastOrDefault(h =>
            fadeTime >= h.Time && fadeTime - h.Time <= OverwriteWindow);
        return hit.Spell is { Length: > 0 }
            ? $"lost as {hit.Spell} landed" + (hit.Attacker.Length > 0 ? $" ({hit.Attacker})" : "")
            : "faded";
    }

    private void RememberHostile(string spell, string attacker, DateTime time)
    {
        _hostileLands.Add((spell, attacker, time));
        _hostileLands.RemoveAll(h => time - h.Time > HostileRetention);
    }

    private bool Record(DateTime time, string spell, string cause)
    {
        _entries.Insert(0, new BuffLossEntry(time, spell, cause));
        if (_entries.Count > Cap) _entries.RemoveAt(_entries.Count - 1);
        return true;
    }

    public int Count { get { lock (_lock) return _entries.Count; } }

    /// <summary>Losses newest first (copy) — the breakout fold's rows.</summary>
    public List<BuffLossEntry> Snapshot() { lock (_lock) return [.. _entries]; }

    /// <summary>Log-switch reset, called by LogWatcher.Select alongside the trackers':
    /// entries and evidence are session claims about ONE character and must never
    /// leak across a switch. Starting blind (_previous null) re-arms the first-look
    /// rule for the replay that follows.</summary>
    public void ResetSession()
    {
        lock (_lock)
        {
            _entries.Clear();
            _hostileLands.Clear();
            _lastFade.Clear();
            _lastDeath = null;
            _previous = null;
            _observedAt = null;
        }
        Changed?.Invoke();
    }

    /// <summary>The fold's ⧉ payload: the history as plain text, oldest first (a bug
    /// report reads chronologically) — the requester's dev-report evidence, same
    /// content-copy idiom as the fight export.</summary>
    public string ExportText(string character)
    {
        var sb = new StringBuilder();
        sb.AppendLine(character.Length > 0 ? $"EQBuddy buff losses — {character}" : "EQBuddy buff losses");
        foreach (var e in Snapshot().AsEnumerable().Reverse())
            sb.AppendLine($"{e.Time:h:mm:ss tt}  {e.Spell} — {e.Cause}");
        return sb.ToString().TrimEnd();
    }
}
