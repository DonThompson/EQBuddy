using System.Reflection;
using System.Text.Json;

namespace EQBuddy.Core;

/// <summary>
/// Attack-speed debuffs (slows) keyed by their cast-on-you landing line, plus the
/// cures that remove their counters (#94, Frankthetankk: a 40% slow that lands
/// unnoticed quietly doubles a fight; the alert needs the "how do I get rid of
/// this" answer attached). Several slows share one landing line — "You feel
/// drowsy." is the whole insect line — so entries carry every candidate and the
/// UI shows the honest range instead of picking one. Generated from the eqlwiki
/// spell harvest by scripts/harvests/eqlwiki/slows-harvest.py; beneficial slows
/// (Torpor) are excluded there — a self-chosen tradeoff buff is not an attack.
/// </summary>
public sealed class SlowDebuffCatalog
{
    public sealed class SlowSpell
    {
        public string Name { get; set; } = "";
        public int PctMin { get; set; }
        public int PctMax { get; set; }
        /// <summary>"Poison"/"Disease"/"Curse", or "" for pure slows with no
        /// counters (bard-song slows) — those only wear off or run out.</summary>
        public string CounterType { get; set; } = "";
        public int CounterMin { get; set; }
        public int CounterMax { get; set; }
        public double? DurationSeconds { get; set; }
    }

    public sealed class Entry
    {
        public string Message { get; set; } = "";
        public string Label { get; set; } = "";
        public SlowSpell[] Spells { get; set; } = [];

        public int PctMin => Spells.Min(s => s.PctMin);
        public int PctMax => Spells.Max(s => s.PctMax);
        /// <summary>The candidates' shared counter type, or "" when they disagree —
        /// a mixed line can't honestly name one cure.</summary>
        public string CounterType =>
            Spells.Select(s => s.CounterType).Distinct().Count() == 1 ? Spells[0].CounterType : "";
        /// <summary>Longest candidate duration: the honest ceiling for "how long
        /// could this last", and null when no candidate documents one.</summary>
        public double? MaxDurationSeconds =>
            Spells.All(s => s.DurationSeconds is null) ? null : Spells.Max(s => s.DurationSeconds ?? 0);
    }

    public sealed class CureOption
    {
        public string Name { get; set; } = "";
        public int PerCastMin { get; set; }
        public int PerCastMax { get; set; }
        public string Classes { get; set; } = "";
    }

    public sealed class CureGroup
    {
        public string CounterType { get; set; } = "";
        public CureOption[] Options { get; set; } = [];
    }

    public sealed class AaCure
    {
        public string Name { get; set; } = "";
        public string Class { get; set; } = "";
        public string Note { get; set; } = "";
    }

    private sealed class Root
    {
        public List<Entry> Messages { get; set; } = [];
        public List<CureGroup> Cures { get; set; } = [];
        public List<AaCure> AaCures { get; set; } = [];
    }

    private readonly Dictionary<string, Entry> _byMessage;
    private readonly Dictionary<string, CureGroup> _cures;

    public IReadOnlyList<AaCure> AaCures { get; }

    public SlowDebuffCatalog(IEnumerable<Entry> entries,
        IEnumerable<CureGroup>? cures = null, IEnumerable<AaCure>? aaCures = null)
    {
        _byMessage = entries.Where(e => e.Message.Length > 0 && e.Spells.Length > 0)
            .ToDictionary(e => e.Message, e => e, StringComparer.OrdinalIgnoreCase);
        _cures = (cures ?? []).ToDictionary(c => c.CounterType, c => c, StringComparer.OrdinalIgnoreCase);
        AaCures = (aaCures ?? []).ToList();
    }

    public int Count => _byMessage.Count;

    public Entry? Find(string message) =>
        _byMessage.TryGetValue(message, out var e) ? e : null;

    /// <summary>The strongest few cures for a counter type — "Abolish Disease
    /// removes 4/cast" — for the chip tooltip. Empty for counterless slows.</summary>
    public IReadOnlyList<CureOption> CuresFor(string counterType) =>
        counterType.Length > 0 && _cures.TryGetValue(counterType, out var g) ? g.Options : [];

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public static SlowDebuffCatalog LoadEmbedded()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("EQBuddy.Core.Data.SlowSpells.json")
            ?? throw new InvalidOperationException("SlowSpells.json missing from resources");
        var root = JsonSerializer.Deserialize<Root>(stream, JsonOpts)
            ?? throw new InvalidOperationException("SlowSpells.json unreadable");
        return new SlowDebuffCatalog(root.Messages, root.Cures, root.AaCures);
    }

    /// <summary>Shared instance for the parser's per-line lookups.</summary>
    public static SlowDebuffCatalog Default { get; } = LoadEmbedded();
}
