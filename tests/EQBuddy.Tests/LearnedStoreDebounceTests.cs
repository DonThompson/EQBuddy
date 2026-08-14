using EQBuddy.Core;

namespace EQBuddy.Tests;

/// <summary>
/// Perf audit #13: SpellCatalog and BuffTracker learned-store writes are debounced
/// (the StackingLedgerStore idiom — flag now, one write ~2 s later, Flush at exit)
/// instead of a synchronous file write on the ingest path. What must not change:
/// everything learned still reaches disk — via the debounce or the exit flush — and
/// a reload round-trips it.
/// </summary>
public class LearnedStoreDebounceTests
{
    private static string TempStore(string name) =>
        Path.Combine(Path.GetTempPath(), $"eqbuddy-test-{name}-{Guid.NewGuid():N}.json");

    [Fact]
    public void SpellCatalogLearnPersistsViaFlushAndRoundTrips()
    {
        var path = TempStore("spells");
        try
        {
            var cat = new SpellCatalog();
            cat.AttachStore(path);
            Assert.True(cat.Learn("Fizzlewick's Torment", SpellCategory.DamageOverTime));
            // The write is debounced — Flush is the deterministic "now".
            cat.Flush();
            Assert.True(File.Exists(path));

            var reloaded = new SpellCatalog();
            reloaded.AttachStore(path);
            Assert.Equal(SpellCategory.DamageOverTime, reloaded.Classify("Fizzlewick's Torment"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SpellCatalogDebouncedSaveLandsWithoutAFlush()
    {
        var path = TempStore("spells-debounce");
        try
        {
            var cat = new SpellCatalog();
            cat.AttachStore(path);
            Assert.True(cat.Learn("Fizzlewick's Lament", SpellCategory.Heal));
            // ~2 s debounce; poll up to 10 s so slow CI never flakes.
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!File.Exists(path) && DateTime.UtcNow < deadline)
                Thread.Sleep(100);
            Assert.True(File.Exists(path), "debounced background save never landed");

            var reloaded = new SpellCatalog();
            reloaded.AttachStore(path);
            Assert.Equal(SpellCategory.Heal, reloaded.Classify("Fizzlewick's Lament"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void BuffTrackerLearnedDurationPersistsViaFlushAndRoundTrips()
    {
        var t0 = DateTime.Parse("2026-08-12T21:00:00");
        GameEvent Ev(int seconds, string message) =>
            LogParser.Parse($"[{t0.AddSeconds(seconds).ToString("ddd MMM d HH:mm:ss yyyy", System.Globalization.CultureInfo.InvariantCulture)}] {message}")!;

        var path = TempStore("buffs");
        try
        {
            var fadeLine = FadeMessageCatalog.Default.FindBySpell("Armor of Faith")!;
            var t = new BuffTracker();
            t.AttachStore(path);
            // The natural-fade learning recipe from BuffTrackerTests: rank-lengthened
            // fade at 4200 s teaches 4200 (tick-floored).
            t.Apply(Ev(0, "You begin casting Armor of Faith."));
            t.Apply(Ev(2, "You feel the favor of the gods upon you."));
            t.Apply(Ev(2 + 4201, fadeLine.Message));
            Assert.Equal(4200, t.LearnedDurations["Armor of Faith"], 0);

            t.Flush();
            Assert.True(File.Exists(path));

            var reloaded = new BuffTracker();
            reloaded.AttachStore(path);
            Assert.Equal(4200, reloaded.LearnedDurations["Armor of Faith"], 0);
        }
        finally { File.Delete(path); }
    }
}
