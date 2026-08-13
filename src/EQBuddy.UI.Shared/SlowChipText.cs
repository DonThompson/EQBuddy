using EQBuddy.Core;

namespace EQBuddy.UI.Shared;

/// <summary>The 🐌 chip's face text (#94, Frankthetankk's field report): the type and
/// count of counters belong ON the chip, not behind a hover — mid-fight nobody
/// mouses over things. Count is the honest initial range from the catalog; cures
/// already applied aren't tracked yet, and pretending otherwise would be a guess.</summary>
public static class SlowChipText
{
    public static string Label(SlowState s)
    {
        if (s.CounterType.Length == 0) return $"Slowed {s.PctText}";
        var count = s.CounterMin == s.CounterMax
            ? $"{s.CounterMax}" : $"{s.CounterMin}–{s.CounterMax}";
        return $"Slowed {s.PctText} · {s.CounterType.ToLowerInvariant()} {count}";
    }
}
