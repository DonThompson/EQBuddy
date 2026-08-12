using EQBuddy.Core;

namespace EQBuddy.UI.Shared;

/// <summary>One drawable mark: a hit, a miss, a resist. Hollow marks are the attempts
/// that produced nothing — a miss, a resist, a fizzle, a rune absorb — drawn as
/// outline-only so a lane full of failure reads differently than a lane full of
/// damage at a glance. Label carries the log's own words for the tooltip.</summary>
public sealed record TimelineMark(double Sec, int Amount, bool Crit, bool Hollow, string Label);

public enum LaneKind { You, Pet, Incoming }

/// <summary>One horizontal lane: everything one skill/spell did across the fight.</summary>
public sealed record TimelineLane(string Name, LaneKind Kind, long Total, List<TimelineMark> Marks);

/// <summary>Everything the fight-timeline window draws, precomputed off the UI thread.
/// Durations and positions are seconds from the pull's start — the view multiplies by
/// pixels-per-second and never touches wall-clock time.</summary>
public sealed class FightTimeline
{
    public double DurationSeconds { get; init; }
    public List<TimelineLane> Lanes { get; init; } = [];
    /// <summary>Rolling DPS per whole second of the fight (you + pet together —
    /// the number the meter headlines), plus the pet's own share and what the
    /// mobs did to you, for the three-line graph.</summary>
    public double[] DpsSeries { get; init; } = [];
    public double[] PetDpsSeries { get; init; } = [];
    public double[] IncomingDpsSeries { get; init; } = [];
    public double PeakDps { get; init; }
    public double PeakSec { get; init; }
    public int EventCount { get; init; }
}

/// <summary>
/// Turns a slice of the combat journal into the timeline model. Pure math, no UI —
/// the whole layout brain is testable without a window. Concept credit where due:
/// the lane-per-skill fight timeline is EQ Legends Companion's signature view; this
/// is an independent implementation over EQBuddy's own event journal, with our own
/// twists (miss REASONS on the tooltip, rune absorbs as hollow incoming marks).
/// </summary>
public static class TimelineBuilder
{
    /// <summary>Lanes beyond this merge into "Other" — a badly fragmented fight
    /// (procs, one-off clickies) must not turn the window into a barcode.</summary>
    public const int MaxLanes = 12;

    /// <summary>Rolling window for the DPS series, seconds. Matches EQ's server tick:
    /// per-second buckets alone are spiky lies (a 2k backstab is not 2k DPS), and a
    /// window this size settles them without hiding real burst phases.</summary>
    public const int DpsWindowSeconds = 6;

    public static FightTimeline Build(IReadOnlyList<GameEvent> events, DateTime start,
        double durationSeconds, string petName = "")
    {
        var duration = Math.Max(1, durationSeconds);
        var seconds = (int)Math.Ceiling(duration) + 1;
        var you = new long[seconds];
        var pet = new long[seconds];
        var incoming = new long[seconds];
        var lanes = new Dictionary<(LaneKind Kind, string Name), List<TimelineMark>>();
        var count = 0;

        foreach (var e in events)
        {
            var sec = (e.Time - start).TotalSeconds;
            if (sec < 0 || sec > duration) continue;
            var bucket = Math.Min(seconds - 1, (int)sec);

            switch (e)
            {
                case DamageDealtEvent d:
                    you[bucket] += d.Amount;
                    Add(lanes, LaneKind.You, LaneName(d), new TimelineMark(
                        sec, d.Amount, d.Critical, Hollow: false,
                        d.Critical ? "Critical" : ""));
                    break;

                case MissEvent { Outgoing: true } m:
                    Add(lanes, LaneKind.You, m.Ability.Length > 0 ? m.Ability : "Melee",
                        new TimelineMark(sec, 0, false, Hollow: true,
                            m.Reason.Length > 0 ? m.Reason : "missed"));
                    break;

                case ResistEvent res:
                    Add(lanes, LaneKind.You, res.Spell.Length > 0 ? res.Spell : "Spell",
                        new TimelineMark(sec, 0, false, Hollow: true, "resisted"));
                    break;

                case FizzleEvent f:
                    Add(lanes, LaneKind.You, f.Spell.Length > 0 ? f.Spell : "Spell",
                        new TimelineMark(sec, 0, false, Hollow: true, "fizzled"));
                    break;

                case ThirdMeleeEvent tm when IsPet(tm.Attacker, petName):
                    pet[bucket] += tm.Amount;
                    Add(lanes, LaneKind.Pet, tm.Skill.Length > 0 ? tm.Skill : "Melee",
                        new TimelineMark(sec, tm.Amount, tm.Critical, false,
                            tm.Critical ? "Critical" : ""));
                    break;

                case ThirdDotEvent td when IsPet(td.Caster, petName):
                    pet[bucket] += td.Amount;
                    Add(lanes, LaneKind.Pet, td.Spell,
                        new TimelineMark(sec, td.Amount, td.Critical, false, ""));
                    break;

                case ThirdSchoolEvent tsc when IsPet(tsc.Attacker, petName):
                    pet[bucket] += tsc.Amount;
                    Add(lanes, LaneKind.Pet, tsc.Spell,
                        new TimelineMark(sec, tsc.Amount, tsc.Critical, false, ""));
                    break;

                case DamageTakenEvent { Self: false } dt:
                    incoming[bucket] += dt.Amount;
                    Add(lanes, LaneKind.Incoming, "Incoming",
                        new TimelineMark(sec, dt.Amount, false, false,
                            dt.Ability.Length > 0 ? $"{dt.Attacker} · {dt.Ability}" : dt.Attacker));
                    break;

                case MissEvent { Outgoing: false }:
                    Add(lanes, LaneKind.Incoming, "Incoming",
                        new TimelineMark(sec, 0, false, Hollow: true, "missed you"));
                    break;

                case RuneBlockEvent rb:
                    Add(lanes, LaneKind.Incoming, "Incoming",
                        new TimelineMark(sec, 0, false, Hollow: true, $"{rb.Attacker} · absorbed by rune"));
                    break;

                default:
                    continue;   // casts/kills ride the window for future lanes, not this one
            }
            count++;
        }

        var youSeries = Rolling(you);
        var petSeries = Rolling(pet);
        var (peak, peakSec) = Peak(youSeries);
        return new FightTimeline
        {
            DurationSeconds = duration,
            Lanes = OrderAndCap(lanes),
            DpsSeries = youSeries,
            PetDpsSeries = petSeries,
            IncomingDpsSeries = Rolling(incoming),
            PeakDps = peak,
            PeakSec = peakSec,
            EventCount = count,
        };
    }

    /// <summary>DoT ticks share their spell's lane but say so — the same spell can
    /// land direct and tick, and merging them silently misstates the direct hits.</summary>
    private static string LaneName(DamageDealtEvent d) =>
        d.OverTime ? $"{d.Source} (DoT)" : d.Source;

    private static bool IsPet(string attacker, string petName) =>
        petName.Length > 0 && attacker.Equals(petName, StringComparison.OrdinalIgnoreCase);

    private static void Add(Dictionary<(LaneKind, string), List<TimelineMark>> lanes,
        LaneKind kind, string name, TimelineMark mark)
    {
        if (!lanes.TryGetValue((kind, name), out var list))
            lanes[(kind, name)] = list = [];
        list.Add(mark);
    }

    /// <summary>You-lanes by damage, pet lanes after, one Incoming lane last; the
    /// long tail folds into "Other" so the window never becomes a barcode. Hollow-only
    /// lanes (a spell that never landed) sort by attempt count — a lane of pure
    /// resists is exactly the thing worth seeing.</summary>
    private static List<TimelineLane> OrderAndCap(
        Dictionary<(LaneKind Kind, string Name), List<TimelineMark>> lanes)
    {
        var built = lanes
            .Select(kv => new TimelineLane(kv.Key.Name, kv.Key.Kind,
                kv.Value.Sum(m => (long)m.Amount), kv.Value.OrderBy(m => m.Sec).ToList()))
            .OrderBy(l => l.Kind)
            .ThenByDescending(l => l.Total)
            .ThenByDescending(l => l.Marks.Count)
            .ToList();

        var keep = built.Where(l => l.Kind != LaneKind.You).ToList();
        var yours = built.Where(l => l.Kind == LaneKind.You).ToList();
        var budget = Math.Max(1, MaxLanes - keep.Count);
        if (yours.Count > budget)
        {
            var folded = yours.Skip(budget - 1).ToList();
            yours = yours.Take(budget - 1).Append(new TimelineLane(
                $"Other ({folded.Count} skills)", LaneKind.You,
                folded.Sum(l => l.Total),
                folded.SelectMany(l => l.Marks.Select(m =>
                        m with { Label = m.Label.Length > 0 ? $"{l.Name} · {m.Label}" : l.Name }))
                    .OrderBy(m => m.Sec).ToList())).ToList();
        }
        return yours.Concat(keep).ToList();
    }

    /// <summary>Trailing rolling mean over <see cref="DpsWindowSeconds"/> buckets.</summary>
    private static double[] Rolling(long[] perSecond)
    {
        var result = new double[perSecond.Length];
        long window = 0;
        for (var i = 0; i < perSecond.Length; i++)
        {
            window += perSecond[i];
            if (i >= DpsWindowSeconds) window -= perSecond[i - DpsWindowSeconds];
            result[i] = window / (double)Math.Min(i + 1, DpsWindowSeconds);
        }
        return result;
    }

    private static (double Peak, double Sec) Peak(double[] series)
    {
        double best = 0, at = 0;
        for (var i = 0; i < series.Length; i++)
            if (series[i] > best) { best = series[i]; at = i; }
        return (best, at);
    }
}
